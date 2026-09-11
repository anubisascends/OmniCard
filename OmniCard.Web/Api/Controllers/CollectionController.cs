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
    /// (<c>set:</c>, <c>cn:</c>, <c>c:</c>, <c>r:</c>, <c>t:</c>, <c>tag:</c>, <c>is:foil</c>, …).</summary>
    [HttpGet]
    public ActionResult<PagedResult<CardDto>> Get(
        [FromQuery] string? game,
        [FromQuery] string? q,
        [FromQuery] int? containerId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        [FromQuery] bool stacked = false)
    {
        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);
        var gameFilter = LocationsController.ParseGame(game);

        using var ctx = dbFactory.CreateDbContext();
        var query = CollectionQueryBuilder.BuildFilteredQuery(ctx, q ?? "", gameFilter, containerId, filterPreset: null, _gameServices);

        var (total, cards) = stacked ? PageStacked(query, skip, take) : PageFlat(query, skip, take);

        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        imageCache.PreferCached(cards);
        MarketPriceHydrator.Populate(cardService, cards);
        AnnotateListingStatus(cards);
        PopulateTags(cards);

        var items = cards.Select(DtoMapping.ToDto).ToList();
        return new PagedResult<CardDto>(total, skip, take, items);
    }

    /// <summary>One row per lot (unstacked).</summary>
    private static (int Total, List<CollectionCard> Cards) PageFlat(IQueryable<CollectionCard> query, int skip, int take)
    {
        var ordered = query.OrderBy(c => c.Name).ThenBy(c => c.SetCode).ThenBy(c => c.Number).ThenBy(c => c.Id);
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
    internal static (int Total, List<CollectionCard> Cards) PageStacked(IQueryable<CollectionCard> query, int skip, int take)
    {
        // Distinct printing identity. Ordered so pagination is stable across requests.
        var keys = query
            .Select(c => new { c.Name, c.SetCode, c.Number, c.IsFoil })
            .Distinct();
        var total = keys.Count();
        var pageKeys = keys
            .OrderBy(k => k.Name).ThenBy(k => k.SetCode).ThenBy(k => k.Number).ThenBy(k => k.IsFoil)
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
