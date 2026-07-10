using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class CollectionQueryService(
    IDbContextFactory<CollectionDbContext> dbContextFactory,
    IStorageContainerService containerService,
    ICardService cardService) : ICollectionQueryService
{
    public async Task<List<LocationTileSummary>> GetLocationOverviewsAsync(CardGame? gameFilter = null)
    {
        var summaries = await Task.Run(() =>
        {
            var containers = containerService.GetAll();
            using var context = dbContextFactory.CreateDbContext();
            IQueryable<CollectionCard> cardsQuery = context.Cards.AsNoTracking();

            if (gameFilter.HasValue)
                cardsQuery = cardsQuery.Where(c => c.Game == gameFilter.Value);

            // SQL aggregate: count + purchase total per container
            var aggregates = cardsQuery
                .GroupBy(c => c.ContainerId)
                .Select(g => new
                {
                    ContainerId = g.Key,
                    Count = g.Count(),
                    PurchaseTotal = g.Sum(c => c.PurchasePrice ?? 0m)
                })
                .ToDictionary(a => a.ContainerId);

            // Lightweight projection for price data (no full card materialization)
            var priceData = cardsQuery
                .Select(c => new { c.ContainerId, c.GameCardId, c.IsFoil, c.Game })
                .ToList();

            // Batch price lookup grouped by (Game, IsFoil)
            var allPrices = new Dictionary<(string GameCardId, bool IsFoil), decimal>();
            foreach (var gameGroup in priceData.GroupBy(c => c.Game))
            {
                var gameService = cardService.GetGameService(gameGroup.Key);
                foreach (var foilGroup in gameGroup.GroupBy(c => c.IsFoil))
                {
                    var batchPrices = gameService.GetCurrentPrices(
                        foilGroup.Select(c => c.GameCardId).Distinct(), foilGroup.Key);
                    foreach (var kvp in batchPrices)
                        allPrices.TryAdd((kvp.Key, foilGroup.Key), kvp.Value);
                }
            }

            // Market totals per container
            var marketTotals = priceData
                .GroupBy(c => c.ContainerId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(c => allPrices.GetValueOrDefault((c.GameCardId, c.IsFoil))));

            // Cover images: only fetch the specific cards needed
            var coverCardIds = containers
                .Where(c => c.CoverCardId.HasValue)
                .Select(c => c.CoverCardId!.Value)
                .ToList();
            var coverImages = coverCardIds.Count > 0
                ? cardsQuery
                    .Where(c => coverCardIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.ImageUri })
                    .ToDictionary(c => c.Id, c => c.ImageUri)
                : new Dictionary<int, string>();
            // Fallback cover images: first card per container
            var fallbackCovers = cardsQuery
                .GroupBy(c => c.ContainerId)
                .Select(g => new { ContainerId = g.Key, ImageUri = g.Select(c => c.ImageUri).FirstOrDefault() })
                .ToDictionary(c => c.ContainerId, c => c.ImageUri);

            var summaries = new List<LocationTileSummary>();
            foreach (var container in containers)
            {
                var agg = aggregates.GetValueOrDefault(container.Id);
                var cardCount = agg?.Count ?? 0;
                var totalPurchase = agg?.PurchaseTotal ?? 0m;
                var totalMarket = marketTotals.GetValueOrDefault(container.Id);

                var delta = totalMarket - totalPurchase;
                var deltaPercent = totalPurchase > 0 ? (double)(delta / totalPurchase) * 100 : 0;

                // Resolve cover image
                string? coverUri = null;
                if (container.CoverCardId.HasValue)
                    coverImages.TryGetValue(container.CoverCardId.Value, out coverUri);
                coverUri ??= fallbackCovers.GetValueOrDefault(container.Id);

                summaries.Add(new LocationTileSummary
                {
                    Container = container,
                    CardCount = cardCount,
                    TotalMarketValue = totalMarket,
                    TotalPurchaseCost = totalPurchase,
                    PriceDelta = delta,
                    PriceDeltaPercent = deltaPercent,
                    CoverImageUri = coverUri,
                });
            }

            return summaries;
        });

        return summaries;
    }
}
