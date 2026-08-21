using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;

namespace OmniCard.Web.Pages;

/// <summary>Multi-card trade "session" builder for the phone: start a trade, add outgoing cards
/// (owned lots looked up from the read-only DB, or off-database card-show pickups captured via
/// photo), then finalize with a note + a photo of the cards received. Everything is written to a
/// draft folder under the shared trades directory (never touching the read-only DBs); the desktop
/// app applies it on next launch once finalized. See <see cref="TradeSessionRecord"/>.</summary>
public class TradeSessionModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly IDataPathService _dataPathService;
    private readonly ICardService _cardService;
    private readonly IDbContextFactory<ScryfallDbContext>? _scryfallFactory;

    public TradeSessionModel(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        IDataPathService dataPathService,
        ICardService cardService,
        IDbContextFactory<ScryfallDbContext>? scryfallFactory = null)
    {
        _dbFactory = dbFactory;
        _dataPathService = dataPathService;
        _cardService = cardService;
        _scryfallFactory = scryfallFactory;
    }

    public Guid? SessionId { get; private set; }
    public TradeSessionRecord? Record { get; private set; }

    /// <summary>TCGPlayer link per outgoing owned card, keyed by lot id (only where the card's
    /// catalog identity is known). Off-database cards aren't in any catalog, so they get no link.</summary>
    public Dictionary<int, string> TcgPlayerUrlByLotId { get; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<OwnedResult> SearchResults { get; set; } = [];

    public decimal OutgoingTotal => Record?.OutgoingItems.Sum(i => i.EstimatedValue ?? 0m) ?? 0m;

    // ---- GET: open the current session, or start one -------------------------------------------

    public IActionResult OnGet(Guid? id)
    {
        // No id: open the current draft (from the cookie) if there is one, else start a fresh one.
        // Clicking "Start a Trade" / "Open Current Trade Session…" both land here.
        if (id is null)
        {
            var active = TradeSessionCookie.GetActive(HttpContext, _dataPathService);
            return RedirectToPage("TradeSession", new { id = active ?? CreateDraft() });
        }

        if (!TryLoadDraft(id.Value, out var record))
        {
            // Stale/finalized link — fall back to the current/new draft.
            return RedirectToPage("TradeSession");
        }

        // Opening a valid draft makes it the current session.
        TradeSessionCookie.Set(HttpContext, id.Value);
        SessionId = id;
        Record = record;
        BuildTcgPlayerLinks(record);

        if (!string.IsNullOrWhiteSpace(Q))
            ExecuteSearch();

        return Page();
    }

    /// <summary>Cancels the current trade session — deletes the draft folder (and any captured
    /// photos) and clears the current-session cookie. The draft never touched the collection DB,
    /// so removing it undoes every action the session took.</summary>
    public IActionResult OnPostCancel(Guid id)
    {
        var folder = FolderFor(id);
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch { /* best effort — a leftover folder is harmless (never applied) */ }

        TradeSessionCookie.Clear(HttpContext);
        if (TempData is not null)
            TempData["TradeMessage"] = "Trade canceled.";
        return RedirectToPage("Index");
    }

    // ---- Handlers -------------------------------------------------------------------------------

    public IActionResult OnPostStart()
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _dataPathService) ?? CreateDraft();
        return RedirectToPage("TradeSession", new { id });
    }

    /// <summary>Entry point from a card's detail page: add this owned card to the current trade
    /// session, starting one if none is open yet. Returns the user to the card so they can keep
    /// browsing and adding.</summary>
    public IActionResult OnPostAddCard(int lotId)
    {
        var id = TradeSessionCookie.GetActive(HttpContext, _dataPathService) ?? CreateDraft();
        TradeSessionCookie.Set(HttpContext, id);
        AddOwnedItem(id, lotId, out var added, out var name);

        if (TempData is not null)
            TempData["TradeMessage"] = added
                ? $"Added {name} to your trade session."
                : $"{name} is already in your trade session.";
        return RedirectToPage("Card", new { id = lotId });
    }

    public IActionResult OnPostAddOwned(Guid id, int lotId)
    {
        AddOwnedItem(id, lotId, out _, out _);
        return RedirectToPage("TradeSession", new { id, Q });
    }

    /// <summary>Appends an owned lot to the given draft (no-op if the draft is gone or the lot is
    /// already present / traded). Reports whether it was added and the card's display name.</summary>
    private void AddOwnedItem(Guid id, int lotId, out bool added, out string name)
    {
        added = false;
        name = "Card";
        if (!TryLoadDraft(id, out var record))
            return;

        using (var db = _dbFactory.CreateDbContext())
        {
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
        }

        SaveDraft(id, record);
        added = true;
    }

    public async Task<IActionResult> OnPostAddOffDbAsync(Guid id, string? name, decimal? value, IFormFile? photo)
    {
        if (!TryLoadDraft(id, out var record))
            return RedirectToPage("TradeSession");

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
        return RedirectToPage("TradeSession", new { id });
    }

    public IActionResult OnPostRemoveItem(Guid id, int index)
    {
        if (!TryLoadDraft(id, out var record))
            return RedirectToPage("TradeSession");

        if (index >= 0 && index < record.OutgoingItems.Count)
        {
            var item = record.OutgoingItems[index];
            if (item.IsOffDatabase && !string.IsNullOrEmpty(item.PhotoFileName))
                TryDeleteFile(Path.Combine(FolderFor(id), item.PhotoFileName));
            record.OutgoingItems.RemoveAt(index);
            SaveDraft(id, record);
        }

        return RedirectToPage("TradeSession", new { id });
    }

    public async Task<IActionResult> OnPostFinalizeAsync(Guid id, string? note, decimal? receivedValue, IFormFile? receivedPhoto)
    {
        if (!TryLoadDraft(id, out var record))
            return RedirectToPage("TradeSession");

        if (record.OutgoingItems.Count == 0)
            return RedirectToPage("TradeSession", new { id }); // nothing to trade

        record.Note = note ?? "";
        record.ReceivedValue = receivedValue;
        record.ReceivedPhotoFileName = await SavePhotoAsync(id, receivedPhoto, "received")
            ?? record.ReceivedPhotoFileName;
        record.Status = "final";
        SaveDraft(id, record);
        TradeSessionCookie.Clear(HttpContext); // trade is done — no longer the "current" session

        if (TempData is not null)
            TempData["TradeMessage"] =
                "Trade finalized — it'll be applied next time the desktop app opens.";
        return RedirectToPage("Index");
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>Creates a fresh empty draft session (folder + trade.json) and marks it the current
    /// session (cookie). Returns its id.</summary>
    private Guid CreateDraft()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(FolderFor(id));
        SaveDraft(id, new TradeSessionRecord { SessionId = id, Status = "draft" });
        TradeSessionCookie.Set(HttpContext, id);
        return id;
    }

    private string FolderFor(Guid id) => Path.Combine(_dataPathService.TradesDirectory, id.ToString());

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

    /// <summary>Populates <see cref="TcgPlayerUrlByLotId"/> for the owned outgoing cards, resolving
    /// MTG cards' real TCGplayer product ids from scryfall.db like the card detail page does.</summary>
    private void BuildTcgPlayerLinks(TradeSessionRecord record)
    {
        var lotIds = record.OutgoingItems.Where(i => i.LotId is not null).Select(i => i.LotId!.Value).ToList();
        if (lotIds.Count == 0)
            return;

        using var db = _dbFactory.CreateDbContext();
        var products = db.Lots.AsNoTracking().Include(l => l.Product)
            .Where(l => lotIds.Contains(l.Id))
            .Select(l => new { LotId = l.Id, l.Product.Game, l.Product.GameCardId, l.Product.Name, l.Product.SetName, l.Product.Foil, l.Product.FoilType })
            .ToList();

        // Resolve MTG Scryfall ids → TCGplayer product ids in one batch.
        var mtgIds = products.Where(p => p.Game == CardGame.Mtg).Select(p => p.GameCardId);
        var resolved = ScryfallTcgIdResolver.Resolve(_scryfallFactory, mtgIds);

        foreach (var p in products)
        {
            int? productId = null;
            if (p.Game == CardGame.Mtg)
            {
                var etched = p.Foil && (p.FoilType?.Contains("Etched", StringComparison.OrdinalIgnoreCase) ?? false);
                productId = resolved.GetValueOrDefault(p.GameCardId).Pick(etched);
            }
            TcgPlayerUrlByLotId[p.LotId] = TcgPlayerLink.Build(p.Game, p.GameCardId, p.Name, p.SetName, productId);
        }
    }

    private void ExecuteSearch()
    {
        using var db = _dbFactory.CreateDbContext();

        var allCards = db.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.Product.Category == ProductCategory.Single && !l.IsTraded)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, l.Product.LastMarketPrice ?? 0m))
            .ToList();

        IEnumerable<CollectionCard> query = allCards;
        var terms = Q!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var term in terms)
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

        SearchResults = reps.Select(c => new OwnedResult
        {
            LotId = c.Id,
            Name = c.Name,
            SetName = c.SetName,
            SetCode = c.SetCode,
            Number = c.Number,
            IsFoil = c.IsFoil,
            Condition = c.Condition,
            MarketPrice = c.MarketPrice > 0m ? c.MarketPrice : null,
            ImageUrl = CardImageUrl.Resolve(c.ScanImagePath, c.ImageUri, _dataPathService.ScansDirectory),
        }).ToList();
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public record OwnedResult
    {
        public int LotId { get; init; }
        public string Name { get; init; } = "";
        public string SetName { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string Number { get; init; } = "";
        public bool IsFoil { get; init; }
        public string Condition { get; init; } = "";
        public decimal? MarketPrice { get; init; }
        public string? ImageUrl { get; init; }
    }
}
