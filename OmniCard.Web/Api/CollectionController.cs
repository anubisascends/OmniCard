using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Collection search + single-card edit. Reads reuse the desktop's Scryfall-syntax filter
/// (<see cref="CollectionQueryBuilder"/>) against the read DB; writes go through
/// <see cref="WebBinderCardService"/> to the SQL Server unified store.
/// </summary>
public sealed class CollectionController(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ICardService cardService,
    WebBinderCardService binderCards,
    ITagService tags) : ApiControllerBase
{
    /// <summary>Search owned singles. <paramref name="q"/> accepts the Scryfall-style tokens
    /// (<c>set:</c>, <c>cn:</c>, <c>c:</c>, <c>r:</c>, <c>t:</c>, <c>tag:</c>, <c>is:foil</c>, …).</summary>
    [HttpGet]
    public ActionResult<PagedResult<CardDto>> Get(
        [FromQuery] string? game,
        [FromQuery] string? q,
        [FromQuery] int? containerId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);
        var gameFilter = LocationsController.ParseGame(game);

        using var ctx = dbFactory.CreateDbContext();
        var query = CollectionQueryBuilder
            .BuildFilteredQuery(ctx, q ?? "", gameFilter, containerId, filterPreset: null)
            .OrderBy(c => c.Name).ThenBy(c => c.SetCode).ThenBy(c => c.Number).ThenBy(c => c.Id);

        var total = query.Count();
        var cards = query.Skip(skip).Take(take).ToList();

        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        MarketPriceHydrator.Populate(cardService, cards);

        var items = cards.Select(DtoMapping.ToDto).ToList();
        return new PagedResult<CardDto>(total, skip, take, items);
    }

    /// <summary>One card with its tags, for the edit drawer.</summary>
    [HttpGet("{id:int}")]
    public ActionResult<CardDto> GetOne(int id)
    {
        var card = binderCards.GetCollectionCards([id]).FirstOrDefault();
        if (card is null) return NotFound();

        CardArtHydrator.HydrateMissingImageUris(cardService, [card]);
        MarketPriceHydrator.Populate(cardService, [card]);
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

    /// <summary>Move one or more cards to another location.</summary>
    [HttpPost("move")]
    public IActionResult Move([FromBody] MoveCardsRequest req)
    {
        if (req.CardIds.Count == 0) return BadRequest(new { error = "No cards specified." });
        binderCards.MoveCardsToContainer(req.CardIds, req.ContainerId, req.Section);
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
