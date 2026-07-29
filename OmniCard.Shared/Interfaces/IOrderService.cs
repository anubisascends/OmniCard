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
    void RemoveLine(int orderLineId);
    /// <summary>Applies a status change. On a transition into Shipped it records the sale,
    /// marks listings sold, and best-effort auto-ends any active eBay listing for the sold lots.</summary>
    Task SetStatusAsync(int orderId, OrderStatus status);

    /// <summary>Deletes a pre-ship order and its lines. Throws if the order is Shipped or
    /// Completed (its sale is recorded and inventory already removed).</summary>
    void DeleteOrder(int orderId);
}
