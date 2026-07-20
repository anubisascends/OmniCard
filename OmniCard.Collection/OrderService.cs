using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class OrderService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    IListingService listingService) : IOrderService
{
    public List<Order> GetOrders()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Orders.AsNoTracking().OrderByDescending(o => o.OrderDate).ToList();
    }

    public Order? GetOrder(int id)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Orders.AsNoTracking().FirstOrDefault(o => o.Id == id);
    }

    public List<OrderLine> GetLines(int orderId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.OrderLines.AsNoTracking().Where(l => l.OrderId == orderId).ToList();
    }

    public Order CreateOrder(int customerId, SalesChannel channel, string? orderNumber)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = new Order
        {
            CustomerId = customerId,
            Channel = channel,
            OrderNumber = orderNumber,
            Status = OrderStatus.Open,
            OrderDate = DateTime.UtcNow,
        };
        ctx.Orders.Add(order);
        ctx.SaveChanges();
        return order;
    }

    public void UpdateOrder(Order order)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Orders.Update(order);
        ctx.SaveChanges();
    }

    public OrderLine AddLine(int orderId, int lotId, decimal unitSalePrice)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var lot = ctx.Lots.AsNoTracking().FirstOrDefault(l => l.Id == lotId)
                  ?? throw new InvalidOperationException($"Lot {lotId} not found.");
        var product = ctx.Products.AsNoTracking().FirstOrDefault(p => p.Id == lot.ProductId);

        var line = new OrderLine
        {
            OrderId = orderId,
            LotId = lotId,
            ProductId = lot.ProductId,
            NameSnapshot = product?.Name ?? "",
            SetSnapshot = product?.SetName,
            ConditionSnapshot = lot.Condition,
            IsFoilSnapshot = product?.Foil ?? false,
            Quantity = 1,
            UnitSalePrice = unitSalePrice,
        };
        ctx.OrderLines.Add(line);
        ctx.SaveChanges();
        return line;
    }

    public void RemoveLine(int orderLineId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var line = ctx.OrderLines.FirstOrDefault(l => l.Id == orderLineId);
        if (line is null) return;
        ctx.OrderLines.Remove(line);
        ctx.SaveChanges();
    }

    public void SetStatus(int orderId, OrderStatus status) => throw new NotImplementedException();
}
