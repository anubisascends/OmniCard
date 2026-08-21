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

    // Set by TradeImportService when a web-app trade record for this lot is applied.
    public bool IsTraded { get; set; }
    public string? TradeNote { get; set; }
    public string? TradePhotoPath { get; set; }

    /// <summary>Legacy: set when this lot was added as the replacement for a linked scan under the
    /// old single-card model. Superseded by <see cref="FulfilledTradeSessionId"/>; retained for
    /// reading data written before multi-card trade sessions.</summary>
    public int? FulfilledTradeId { get; set; }

    /// <summary>Set when this lot was added as the replacement for a linked scan at commit time
    /// (see CardService.CommitScans) — the <see cref="TradeSession"/> this card came in for.</summary>
    public int? FulfilledTradeSessionId { get; set; }
}
