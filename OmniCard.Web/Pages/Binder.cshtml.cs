using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

/// <summary>
/// A flip-through binder view of one storage location: placed cards laid out into pages of
/// <see cref="StorageContainer.SlotsPerPage"/> slots, mirroring the desktop binder's page/slot
/// model. Read-only — the "Trade this card…" action links to the existing <c>/Trade</c> workflow.
/// </summary>
public class BinderModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly ICardService _cardService;
    private readonly IDataPathService _dataPathService;
    private readonly IDbContextFactory<ScryfallDbContext>? _scryfallFactory;

    /// <summary>MTG Scryfall-id → real TCGplayer ids, resolved once per page load. See
    /// <see cref="ScryfallTcgIdResolver"/>.</summary>
    private Dictionary<string, ScryfallTcgIdResolver.Ids> _mtgTcgIds = new();

    public BinderModel(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        ICardService cardService,
        IDataPathService dataPathService,
        IDbContextFactory<ScryfallDbContext>? scryfallFactory = null)
    {
        _dbFactory = dbFactory;
        _cardService = cardService;
        _dataPathService = dataPathService;
        _scryfallFactory = scryfallFactory;
    }

    public StorageContainer Container { get; set; } = null!;
    public int SlotsPerPage { get; private set; }
    public int Columns { get; private set; }
    public int TotalPages { get; private set; }
    public int PlacedCount { get; private set; }
    public List<BinderPage> Pages { get; private set; } = [];

    /// <summary>Per-lot card details for the client-side preview modal, keyed by lot id.</summary>
    public string DetailsJson { get; private set; } = "{}";

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var container = db.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == id);
        if (container is null)
            return NotFound();

        Container = container;
        SlotsPerPage = container.SlotsPerPage > 0 ? container.SlotsPerPage : 9;
        Columns = container.Columns > 0 ? container.Columns : 3;

        // Placed singles only — mirrors the desktop StorageContainerService.GetPlacedCardsOnPage query.
        var placed = db.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == id
                        && l.Page != null
                        && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, l.Product.LastMarketPrice ?? 0m))
            .ToList();

        PlacedCount = placed.Count;

        var tagsByLot = TagLookup.GetTagsByLots(db, placed.Select(c => c.Id));
        foreach (var c in placed)
            c.Tags = tagsByLot.GetValueOrDefault(c.Id, []);

        CardArtHydrator.HydrateMissingImageUris(_cardService, placed);
        // Singles don't persist a price on the row — look up the live catalog price for each slot.
        MarketPriceHydrator.Populate(_cardService, placed);

        // Resolve MTG cards' real TCGplayer ids once (single scryfall.db query for the whole page).
        _mtgTcgIds = ScryfallTcgIdResolver.Resolve(
            _scryfallFactory,
            placed.Where(c => c.Game == CardGame.Mtg).Select(c => c.GameCardId));

        var maxUsedPage = placed.Count > 0 ? placed.Max(c => c.Page!.Value) : 0;
        TotalPages = Math.Max(1, Math.Max(container.TotalPages, maxUsedPage));

        // Which physical sheet each logical page sits on — so an empty pocket can show the back of a
        // card sitting in the mirrored pocket on the reverse side of the same sheet.
        var sheetLayout = BinderSheetLayout.Parse(container.SheetSides, TotalPages);
        var placedByPage = placed.Where(c => c.Page is not null).ToLookup(c => c.Page!.Value);

        var details = new Dictionary<int, BinderCard>();
        Pages = new List<BinderPage>(TotalPages);
        for (var page = 1; page <= TotalPages; page++)
        {
            var cards = new BinderCard?[SlotsPerPage];
            foreach (var card in placed.Where(c => c.Page == page).OrderBy(c => c.Slot ?? int.MaxValue))
            {
                var vm = ToBinderCard(card);
                details[card.Id] = vm;

                // Prefer the card's real slot; if it's missing or already taken, drop it into the
                // first free slot so a mis-placed card is still visible rather than silently lost.
                var slot = card.Slot;
                if (slot is >= 0 && slot < SlotsPerPage && cards[slot.Value] is null)
                {
                    cards[slot.Value] = vm;
                }
                else
                {
                    var free = Array.IndexOf(cards, null);
                    if (free >= 0)
                        cards[free] = vm;
                }
            }

            var reverseCards = sheetLayout.ReversePageOf(page) is int rev ? placedByPage[rev].ToList() : [];
            var slots = new BinderSlot[SlotsPerPage];
            for (var i = 0; i < SlotsPerPage; i++)
            {
                string? reverseSlug = null;
                if (cards[i] is null &&
                    CardBackAssets.ReverseCardFor(i, Columns, SlotsPerPage, reverseCards) is { } behind)
                {
                    reverseSlug = CardBackAssets.Slug(behind.Game);
                }
                slots[i] = new BinderSlot(cards[i], reverseSlug);
            }

            Pages.Add(new BinderPage(page, slots));
        }

        DetailsJson = JsonSerializer.Serialize(details, JsonOptions);
        return Page();
    }

    private BinderCard ToBinderCard(CollectionCard c) => new(
        c.Id,
        c.Name,
        c.SetName,
        c.SetCode,
        c.Number,
        c.Rarity,
        c.Color,
        c.CardType,
        c.IsFoil,
        c.Condition,
        c.MarketPrice > 0m ? c.MarketPrice.ToString("C") : null,
        CardImageUrl.Resolve(c.ScanImagePath, c.ImageUri, _dataPathService.ScansDirectory),
        c.IsTraded,
        c.Tags,
        BuildTcgPlayerUrl(c));

    private string BuildTcgPlayerUrl(CollectionCard c)
    {
        int? resolved = null;
        if (c.Game == CardGame.Mtg && _mtgTcgIds.TryGetValue(c.GameCardId, out var ids))
        {
            var etched = c.IsFoil && (c.FoilType?.Contains("Etched", StringComparison.OrdinalIgnoreCase) ?? false);
            resolved = ids.Pick(etched);
        }

        return TcgPlayerLink.Build(c.Game, c.GameCardId, c.Name, c.SetName, resolved);
    }

    public string TypeDisplay => Container.ContainerType switch
    {
        ContainerType.Bulk => "Bulk",
        ContainerType.Binder => "Binder",
        ContainerType.Box => "Box",
        ContainerType.DeckBox => "Deck Box",
        ContainerType.DisplayCase => "Display Case",
        _ => Container.ContainerType.ToString(),
    };

    public record BinderPage(int Number, BinderSlot[] Slots);

    /// <summary>One pocket in a rendered page: the card in it (null = empty), and — for an empty
    /// pocket whose mirrored pocket on the reverse side of the sheet is filled — the slug of that
    /// game's card back to show behind it (<c>/img/card-back-{slug}.png</c>).</summary>
    public record BinderSlot(BinderCard? Card, string? ReverseBackSlug);

    public record BinderCard(
        int Id,
        string Name,
        string SetName,
        string SetCode,
        string Number,
        string Rarity,
        string? Color,
        string? CardType,
        bool Foil,
        string Condition,
        string? Price,
        string? ImageUrl,
        bool IsTraded,
        List<string> Tags,
        string TcgPlayerUrl);
}
