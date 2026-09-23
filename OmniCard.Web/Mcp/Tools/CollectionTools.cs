using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Helpers;
using OmniCard.Web.Services;

namespace OmniCard.Web.Mcp.Tools;

/// <summary>
/// Read-only MCP tools over the owned-card collection. These mirror the read paths of
/// <see cref="CollectionController"/> (same query builder, paging, and price/art hydration) so MCP
/// clients see exactly what the SPA does. No writes and no per-user permission checks — the endpoint
/// is loopback-gated (see <see cref="LoopbackOnly"/>).
/// </summary>
[McpServerToolType]
public sealed class CollectionTools(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ICardService cardService,
    WebBinderCardService binderCards,
    CardImageCacheService imageCache,
    ICollectionQueryService collectionQuery,
    IAnalyticsService analytics,
    IEnumerable<ICardGameService> gameServices)
{
    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _gameServices =
        gameServices.ToDictionary(s => s.Game);

    [McpServerTool(Name = "search_collection")]
    [Description("Search the owned-card collection using Scryfall-style query syntax (e.g. set:, cn:, " +
        "c: color, r: rarity, t: type, tag:, is:foil, cmc>=, pow>=). Returns matching cards with " +
        "market price, quantity, set, condition, and storage location. Results are paged.")]
    public PagedResult<CardDto> SearchCollection(
        [Description("Scryfall-style query. Empty returns everything. Example: 't:creature c:u r>=rare'.")]
        string? query = null,
        [Description("Optional game filter: mtg, pokemon, optcg, riftbound, yugioh, finalfantasy.")]
        string? game = null,
        [Description("Optional storage-location (container) id to restrict to.")]
        int? containerId = null,
        [Description("Rows to skip (paging). Default 0.")] int skip = 0,
        [Description("Rows to return. Default 50, max 500.")] int take = 50,
        [Description("When true, collapse identical printings into one row with summed quantity.")]
        bool stacked = false,
        [Description("Sort column: name, setCode, number, rarity, condition, isFoil, quantity, marketPrice, containerName.")]
        string? sort = null,
        [Description("Sort direction: asc or desc.")] string? dir = null)
    {
        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);
        var gameFilter = McpGameParsing.ParseGame(game);
        var sortKey = NormalizeSort(sort);
        var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        using var ctx = dbFactory.CreateDbContext();
        var q = CollectionQueryBuilder.BuildFilteredQuery(ctx, query ?? "", gameFilter, containerId, filterPreset: null, _gameServices);

        void HydratePrices(IReadOnlyCollection<CollectionCard> cs) => MarketPriceHydrator.Populate(cardService, cs);

        var (total, cards) = stacked
            ? CollectionController.PageStacked(q, skip, take, sortKey, desc, HydratePrices)
            : CollectionController.PageFlat(q, skip, take, sortKey, desc, HydratePrices);

        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        imageCache.PreferCached(cards);
        MarketPriceHydrator.Populate(cardService, cards); // idempotent; prices the page for non-price sorts

        var items = cards.Select(DtoMapping.ToDto).ToList();
        return new PagedResult<CardDto>(total, skip, take, items);
    }

    [McpServerTool(Name = "get_card")]
    [Description("Get one owned card (inventory lot) by its id, with market price and image.")]
    public CardDto? GetCard([Description("The lot/card id.")] int id)
    {
        var card = binderCards.GetCollectionCards([id]).FirstOrDefault();
        if (card is null) return null;

        CardArtHydrator.HydrateMissingImageUris(cardService, [card]);
        imageCache.PreferCached([card]);
        MarketPriceHydrator.Populate(cardService, [card]);
        using (var ctx = dbFactory.CreateDbContext())
            card.Quantity = ctx.Lots.Where(l => l.Id == id).Select(l => l.Quantity).FirstOrDefault();
        return DtoMapping.ToDto(card);
    }

    [McpServerTool(Name = "list_locations")]
    [Description("List storage locations (binders, boxes, deck boxes, bulk) with card counts and total value.")]
    public async Task<IReadOnlyList<LocationSummaryDto>> ListLocations(
        [Description("Optional game filter: mtg, pokemon, optcg, riftbound, yugioh, finalfantasy.")]
        string? game = null)
    {
        var overviews = await collectionQuery.GetLocationOverviewsAsync(McpGameParsing.ParseGame(game));
        return overviews.Select(DtoMapping.ToDto).ToList();
    }

    [McpServerTool(Name = "top_value_cards")]
    [Description("The highest market-value unlisted single cards across all games, ranked descending. " +
        "Excludes traded lots, sealed product, and cards already listed for sale.")]
    public IReadOnlyList<CardDto> TopValueCards(
        [Description("How many cards to return. Default 25, max 200.")] int take = 25)
    {
        take = Math.Clamp(take, 1, 200);
        var cards = collectionQuery.GetTopValueCards(take);
        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        imageCache.PreferCached(cards);
        MarketPriceHydrator.Populate(cardService, cards);
        return cards.Select(DtoMapping.ToDto).ToList();
    }

    [McpServerTool(Name = "collection_dashboard")]
    [Description("Portfolio summary: total holdings valuation (cost vs current market) and realized " +
        "profit/loss from completed sales.")]
    public DashboardDto CollectionDashboard() =>
        DtoMapping.ToDto(analytics.GetHoldings(), analytics.GetRealized());

    /// <summary>Mirrors <see cref="CollectionController"/>'s sort normalization; unknown falls back to name.</summary>
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
}
