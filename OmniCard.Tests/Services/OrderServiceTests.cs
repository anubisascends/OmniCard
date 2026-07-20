using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
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

    private OrderService OrderSvc() => new(new Factory(_opts), new ListingService(new Factory(_opts), new StubSettings()));

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }
    private sealed class StubSettings : OmniCard.Interfaces.ISalesSettingsService
    { public int? ForSaleLocationId => 99; public void SetForSaleLocationId(int? id) { } }

    private (int customerId, int lotId) SeedCustomerAndLot()
    {
        using var ctx = new OmniCardDbContext(_opts);
        var c = new Customer { Name = "Ada" };
        ctx.Customers.Add(c);
        var p = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring", SetName = "Commander", Foil = false };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = p.Id, Quantity = 1, Condition = "NM", UnitCost = 1.00m };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return (c.Id, lot.Id);
    }

    [Fact]
    public void CreateOrder_AddLine_SnapshotsCardAndRemoveLine()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "TCG-42");
        var line = svc.AddLine(order.Id, lotId, 3.50m);

        Assert.Equal("Sol Ring", line.NameSnapshot);
        Assert.Equal("Commander", line.SetSnapshot);
        Assert.Equal("NM", line.ConditionSnapshot);
        Assert.Equal(3.50m, line.UnitSalePrice);
        Assert.Equal(lotId, line.LotId);
        Assert.Single(svc.GetLines(order.Id));

        svc.RemoveLine(line.Id);
        Assert.Empty(svc.GetLines(order.Id));
    }

    [Fact]
    public void SetStatus_Shipped_RemovesInventory_RecordsSell_MarksListingSold()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var listing = new ListingService(new Factory(_opts), new StubSettings());
        listing.ListForSale([lotId], SalesChannel.TcgPlayer, 3.50m, 1);

        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "TCG-42");
        var line = svc.AddLine(order.Id, lotId, 3.50m);

        svc.SetStatus(order.Id, OrderStatus.Shipped);

        using var ctx = new OmniCardDbContext(_opts);
        // Lot removed (qty 1 -> 0)
        Assert.Null(ctx.Lots.FirstOrDefault(l => l.Id == lotId));
        // Sell movement recorded with proceeds
        var sell = Assert.Single(ctx.Movements.Where(m => m.Type == MovementType.Sell && m.LotId == lotId).ToList());
        Assert.Equal(3.50m, sell.UnitValue);
        // Listing marked Sold
        Assert.Equal(ListingStatus.Sold, ctx.Listings.Single(l => l.LotId == lotId).Status);
        // Order stamped
        var shipped = ctx.Orders.Single(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Shipped, shipped.Status);
        Assert.NotNull(shipped.ShippedAt);
    }

    [Fact]
    public void SetStatus_Shipped_IsIdempotent()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.Manual, null);
        svc.AddLine(order.Id, lotId, 2m);
        svc.SetStatus(order.Id, OrderStatus.Shipped);
        svc.SetStatus(order.Id, OrderStatus.Shipped); // second call must not double-decrement

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Movements.Where(m => m.Type == MovementType.Sell && m.LotId == lotId).ToList());
    }

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
