using System.ComponentModel.DataAnnotations.Schema;

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

    /// <summary>Buyer-paid item count from a TCGPlayer import (null for non-imported orders);
    /// used for the order-editor reconciliation hint.</summary>
    public int? ImportedItemCount { get; set; }
    /// <summary>Buyer-paid product subtotal from a TCGPlayer import (null for non-imported orders).</summary>
    public decimal? ImportedProductValue { get; set; }

    // ── Display-only fields (not persisted) hydrated for the kanban cards in OrdersViewModel.Load ──

    /// <summary>Customer name, resolved for card display.</summary>
    [NotMapped] public string? CustomerNameDisplay { get; set; }
    /// <summary>Sum of line quantities on this order (card display).</summary>
    [NotMapped] public int LineItemCount { get; set; }
    /// <summary>Sum of line (qty × unit price) on this order (card display).</summary>
    [NotMapped] public decimal LineTotal { get; set; }
}
