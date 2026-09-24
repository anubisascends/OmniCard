using OmniCard.Shared.Collection;
using OmniCard.Shared.Inventory;

namespace OmniCard.Shared.Ebay;

public interface IEbayListingService
{
    Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options);

    /// <summary>Lists a sealed inventory product (a booster box/pack/case/etc.) on eBay. Unlike
    /// <see cref="CreateListingAsync"/>, this builds a NEW-condition inventory item without the
    /// trading-card-single grade descriptor. <paramref name="lotId"/> is the <see cref="InventoryLot"/>
    /// id that keys the SKU and bridges the result into the generic listing/pick-list system.</summary>
    Task<bool> CreateSealedListingAsync(Product product, int lotId, EbayListingOptions options);

    Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options);

    /// <summary>Updates only the available quantity of an already-published listing (the SKU is keyed by
    /// <see cref="CollectionCard.Id"/>). A published listing's quantity is sourced from its inventory
    /// item, so this re-PUTs just the inventory item with <see cref="EbayListingOptions.Quantity"/> and
    /// does NOT touch/re-publish the offer — re-publishing right after a quantity change trips eBay's
    /// transient "Availability not found" error (errorId 25604). Used when folding an identical item
    /// into an existing multi-quantity listing.</summary>
    Task<bool> UpdateQuantityAsync(CollectionCard card, EbayListingOptions options);

    Task<bool> EndListingAsync(EbayListing listing);
    Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType);
}
