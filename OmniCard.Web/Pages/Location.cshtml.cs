using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class LocationModel : PageModel
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly ICardService _cardService;
    private readonly IDataPathService _dataPathService;

    public LocationModel(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        ICardService cardService,
        IDataPathService dataPathService)
    {
        _dbFactory = dbFactory;
        _cardService = cardService;
        _dataPathService = dataPathService;
    }

    public StorageContainer Container { get; set; } = null!;
    public int CardCount { get; set; }
    public List<SetSummary> Sets { get; set; } = [];
    public List<StackedCard> Cards { get; set; } = [];

    /// <summary>Deck-box grouping axis from the <c>?group=</c> query param: "none", "type", or "mv".</summary>
    public string GroupMode { get; set; } = "none";

    /// <summary>Grouped stacks, populated only for a deck box with an active <see cref="GroupMode"/>.</summary>
    public List<DeckColumn> Groups { get; set; } = [];

    public bool IsDeckBox => Container.ContainerType == ContainerType.DeckBox;

    public IActionResult OnGet(int id, string? group = null)
    {
        using var db = _dbFactory.CreateDbContext();

        var container = db.StorageContainers
            .AsNoTracking()
            .FirstOrDefault(c => c.Id == id);

        if (container is null)
            return NotFound();

        Container = container;

        var rawCards = db.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == id && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, l.Product.LastMarketPrice ?? 0m))
            .OrderBy(c => c.Name)
            .ToList();

        CardCount = rawCards.Count;

        var tagsByLot = TagLookup.GetTagsByLots(db, rawCards.Select(c => c.Id));
        foreach (var c in rawCards)
            c.Tags = tagsByLot.GetValueOrDefault(c.Id, []);

        // Fill in catalog art for cards with no stored ImageUri, same as the desktop collection list.
        CardArtHydrator.HydrateMissingImageUris(_cardService, rawCards);
        // Singles don't persist a price on the row — look up the live catalog price for each card.
        MarketPriceHydrator.Populate(_cardService, rawCards);

        // For a grouped deck-box view, pull each card's type line / mana value from the catalog so
        // the shared classifier can bucket them (cheap: a deck box holds ~100 cards).
        var wantGroups = container.ContainerType == ContainerType.DeckBox && group is "type" or "mv";
        if (wantGroups) HydrateGroupingFields(rawCards);

        Cards = rawCards
            .GroupBy(c => new { c.Name, c.SetCode })
            .Select(g =>
            {
                // Representative copy (lowest Id) supplies set name, art, and price.
                var rep = g.OrderBy(c => c.Id).First();
                return new StackedCard(
                    rep.Id,
                    rep.Name,
                    rep.SetName,
                    rep.SetCode,
                    rep.Number,
                    rep.Rarity,
                    rep.Color,
                    g.Count(),
                    CardImageUrl.Resolve(rep.ScanImagePath, rep.ImageUri, _dataPathService.ScansDirectory),
                    rep.MarketPrice > 0m ? rep.MarketPrice : null,
                    rep.Condition,
                    rep.IsFoil,
                    rep.Tags,
                    rep.TypeLine,
                    rep.ManaValue);
            })
            .OrderBy(c => c.Name)
            .ToList();

        Sets = rawCards
            .GroupBy(c => new { c.SetCode, c.SetName })
            .Select(g => new SetSummary
            {
                SetCode = g.Key.SetCode,
                SetName = g.Key.SetName,
                Count = g.Count(),
            })
            .OrderBy(s => s.SetName)
            .ToList();

        BuildGroups(group);

        return Page();
    }

    /// <summary>Maps the query param to a grouping axis and, for a deck box, hydrates each stack's
    /// catalog type line / mana value and classifies it into ordered columns — mirroring the desktop
    /// deck view via the shared <see cref="DeckCardClassifier"/>.</summary>
    private void BuildGroups(string? group)
    {
        var axis = group?.ToLowerInvariant() switch
        {
            "type" => DeckGroupAxis.Type,
            "mv" => DeckGroupAxis.ManaValue,
            _ => DeckGroupAxis.None,
        };

        GroupMode = axis switch
        {
            DeckGroupAxis.Type => "type",
            DeckGroupAxis.ManaValue => "mv",
            _ => "none",
        };

        if (!IsDeckBox || axis == DeckGroupAxis.None || Cards.Count == 0) return;

        Groups = Cards
            .Select(c => (Card: c, Group: DeckCardClassifier.Classify(axis, c.TypeLine, c.ManaValue, c.Tags)))
            .GroupBy(x => x.Group.Key)
            .Select(g => (
                Header: g.Key,
                Order: g.Min(x => x.Group.SortOrder),
                Cards: g.Select(x => x.Card).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(g => g.Order)
            .ThenBy(g => g.Header, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeckColumn(g.Header, g.Cards.Sum(c => c.Quantity), g.Cards))
            .ToList();
    }

    /// <summary>Looks up each card's catalog type line and converted mana value (MTG only) so the
    /// grouped deck view can classify them. Non-MTG cards fall back to no type line / mana value.</summary>
    private void HydrateGroupingFields(IEnumerable<CollectionCard> cards)
    {
        foreach (var c in cards)
        {
            try
            {
                if (_cardService.GetGameService(c.Game).FindCardById(c.GameCardId) is Card mtg)
                {
                    c.TypeLine = mtg.TypeLine;
                    c.ManaValue = mtg.Cmc;
                }
                else
                {
                    c.TypeLine = c.CardType;
                }
            }
            catch
            {
                c.TypeLine = c.CardType;
            }
        }
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

    public record SetSummary
    {
        public string SetCode { get; init; } = "";
        public string SetName { get; init; } = "";
        public int Count { get; init; }
    }

    public record StackedCard(
        int Id,
        string Name,
        string SetName,
        string SetCode,
        string Number,
        string Rarity,
        string? Color,
        int Quantity,
        string? ImageUrl,
        decimal? MarketPrice,
        string Condition,
        bool IsFoil,
        List<string> Tags,
        string? TypeLine,
        double? ManaValue);

    public record DeckColumn(string Header, int Count, List<StackedCard> Cards);
}
