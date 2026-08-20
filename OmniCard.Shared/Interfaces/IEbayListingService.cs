using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IEbayListingService
{
    Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options);

    /// <summary>Lists a sealed inventory product (a booster box/pack/case/etc.) on eBay. Unlike
    /// <see cref="CreateListingAsync"/>, this builds a NEW-condition inventory item without the
    /// trading-card-single grade descriptor. <paramref name="lotId"/> is the <see cref="InventoryLot"/>
    /// id that keys the SKU and bridges the result into the generic listing/pick-list system.</summary>
    Task<bool> CreateSealedListingAsync(Product product, int lotId, EbayListingOptions options);

    Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options);
    Task<bool> EndListingAsync(EbayListing listing);
    Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType);
}
