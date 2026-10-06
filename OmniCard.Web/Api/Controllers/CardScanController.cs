using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Services;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Security;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Helpers;

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
    ICsvExportImportService csv,
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

    /// <summary>Match one uploaded card image against <paramref name="game"/>'s catalog.
    /// <paramref name="language"/> is the scan session's card language ("ja", …); blank = auto — the
    /// language is then read off the card where it prints one, else taken from the matched printing.</summary>
    [HttpPost("match")]
    [RequestSizeLimit(MaxFileSize)]
    [RequirePermission(Permissions.ScanView)]
    public async Task<ActionResult<ScanMatchDto>> Match(
        IFormFile image, [FromForm] string game, [FromForm] bool isFoil, [FromForm] string[]? set,
        CancellationToken ct, [FromForm] string? language = null)
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
        var result = await matcher.MatchAsync(bytes, parsedGame, isFoil, setCodes, CardLanguages.Normalize(language), ct);

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
            m.GameSpecificId, m.Name, m.SetCode, m.SetName, m.CollectorNumber, m.Rarity, m.ImageUri, m.Language)).ToList();
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
    [RequireSiteAccess(SiteAccessLevel.Write, Location = "ContainerId")]
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
            if (MapScanItem(item, request.ContainerId) is not { } card)
                return BadRequest(new { error = $"Unknown game '{item.Game}'" });
            cards.Add(card);
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

    /// <summary>Export staged scans to CSV (or a zip of several formats) WITHOUT adding them to the
    /// collection — for scanning cards on video, or handing a list to another app, while keeping the
    /// option to commit them later. Nothing is written to the database. Each item is expanded to one row
    /// per copy, so formats that write a fixed quantity of 1 per row still count every copy.</summary>
    [HttpPost("export")]
    [RequirePermission(Permissions.ExportRun)]
    public IActionResult Export([FromBody] ScanExportRequest request)
    {
        if (request.Items.Count == 0)
            return BadRequest(new { error = "No scanned cards to export" });

        var cards = new List<CollectionCard>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (MapScanItem(item, containerId: null) is not { } card)
                return BadRequest(new { error = $"Unknown game '{item.Game}'" });
            card.Tags = item.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            var copies = card.Quantity;
            card.Quantity = 1;
            for (var i = 0; i < copies; i++)
                cards.Add(i == 0 ? card : CloneCopy(card));
        }

        MarketPriceHydrator.Populate(cardService, cards);

        var formats = (request.Formats.Count == 0 ? ["appnative"] : request.Formats)
            .Select(CsvExportFormats.Resolve)
            .DistinctBy(f => f.Key)
            .ToList();
        var baseName = SafeFileName(request.FileName) ?? "scan";

        logger.LogInformation("Exported {Count} scanned card(s) as {Formats}", cards.Count, string.Join(", ", formats.Select(f => f.Key)));

        if (formats.Count == 1)
        {
            var format = formats[0];
            return File(CsvExportFormats.Produce(csv, format, cards), format.ContentType,
                $"{baseName}-{format.Key}{format.Extension}");
        }
        return File(CsvExportFormats.ProduceZip(csv, formats, cards, baseName), "application/zip", $"{baseName}.zip");
    }

    private static CollectionCard CloneCopy(CollectionCard c) => new()
    {
        Game = c.Game, GameCardId = c.GameCardId, Name = c.Name, SetCode = c.SetCode, SetName = c.SetName,
        Number = c.Number, Rarity = c.Rarity, ImageUri = c.ImageUri, Condition = c.Condition, Language = c.Language,
        IsFoil = c.IsFoil, FoilType = c.FoilType, Quantity = 1, PurchasePrice = c.PurchasePrice, Note = c.Note,
        DateAdded = c.DateAdded, Tags = [.. c.Tags],
    };

    /// <summary>The client's base file name with path/invalid characters stripped; null when nothing
    /// usable is left.</summary>
    internal static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim(' ', '.', '-');
        if (cleaned.Length > 100) cleaned = cleaned[..100];
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Maps a confirmed scan item to a <see cref="CollectionCard"/> destined for
    /// <paramref name="containerId"/> (null for an export, which places nothing). Returns <c>null</c>
    /// when the item's game is unknown. Shared by the scan-commit, audit-commit and export paths.</summary>
    private static CollectionCard? MapScanItem(ScanCommitItem item, int? containerId)
    {
        if (LocationsController.ParseGame(item.Game) is not { } game)
            return null;

        // Honor an explicit per-item foil finish; fall back to the game's basic foil when foil but
        // no finish was chosen. Non-foil ⇒ no finish.
        var foilType = item.IsFoil
            ? (string.IsNullOrWhiteSpace(item.FoilType) ? FoilTypes.BasicFoilType(game) : item.FoilType.Trim())
            : null;

        return new CollectionCard
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
            Language = CardLanguages.Normalize(item.Language) ?? CardLanguages.English,
            IsFoil = item.IsFoil,
            FoilType = foilType,
            Quantity = Math.Max(1, item.Quantity),
            PurchasePrice = item.PurchasePrice,
            Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(),
            DateAdded = DateTime.UtcNow,
            ContainerId = containerId,
        };
    }

    /// <summary>Commit a location audit: the confirmed scans become the source of truth for the
    /// location. Matched cards are kept (condition/foil overwritten from the scan), scanned-but-absent
    /// cards are added, and expected-but-unscanned cards are deleted. Returns a summary of each bucket.
    /// Destructive (deletes lots), so it requires the collection-delete grant rather than scan-commit.</summary>
    [HttpPost("audit-commit")]
    [RequirePermission(Permissions.CollectionDelete)]
    [RequireSiteAccess(SiteAccessLevel.Write, Location = "ContainerId")]
    public ActionResult<AuditCommitResultDto> AuditCommit([FromBody] AuditCommitRequest request)
    {
        if (request.ContainerId <= 0)
            return BadRequest(new { error = "A target location is required" });
        if (request.Items.Count == 0)
            return BadRequest(new { error = "An audit must include at least one scanned card" });

        var cards = new List<CollectionCard>(request.Items.Count);
        // Union of tags per identity key, so newly-added lots inherit the tags the user set on the scan.
        var tagsByKey = new Dictionary<string, HashSet<string>>();
        foreach (var item in request.Items)
        {
            if (MapScanItem(item, request.ContainerId) is not { } card)
                return BadRequest(new { error = $"Unknown game '{item.Game}'" });
            cards.Add(card);

            var key = WebBinderCardService.AuditIdentityKey(card.GameCardId, card.SetCode, card.Number);
            if (!tagsByKey.TryGetValue(key, out var set)) tagsByKey[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in item.Tags.Where(t => !string.IsNullOrWhiteSpace(t))) set.Add(tag.Trim());
        }

        WebBinderCardService.AuditReconcileResult result;
        try
        {
            result = binderCards.ReconcileLocationAudit(request.ContainerId, cards);
        }
        catch (DeckBoxGameMismatchException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        // Attach tags to the lots the audit created (one added lot per identity key).
        foreach (var (key, lotId) in result.AddedLots)
        {
            if (tagsByKey.TryGetValue(key, out var set) && set.Count > 0)
                tags.SetTagsForLot(lotId, set.ToList());
        }

        // Teach the matcher from every confirmed identity, matched or added (same as scan commit).
        RecordScanCorrections(request.Items);

        logger.LogInformation(
            "Audited location {LocationId}: {Matched} matched, {Added} added, {NotFound} not found ({Updated} updated)",
            request.ContainerId, result.Matched.Count, result.Added.Count, result.NotFound.Count, result.UpdatedCount);

        static AuditLineDto ToDto(WebBinderCardService.AuditLine l) =>
            new(l.Name, l.SetCode, l.CollectorNumber, l.Condition, l.IsFoil, l.Quantity);

        return Ok(new AuditCommitResultDto(
            result.Matched.Select(ToDto).ToList(),
            result.NotFound.Select(ToDto).ToList(),
            result.Added.Select(ToDto).ToList(),
            result.UpdatedCount));
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
