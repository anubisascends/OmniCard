using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>
/// Placeholder <see cref="IEbayListingService"/> for the web app until eBay OAuth is wired up
/// server-side (migration Phase 5). <see cref="OrderService"/> only calls eBay on a best-effort
/// basis (auto-ending a listing when an order ships), so a no-op keeps order status changes working
/// without a live eBay connection. Returns "nothing happened" for every operation.
/// </summary>
public sealed class NoOpEbayListingService : IEbayListingService
{
    public Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options) => Task.FromResult(false);
    public Task<bool> CreateSealedListingAsync(Product product, int lotId, EbayListingOptions options) => Task.FromResult(false);
    public Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options) => Task.FromResult(false);
    public Task<bool> EndListingAsync(EbayListing listing) => Task.FromResult(false);
    public Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType) => Task.FromResult(new List<EbaySellerPolicy>());
}
