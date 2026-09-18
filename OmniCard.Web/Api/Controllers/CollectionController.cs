using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Web.Services;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;
using OmniCard.Web.Helpers;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Collection search + single-card edit. Reads reuse the desktop's Scryfall-syntax filter
/// (<see cref="CollectionQueryBuilder"/>) against the read DB; writes go through
/// <see cref="WebBinderCardService"/> to the SQL Server unified store.
/// </summary>
public sealed class CollectionController(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ICardService cardService,
    WebBinderCardService binderCards,
    ITagService tags,
    CardImageCacheService imageCache,
    IListingService listings,
    IEnumerable<ICardGameService> gameServices) : ApiControllerBase
{
    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _gameServices = gameServices.ToDictionary(s => s.Game);

    /// <summary>Stamps each row's <see cref="CollectionCard.ListingStatus"/> from its active listings so
    /// the client can disable "List for sale" on cards already on the market. A stacked row is only
    /// marked when *every* underlying lot is listed — a partially-listed stack stays listable (the
    /// backend skips the already-listed copies when the remainder is listed).</summary>
    private void AnnotateListingStatus(IReadOnlyList<CollectionCard> cards)
    {
        if (cards.Count == 0) return;
        var lotIdsFor = (CollectionCard c) => c.StackedIds is { Count: > 0 } ids ? ids : [c.Id];

        var allLotIds = cards.SelectMany(lotIdsFor).Distinct().ToList();
        var statusByLot = listings.GetActiveListingStatusByLot(allLotIds);
        if (statusByLot.Count == 0) return;

        foreach (var c in cards)
        {
            var statuses = lotIdsFor(c)
                .Select(id => statusByLot.TryGetValue(id, out var s) ? (ListingStatus?)s : null)
                .ToList();
            if (statuses.All(s => s.HasValue))
                c.ListingStatus = statuses.Max(); // Picked outranks Listed (higher enum value)
        }
    }

    /// <summary>Search owned singles. <paramref name="q"/> accepts the Scryfall-style tokens
    /// (<c>set:</c>, <c>cn:</c>, <c>c:</c>, <c>r:</c>, <c>t:</c>, <c>tag:</c>, <c>is:foil</c>, …).
    /// <paramref name="sort"/> is a column key (name/setCode/number/rarity/condition/isFoil/quantity/
    /// marketPrice/containerName) and <paramref name="dir"/> is asc|desc. Sorting runs server-side over
    /// the *whole* filtered result set — never just the current page — so ordering by market price (or
    /// any column) returns the true top-of-collection, not the top of page 1.</summary>
    [HttpGet]
    public ActionResult<PagedResult<CardDto>> Get(
        [FromQuery] string? game,
        [FromQuery] string? q,
        [FromQuery] int? containerId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        [FromQuery] bool stacked = false,
        [FromQuery] string? sort = null,
        [FromQuery] string? dir = null)
    {
        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);
        var gameFilter = LocationsController.ParseGame(game);
        var sortKey = NormalizeSort(sort);
        var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        using var ctx = dbFactory.CreateDbContext();
        var query = CollectionQueryBuilder.BuildFilteredQuery(ctx, q ?? "", gameFilter, containerId, filterPreset: null, _gameServices);

        // Market-price sorting needs prices in hand *before* paging, so the paging helpers hydrate the
        // full set through this delegate. All other sorts page in the DB and only the page is priced below.
        void HydratePrices(IReadOnlyCollection<CollectionCard> cs) => MarketPriceHydrator.Populate(cardService, cs);

        var (total, cards) = stacked
            ? PageStacked(query, skip, take, sortKey, desc, HydratePrices)
            : PageFlat(query, skip, take, sortKey, desc, HydratePrices);

        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        imageCache.PreferCached(cards);
        MarketPriceHydrator.Populate(cardService, cards); // idempotent; prices the page for non-price sorts
        AnnotateListingStatus(cards);
        PopulateTags(cards);

        var items = cards.Select(DtoMapping.ToDto).ToList();
        return new PagedResult<CardDto>(total, skip, take, items);
    }

    /// <summary>Sortable column keys, lower-cased to match the client's DataGrid field names. Anything
    /// unknown (or null) falls back to name.</summary>
    private static string NormalizeSort(string? sort) => (sort ?? "").Trim().ToLowerInvariant() switch
    {
        "setcode" => "setcode",
        "number" => "number",
        "rarity" => "rarity",
        "condition" => "condition",
        "isfoil" => "isfoil",
        "quantity" => "quantity",
        "marketprice" => "marketprice",
        "containername" => "containername",
        _ => "name",
    };

    private static IOrderedQueryable<T> Dir<T, TKey>(IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, TKey>> key, bool desc)
        => desc ? q.OrderByDescending(key) : q.OrderBy(key);

    /// <summary>Global secondary ordering (in memory) so equal primary keys page deterministically.</summary>
    private static List<CollectionCard> SortRows(IEnumerable<CollectionCard> rows, string sort, bool desc)
    {
        Func<CollectionCard, IComparable?> key = sort switch
        {
            "marketprice" => c => c.MarketPrice,
            "quantity" => c => c.Quantity,
            "setcode" => c => c.SetCode,
            "number" => c => c.Number,
            "rarity" => c => c.Rarity,
            "condition" => c => c.Condition,
            "isfoil" => c => c.IsFoil,
            "containername" => c => c.Container?.Name,
            _ => c => c.Name,
        };
        var primary = desc ? rows.OrderByDescending(key) : rows.OrderBy(key);
        return primary
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.SetCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Number, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id)
            .ToList();
    }

    /// <summary>One row per lot (unstacked).</summary>
    internal static (int Total, List<CollectionCard> Cards) PageFlat(
        IQueryable<CollectionCard> query, int skip, int take,
        string sort = "name", bool desc = false, Action<IReadOnlyCollection<CollectionCard>>? hydratePrices = null)
    {
        // Market price isn't a DB column (looked up live per game catalog), so it can't be ordered in
        // SQL. Materialize the whole filtered set, price it, then sort + page in memory.
        if (sort == "marketprice")
        {
            var all = query.ToList();
            foreach (var c in all)
                c.StackedIds = [c.Id];
            hydratePrices?.Invoke(all);
            var sorted = SortRows(all, sort, desc);
            return (all.Count, sorted.Skip(skip).Take(take).ToList());
        }

        // Everything else is a real projected column — order and page in the DB.
        IOrderedQueryable<CollectionCard> keyed = sort switch
        {
            "setcode" => Dir(query, c => c.SetCode, desc),
            "number" => Dir(query, c => c.Number, desc),
            "rarity" => Dir(query, c => c.Rarity, desc),
            "condition" => Dir(query, c => c.Condition, desc),
            "isfoil" => Dir(query, c => c.IsFoil, desc),
            "quantity" => Dir(query, c => c.Quantity, desc),
            "containername" => Dir(query, c => c.Container != null ? c.Container.Name : null, desc),
            _ => Dir(query, c => c.Name, desc),
        };
        var ordered = keyed.ThenBy(c => c.Name).ThenBy(c => c.SetCode).ThenBy(c => c.Number).ThenBy(c => c.Id);
        var total = ordered.Count();
        var cards = ordered.Skip(skip).Take(take).ToList();
        foreach (var c in cards)
            c.StackedIds = [c.Id]; // uniform bulk-op shape with stacked rows
        return (total, cards);
    }

    /// <summary>One row per unique printing (stacked), quantities summed. A "printing" is identified
    /// by name + set + collector number + foil — only genuinely identical cards collapse into one
    /// stack (different sets/printings/foils stay separate). Paginates the distinct printing keys first
    /// (cheap), then loads just that page's lots to build the representative rows — so it scales to the
    /// whole collection without materializing everything.</summary>
    internal static (int Total, List<CollectionCard> Cards) PageStacked(
        IQueryable<CollectionCard> query, int skip, int take,
        string sort = "name", bool desc = false, Action<IReadOnlyCollection<CollectionCard>>? hydratePrices = null)
    {
        // Sorts on a field that is part of the printing identity can page cheaply off the distinct keys
        // (below). Sorts on a *computed* field — market price, summed quantity, or the representative's
        // rarity/condition/location — must see every stack, so materialize the whole set, build the
        // stacks, then sort + page in memory.
        if (sort is not ("name" or "setcode" or "number" or "isfoil"))
            return PageStackedComputed(query, skip, take, sort, desc, hydratePrices);

        // Distinct printing identity. Ordered so pagination is stable across requests.
        var keys = query
            .Select(c => new { c.Name, c.SetCode, c.Number, c.IsFoil })
            .Distinct();
        var total = keys.Count();
        IOrderedQueryable<T> KeyDir<T, TKey>(IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, TKey>> k)
            => desc ? q.OrderByDescending(k) : q.OrderBy(k);
        var orderedKeys = sort switch
        {
            "setcode" => KeyDir(keys, k => k.SetCode),
            "number" => KeyDir(keys, k => k.Number),
            "isfoil" => KeyDir(keys, k => k.IsFoil),
            _ => KeyDir(keys, k => k.Name),
        };
        var pageKeys = orderedKeys
            .ThenBy(k => k.Name).ThenBy(k => k.SetCode).ThenBy(k => k.Number).ThenBy(k => k.IsFoil)
            .Skip(skip).Take(take)
            .ToList();
        if (pageKeys.Count == 0)
            return (total, []);

        // Load lots for the page's names (Contains on a scalar is EF-translatable), then narrow to the
        // exact page keys in memory — composite-key Contains doesn't translate to SQL.
        var pageNames = pageKeys.Select(k => k.Name).Distinct().ToList();
        var pageKeySet = pageKeys.Select(k => (k.Name, k.SetCode, k.Number, k.IsFoil)).ToHashSet();
        var members = query.Where(c => pageNames.Contains(c.Name)).ToList();
        var rows = members
            .Where(c => pageKeySet.Contains((c.Name, c.SetCode, c.Number, c.IsFoil)))
            .GroupBy(c => (c.Name, c.SetCode, c.Number, c.IsFoil))
            .Select(g =>
            {
                var rep = g.OrderBy(c => c.Id).First();
                rep.Quantity = g.Sum(c => c.Quantity);
                rep.StackedIds = g.Select(c => c.Id).ToList();
                return rep;
            })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.SetCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (total, rows);
    }

    /// <summary>Stacked paging for a sort that isn't part of the printing identity (market price, summed
    /// quantity, or the representative's rarity/condition/location). These can't be resolved from the
    /// distinct keys alone, so the whole filtered set is materialized, collapsed into stacks, priced
    /// (for a price sort), then globally sorted and paged.</summary>
    private static (int Total, List<CollectionCard> Cards) PageStackedComputed(
        IQueryable<CollectionCard> query, int skip, int take,
        string sort, bool desc, Action<IReadOnlyCollection<CollectionCard>>? hydratePrices)
    {
        var rows = query.ToList()
            .GroupBy(c => (c.Name, c.SetCode, c.Number, c.IsFoil))
            .Select(g =>
            {
                var rep = g.OrderBy(c => c.Id).First();
                rep.Quantity = g.Sum(c => c.Quantity);
                rep.StackedIds = g.Select(c => c.Id).ToList();
                return rep;
            })
            .ToList();
        var total = rows.Count;
        if (sort == "marketprice")
            hydratePrices?.Invoke(rows);
        var sorted = SortRows(rows, sort, desc);
        return (total, sorted.Skip(skip).Take(take).ToList());
    }

    /// <summary>Fills each row's tags in one batch query (union of tags across a stacked row's lots),
    /// so list consumers — e.g. the deck stack view's "Commander" group — can see per-lot tags without
    /// a request per card. Rows with no tags stay empty.</summary>
    private void PopulateTags(List<CollectionCard> cards)
    {
        if (cards.Count == 0) return;
        static List<int> LotsOf(CollectionCard c) => c.StackedIds is { Count: > 0 } s ? s : [c.Id];

        var tagsByLot = tags.GetTagsByLots(cards.SelectMany(LotsOf).Distinct());
        if (tagsByLot.Count == 0) return;

        foreach (var card in cards)
        {
            var union = LotsOf(card)
                .SelectMany(id => tagsByLot.TryGetValue(id, out var t) ? t : [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (union.Count > 0)
                card.Tags = union;
        }
    }

    /// <summary>One card with its tags, for the edit drawer.</summary>
    [HttpGet("{id:int}")]
    public ActionResult<CardDto> GetOne(int id)
    {
        var card = binderCards.GetCollectionCards([id]).FirstOrDefault();
        if (card is null) return NotFound();

        CardArtHydrator.HydrateMissingImageUris(cardService, [card]);
        imageCache.PreferCached([card]);
        MarketPriceHydrator.Populate(cardService, [card]);
        AnnotateListingStatus([card]);
        card.Tags = tags.GetTagsForLot(id);
        // CollectionCardMapper doesn't carry Quantity; read it straight from the lot for the editor.
        using (var ctx = dbFactory.CreateDbContext())
            card.Quantity = ctx.Lots.Where(l => l.Id == id).Select(l => l.Quantity).FirstOrDefault();
        return DtoMapping.ToDto(card);
    }

    /// <summary>Edit a card's condition / foil / quantity / cost.</summary>
    [HttpPut("{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdateCardRequest req)
    {
        var card = binderCards.GetCollectionCards([id]).FirstOrDefault();
        if (card is null) return NotFound();

        card.Condition = req.Condition;
        card.IsFoil = req.IsFoil;
        card.FoilType = req.FoilType;
        card.PurchasePrice = req.PurchasePrice;
        card.Note = req.Note;
        binderCards.UpdateCollectionCard(card);
        // Quantity isn't part of the identity/attribute copy above — persist it directly.
        binderCards.SetQuantity(id, req.Quantity);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        binderCards.DeleteCollectionCard(id);
        return NoContent();
    }

    /// <summary>Move one or more cards to another location. 409 if the target is a game-locked deck
    /// box and any card belongs to a different game.</summary>
    [HttpPost("move")]
    public IActionResult Move([FromBody] MoveCardsRequest req)
    {
        if (req.CardIds.Count == 0) return BadRequest(new { error = "No cards specified." });
        try
        {
            binderCards.MoveCardsToContainer(req.CardIds, req.ContainerId, req.Section);
        }
        catch (DeckBoxGameMismatchException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        return NoContent();
    }

    /// <summary>Bulk-edit the selected cards. Only the ticked fields (SetX flags) are applied; each
    /// otherwise keeps its per-card value. Condition/foil/price/note go through one batched write,
    /// quantity through the bulk quantity setter, and tags add-union or replace per TagsMode.</summary>
    [HttpPost("bulk-update")]
    public IActionResult BulkUpdate([FromBody] BulkUpdateCardsRequest req)
    {
        if (req.CardIds.Count == 0)
            return BadRequest(new { error = "No cards specified." });
        var ids = req.CardIds;

        // Fields carried by the identity/attribute copy path — set in one pass over the lots.
        if (req.SetCondition || req.SetFoil || req.SetPurchasePrice || req.SetNote)
        {
            binderCards.BulkUpdateField(ids, c =>
            {
                if (req.SetCondition && !string.IsNullOrWhiteSpace(req.Condition))
                    c.Condition = req.Condition;
                if (req.SetFoil)
                    c.IsFoil = req.IsFoil;
                if (req.SetPurchasePrice)
                    c.PurchasePrice = req.PurchasePrice;
                if (req.SetNote)
                    c.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
            });
        }

        if (req.SetQuantity)
            binderCards.SetQuantity(ids, Math.Max(1, req.Quantity));

        if (req.SetTags)
        {
            var tagNames = req.Tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.Equals(req.TagsMode, "replace", StringComparison.OrdinalIgnoreCase))
                foreach (var id in ids) tags.SetTagsForLot(id, tagNames);
            else
                foreach (var name in tagNames) tags.AddTagToLots(ids, name);
        }

        return NoContent();
    }

    [HttpGet("{id:int}/tags")]
    public ActionResult<IReadOnlyList<string>> GetTags(int id) => tags.GetTagsForLot(id);

    [HttpPut("{id:int}/tags")]
    public IActionResult SetTags(int id, [FromBody] SetTagsRequest req)
    {
        tags.SetTagsForLot(id, req.Tags);
        return NoContent();
    }
}
