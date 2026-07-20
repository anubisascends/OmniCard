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
}
