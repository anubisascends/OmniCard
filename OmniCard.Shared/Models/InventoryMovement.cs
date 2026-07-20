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
}
