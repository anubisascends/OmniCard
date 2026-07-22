using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IOrderService
{
    List<Order> GetOrders();
    Order? GetOrder(int id);
    List<OrderLine> GetLines(int orderId);
    Order CreateOrder(int customerId, SalesChannel channel, string? orderNumber);
    void UpdateOrder(Order order);
    OrderLine AddLine(int orderId, int lotId, decimal unitSalePrice);
    void RemoveLine(int orderLineId);
    void SetStatus(int orderId, OrderStatus status); // implemented in Task 5

    /// <summary>Deletes a pre-ship order and its lines. Throws if the order is Shipped or
    /// Completed (its sale is recorded and inventory already removed).</summary>
    void DeleteOrder(int orderId);
}
