namespace OmniCard.Models;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public SalesChannel Channel { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public OrderStatus Status { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public decimal ShippingChargedToBuyer { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal MarketplaceFees { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ShippedAt { get; set; }
}
