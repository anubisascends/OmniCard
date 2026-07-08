namespace OmniCard.Models;

public class EbayListingOptions
{
    public EbayListingType ListingType { get; set; } = EbayListingType.FixedPrice;
    public decimal Price { get; set; }
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
