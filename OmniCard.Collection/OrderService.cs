using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class OrderService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    IListingService listingService,
    IEbayListingService ebayListingService,
    ILogger<OrderService> logger) : IOrderService
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

    public List<OrderLineSummary> GetOrderLineSummaries()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        // Aggregate client-side: SQLite stores decimal as TEXT, so SUM(decimal) can't be
        // translated server-side. Volumes (order lines) are small.
        return ctx.OrderLines.AsNoTracking()
            .Select(l => new { l.OrderId, l.Quantity, l.UnitSalePrice })
            .AsEnumerable()
            .GroupBy(l => l.OrderId)
            .Select(g => new OrderLineSummary(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.Quantity * l.UnitSalePrice)))
            .ToList();
    }

    public Order CreateOrder(int customerId, SalesChannel channel, string? orderNumber)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = new Order
        {
            CustomerId = customerId,
            Channel = channel,
            OrderNumber = orderNumber,
            Status = OrderStatus.Created,
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

    public async Task SetStatusAsync(int orderId, OrderStatus status)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = ctx.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return;

        var shipping = status == OrderStatus.Shipped && order.Status != OrderStatus.Shipped
                       && order.Status != OrderStatus.Completed && order.Status != OrderStatus.Cancelled;

        order.Status = status;

        if (!shipping)
        {
            await ctx.SaveChangesAsync();
            return;
        }

        order.ShippedAt = DateTime.UtcNow;
        var lines = ctx.OrderLines.Where(l => l.OrderId == orderId).ToList();
        var lotIds = lines.Where(l => l.LotId is not null).Select(l => l.LotId!.Value).ToList();

        // Capture active eBay listings BEFORE the lots are removed — removing a lot
        // cascade-deletes its EbayListing row, so we must grab them first.
        var ebayToEnd = ctx.EbayListings.AsNoTracking()
            .Where(e => lotIds.Contains(e.LotId) && e.Status == EbayListingStatus.Active)
            .ToList();

        foreach (var line in lines)
        {
            if (line.LotId is not int lotId) continue;
            var lot = ctx.Lots.FirstOrDefault(l => l.Id == lotId);
            if (lot is null) continue;

            // Record the sale first (scalar values survive the lot removal below).
            ctx.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Sell,
                Quantity = line.Quantity,
                UnitValue = line.UnitSalePrice,
                Timestamp = DateTime.UtcNow,
                Note = order.OrderNumber ?? $"Order {order.Id}",
            });

            lot.Quantity -= line.Quantity;
            if (lot.Quantity <= 0)
                ctx.Lots.Remove(lot);
        }
        await ctx.SaveChangesAsync();

        // Mark each sold lot's active listing Sold (separate context inside the service).
        foreach (var line in lines.Where(l => l.LotId is not null))
            listingService.MarkSold(line.LotId!.Value, line.Id);

        // Auto-end any active eBay listing for the sold lots so it can't double-sell.
        // Best-effort: a failure never blocks the sale — it's logged with the item id so
        // the still-live listing can be ended manually on eBay.
        foreach (var listing in ebayToEnd)
        {
            try
            {
                var ended = await ebayListingService.EndListingAsync(listing);
                if (!ended)
                    logger.LogError("Auto-end of eBay listing {ItemId} (lot {LotId}) after sale FAILED — end it manually on eBay.", listing.EbayItemId, listing.LotId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-end of eBay listing {ItemId} (lot {LotId}) after sale threw — end it manually on eBay.", listing.EbayItemId, listing.LotId);
            }
        }
    }

    public void DeleteOrder(int orderId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = ctx.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return;
        if (order.Status is OrderStatus.Shipped or OrderStatus.Completed)
            throw new InvalidOperationException(
                $"Can't delete a {order.Status} order (its sale is recorded and inventory removed).");

        var lines = ctx.OrderLines.Where(l => l.OrderId == orderId).ToList();
        ctx.OrderLines.RemoveRange(lines);
        ctx.Orders.Remove(order);
        ctx.SaveChanges();
    }
}
