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
    {
        public int? ForSaleLocationId => 99;
        public void SetForSaleLocationId(int? id) { }
        public OmniCard.Models.CompanyProfile GetCompany() => new();
        public void SaveCompany(OmniCard.Models.CompanyProfile company) { }
        public OmniCard.Models.ReceiptSettings GetReceipt() => new();
        public void SaveReceipt(OmniCard.Models.ReceiptSettings receipt) { }
        public string SetLogo(string sourcePath) => "company-logo.png";
        public double? OrdersEditorWidth => null;
        public void SetOrdersEditorWidth(double width) { }
        public bool OrdersEditorCollapsed => false;
        public void SetOrdersEditorCollapsed(bool collapsed) { }
    }

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
    public void GetOrderLineSummaries_AggregatesItemCountAndTotal_PerOrder_AndOmitsEmptyOrders()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "SUM-1");
        svc.AddLine(order.Id, lotId, 3.50m);   // qty 1
        svc.AddLine(order.Id, lotId, 2.00m);   // qty 1
        var empty = svc.CreateOrder(customerId, SalesChannel.Manual, "SUM-EMPTY");

        var summaries = svc.GetOrderLineSummaries();

        var s = Assert.Single(summaries, x => x.OrderId == order.Id);
        Assert.Equal(2, s.ItemCount);
        Assert.Equal(5.50m, s.Total);
        Assert.DoesNotContain(summaries, x => x.OrderId == empty.Id);
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
                Status = OrderStatus.Created,
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
            Assert.Equal(OrderStatus.Created, order.Status);
            Assert.Equal(1.10m, order.MarketplaceFees);
            var line = Assert.Single(ctx.OrderLines.ToList());
            Assert.Equal("Sol Ring", line.NameSnapshot);
            Assert.Equal(2.50m, line.UnitSalePrice);
        }
    }

    [Fact]
    public void Order_ImportedReconciliationFields_RoundTrip()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Customers.Add(new Customer { Id = 1, Name = "Ada" });
            ctx.SaveChanges();
            ctx.Orders.Add(new Order
            {
                CustomerId = 1,
                Channel = SalesChannel.TcgPlayer,
                Status = OrderStatus.Created,
                OrderNumber = "TCG-1",
                OrderDate = new DateTime(2026, 7, 17),
                ImportedItemCount = 8,
                ImportedProductValue = 320.00m,
            });
            ctx.SaveChanges();
        }

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var order = ctx.Orders.Single(o => o.OrderNumber == "TCG-1");
            Assert.Equal(8, order.ImportedItemCount);
            Assert.Equal(320.00m, order.ImportedProductValue);
        }
    }

    [Fact]
    public void DeleteOrder_RemovesOrderAndLines_WhenPreShip()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "DEL-1");
        svc.AddLine(order.Id, lotId, 3.50m);

        svc.DeleteOrder(order.Id);

        Assert.Null(svc.GetOrder(order.Id));
        Assert.Empty(svc.GetLines(order.Id));
    }

    [Fact]
    public void DeleteOrder_Throws_WhenShippedOrCompleted()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "DEL-2");
        svc.AddLine(order.Id, lotId, 3.50m);
        svc.SetStatus(order.Id, OrderStatus.Shipped);

        Assert.Throws<InvalidOperationException>(() => svc.DeleteOrder(order.Id));
        Assert.NotNull(svc.GetOrder(order.Id));
    }

    [Fact]
    public void DeleteOrder_NoOp_WhenMissing()
    {
        var svc = OrderSvc();
        svc.DeleteOrder(999999); // must not throw
    }

    [Fact]
    public void DeleteOrder_FreesLotBackIntoPicker_WhenPreShip()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var listing = new ListingService(new Factory(_opts), new StubSettings());
        listing.ListForSale([lotId], SalesChannel.TcgPlayer, 3.50m, 1);

        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "DEL-3");
        svc.AddLine(order.Id, lotId, 3.50m);

        // Committed: the lot is on a Created order's line, so the picker must exclude it.
        Assert.DoesNotContain(listing.GetActiveListings(), a => a.LotId == lotId);

        svc.DeleteOrder(order.Id);

        // Freed: deleting the pre-ship order releases the lot back into the picker.
        Assert.Contains(listing.GetActiveListings(), a => a.LotId == lotId);
    }
}
