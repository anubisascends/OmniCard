using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IEbayListingService
{
    Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options);
    Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options);
    Task<bool> EndListingAsync(EbayListing listing);
    Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType);
}
