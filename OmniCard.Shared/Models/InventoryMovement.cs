namespace OmniCard.Models;

public class InventoryMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public MovementType Type { get; set; }
    public int Quantity { get; set; }
    public decimal? UnitValue { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
    public int? RelatedMovementId { get; set; }

    /// <summary>Order line this Sell movement fulfilled (populated going forward only — ship time
    /// and completed-order edits). Lets a correction find and fix the exact linked entry instead
    /// of guessing by LotId, which is ambiguous once a lot has been partially sold across orders.</summary>
    public int? OrderLineId { get; set; }
}
