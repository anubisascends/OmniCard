using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Sales;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Tests.Services.Ebay;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Tests.Web;

/// <summary>The Ship page's label-scan endpoints: lookup vs. auto-ship, ambiguity, already-shipped, and
/// that shipping lands in the configured Shipped lane with the normal ship accounting.</summary>
public class OrdersShipScanTests : IDisposable
{
    private const string Tracking = "9400111899223456789012";
    private const string UspsBarcode = "42090210" + Tracking;

    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly OrderService _orders;
    private readonly OrdersController _controller;

    public OrdersShipScanTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts))
            ctx.Database.EnsureCreated();

        var factory = new Factory(_opts);
        var settings = new LaneSettings();
        _orders = new OrderService(factory, new ListingService(factory, settings), new FakeEbayListingService(),
            NullLogger<OrderService>.Instance);
        _controller = new OrdersController(_orders, new CustomerService(factory), settings, null!, null!, factory);
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task Lookup_WithoutShip_ReportsReady_AndLeavesOrderOpen()
    {
        var id = SeedOrder(Tracking);

        var result = Value(await _controller.ShipScan(new ShipScanRequest { Code = UspsBarcode }));

        Assert.Equal("ready", result.Outcome);
        Assert.Equal(Tracking, result.Tracking);
        Assert.Equal(id, Assert.Single(result.Orders).Id);
        Assert.Equal("Ada", result.Orders[0].CustomerName);
        Assert.Equal(OrderStatus.Packed, _orders.GetOrder(id)!.Status);
    }

    [Fact]
    public async Task AutoShip_ShipsSingleMatch_IntoConfiguredShippedLane_WithShipAccounting()
    {
        var id = SeedOrder(Tracking, out var lotId);

        var result = Value(await _controller.ShipScan(new ShipScanRequest { Code = UspsBarcode, Ship = true }));

        Assert.Equal("shipped", result.Outcome);
        var order = _orders.GetOrder(id)!;
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Equal("out-the-door", order.StageKey);
        Assert.NotNull(order.ShippedAt);
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Null(ctx.Lots.FirstOrDefault(l => l.Id == lotId)); // sold copy left inventory
    }

    [Fact]
    public async Task RescanAfterShipping_ReportsAlreadyShipped_WithoutShippingTwice()
    {
        SeedOrder(Tracking);
        await _controller.ShipScan(new ShipScanRequest { Code = UspsBarcode, Ship = true });

        var again = Value(await _controller.ShipScan(new ShipScanRequest { Code = UspsBarcode, Ship = true }));

        Assert.Equal("alreadyShipped", again.Outcome);
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Movements.Where(m => m.Type == MovementType.Sell).ToList());
    }

    [Fact]
    public async Task TwoOpenOrdersWithSameTracking_AreAmbiguous_AndNeitherShips()
    {
        var a = SeedOrder(Tracking);
        var b = SeedOrder(Tracking);

        var result = Value(await _controller.ShipScan(new ShipScanRequest { Code = UspsBarcode, Ship = true }));

        Assert.Equal("ambiguous", result.Outcome);
        Assert.Equal(new[] { a, b }.Order(), result.Orders.Select(o => o.Id).Order());
        Assert.All([a, b], id => Assert.Equal(OrderStatus.Packed, _orders.GetOrder(id)!.Status));
    }

    [Fact]
    public async Task UnknownCode_IsNotFound_AndCancelledOrdersAreIgnored()
    {
        var id = SeedOrder(Tracking);
        await _orders.SetStatusAsync(id, OrderStatus.Cancelled);

        var result = Value(await _controller.ShipScan(new ShipScanRequest { Code = UspsBarcode, Ship = true }));

        Assert.Equal("notFound", result.Outcome);
        Assert.Empty(result.Orders);
    }

    [Fact]
    public async Task BlankCode_IsBadRequest()
    {
        var response = await _controller.ShipScan(new ShipScanRequest { Code = "  --  " });
        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task ShipById_ShipsOpenOrder_AndRejectsShippedOne()
    {
        var id = SeedOrder(Tracking);

        var first = await _controller.Ship(id);
        Assert.Equal("Shipped", first.Value!.Status);

        var second = await _controller.Ship(id);
        Assert.IsType<ConflictObjectResult>(second.Result);
        Assert.IsType<NotFoundResult>((await _controller.Ship(9999)).Result);
    }

    private static ShipScanResultDto Value(ActionResult<ShipScanResultDto> r) => Assert.IsType<ShipScanResultDto>(r.Value);

    private int SeedOrder(string tracking) => SeedOrder(tracking, out _);

    private int SeedOrder(string tracking, out int lotId)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var customer = ctx.Customers.FirstOrDefault() ?? ctx.Customers.Add(new Customer { Name = "Ada" }).Entity;
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring", SetName = "Commander" };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, Quantity = 1, Condition = "NM" };
        ctx.Lots.Add(lot);
        var order = new Order
        {
            CustomerId = customer.Id, Channel = SalesChannel.TcgPlayer, Status = OrderStatus.Packed,
            StageKey = "packed", TrackingNumber = tracking,
        };
        ctx.Orders.Add(order);
        ctx.SaveChanges();
        ctx.OrderLines.Add(new OrderLine { OrderId = order.Id, LotId = lot.Id, ProductId = product.Id, NameSnapshot = "Sol Ring", Quantity = 1, UnitSalePrice = 2m });
        ctx.SaveChanges();
        lotId = lot.Id;
        return order.Id;
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    /// <summary>Lanes with a custom Shipped lane key, so the test proves the ship lands in it.</summary>
    private sealed class LaneSettings : ISalesSettingsService
    {
        public int? ForSaleLocationId => null;
        public void SetForSaleLocationId(int? id) { }
        public bool MovePickedToForSaleLocation => false;
        public void SetMovePickedToForSaleLocation(bool move) { }
        public CompanyProfile GetCompany() => new();
        public void SaveCompany(CompanyProfile company) { }
        public ReceiptSettings GetReceipt() => new();
        public void SaveReceipt(ReceiptSettings receipt) { }
        public string SetLogo(string sourcePath) => "";
        public double? OrdersEditorWidth => null;
        public void SetOrdersEditorWidth(double width) { }
        public bool OrdersEditorCollapsed => false;
        public void SetOrdersEditorCollapsed(bool collapsed) { }
        public IReadOnlyList<WorkflowLane> GetWorkflowLanes() =>
        [
            new() { Key = "created", Name = "Created", Behavior = OrderStatus.Created },
            new() { Key = "packed", Name = "Packed", Behavior = OrderStatus.Packed },
            new() { Key = "out-the-door", Name = "Out the door", Behavior = OrderStatus.Shipped },
            new() { Key = "shipped", Name = "Shipped", Behavior = OrderStatus.Shipped },
        ];
        public void SaveWorkflowLanes(IEnumerable<WorkflowLane> lanes) { }
    }
}
