using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>
/// Paged collection search. Reuses the desktop's Scryfall-syntax filter
/// (<see cref="CollectionQueryBuilder"/>) against the read-only inventory DB, then hydrates missing
/// art and live market prices exactly like the existing web pages.
/// </summary>
public sealed class CollectionController(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ICardService cardService) : ApiControllerBase
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

        // Display-only hydration (mirrors the existing Razor pages): art from the game catalog,
        // live single prices from the read-only catalog DBs.
        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        MarketPriceHydrator.Populate(cardService, cards);

        var items = cards.Select(DtoMapping.ToDto).ToList();
        return new PagedResult<CardDto>(total, skip, take, items);
    }
}
