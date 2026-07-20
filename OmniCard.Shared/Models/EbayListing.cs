namespace OmniCard.Models;

public class EbayListing
{
    public int Id { get; set; }
    public int LotId { get; set; }
    public InventoryLot? Lot { get; set; }
    public string EbayItemId { get; set; } = "";
    public string? EbayCatalogProductId { get; set; }
    public EbayListingStatus Status { get; set; }
    public EbayListingType ListingType { get; set; }
    public decimal ListedPrice { get; set; }
    public decimal? SoldPrice { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? AuctionDuration { get; set; }
    public string? BuyerUsername { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}
