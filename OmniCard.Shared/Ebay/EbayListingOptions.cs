namespace OmniCard.Shared.Ebay;

public class EbayListingOptions
{
    public EbayListingType ListingType { get; set; } = EbayListingType.FixedPrice;
    public decimal Price { get; set; }
    /// <summary>Quantity available on the eBay listing. eBay disallows two identical listings from the
    /// same seller — multiple copies must be one multi-quantity listing — so this drives the inventory
    /// item's available quantity.</summary>
    public int Quantity { get; set; } = 1;
    public int? AuctionDuration { get; set; }
    public string Condition { get; set; } = "NM";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IncludeScanImage { get; set; } = true;
    public bool IncludeStockImage { get; set; } = true;
    public string? ShippingPolicyId { get; set; }
    public string? ReturnPolicyId { get; set; }
    public string? PaymentPolicyId { get; set; }
    public string? EbayCategoryId { get; set; }
}
