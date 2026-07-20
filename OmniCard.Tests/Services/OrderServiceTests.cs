using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class OrderServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    protected readonly DbContextOptions<OmniCardDbContext> _opts;

    public OrderServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void OrderGraph_RoundTrips()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var cust = new Customer { Name = "Ada" };
            ctx.Customers.Add(cust);
            ctx.SaveChanges();
            var order = new Order
            {
                CustomerId = cust.Id,
                Channel = SalesChannel.TcgPlayer,
                OrderNumber = "TCG-1",
                Status = OrderStatus.Open,
                MarketplaceFees = 1.10m,
                ShippingCost = 0.63m,
                ShippingChargedToBuyer = 1.25m,
            };
            ctx.Orders.Add(order);
            ctx.SaveChanges();
            ctx.OrderLines.Add(new OrderLine
            {
                OrderId = order.Id,
                NameSnapshot = "Sol Ring",
                SetSnapshot = "Commander",
                ConditionSnapshot = "NM",
                Quantity = 1,
                UnitSalePrice = 2.50m,
            });
            ctx.SaveChanges();
        }

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var order = Assert.Single(ctx.Orders.ToList());
            Assert.Equal(OrderStatus.Open, order.Status);
            Assert.Equal(1.10m, order.MarketplaceFees);
            var line = Assert.Single(ctx.OrderLines.ToList());
            Assert.Equal("Sol Ring", line.NameSnapshot);
            Assert.Equal(2.50m, line.UnitSalePrice);
        }
    }
}
