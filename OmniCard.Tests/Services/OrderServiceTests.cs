using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
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

    private OrderService OrderSvc(IEbayListingService? ebay = null) => new(
        new Factory(_opts),
        new ListingService(new Factory(_opts), new StubSettings()),
        ebay ?? new FakeEbayListingService(),
        NullLogger<OrderService>.Instance);

    private sealed class RecordingEbayListingService : FakeEbayListingService
    {
        public List<int> EndedLotIds { get; } = [];
        public bool EndResult { get; set; } = true;
        public override Task<bool> EndListingAsync(EbayListing listing)
        {
            EndedLotIds.Add(listing.LotId);
            return Task.FromResult(EndResult);
        }
    }

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
        public System.Collections.Generic.IReadOnlyList<OmniCard.Models.WorkflowLane> GetWorkflowLanes() => OmniCard.Models.WorkflowLane.Defaults();
        public void SaveWorkflowLanes(System.Collections.Generic.IEnumerable<OmniCard.Models.WorkflowLane> lanes) { }
    }

    private (int customerId, int lotId) SeedCustomerAndLot(int quantity = 1, decimal? unitCost = 1.00m)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var c = new Customer { Name = "Ada" };
        ctx.Customers.Add(c);
        var p = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring", SetName = "Commander", Foil = false };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = p.Id, Quantity = quantity, Condition = "NM", UnitCost = unitCost };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return (c.Id, lot.Id);
    }

    /// <summary>Seeds a customer plus N distinct single-copy lots of the same product/condition —
    /// mirrors how identical singles are actually stored (one lot per physical card).</summary>
    private (int customerId, List<int> lotIds) SeedCustomerAndLots(int count)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var c = new Customer { Name = "Ada" };
        ctx.Customers.Add(c);
        var p = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring", SetName = "Commander", Foil = false };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        var lotIds = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var lot = new InventoryLot { ProductId = p.Id, Quantity = 1, Condition = "NM" };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotIds.Add(lot.Id);
        }
        return (c.Id, lotIds);
    }

    /// <summary>Creates an OrderLine directly (bypassing AddLine's hardcoded Quantity=1) so tests
    /// can exercise quantities &gt; 1.</summary>
    private OrderLine AddLineDirect(int orderId, int? lotId, int quantity, decimal unitPrice, string name = "Sol Ring")
    {
        using var ctx = new OmniCardDbContext(_opts);
        var productId = lotId is int id ? ctx.Lots.Single(l => l.Id == id).ProductId : (int?)null;
        var line = new OrderLine { OrderId = orderId, LotId = lotId, ProductId = productId, Quantity = quantity, UnitSalePrice = unitPrice, NameSnapshot = name };
        ctx.OrderLines.Add(line);
        ctx.SaveChanges();
        return line;
    }

    /// <summary>Creates a Completed order with a single line (of any quantity), fully shipped
    /// through the real SetStatusAsync pipeline so the linked Sell movement (OrderLineId) exists
    /// exactly as it would in production.</summary>
    private async Task<(Order Order, OrderLine Line)> CreateCompletedOrderWithLine(
        OrderService svc, int customerId, int? lotId, int quantity, decimal unitPrice)
    {
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, $"ORD-{Guid.NewGuid():N}"[..12]);
        var line = AddLineDirect(order.Id, lotId, quantity, unitPrice);
        await svc.SetStatusAsync(order.Id, OrderStatus.Shipped);
        await svc.SetStatusAsync(order.Id, OrderStatus.Completed);
        return (svc.GetOrder(order.Id)!, line);
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
    public void AddLines_CreatesOneOrderLinePerLot_InOneCall()
    {
        var (customerId, lotIds) = SeedCustomerAndLots(3);
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "TCG-BULK");

        var created = svc.AddLines(order.Id, lotIds, 3.50m);

        Assert.Equal(3, created.Count);
        Assert.All(created, l => Assert.Equal(1, l.Quantity));
        Assert.All(created, l => Assert.Equal(3.50m, l.UnitSalePrice));
        Assert.Equal(lotIds, created.Select(l => l.LotId!.Value).ToList());
        Assert.Equal(3, svc.GetLines(order.Id).Count);
    }

    [Fact]
    public async Task SetStatus_Shipped_RemovesInventory_RecordsSell_MarksListingSold()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var listing = new ListingService(new Factory(_opts), new StubSettings());
        listing.ListForSale([lotId], SalesChannel.TcgPlayer, 3.50m, 1);

        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "TCG-42");
        var line = svc.AddLine(order.Id, lotId, 3.50m);

        await svc.SetStatusAsync(order.Id, OrderStatus.Shipped);

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
    public async Task SetStatus_Shipped_IsIdempotent()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.Manual, null);
        svc.AddLine(order.Id, lotId, 2m);
        await svc.SetStatusAsync(order.Id, OrderStatus.Shipped);
        await svc.SetStatusAsync(order.Id, OrderStatus.Shipped); // second call must not double-decrement

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Movements.Where(m => m.Type == MovementType.Sell && m.LotId == lotId).ToList());
    }

    [Fact]
    public async Task SetStatus_Shipped_EndsActiveEbayListing_ForSoldLot()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.EbayListings.Add(new EbayListing { LotId = lotId, EbayItemId = "E-1", Status = EbayListingStatus.Active, ListedPrice = 3m });
            ctx.SaveChanges();
        }
        var ebay = new RecordingEbayListingService();
        var svc = OrderSvc(ebay);
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "TCG-99");
        svc.AddLine(order.Id, lotId, 3m);

        await svc.SetStatusAsync(order.Id, OrderStatus.Shipped);

        Assert.Contains(lotId, ebay.EndedLotIds);
    }

    [Fact]
    public async Task SetStatus_Shipped_EbayEndFailure_DoesNotBlockSale()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.EbayListings.Add(new EbayListing { LotId = lotId, EbayItemId = "E-2", Status = EbayListingStatus.Active, ListedPrice = 3m });
            ctx.SaveChanges();
        }
        var ebay = new RecordingEbayListingService { EndResult = false }; // simulate eBay end failing
        var svc = OrderSvc(ebay);
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "TCG-100");
        svc.AddLine(order.Id, lotId, 3m);

        await svc.SetStatusAsync(order.Id, OrderStatus.Shipped);

        Assert.Contains(lotId, ebay.EndedLotIds); // end was attempted
        using var verify = new OmniCardDbContext(_opts);
        Assert.Null(verify.Lots.FirstOrDefault(l => l.Id == lotId)); // sale still completed (lot removed)
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
    public async Task DeleteOrder_Throws_WhenShippedOrCompleted()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "DEL-2");
        svc.AddLine(order.Id, lotId, 3.50m);
        await svc.SetStatusAsync(order.Id, OrderStatus.Shipped);

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

    // ---------------------------------------------------------------------
    // EditCompletedOrder
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EditCompletedOrder_QuantityDecrease_IncrementsExistingLot_WhenLotStillPresent()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 5);
        var svc = OrderSvc();
        var (order, line) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 2, unitPrice: 10m);
        // Ship consumed 2 of 5 -> lot now at 3.

        var updatedLine = new OrderLine { Id = line.Id, OrderId = order.Id, Quantity = 1, UnitSalePrice = 10m };
        await svc.EditCompletedOrder(order.Id, order, [updatedLine], "Item damaged, partial refund");

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(4, ctx.Lots.Single(l => l.Id == lotId).Quantity); // 3 + 1 restored
        var movement = ctx.Movements.Single(m => m.OrderLineId == line.Id && m.Type == MovementType.Sell);
        Assert.Equal(1, movement.Quantity);
        Assert.Equal(1, ctx.OrderLines.Single(l => l.Id == line.Id).Quantity);
    }

    [Fact]
    public async Task EditCompletedOrder_QuantityDecrease_RecreatesLot_WithBestEffortCost_WhenOriginalFullyConsumed()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 2, unitCost: null);
        int productId;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            productId = ctx.Lots.Single(l => l.Id == lotId).ProductId;
            ctx.Movements.Add(new InventoryMovement { ProductId = productId, LotId = lotId, Type = MovementType.Acquire, Quantity = 2, UnitValue = 1.50m });
            ctx.SaveChanges();
        }

        var svc = OrderSvc();
        var (order, line) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 2, unitPrice: 10m);
        // Ship consumed all 2 -> original lot removed entirely.
        using (var verify = new OmniCardDbContext(_opts))
            Assert.Null(verify.Lots.FirstOrDefault(l => l.Id == lotId));

        var updatedLine = new OrderLine { Id = line.Id, OrderId = order.Id, Quantity = 1, UnitSalePrice = 10m };
        await svc.EditCompletedOrder(order.Id, order, [updatedLine], "Item damaged, partial refund");

        using var ctx2 = new OmniCardDbContext(_opts);
        var newLot = Assert.Single(ctx2.Lots.Where(l => l.ProductId == productId).ToList());
        Assert.NotEqual(lotId, newLot.Id); // never reuses the deleted lot's id
        Assert.Equal(1, newLot.Quantity);
        Assert.Equal(1.50m, newLot.UnitCost); // 3.00 acquired / 2 units
        Assert.Null(newLot.LocationId);

        var acquireMovement = Assert.Single(ctx2.Movements.Where(m => m.LotId == newLot.Id && m.Type == MovementType.Acquire).ToList());
        Assert.Equal(1, acquireMovement.Quantity);
        Assert.Equal(1.50m, acquireMovement.UnitValue);

        var edit = Assert.Single(ctx2.OrderEdits.Where(e => e.OrderId == order.Id).ToList());
        var changes = JsonSerializer.Deserialize<List<OrderEditChange>>(edit.ChangesJson)!;
        Assert.Contains(changes, c => c.Field == "Inventory note");
    }

    [Fact]
    public async Task EditCompletedOrder_FullLineRemoval_DeletesLinkedMovement_AndRestoresInventory()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 3);
        var svc = OrderSvc();
        var (order, line) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 1, unitPrice: 5m);
        // Ship consumed 1 of 3 -> lot now at 2.

        await svc.EditCompletedOrder(order.Id, order, [], "Card never actually shipped");

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(3, ctx.Lots.Single(l => l.Id == lotId).Quantity); // 2 + 1 restored
        Assert.False(ctx.OrderLines.Any(l => l.Id == line.Id));
        Assert.False(ctx.Movements.Any(m => m.OrderLineId == line.Id));
    }

    [Fact]
    public async Task EditCompletedOrder_PriceOnlyChange_UpdatesMovementUnitValueOnly()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 5);
        var svc = OrderSvc();
        var (order, line) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 2, unitPrice: 10m);

        var updatedLine = new OrderLine { Id = line.Id, OrderId = order.Id, Quantity = 2, UnitSalePrice = 8m };
        await svc.EditCompletedOrder(order.Id, order, [updatedLine], "Buyer negotiated a partial refund");

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(3, ctx.Lots.Single(l => l.Id == lotId).Quantity); // unchanged (5 - 2, no restore)
        var movement = ctx.Movements.Single(m => m.OrderLineId == line.Id && m.Type == MovementType.Sell);
        Assert.Equal(2, movement.Quantity);
        Assert.Equal(8m, movement.UnitValue);
        Assert.Equal(8m, ctx.OrderLines.Single(l => l.Id == line.Id).UnitSalePrice);
    }

    [Fact]
    public async Task EditCompletedOrder_AddingNewLine_DecrementsFreshLot_LinksNewMovement_MarksListingSold()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 1);
        var svc = OrderSvc();
        var (order, _) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 1, unitPrice: 5m);

        var (_, newLotId) = SeedCustomerAndLot(quantity: 1);
        var listing = new ListingService(new Factory(_opts), new StubSettings());
        listing.ListForSale([newLotId], SalesChannel.TcgPlayer, 7m, 1);

        var newLine = new OrderLine { Id = 0, OrderId = order.Id, LotId = newLotId, UnitSalePrice = 7m };
        await svc.EditCompletedOrder(order.Id, order, [.. svc.GetLines(order.Id), newLine], "Missed a line item when the order was built");

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Null(ctx.Lots.FirstOrDefault(l => l.Id == newLotId)); // consumed (qty 1 -> 0, removed)
        var addedLine = Assert.Single(ctx.OrderLines.Where(l => l.OrderId == order.Id && l.LotId == newLotId).ToList());
        Assert.Equal(7m, addedLine.UnitSalePrice);
        var movement = Assert.Single(ctx.Movements.Where(m => m.LotId == newLotId && m.Type == MovementType.Sell).ToList());
        Assert.Equal(addedLine.Id, movement.OrderLineId);
        Assert.Equal(ListingStatus.Sold, ctx.Listings.Single(l => l.LotId == newLotId).Status);
    }

    [Fact]
    public async Task EditCompletedOrder_EndsActiveEbayListing_ForNewlyAddedLine()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 1);
        var ebay = new RecordingEbayListingService();
        var svc = OrderSvc(ebay);
        var (order, _) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 1, unitPrice: 5m);

        var (_, newLotId) = SeedCustomerAndLot(quantity: 1);
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.EbayListings.Add(new EbayListing { LotId = newLotId, EbayItemId = "E-EDIT-1", Status = EbayListingStatus.Active, ListedPrice = 7m });
            ctx.SaveChanges();
        }

        var newLine = new OrderLine { Id = 0, OrderId = order.Id, LotId = newLotId, UnitSalePrice = 7m };
        await svc.EditCompletedOrder(order.Id, order, [.. svc.GetLines(order.Id), newLine], "Missed a line item");

        Assert.Contains(newLotId, ebay.EndedLotIds);
    }

    [Fact]
    public async Task EditCompletedOrder_MissingLinkedMovement_Throws_AndAppliesNothing()
    {
        // Simulate a pre-feature Completed order: a line with no OrderLineId-linked Sell movement.
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 3);
        int orderId, lineId;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var order = new Order { CustomerId = customerId, Channel = SalesChannel.Manual, Status = OrderStatus.Completed };
            ctx.Orders.Add(order);
            ctx.SaveChanges();
            orderId = order.Id;
            var line = new OrderLine { OrderId = orderId, LotId = lotId, Quantity = 1, UnitSalePrice = 5m, NameSnapshot = "Sol Ring" };
            ctx.OrderLines.Add(line);
            ctx.SaveChanges();
            lineId = line.Id;
        }

        var svc = OrderSvc();
        var order2 = svc.GetOrder(orderId)!;
        var updatedLine = new OrderLine { Id = lineId, OrderId = orderId, Quantity = 0, UnitSalePrice = 5m }; // remove

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.EditCompletedOrder(orderId, order2, [updatedLine], "Trying to fix old data"));

        using var verify = new OmniCardDbContext(_opts);
        Assert.True(verify.OrderLines.Any(l => l.Id == lineId)); // untouched
        Assert.Equal(3, verify.Lots.Single(l => l.Id == lotId).Quantity); // untouched
        Assert.Empty(verify.OrderEdits.Where(e => e.OrderId == orderId).ToList());
    }

    [Fact]
    public async Task EditCompletedOrder_BlankReason_ThrowsArgumentException_BeforeTouchingDb()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 3);
        var svc = OrderSvc();
        var (order, line) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 1, unitPrice: 5m);

        var updatedLine = new OrderLine { Id = line.Id, OrderId = order.Id, Quantity = 0, UnitSalePrice = 5m };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.EditCompletedOrder(order.Id, order, [updatedLine], "   "));

        using var verify = new OmniCardDbContext(_opts);
        Assert.True(verify.OrderLines.Any(l => l.Id == line.Id)); // untouched
    }

    [Fact]
    public async Task EditCompletedOrder_QuantityIncreaseOnExistingLine_Throws()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 5);
        var svc = OrderSvc();
        var (order, line) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 1, unitPrice: 5m);

        var updatedLine = new OrderLine { Id = line.Id, OrderId = order.Id, Quantity = 2, UnitSalePrice = 5m };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.EditCompletedOrder(order.Id, order, [updatedLine], "Trying to add more units this way"));

        using var verify = new OmniCardDbContext(_opts);
        Assert.Equal(1, verify.OrderLines.Single(l => l.Id == line.Id).Quantity); // untouched
    }

    [Fact]
    public async Task EditCompletedOrder_NotCompletedOrder_Throws()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "NOT-COMPLETE-1");
        svc.AddLine(order.Id, lotId, 5m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.EditCompletedOrder(order.Id, order, svc.GetLines(order.Id), "Trying to edit a non-completed order"));
    }

    [Fact]
    public async Task EditCompletedOrder_WritesExactlyOneOrderEditRow_WithReasonAndChangesJson()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 5);
        var svc = OrderSvc();
        var (order, line) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 2, unitPrice: 10m);

        var updatedLine = new OrderLine { Id = line.Id, OrderId = order.Id, Quantity = 1, UnitSalePrice = 10m };
        await svc.EditCompletedOrder(order.Id, order, [updatedLine], "Item damaged");

        var edits = svc.GetOrderEdits(order.Id);
        var edit = Assert.Single(edits);
        Assert.Equal("Item damaged", edit.Reason);
        var changes = JsonSerializer.Deserialize<List<OrderEditChange>>(edit.ChangesJson)!;
        Assert.NotEmpty(changes);
        Assert.Contains(changes, c => c.Field.Contains("quantity"));
    }

    [Fact]
    public async Task EditCompletedOrder_NoActualChanges_IsNoOp_WritesNoAuditRow()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 5);
        var svc = OrderSvc();
        var (order, line) = await CreateCompletedOrderWithLine(svc, customerId, lotId, quantity: 2, unitPrice: 10m);

        var unchangedLine = new OrderLine { Id = line.Id, OrderId = order.Id, Quantity = 2, UnitSalePrice = 10m };
        await svc.EditCompletedOrder(order.Id, order, [unchangedLine], "No actual change");

        Assert.Empty(svc.GetOrderEdits(order.Id));
        using var verify = new OmniCardDbContext(_opts);
        Assert.Equal(3, verify.Lots.Single(l => l.Id == lotId).Quantity); // unchanged
    }

    [Fact]
    public void CreateOrder_SeedsStageKey_ToCreatedLane()
    {
        var (customerId, _) = SeedCustomerAndLot();
        var svc = OrderSvc();

        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "ORD-1");

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal("created", ctx.Orders.Single(o => o.Id == order.Id).StageKey);
    }

    [Fact]
    public async Task SetStatusAsync_PersistsCustomStageKey_AlongsideBehavior()
    {
        var (customerId, lotId) = SeedCustomerAndLot(quantity: 3);
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "ORD-2");
        AddLineDirect(order.Id, lotId, quantity: 1, unitPrice: 5m);

        await svc.SetStatusAsync(order.Id, OrderStatus.Shipped, "out-the-door");

        using var ctx = new OmniCardDbContext(_opts);
        var reloaded = ctx.Orders.Single(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Shipped, reloaded.Status); // behavior drives accounting as before
        Assert.Equal("out-the-door", reloaded.StageKey);    // exact custom lane remembered
        Assert.Equal(2, ctx.Lots.Single(l => l.Id == lotId).Quantity); // ship accounting still ran (3 → 2)
    }
}
