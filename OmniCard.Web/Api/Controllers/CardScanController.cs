using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Services;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Security;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Server-side card scanning for the SPA: upload an image, match it against a game's catalog
/// (<c>WebScanMatchingService</c>), search the catalog to correct a bad match, and commit confirmed
/// cards into a storage location. This replaces the desktop the migration is retiring — matching runs
/// here, not relayed to a WPF app. Routed under <c>api/scan/*</c> and passphrase-gated
/// (<see cref="ApiAuth"/>); the legacy phone→desktop <c>ScanController</c> is left untouched.
/// </summary>
[ApiController]
[ApiAuth]
[Route("api/scan")]
public sealed class CardScanController(
    WebScanMatchingService matcher,
    ICardService cardService,
    WebBinderCardService binderCards,
    ITagService tags,
    ILogger<CardScanController> logger) : ControllerBase
{
    /// <summary>Upper bound on correction-search results. High enough to show every printing of a
    /// card (the old hard cap of 20 hid later printings); still bounded so a bare common name can't
    /// stream the whole catalog.</summary>
    private const int MaxSearchResults = 500;

    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/tiff", "image/tif", "image/x-tiff" };
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tif", ".tiff" };
    // TIFF scans (e.g. from a flatbed) are far larger than JPEG/PNG phone photos, so the cap is
    // generous. GDI+ still has to decode the whole thing, so it isn't unbounded.
    private const long MaxFileSize = 30 * 1024 * 1024; // 30 MB

    /// <summary>An upload is accepted when its content type OR file extension is a known image format.
    /// The extension fallback matters because some OSes hand a <c>.tif</c> up as
    /// <c>application/octet-stream</c> (or blank) rather than <c>image/tiff</c>.</summary>
    public static bool IsAcceptedImage(string? contentType, string? fileName)
    {
        if (contentType is not null && AllowedContentTypes.Contains(contentType))
            return true;
        var ext = Path.GetExtension(fileName ?? "");
        return ext.Length > 0 && AllowedExtensions.Contains(ext);
    }

    /// <summary>Whether the upload's own format can't render in a browser <c>&lt;img&gt;</c> and so needs
    /// a server-rendered preview (TIFF). JPEG/PNG preview from the client's local copy.</summary>
    private static bool NeedsServerPreview(string? contentType, string? fileName)
    {
        if (contentType is not null && (contentType.Equals("image/tiff", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("image/tif", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("image/x-tiff", StringComparison.OrdinalIgnoreCase)))
            return true;
        var ext = Path.GetExtension(fileName ?? "");
        return ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Match one uploaded card image against <paramref name="game"/>'s catalog.</summary>
    [HttpPost("match")]
    [RequestSizeLimit(MaxFileSize)]
    [RequirePermission(Permissions.ScanView)]
    public async Task<ActionResult<ScanMatchDto>> Match(
        IFormFile image, [FromForm] string game, [FromForm] bool isFoil, [FromForm] string[]? set, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "No image provided" });
        if (!IsAcceptedImage(image.ContentType, image.FileName))
            return BadRequest(new { error = "Only JPEG, PNG and TIFF images are accepted" });
        if (image.Length > MaxFileSize)
            return BadRequest(new { error = "Image exceeds 30 MB limit" });
        if (LocationsController.ParseGame(game) is not { } parsedGame)
            return BadRequest(new { error = $"Unknown game '{game}'" });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        // The client may send several `set` values (multiple art-fallback sets); constrain matching
        // to the union of them. Empty ⇒ no set constraint.
        var setCodes = (set ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();
        var result = await matcher.MatchAsync(bytes, parsedGame, isFoil, setCodes, ct);

        // Flag whether this is a card the collection doesn't already hold (drives the "new card"
        // gold-star badge). IsNewCard opens its own DbContext, so it's safe alongside concurrent
        // batch matches.
        if (result.Matched && !string.IsNullOrEmpty(result.GameCardId))
            result = result with { IsNew = binderCards.IsNewCard(parsedGame, result.GameCardId) };

        // For a TIFF (or any non-browser-native upload) the client can't preview its own copy, so hand
        // back a downscaled JPEG data URI for the side-by-side compare.
        if (NeedsServerPreview(image.ContentType, image.FileName))
            result = result with { ScanPreviewDataUri = WebScanMatchingService.RenderPreviewDataUri(bytes) };
        return Ok(result);
    }

    /// <summary>Catalog search for the correction screen (read-only, scoped to one game). Beyond the
    /// free-text <paramref name="q"/> (matched against name) and optional <paramref name="cn"/>
    /// collector number, the results are scoped to <paramref name="set"/> — the union of the "Sets
    /// (art fallback)" the user chose on the scan page (the single source of truth for which sets to
    /// look through). Empty ⇒ all sets. Each term folds into the same <c>set:</c>/<c>cn:</c> query
    /// grammar every game's <c>SearchCards</c> already understands.</summary>
    [HttpGet("search")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<IReadOnlyList<ScanSearchResultDto>> Search(
        [FromQuery] string game, [FromQuery] string? q = null, [FromQuery] string[]? set = null, [FromQuery] string? cn = null)
    {
        if (LocationsController.ParseGame(game) is not { } parsedGame)
            return BadRequest(new { error = $"Unknown game '{game}'" });

        var setCodes = (set ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();

        // Terms shared by every sub-query: bare name + collector number.
        var baseTerms = new List<string>();
        if (!string.IsNullOrWhiteSpace(q)) baseTerms.Add(q.Trim());
        if (!string.IsNullOrWhiteSpace(cn)) baseTerms.Add($"cn:{cn.Trim()}");

        // With no set constraint AND nothing to search on, return empty without touching the catalog.
        // (A bare set list is still a valid search — it lists the whole set.)
        if (setCodes.Length == 0 && baseTerms.Count == 0)
            return Ok(Array.Empty<ScanSearchResultDto>());

        var gameService = cardService.GetGameService(parsedGame);
        List<CardMatch> matches;
        if (setCodes.Length == 0)
        {
            // No set constraint (no art-fallback sets chosen) — search across all sets, as before.
            matches = gameService.SearchCards(string.Join(' ', baseTerms), MaxSearchResults);
        }
        else
        {
            // The query grammar ANDs its terms, so several set:s can't be ORed in a single query.
            // Run one search per chosen set and union the results — a bare set (no name/cn) simply
            // lists that whole set. This keeps the correction search scoped to exactly the same
            // art-fallback sets that constrain auto-matching.
            var seen = new HashSet<string>();
            var union = new List<CardMatch>();
            foreach (var code in setCodes)
            {
                var terms = new List<string>(baseTerms) { $"set:{code}" };
                foreach (var m in gameService.SearchCards(string.Join(' ', terms), MaxSearchResults))
                {
                    if (seen.Add($"{m.SetCode}|{m.CollectorNumber}|{m.GameSpecificId}"))
                        union.Add(m);
                }
            }
            matches = union
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxSearchResults)
                .ToList();
        }

        var results = matches.Select(m => new ScanSearchResultDto(
            m.GameSpecificId, m.Name, m.SetCode, m.SetName, m.CollectorNumber, m.Rarity, m.ImageUri)).ToList();
        return Ok(results);
    }

    /// <summary>Curated foil-finish presets for a game, for the scan bulk/per-item foil-type picker.</summary>
    [HttpGet("foil-types")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<IReadOnlyList<string>> FoilTypesFor([FromQuery] string game)
    {
        if (LocationsController.ParseGame(game) is not { } parsedGame)
            return BadRequest(new { error = $"Unknown game '{game}'" });
        return Ok(FoilTypes.ForGame(parsedGame));
    }

    /// <summary>Write a batch of confirmed scans into a storage location as owned lots.</summary>
    [HttpPost("commit")]
    [RequirePermission(Permissions.ScanCommit)]
    public ActionResult<ScanCommitResultDto> Commit([FromBody] ScanCommitRequest request)
    {
        if (request.ContainerId <= 0)
            return BadRequest(new { error = "A target location is required" });
        if (request.Items.Count == 0)
            return BadRequest(new { error = "No cards to commit" });

        var cards = new List<CollectionCard>(request.Items.Count);
        var tagsPerCard = new List<IReadOnlyList<string>>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (LocationsController.ParseGame(item.Game) is not { } game)
                return BadRequest(new { error = $"Unknown game '{item.Game}'" });

            // Honor an explicit per-item foil finish; fall back to the game's basic foil when foil but
            // no finish was chosen. Non-foil ⇒ no finish.
            var foilType = item.IsFoil
                ? (string.IsNullOrWhiteSpace(item.FoilType) ? FoilTypes.BasicFoilType(game) : item.FoilType.Trim())
                : null;

            cards.Add(new CollectionCard
            {
                Game = game,
                GameCardId = item.GameCardId,
                Name = item.Name,
                SetCode = item.SetCode,
                SetName = item.SetName,
                Number = item.CollectorNumber,
                Rarity = item.Rarity,
                ImageUri = item.ImageUri,
                Condition = string.IsNullOrWhiteSpace(item.Condition) ? "NM" : item.Condition,
                IsFoil = item.IsFoil,
                FoilType = foilType,
                Quantity = Math.Max(1, item.Quantity),
                PurchasePrice = item.PurchasePrice,
                Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(),
                DateAdded = DateTime.UtcNow,
                ContainerId = request.ContainerId,
            });
            tagsPerCard.Add(item.Tags);
        }

        // A scanned card is a real physical copy — always create a new lot (never skip as a duplicate).
        // AddScannedLots returns the created lot ids in input order so we can attach per-copy tags.
        IReadOnlyList<int> lotIds;
        try
        {
            lotIds = binderCards.AddScannedLots(cards);
        }
        catch (DeckBoxGameMismatchException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        for (var i = 0; i < lotIds.Count; i++)
        {
            var cardTags = tagsPerCard[i].Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (cardTags.Count > 0)
                tags.SetTagsForLot(lotIds[i], cardTags);
        }

        // Teach the matcher from confirmed identities: record each scanned item's (scan pHash → card)
        // so future scans of the same card auto-match. The desktop did this; the web flow had dropped
        // it entirely, so the matcher never learned from bulk entry. Best-effort — a failure to record
        // must never fail the commit (the lots are already written).
        RecordScanCorrections(request.Items);

        logger.LogInformation("Committed {Count} scanned card(s) to location {LocationId}", lotIds.Count, request.ContainerId);
        return Ok(new ScanCommitResultDto(lotIds.Count));
    }

    /// <summary>Record a scan-hash → confirmed-card mapping for each committed item that carries a scan
    /// hash, so the matcher recognizes the same card next time (see <see cref="ScanCommitItem.ScanHash"/>).</summary>
    private void RecordScanCorrections(IReadOnlyList<ScanCommitItem> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ScanHash) || string.IsNullOrWhiteSpace(item.GameCardId)) continue;
            if (!ulong.TryParse(item.ScanHash, out var hash)) continue;
            if (LocationsController.ParseGame(item.Game) is not { } game) continue;
            try
            {
                cardService.GetGameService(game).RecordCorrection(hash, item.GameCardId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record scan correction for {Game} card {CardId}", item.Game, item.GameCardId);
            }
        }
    }
}
