using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>
/// Produces the serializable view-state for the web binder editor — the current two-page spread,
/// the pagination strip, and the Unplaced pool — mirroring the desktop <c>BinderViewModel</c>'s
/// spread math (page 1 stands alone on the right; pages 2.. pair up two per spread). Shared by the
/// initial server render (<c>BinderEdit.cshtml.cs</c>) and the API (<c>BinderEditController</c>) so
/// both compute identical layouts. Card DTOs are hydrated (art, live price, tags, TCGPlayer link)
/// the same way the read-only binder page does.
/// </summary>
public sealed class BinderStateBuilder
{
    private readonly IStorageContainerService _containers;
    private readonly WebBinderCardService _binderCards;
    private readonly ITagService _tags;
    private readonly ICardService _cardService;
    private readonly IDataPathService _dataPath;
    private readonly IDbContextFactory<ScryfallDbContext>? _scryfallFactory;

    public BinderStateBuilder(
        IStorageContainerService containers,
        WebBinderCardService binderCards,
        ITagService tags,
        ICardService cardService,
        IDataPathService dataPath,
        IDbContextFactory<ScryfallDbContext>? scryfallFactory = null)
    {
        _containers = containers;
        _binderCards = binderCards;
        _tags = tags;
        _cardService = cardService;
        _dataPath = dataPath;
        _scryfallFactory = scryfallFactory;
    }

    // --- Spread math (mirrors BinderViewModel) ---
    public static int TotalSpreads(int totalPages) => 1 + totalPages / 2;

    public static int ClampSpread(int spreadIndex, int totalPages)
        => Math.Clamp(spreadIndex, 0, Math.Max(0, TotalSpreads(totalPages) - 1));

    private static int? LeftPageNumber(int spreadIndex) => spreadIndex == 0 ? null : spreadIndex * 2;

    private static int? RightPageNumber(int spreadIndex, int totalPages)
        => spreadIndex == 0 ? 1 : (spreadIndex * 2 + 1 <= totalPages ? spreadIndex * 2 + 1 : null);

    public BinderStateDto BuildState(int containerId, int spreadIndex)
    {
        var layout = _containers.GetBinderLayout(containerId);
        var totalPages = Math.Max(1, layout.TotalPages);
        var slotsPerPage = layout.SlotsPerPage > 0 ? layout.SlotsPerPage : 9;
        var columns = layout.Columns > 0 ? layout.Columns : 3;
        var name = _containers.GetAll().FirstOrDefault(c => c.Id == containerId)?.Name ?? "";

        spreadIndex = ClampSpread(spreadIndex, totalPages);
        var leftPage = LeftPageNumber(spreadIndex);
        var rightPage = RightPageNumber(spreadIndex, totalPages);

        var leftCards = leftPage is int lp ? _containers.GetPlacedCardsOnPage(containerId, lp) : [];
        var rightCards = rightPage is int rp ? _containers.GetPlacedCardsOnPage(containerId, rp) : [];

        var all = leftCards.Concat(rightCards).ToList();
        Hydrate(all);
        var tcg = ResolveTcgIds(all);

        // Cards on the reverse side of each visible page's physical sheet, so an empty pocket can
        // show the back of the card behind it. These pages live on adjacent spreads, so they aren't
        // already loaded — fetch them (no hydration needed; we only need the game + occupied slot).
        var sheetLayout = BinderSheetLayout.Parse(string.Join(",", layout.SheetSides), totalPages);
        var leftReverse = ReverseCards(containerId, sheetLayout, leftPage);
        var rightReverse = ReverseCards(containerId, sheetLayout, rightPage);

        var leftSlots = BuildSlots(leftCards, leftReverse, slotsPerPage, columns, tcg);
        var rightSlots = BuildSlots(rightCards, rightReverse, slotsPerPage, columns, tcg);

        return new BinderStateDto(
            name, slotsPerPage, columns, totalPages,
            spreadIndex, TotalSpreads(totalPages),
            leftPage, rightPage, PageRangeLabel(leftPage, rightPage),
            leftSlots, rightSlots, BuildTabs(spreadIndex, totalPages));
    }

    public List<BinderCardDto> BuildUnplaced(int containerId, string? filter)
    {
        var preset = string.IsNullOrWhiteSpace(filter) ? null : new FilterPreset { Query = filter };
        var cards = _binderCards.GetUnplacedBinderCards(containerId, preset);
        Hydrate(cards);
        var tcg = ResolveTcgIds(cards);
        return cards.Select(c => Map(c, tcg)).ToList();
    }

    /// <summary>Full card DTOs for the given lot ids (for the card editor and post-action refresh).</summary>
    public List<BinderCardDto> BuildCards(IEnumerable<int> lotIds)
    {
        var cards = _binderCards.GetCollectionCards(lotIds);
        Hydrate(cards);
        var tcg = ResolveTcgIds(cards);
        return cards.Select(c => Map(c, tcg)).ToList();
    }

