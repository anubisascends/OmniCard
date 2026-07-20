using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Task 1 (Phase 3): prices sealed products (Category != Single) via eBay's median
/// sold/listed price, persisting the result on <see cref="Product.LastMarketPrice"/> so
/// <see cref="AnalyticsService"/> and <see cref="InventoryService"/> can value sealed
/// holdings without a live query on every read.
/// </summary>
public class SealedPriceUpdateService : ISealedPriceUpdateService
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbContextFactory;
    private readonly IEbayCatalogService _ebayCatalog;
    private readonly IEbayAuthService _ebayAuth;
    private readonly ILogger<SealedPriceUpdateService> _logger;

    public SealedPriceUpdateService(
        IDbContextFactory<OmniCardDbContext> dbContextFactory,
        IEbayCatalogService ebayCatalog,
        IEbayAuthService ebayAuth,
        ILogger<SealedPriceUpdateService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _ebayCatalog = ebayCatalog;
        _ebayAuth = ebayAuth;
        _logger = logger;
    }

    public async Task<int> RefreshSealedPricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        if (!_ebayAuth.IsConnected)
        {
            progress?.Report(new PriceUpdateProgress(default, null, 0, 0, "Connect eBay to price sealed products"));
            return 0;
        }

        using var ctx = _dbContextFactory.CreateDbContext();
        var sealedProducts = ctx.Products.Where(p => p.Category != ProductCategory.Single).ToList();

        var total = sealedProducts.Count;
        var updated = 0;

        for (var i = 0; i < sealedProducts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var product = sealedProducts[i];

            progress?.Report(new PriceUpdateProgress(product.Game, product.SetCode, i, total, $"Pricing {product.Name}..."));

            try
            {
                var query = !string.IsNullOrWhiteSpace(product.Upc)
                    ? product.Upc!
                    : $"{product.Name} {product.SetCode}".Trim();

                var marketPrice = await _ebayCatalog.GetMarketPriceAsync(query, "", false);
                if (marketPrice is { SampleCount: > 0 })
                {
                    product.LastMarketPrice = marketPrice.MedianPrice;
                    product.PriceUpdatedAt = DateTime.UtcNow;
                    updated++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Best-effort per product: one bad eBay response/query must not abort the batch.
                _logger.LogWarning(ex, "Sealed price refresh failed for product {ProductId} ({Name})", product.Id, product.Name);
            }
        }

        ctx.SaveChanges();
        progress?.Report(new PriceUpdateProgress(default, null, total, total, $"Updated {updated} of {total} sealed product price(s)"));
        return updated;
    }
}
