using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>
/// Task 1 (Phase 3): automated sealed-product pricing via eBay median sold/listed price.
/// Singles are priced live elsewhere (<see cref="ICardGameService"/>); sealed products
/// (Category != Single) have no such source, so this service queries eBay on demand and
/// persists the result on <see cref="Product.LastMarketPrice"/>.
/// </summary>
public interface ISealedPriceUpdateService
{
    /// <summary>
    /// Refreshes <see cref="Product.LastMarketPrice"/> for every sealed product, best-effort
    /// per product. Returns 0 (no-op) if eBay is not connected. Returns the count of products
    /// actually updated (skipping any with zero eBay sample results).
    /// </summary>
    Task<int> RefreshSealedPricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default);
}
