using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.CardMatching;
using OmniCard.Shared.Storage;
using OmniCard.Collection.Lists;
using OmniCard.Web.Services;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Collection import: CSV via file upload, or a Moxfield/Archidekt deck URL straight into a
/// location. CSV parses with the shared <see cref="ICsvExportImportService"/> (auto-detects app-native /
/// TCGplayer / Moxfield / Manabox); URLs are fetched by <see cref="IDecklistService"/> (curl.exe for
/// Moxfield's Cloudflare-fronted API). Both write the rows as new lots via <see cref="WebBinderCardService"/>.</summary>
public sealed class ImportController(
    ICsvExportImportService csv,
    WebBinderCardService binderCards,
    IDecklistService decklists,
    ICardService cardService,
    LocationImportService locationImport) : ApiControllerBase
{
    [HttpPost("csv")]
    [RequirePermission(Permissions.ImportRun)]
    public IActionResult Csv(
        IFormFile file,
        [FromQuery] bool skipDuplicates = true,
        [FromQuery] int? targetContainerId = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var path = Path.Combine(Path.GetTempPath(), $"omnicard-import-{Guid.NewGuid():N}.csv");
        try
        {
            using (var fs = System.IO.File.Create(path))
                file.CopyTo(fs);

            var preview = csv.PreviewImport(path);
            if (preview.Cards.Count == 0)
                return BadRequest(new { error = "No importable rows found.", warnings = preview.Warnings });

            foreach (var card in preview.Cards)
            {
                // Foil finish default (mirrors CsvExportImportService.ImportCards).
                if (!card.IsFoil) card.FoilType = null;
                else if (string.IsNullOrEmpty(card.FoilType)) card.FoilType = FoilTypes.BasicFoilType(card.Game);

                if (targetContainerId is not null && card.ContainerId is null)
                {
                    card.ContainerId = targetContainerId.Value;
                    card.Container = null;
                }
            }

            var imported = binderCards.ImportCollectionCards(preview.Cards, skipDuplicates);
            return Ok(new
            {
                imported,
                totalRows = preview.TotalRows,
                detectedFormat = preview.DetectedFormat.ToString(),
                warnings = preview.Warnings,
            });
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    /// <summary>Fetch a Moxfield/Archidekt deck by URL and add every card to <c>ContainerId</c> as owned
    /// lots. Each line resolves to its exact set + collector printing (falling back to the cheapest
    /// printing of the name, reported in <c>SubstitutedNames</c>) and keeps the deck's foil/etched finish.
    /// Repeat lines of the same printing + finish merge into one lot.</summary>
    [HttpPost("url")]
    [RequirePermission(Permissions.ImportRun)]
    public async Task<ActionResult<ImportUrlResultDto>> Url([FromBody] ImportUrlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { error = "A deck URL is required." });
        if (request.ContainerId <= 0)
            return BadRequest(new { error = "A target location is required." });
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'." });

        var fetched = await decklists.FetchDecklistAsync(request.Url);
        if (fetched is null)
            return BadRequest(new { error = "Couldn't fetch that deck URL. Supported: Moxfield, Archidekt (public decks)." });

        var (deckName, entries) = fetched.Value;
        if (entries.Count == 0)
            return BadRequest(new { error = "That deck has no cards." });

        var gs = cardService.GetGameService(game);
        var condition = string.IsNullOrWhiteSpace(request.Condition) ? "NM" : request.Condition.Trim();
        var unresolved = new List<string>();
        var substituted = new List<string>();
        var cards = new Dictionary<(string GameCardId, string? Finish), CollectionCard>();

        foreach (var entry in entries)
        {
            var printing = DecklistPrintingResolver.Resolve(gs, entry);
            if (printing is null)
            {
                printing = DecklistPrintingResolver.Resolve(gs, entry with { SetCode = null, CollectorNumber = null });
                if (printing is null) { unresolved.Add(entry.CardName); continue; }
                substituted.Add(entry.CardName);
            }

            var key = (printing.GameSpecificId, entry.Finish);
            if (cards.TryGetValue(key, out var existing))
            {
                existing.Quantity += entry.Quantity;
                continue;
            }

            cards[key] = new CollectionCard
            {
                Game = game,
                GameCardId = printing.GameSpecificId,
                Name = printing.Name,
                SetCode = printing.SetCode,
                SetName = printing.SetName,
                Number = printing.CollectorNumber,
                Rarity = printing.Rarity,
                ImageUri = printing.ImageUri,
                Color = CardAttributeExtractor.ExtractColor(printing, game),
                CardType = CardAttributeExtractor.ExtractCardType(printing, game),
                IsFoil = entry.Finish is not null,
                FoilType = entry.Finish,
                Quantity = Math.Max(1, entry.Quantity),
                Condition = condition,
                ContainerId = request.ContainerId,
                DateAdded = DateTime.UtcNow,
            };
        }

        var imported = 0;
        var skipped = 0;
        try
        {
            if (request.SkipDuplicates)
            {
                // One card per call so a skipped duplicate is attributable to its copy count.
                foreach (var card in cards.Values)
                {
                    if (binderCards.ImportCollectionCards([card], skipDuplicates: true) > 0) imported += card.Quantity;
                    else skipped += card.Quantity;
                }
            }
            else if (cards.Count > 0)
            {
                binderCards.ImportCollectionCards(cards.Values.ToList(), skipDuplicates: false);
                imported = cards.Values.Sum(c => c.Quantity);
            }
        }
        catch (DeckBoxGameMismatchException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        return new ImportUrlResultDto(
            deckName, imported, skipped, entries.Sum(e => e.Quantity), unresolved, substituted);
    }

    /// <summary>All-or-nothing CSV import into the location in the route (the Location view's Import).
    /// Every row must parse and resolve to a catalog card; otherwise nothing is written and the response
    /// is a 422 <see cref="LocationImportFailureDto"/> listing every problem.</summary>
    [HttpPost("location/{locationId:int}/csv")]
    [RequirePermission(Permissions.ImportRun)]
    public ActionResult<LocationImportResultDto> LocationCsv(int locationId, IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return UnprocessableEntity(new LocationImportFailureDto("Choose a CSV file to import — the file was empty or missing.", []));

        var path = Path.Combine(Path.GetTempPath(), $"omnicard-import-{Guid.NewGuid():N}.csv");
        try
        {
            using (var fs = System.IO.File.Create(path))
                file.CopyTo(fs);
            return ToResult(locationImport.ImportCsv(locationId, path, file.FileName));
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    /// <summary>All-or-nothing Moxfield/Archidekt deck import into the location in the route. Every card
    /// must resolve to a catalog printing; otherwise nothing is written (422 + every unresolved card).</summary>
    [HttpPost("location/{locationId:int}/url")]
    [RequirePermission(Permissions.ImportRun)]
    public async Task<ActionResult<LocationImportResultDto>> LocationUrl(int locationId, [FromBody] LocationUrlImportRequest request) =>
        ToResult(await locationImport.ImportUrlAsync(locationId, request));

    private ActionResult<LocationImportResultDto> ToResult(LocationImportService.Outcome outcome) =>
        outcome.LocationNotFound ? NotFound(new { error = "That location no longer exists." })
        : outcome.Failure is { } failure ? UnprocessableEntity(failure)
        : outcome.Result!;
}
