using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

/// <summary>Test double for <see cref="IEbayListingService"/>. Defaults to success; override
/// <see cref="EndListingAsync"/> (or use a subclass) to record/vary behavior.</summary>
public class FakeEbayListingService : IEbayListingService
{
    public Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options) => Task.FromResult(true);
    public Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options) => Task.FromResult(true);
    public virtual Task<bool> EndListingAsync(EbayListing listing) => Task.FromResult(true);
    public Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType) => Task.FromResult(new List<EbaySellerPolicy>());
}