    private List<CollectionCard> ReverseCards(int containerId, BinderSheetLayout sheetLayout, int? page)
        => page is int p && sheetLayout.ReversePageOf(p) is int rev
            ? _containers.GetPlacedCardsOnPage(containerId, rev)
            : [];

    private List<BinderSlotDto> BuildSlots(
        List<CollectionCard> pageCards, List<CollectionCard> reverseCards,
        int slotsPerPage, int columns, IReadOnlyDictionary<string, ScryfallTcgIdResolver.Ids> tcg)
    {
        var slots = new List<BinderSlotDto>(slotsPerPage);
        for (var i = 0; i < slotsPerPage; i++)
        {
            var card = pageCards.FirstOrDefault(c => c.Slot == i);

            // Empty pocket: light up the reverse card's back (mirrored pocket on the sheet's back).
            int? reverseGame = null;
            if (card is null &&
                CardBackAssets.ReverseCardFor(i, columns, slotsPerPage, reverseCards) is { } behind)
            {
                reverseGame = (int)behind.Game;
            }

            slots.Add(new BinderSlotDto(i, card is null ? null : Map(card, tcg), reverseGame));
        }
        return slots;
    }

    private static List<SpreadTabDto> BuildTabs(int currentSpread, int totalPages)
    {
        var tabs = new List<SpreadTabDto>();
        for (var i = 0; i < TotalSpreads(totalPages); i++)
        {
            string label;
            if (i == 0) label = "1";
            else
            {
                var left = i * 2;
                var right = i * 2 + 1;
                label = right <= totalPages ? $"{left}–{right}" : $"{left}";
            }
            tabs.Add(new SpreadTabDto(i, label, i == currentSpread));
        }
        return tabs;
    }

    private static string PageRangeLabel(int? left, int? right)
        => left is not null
            ? (right is not null ? $"Pages {left}-{right}" : $"Page {left}")
            : $"Page {right}";

    private void Hydrate(IReadOnlyList<CollectionCard> cards)
    {
        if (cards.Count == 0) return;
        CardArtHydrator.HydrateMissingImageUris(_cardService, cards);
        MarketPriceHydrator.Populate(_cardService, cards);
        var tagsByLot = _tags.GetTagsByLots(cards.Select(c => c.Id));
        foreach (var c in cards)
            c.Tags = tagsByLot.GetValueOrDefault(c.Id, []);
    }

    private Dictionary<string, ScryfallTcgIdResolver.Ids> ResolveTcgIds(IReadOnlyList<CollectionCard> cards)
        => ScryfallTcgIdResolver.Resolve(
            _scryfallFactory,
            cards.Where(c => c.Game == CardGame.Mtg).Select(c => c.GameCardId));

    private BinderCardDto Map(CollectionCard c, IReadOnlyDictionary<string, ScryfallTcgIdResolver.Ids> tcg)
    {
        int? resolved = null;
        if (c.Game == CardGame.Mtg && tcg.TryGetValue(c.GameCardId, out var ids))
        {
            var etched = c.IsFoil && (c.FoilType?.Contains("Etched", StringComparison.OrdinalIgnoreCase) ?? false);
            resolved = ids.Pick(etched);
        }

        return new BinderCardDto(
            c.Id, (int)c.Game, c.Name, c.SetName, c.SetCode, c.Number, c.Rarity, c.Color, c.CardType,
            c.IsFoil, c.FoilType, c.Condition, c.PurchasePrice,
            c.MarketPrice > 0m ? c.MarketPrice.ToString("C") : null,
            c.MarketPrice,
            CardImageUrl.Resolve(c.ScanImagePath, c.ImageUri, _dataPath.ScansDirectory),
            c.IsTraded, c.Tags, c.Page, c.Slot, c.ContainerId,
            TcgPlayerLink.Build(c.Game, c.GameCardId, c.Name, c.SetName, resolved));
    }
}

public sealed record BinderCardDto(
    int Id, int Game, string Name, string SetName, string SetCode, string Number, string Rarity,
    string? Color, string? CardType, bool Foil, string? FoilType, string Condition,
    decimal? PurchasePrice, string? Price, decimal MarketPriceRaw, string? ImageUrl,
    bool IsTraded, List<string> Tags, int? Page, int? Slot, int? ContainerId, string TcgPlayerUrl);

/// <summary>One pocket in the editor spread. <see cref="ReverseGame"/> is set only for an empty
/// pocket whose mirrored pocket on the reverse side of the sheet is filled — the client shows that
/// game's card back (as an integer <see cref="CardGame"/>).</summary>
public sealed record BinderSlotDto(int SlotIndex, BinderCardDto? Card, int? ReverseGame = null);

public sealed record SpreadTabDto(int Index, string Label, bool IsCurrent);

public sealed record BinderStateDto(
    string ContainerName, int SlotsPerPage, int Columns, int TotalPages,
    int SpreadIndex, int TotalSpreads,
    int? LeftPageNumber, int? RightPageNumber, string PageRangeLabel,
    List<BinderSlotDto> LeftSlots, List<BinderSlotDto> RightSlots,
    List<SpreadTabDto> SpreadTabs);
