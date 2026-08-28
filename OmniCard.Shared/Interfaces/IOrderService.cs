using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IOrderService
{
    List<Order> GetOrders();
    Order? GetOrder(int id);
    List<OrderLine> GetLines(int orderId);

    /// <summary>Per-order line aggregates (item count + total) for kanban card display,
    /// keyed implicitly by <see cref="OrderLineSummary.OrderId"/>. Orders with no lines are absent.</summary>
    List<OrderLineSummary> GetOrderLineSummaries();
    Order CreateOrder(int customerId, SalesChannel channel, string? orderNumber);
    void UpdateOrder(Order order);
    OrderLine AddLine(int orderId, int lotId, decimal unitSalePrice);

    /// <summary>Adds one <see cref="OrderLine"/> per lot in a single save — the batch form of
    /// <see cref="AddLine"/>, used when a user bulk-adds identical items (same product/condition/
    /// foil/price) from the grouped picker so N additions cost one DB round trip instead of N.</summary>
    List<OrderLine> AddLines(int orderId, IEnumerable<int> lotIds, decimal unitSalePrice);
    void RemoveLine(int orderLineId);
    /// <summary>Applies a status change and, optionally, records the exact customizable lane the
    /// order now occupies (<paramref name="stageKey"/> → <see cref="Order.StageKey"/>; defaults to
    /// the built-in lane for <paramref name="status"/> when omitted). On a transition into Shipped
    /// it records the sale, marks listings sold, and best-effort auto-ends any active eBay listing
    /// for the sold lots.</summary>
    Task SetStatusAsync(int orderId, OrderStatus status, string? stageKey = null);

    /// <summary>Deletes a pre-ship order and its lines. Throws if the order is Shipped or
    /// Completed (its sale is recorded and inventory already removed).</summary>
    void DeleteOrder(int orderId);

    /// <summary>Applies a fully-audited correction to a Completed order: header fields
    /// (shipping/fees/tracking/notes) plus per-line quantity decreases, price corrections, line
    /// removals, and new lines (sourced from an available lot, exactly like AddLine). Requires a
    /// non-blank reason; throws <see cref="ArgumentException"/> if blank. Throws
    /// <see cref="InvalidOperationException"/> if the order isn't Completed, if a quantity
    /// increase is attempted on an existing line (unsupported — add a new line instead), or if a
    /// line's original Sell movement can't be found via <see cref="InventoryMovement.OrderLineId"/>
    /// (predates this feature — fail loudly rather than silently desyncing realized gains). Writes
    /// exactly one <see cref="OrderEdit"/> audit row per call, capturing every changed field.
    /// Best-effort marks new lines' source listing Sold and ends any active eBay listing (mirrors
    /// SetStatusAsync's ship block) — failures are logged, never block the save.</summary>
    Task EditCompletedOrder(int orderId, Order updatedHeader, List<OrderLine> updatedLines, string reason);

    /// <summary>Audit history for an order (newest first).</summary>
    List<OrderEdit> GetOrderEdits(int orderId);
}
