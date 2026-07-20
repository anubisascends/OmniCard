namespace OmniCard.Models;

public class InventoryLot
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; } = 1;
    public decimal? UnitCost { get; set; }
    public DateTime AcquisitionDate { get; set; } = DateTime.UtcNow;
    public string? Source { get; set; }
    public int? LocationId { get; set; }   // existing StorageContainer.Id
    // Single copy attributes (unused in Phase 1; filled by Phase 2 migration).
    public string? Condition { get; set; }
    public string? ScanImagePath { get; set; }
    public int? Page { get; set; }
    public int? Slot { get; set; }
    public string? Section { get; set; }
    // Added in the Phase 2a unified-store migration (Task 2) to carry over
    // CollectionCard.IsMissing/FlagReason faithfully.
    public bool IsMissing { get; set; }
    public FlagReason? FlagReason { get; set; }
}
