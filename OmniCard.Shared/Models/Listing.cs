namespace OmniCard.Models;

public class Listing
{
    public int Id { get; set; }
    public int LotId { get; set; }
    public SalesChannel Channel { get; set; }
    public ListingStatus Status { get; set; }
    public decimal ListedPrice { get; set; }
    public int Quantity { get; set; } = 1;
    /// <summary>The lot's location when it was listed, so the pick list can show where to find it and Unlist can restore it.</summary>
    public int? OriginalLocationId { get; set; }
    public DateTime ListedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PickedAt { get; set; }
    public string? ExternalRef { get; set; }
    /// <summary>Set when the listing is sold (links to the order line). Populated in Phase 2.</summary>
    public int? OrderLineId { get; set; }
    public string? Note { get; set; }
}
