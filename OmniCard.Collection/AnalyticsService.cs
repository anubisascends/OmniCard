using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Read-only reporting over the unified inventory store: current holdings valuation
/// (cost vs. live/manual market price) and realized P&amp;L from completed sales.
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbContextFactory;
    private readonly Dictionary<CardGame, ICardGameService> _gameServices;

    public AnalyticsService(IDbContextFactory<OmniCardDbContext> dbContextFactory, IEnumerable<ICardGameService> gameServices)
    {
        _dbContextFactory = dbContextFactory;
        _gameServices = gameServices.ToDictionary(s => s.Game);
    }

    public HoldingsValuation GetHoldings()
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        var lots = ctx.Lots.AsNoTracking().Include(l => l.Product).ToList();
        var containerNames = ctx.StorageContainers.AsNoTracking().ToDictionary(c => c.Id, c => c.Name);

        var marketByLotId = new Dictionary<int, decimal>(lots.Count);

        // Singles: batch live prices by (Game, IsFoil) via GetCurrentPrices, keyed by GameCardId.
        foreach (var gameGroup in lots.Where(l => l.Product.Category == ProductCategory.Single).GroupBy(l => l.Product.Game))
        {
            if (!_gameServices.TryGetValue(gameGroup.Key, out var gameService))
            {
                foreach (var lot in gameGroup)
                    marketByLotId[lot.Id] = 0m;
                continue;
            }

            foreach (var foilGroup in gameGroup.GroupBy(l => l.Product.Foil))
            {
                var gameCardIds = foilGroup
                    .Select(l => l.Product.GameCardId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();
                var prices = gameService.GetCurrentPrices(gameCardIds!, foilGroup.Key);

                foreach (var lot in foilGroup)
                {
                    var price = lot.Product.GameCardId is { Length: > 0 } id
                        ? prices.GetValueOrDefault(id)
                        : 0m;
                    marketByLotId[lot.Id] = price * lot.Quantity;
                }
            }
        }

        // Sealed/non-single products: market comes from the persisted eBay-derived
        // Product.LastMarketPrice (Task 1, Phase 3), refreshed via ISealedPriceUpdateService.
        foreach (var lot in lots.Where(l => l.Product.Category != ProductCategory.Single))
            marketByLotId[lot.Id] = (lot.Product.LastMarketPrice ?? 0m) * lot.Quantity;

        decimal CostOf(InventoryLot l) => l.Quantity * (l.UnitCost ?? 0m);
        decimal MarketOf(InventoryLot l) => marketByLotId.GetValueOrDefault(l.Id);

        var totalUnits = lots.Sum(l => l.Quantity);
        var totalCost = lots.Sum(CostOf);
        var totalMarket = lots.Sum(MarketOf);

        List<ValuationLine> Breakdown<TKey>(Func<InventoryLot, TKey> keySelector, Func<TKey, string> keyName) =>
            lots.GroupBy(keySelector)
                .Select(g => new ValuationLine(keyName(g.Key), g.Sum(l => l.Quantity), g.Sum(CostOf), g.Sum(MarketOf)))
                .ToList();

        var byGame = Breakdown(l => l.Product.Game, g => g.ToString());
        var byCategory = Breakdown(l => l.Product.Category, c => c.ToString());
        var byLocation = Breakdown(
            l => l.LocationId,
            locationId => locationId.HasValue && containerNames.TryGetValue(locationId.Value, out var name)
                ? name
                : "Unassigned");

        return new HoldingsValuation(totalUnits, totalCost, totalMarket, byGame, byCategory, byLocation);
    }

    public RealizedSummary GetRealized()
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        var movements = ctx.Movements.AsNoTracking().ToList();
        var products = ctx.Products.AsNoTracking().ToDictionary(p => p.Id);

        var soldLots = new List<(CardGame? Game, int Quantity, decimal Proceeds, decimal Cost)>();

        foreach (var lotGroup in movements.Where(m => m.LotId.HasValue).GroupBy(m => m.LotId!.Value))
        {
            var sells = lotGroup.Where(m => m.Type == MovementType.Sell).ToList();
            if (sells.Count == 0) continue; // unsold lot: excluded from realized

            var quantity = sells.Sum(m => m.Quantity);
            var proceeds = sells.Sum(m => m.Quantity * (m.UnitValue ?? 0m));
            var cost = lotGroup.Where(m => m.Type == MovementType.Acquire)
                .Sum(m => m.Quantity * (m.UnitValue ?? 0m));

            var productId = lotGroup.First().ProductId;
            var game = products.TryGetValue(productId, out var product) ? product.Game : (CardGame?)null;

            soldLots.Add((game, quantity, proceeds, cost));
        }

        var totalSold = soldLots.Sum(l => l.Quantity);
        var totalProceeds = soldLots.Sum(l => l.Proceeds);
        var totalCost = soldLots.Sum(l => l.Cost);

        var byGame = soldLots
            .GroupBy(l => l.Game?.ToString() ?? "Unknown")
            .Select(g => new RealizedLine(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.Proceeds), g.Sum(l => l.Cost)))
            .ToList();

        return new RealizedSummary(totalSold, totalProceeds, totalCost, byGame);
    }
}
