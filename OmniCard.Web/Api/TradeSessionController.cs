using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Write API for the SPA's trade builder — the port of the retired Razor <c>Trade</c> (single card)
/// and <c>TradeSession</c> (multi-card) pages. A trade is built up as a draft <c>trade.json</c> in a
/// per-session folder under the shared trades directory (see <see cref="TradeSessionRecord"/>); the
/// "current" draft is tracked with <see cref="TradeSessionCookie"/>. On finalize the draft is stamped
/// <c>final</c> and applied to the collection immediately via <see cref="ITradeImportService"/> (the
/// desktop app used to do this on launch; it's retired, so the web app now owns application).
/// </summary>
[ApiController]
[Route("api/trade-session")]
[ApiAuth]
public sealed class TradeSessionController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly IDataPathService _paths;
    private readonly ICardService _cardService;
    private readonly ITradeImportService _tradeImport;
    private readonly IDbContextFactory<ScryfallDbContext>? _scryfallFactory;

    public TradeSessionController(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        IDataPathService paths,
        ICardService cardService,
        ITradeImportService tradeImport,
        IDbContextFactory<ScryfallDbContext>? scryfallFactory = null)
    {
        _dbFactory = dbFactory;
        _paths = paths;
        _cardService = cardService;
        _tradeImport = tradeImport;
        _scryfallFactory = scryfallFactory;
    }

    // ---- Read: the current draft (null when none is open) --------------------------------------

    [HttpGet]
    public IActionResult Current()
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _paths);
        if (id is null || !TryLoadDraft(id.Value, out var record))
            return Ok(new { session = (object?)null });
        return Ok(new { session = BuildState(id.Value, record) });
    }

    [HttpPost("start")]
    public IActionResult Start()
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _paths) ?? CreateDraft();
        TryLoadDraft(id, out var record);
        return Ok(new { session = BuildState(id, record!) });
    }

    // ---- Add / remove outgoing cards ------------------------------------------------------------

    public sealed record AddOwnedRequest(int LotId);

    [HttpPost("add-owned")]
    public IActionResult AddOwned([FromBody] AddOwnedRequest r)
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _paths) ?? CreateDraft();
        AddOwnedItem(id, r.LotId, out _, out _);
        TryLoadDraft(id, out var record);
        return Ok(new { session = BuildState(id, record!) });
    }

    [HttpPost("add-offdb")]
    public async Task<IActionResult> AddOffDb([FromForm] string? name, [FromForm] decimal? value, IFormFile? photo)
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _paths) ?? CreateDraft();
        if (!TryLoadDraft(id, out var record))
            return NotFound(new { error = "Trade session not found." });

        var photoFileName = await SavePhotoAsync(id, photo, $"outgoing-{Guid.NewGuid():N}");
        record.OutgoingItems.Add(new TradeOutgoingItem
        {
            LotId = null,
            IsOffDatabase = true,
            CardName = string.IsNullOrWhiteSpace(name) ? "(unnamed card)" : name.Trim(),
            EstimatedValue = value,
            PhotoFileName = photoFileName,
        });
        SaveDraft(id, record);
        return Ok(new { session = BuildState(id, record) });
    }

    public sealed record RemoveItemRequest(int Index);

    [HttpPost("remove-item")]
    public IActionResult RemoveItem([FromBody] RemoveItemRequest r)
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _paths);
        if (id is null || !TryLoadDraft(id.Value, out var record))
            return NotFound(new { error = "Trade session not found." });

        if (r.Index >= 0 && r.Index < record.OutgoingItems.Count)
        {
            var item = record.OutgoingItems[r.Index];
            if (item.IsOffDatabase && !string.IsNullOrEmpty(item.PhotoFileName))
                TryDeleteFile(Path.Combine(FolderFor(id.Value), item.PhotoFileName));
            record.OutgoingItems.RemoveAt(r.Index);
            SaveDraft(id.Value, record);
        }
        return Ok(new { session = BuildState(id.Value, record) });
    }

    // ---- Finalize / cancel ----------------------------------------------------------------------

    [HttpPost("finalize")]
    public async Task<IActionResult> Finalize([FromForm] string? note, [FromForm] decimal? receivedValue, IFormFile? receivedPhoto)
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _paths);
        if (id is null || !TryLoadDraft(id.Value, out var record))
            return NotFound(new { error = "Trade session not found." });
        if (record.OutgoingItems.Count == 0)
            return BadRequest(new { error = "Add at least one card before finalizing." });

        record.Note = note ?? "";
        record.ReceivedValue = receivedValue;
        record.ReceivedPhotoFileName = await SavePhotoAsync(id.Value, receivedPhoto, "received")
            ?? record.ReceivedPhotoFileName;
        record.Status = "final";
        SaveDraft(id.Value, record);
        TradeSessionCookie.Clear(HttpContext);

        // Apply immediately (desktop used to do this on launch). Idempotent.
        var applied = _tradeImport.ImportPendingTrades();
        return Ok(new { applied });
    }

    [HttpPost("cancel")]
    public IActionResult Cancel()
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _paths);
        if (id is not null)
        {
            try
            {
                var folder = FolderFor(id.Value);
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch { /* best effort — an unapplied draft folder is harmless */ }
        }
        TradeSessionCookie.Clear(HttpContext);
        return Ok(new { status = "ok" });
    }

    // ---- Owned-card search (to add outgoing cards) ---------------------------------------------

    [HttpGet("search")]
    public IActionResult Search(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { results = Array.Empty<object>() });

        using var db = _dbFactory.CreateDbContext();
        var allCards = db.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.Product.Category == ProductCategory.Single && !l.IsTraded)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, l.Product.LastMarketPrice ?? 0m))
            .ToList();

        IEnumerable<CollectionCard> query = allCards;
        foreach (var term in q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (term.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[4..];
                query = query.Where(c => Contains(c.SetCode, val) || Contains(c.SetName, val));
            }
            else if (term.StartsWith("cn:", StringComparison.OrdinalIgnoreCase))
            {
                var val = term[3..];
                query = query.Where(c => Contains(c.Number, val));
            }
            else
            {
                query = query.Where(c => Contains(c.Name, term));
            }
        }

        var reps = query.OrderBy(c => c.Name).ThenBy(c => c.SetCode).Take(40).ToList();
        CardArtHydrator.HydrateMissingImageUris(_cardService, reps);
        MarketPriceHydrator.Populate(_cardService, reps);

        var results = reps.Select(c => new
        {
            lotId = c.Id,
            name = c.Name,
            setName = c.SetName,
            setCode = c.SetCode,
            number = c.Number,
            isFoil = c.IsFoil,
            condition = c.Condition,
            marketPrice = c.MarketPrice > 0m ? c.MarketPrice : (decimal?)null,
            imageUrl = CardImageUrl.Resolve(c.ScanImagePath, c.ImageUri, _paths.ScansDirectory),
        });
        return Ok(new { results });
    }

    // ---- State builder --------------------------------------------------------------------------

    /// <summary>Projects a draft record to the client shape, resolving owned-card art + TCGplayer
    /// links in a single batch DB read.</summary>
    private object BuildState(Guid id, TradeSessionRecord record)
    {
        var lotIds = record.OutgoingItems.Where(i => i.LotId is not null).Select(i => i.LotId!.Value).ToList();

        var imageByLot = new Dictionary<int, string?>();
        var tcgByLot = new Dictionary<int, string>();
        if (lotIds.Count > 0)
        {
            using var db = _dbFactory.CreateDbContext();
            var lots = db.Lots.AsNoTracking().Include(l => l.Product)
                .Where(l => lotIds.Contains(l.Id))
                .Select(l => new
                {
                    LotId = l.Id,
                    l.ScanImagePath,
                    l.Product.ImageUri,
                    l.Product.Game,
                    l.Product.GameCardId,
                    l.Product.Name,
                    l.Product.SetName,
                    l.Product.Foil,
                    l.Product.FoilType,
                })
                .ToList();

            var mtgIds = lots.Where(p => p.Game == CardGame.Mtg).Select(p => p.GameCardId ?? "");
            var resolved = ScryfallTcgIdResolver.Resolve(_scryfallFactory, mtgIds);

            foreach (var l in lots)
            {
                imageByLot[l.LotId] = CardImageUrl.Resolve(l.ScanImagePath, l.ImageUri, _paths.ScansDirectory);
                int? productId = null;
                if (l.Game == CardGame.Mtg)
                {
                    var etched = l.Foil && (l.FoilType?.Contains("Etched", StringComparison.OrdinalIgnoreCase) ?? false);
                    productId = resolved.GetValueOrDefault(l.GameCardId ?? "").Pick(etched);
                }
                tcgByLot[l.LotId] = TcgPlayerLink.Build(l.Game, l.GameCardId, l.Name, l.SetName, productId);
            }
        }

        var items = record.OutgoingItems.Select((it, index) => new
        {
            index,
            lotId = it.LotId,
            isOffDatabase = it.IsOffDatabase,
            game = (int)it.Game,
            cardName = it.CardName,
            setCode = it.SetCode,
            setName = it.SetName,
            collectorNumber = it.CollectorNumber,
            foil = it.Foil,
            estimatedValue = it.EstimatedValue,
            imageUrl = it.LotId is int lot ? imageByLot.GetValueOrDefault(lot) : null,
            tcgPlayerUrl = it.LotId is int l2 ? tcgByLot.GetValueOrDefault(l2) : null,
        }).ToList();

        return new
        {
            sessionId = id,
            note = record.Note,
            receivedValue = record.ReceivedValue,
            outgoingTotal = record.OutgoingItems.Sum(i => i.EstimatedValue ?? 0m),
            items,
        };
    }

    // ---- Draft persistence (mirrors the retired TradeSession PageModel) -------------------------

    private Guid CreateDraft()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(FolderFor(id));
        SaveDraft(id, new TradeSessionRecord { SessionId = id, Status = "draft" });
        TradeSessionCookie.Set(HttpContext, id);
        return id;
    }

    private void AddOwnedItem(Guid id, int lotId, out bool added, out string name)
    {
        added = false;
        name = "Card";
        if (!TryLoadDraft(id, out var record))
            return;

        using var db = _dbFactory.CreateDbContext();
        var lot = db.Lots.AsNoTracking().Include(l => l.Product)
            .FirstOrDefault(l => l.Id == lotId && l.Product.Category == ProductCategory.Single);
        if (lot is null || lot.IsTraded)
            return;

        var card = CollectionCardMapper.ToDto(lot, lot.Product, lot.Product.LastMarketPrice ?? 0m);
        name = card.Name;
        if (record.OutgoingItems.Any(i => i.LotId == lotId))
            return; // already added

        MarketPriceHydrator.Populate(_cardService, [card]);
        record.OutgoingItems.Add(new TradeOutgoingItem
        {
            LotId = lotId,
            IsOffDatabase = false,
            Game = card.Game,
            CardName = card.Name,
            SetCode = card.SetCode,
            SetName = card.SetName,
            CollectorNumber = card.Number,
            Foil = card.IsFoil,
            EstimatedValue = card.MarketPrice > 0m ? card.MarketPrice : null,
        });
        SaveDraft(id, record);
        added = true;
    }

    private string FolderFor(Guid id) => Path.Combine(_paths.TradesDirectory, id.ToString());

    private bool TryLoadDraft(Guid id, out TradeSessionRecord record)
    {
        record = null!;
        var jsonPath = Path.Combine(FolderFor(id), "trade.json");
        if (!System.IO.File.Exists(jsonPath))
            return false;
        try
        {
            var loaded = JsonSerializer.Deserialize<TradeSessionRecord>(
                System.IO.File.ReadAllText(jsonPath), JsonOptions);
            if (loaded is null || loaded.SchemaVersion < 2
                || !string.Equals(loaded.Status, "draft", StringComparison.OrdinalIgnoreCase))
                return false;
            record = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveDraft(Guid id, TradeSessionRecord record)
    {
        var folder = FolderFor(id);
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(
            Path.Combine(folder, "trade.json"),
            JsonSerializer.Serialize(record, JsonOptions));
    }

    private async Task<string?> SavePhotoAsync(Guid id, IFormFile? photo, string baseName)
    {
        if (photo is not { Length: > 0 })
            return null;
        var ext = Path.GetExtension(photo.FileName);
        var fileName = baseName + (string.IsNullOrEmpty(ext) ? ".jpg" : ext);
        var folder = FolderFor(id);
        Directory.CreateDirectory(folder);
        await using var stream = System.IO.File.Create(Path.Combine(folder, fileName));
        await photo.CopyToAsync(stream);
        return fileName;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch { /* best effort */ }
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
