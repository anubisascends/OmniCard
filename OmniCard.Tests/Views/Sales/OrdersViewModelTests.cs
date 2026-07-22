using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Sales;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class OrdersViewModelTests
{
    private static Customer Cust(int id, string name) => new() { Id = id, Name = name };

    private static Order NewOrder(int id, int customerId, OrderStatus status = OrderStatus.Created) =>
        new() { Id = id, CustomerId = customerId, Status = status, OrderNumber = $"ORD-{id}" };

    private static OrderLine Line(int id, int orderId, decimal price, int qty = 1) =>
        new() { Id = id, OrderId = orderId, NameSnapshot = "Card", Quantity = qty, UnitSalePrice = price };

    private static ActiveListing Listing(int lotId, decimal price) =>
        new(lotId, "Card", "Set", "SET", "NM", false, price, ListingStatus.Listed);

    private sealed class FakeReceiptService(bool throwOnBuild = false) : IReceiptService
    {
        public ReceiptDocument BuildReceipt(int orderId) =>
            throwOnBuild ? throw new InvalidOperationException("boom") : new ReceiptDocument();
    }

    private sealed class FakeReceiptPdfExporter : IReceiptPdfExporter
    { public void Export(ReceiptDocument document, string filePath) { } }

    private static OrdersViewModel MakeVm(
        out Mock<IOrderService> orderService,
        out Mock<ICustomerService> customerService,
        out Mock<IListingService> listingService)
    {
        orderService = new Mock<IOrderService>();
        orderService.Setup(s => s.GetOrderLineSummaries()).Returns(new List<OrderLineSummary>());
        customerService = new Mock<ICustomerService>();
        listingService = new Mock<IListingService>();
        return new OrdersViewModel(
            orderService.Object, customerService.Object, listingService.Object,
            new FakeReceiptService(), new FakeReceiptPdfExporter(),
            Mock.Of<ITcgPlayerOrderImportService>(), Mock.Of<IDialogService>());
    }

    private static OrdersViewModel MakeVm(
        out Mock<IOrderService> orderService,
        out Mock<ICustomerService> customerService,
        out Mock<IListingService> listingService,
        out Mock<IDialogService> dialogService)
    {
        orderService = new Mock<IOrderService>();
        orderService.Setup(s => s.GetOrderLineSummaries()).Returns(new List<OrderLineSummary>());
        customerService = new Mock<ICustomerService>();
        listingService = new Mock<IListingService>();
        dialogService = new Mock<IDialogService>();
        return new OrdersViewModel(
            orderService.Object, customerService.Object, listingService.Object,
            new FakeReceiptService(), new FakeReceiptPdfExporter(),
            Mock.Of<ITcgPlayerOrderImportService>(), dialogService.Object);
    }

    [Fact]
    public void Load_PopulatesOrdersCustomersAndAvailableCards()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        orderService.Setup(s => s.GetOrders()).Returns([NewOrder(1, 1)]);
        customerService.Setup(s => s.GetAll()).Returns([Cust(1, "Alice")]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([Listing(10, 5.00m)]);

        vm.Load();

        Assert.Single(vm.Orders);
        Assert.Single(vm.Customers);
        Assert.Single(vm.AvailableCards);
    }

    [Fact]
    public void NewOrder_WithNoSelectedCustomer_SetsStatusMessage_AndDoesNotCallCreate()
    {
        var vm = MakeVm(out var orderService, out _, out _);

        vm.NewOrder();

        Assert.Equal("Pick a customer first.", vm.StatusMessage);
        orderService.Verify(s => s.CreateOrder(It.IsAny<int>(), It.IsAny<SalesChannel>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void NewOrder_WithSelectedCustomer_CreatesOrder_ThenReloadsAndSelectsIt()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        var customer = Cust(1, "Alice");
        var created = NewOrder(5, 1);

        orderService.Setup(s => s.CreateOrder(1, SalesChannel.TcgPlayer, null)).Returns(created);
        orderService.Setup(s => s.GetOrders()).Returns([created]);
        orderService.Setup(s => s.GetLines(5)).Returns([]);
        customerService.Setup(s => s.GetAll()).Returns([customer]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);

        vm.SelectedCustomer = customer;
        vm.NewOrder();

        orderService.Verify(s => s.CreateOrder(1, SalesChannel.TcgPlayer, null), Times.Once);
        Assert.NotNull(vm.SelectedOrder);
        Assert.Equal(5, vm.SelectedOrder!.Id);
    }

    [Fact]
    public void AddCard_OnOpenOrder_AddsLine_RefreshesLines_AndRemovesFromAvailable()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Created);
        var listing = Listing(10, 5.00m);

        orderService.Setup(s => s.AddLine(1, 10, 5.00m)).Returns(Line(1, 1, 5.00m));
        orderService.Setup(s => s.GetLines(1)).Returns([Line(1, 1, 5.00m)]);

        vm.AvailableCards.Add(listing);
        vm.SelectedOrder = order;
        vm.SelectedAvailableCard = listing;

        vm.AddCard();

        orderService.Verify(s => s.AddLine(1, 10, 5.00m), Times.Once);
        Assert.Single(vm.Lines);
        Assert.Empty(vm.AvailableCards);
        Assert.Equal(5.00m, vm.OrderTotal);
    }

    [Fact]
    public void AddCard_OnNonOpenOrder_SetsStatusMessage_AndDoesNotAddLine()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Shipped);
        var listing = Listing(10, 5.00m);

        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.SelectedAvailableCard = listing;

        vm.AddCard();

        orderService.Verify(s => s.AddLine(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
        Assert.Equal("Only a Created order can be edited.", vm.StatusMessage);
    }

    [Fact]
    public void RemoveLine_OnOpenOrder_RemovesLine_AndRefreshes()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Created);
        var line = Line(1, 1, 5.00m);

        orderService.SetupSequence(s => s.GetLines(1))
            .Returns([line])
            .Returns([]);

        vm.SelectedOrder = order;
        Assert.Single(vm.Lines);

        vm.RemoveLine(line);

        orderService.Verify(s => s.RemoveLine(1), Times.Once);
        Assert.Empty(vm.Lines);
    }

    [Fact]
    public void RemoveLine_OnNonOpenOrder_SetsStatusMessage_AndDoesNotRemove()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Completed);
        var line = Line(1, 1, 5.00m);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.RemoveLine(line);

        orderService.Verify(s => s.RemoveLine(It.IsAny<int>()), Times.Never);
        Assert.Equal("Only a Created order can be edited.", vm.StatusMessage);
    }

    [Fact]
    public void MoveOrder_PackedToShipped_CallsService_ThenReloadsAndReselectsOrderById()
    {
        // Ported from SetStatus_CallsService_ThenReloadsAndReselectsOrderById: under the new
        // kanban transition rules Created can no longer jump straight to Shipped, so the source
        // status here is Packed (the valid predecessor of Shipped).
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        var order = NewOrder(1, 1, OrderStatus.Packed);

        customerService.Setup(s => s.GetAll()).Returns([]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);
        orderService.Setup(s => s.GetOrders()).Returns([NewOrder(1, 1, OrderStatus.Shipped)]);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.MoveOrder(order, OrderStatus.Shipped);

        orderService.Verify(s => s.SetStatus(1, OrderStatus.Shipped), Times.Once);
        Assert.NotNull(vm.SelectedOrder);
        Assert.Equal(1, vm.SelectedOrder!.Id);
        Assert.Equal(OrderStatus.Shipped, vm.SelectedOrder.Status);
        Assert.Equal("Order moved to Shipped.", vm.StatusMessage);
    }

    [Fact]
    public void MoveOrder_CreatedToCompleted_DoesNotCallService_AndSetsStatusMessage()
    {
        // Ported from SetStatus_Completed_OnOpenOrder_DoesNotCallService_AndSetsStatusMessage:
        // keeps the intent that an invalid jump straight to Completed never calls SetStatus.
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Created);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.MoveOrder(order, OrderStatus.Completed);

        orderService.Verify(s => s.SetStatus(It.IsAny<int>(), It.IsAny<OrderStatus>()), Times.Never);
        Assert.Equal("Can't move Created → Completed.", vm.StatusMessage);
    }

    [Fact]
    public void MoveOrder_CreatedToShipped_DoesNotCallService_AndSetsStatusMessage()
    {
        // Ported from SetStatus_Shipped_OnOpenOrder_CallsService: under the old forward-only
        // rules Created -> Shipped was valid; under the new kanban rules it is not (Shipped is
        // only reachable from Packed), so this now asserts the rejection instead.
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Created);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.MoveOrder(order, OrderStatus.Shipped);

        orderService.Verify(s => s.SetStatus(It.IsAny<int>(), It.IsAny<OrderStatus>()), Times.Never);
        Assert.Equal("Can't move Created → Shipped.", vm.StatusMessage);
    }

    [Fact]
    public void MoveOrder_AllowsPackedBackToCreated_AndRejectsOutOfShipped()
    {
        Assert.True(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Packed, OrderStatus.Created));
        Assert.True(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Created, OrderStatus.Packed));
        Assert.True(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Packed, OrderStatus.Shipped));
        Assert.False(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Shipped, OrderStatus.Created));
        Assert.False(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Completed, OrderStatus.Shipped));
        Assert.True(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Created, OrderStatus.Cancelled));
        Assert.False(OrdersViewModel.IsValidTransitionPublic(OrderStatus.Shipped, OrderStatus.Cancelled));
    }

    [Fact]
    public void IsEditable_TrueOnlyForCreated()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        orderService.Setup(s => s.GetOrders()).Returns([
            NewOrder(1, 1, OrderStatus.Created),
            NewOrder(2, 1, OrderStatus.Packed),
        ]);
        customerService.Setup(s => s.GetAll()).Returns([]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);
        orderService.Setup(s => s.GetLines(It.IsAny<int>())).Returns([]);

        vm.Load();
        vm.SelectedOrder = vm.CreatedOrders.First();
        Assert.True(vm.IsEditable);
        vm.SelectedOrder = vm.PackedOrders.First();
        Assert.False(vm.IsEditable);
    }

    [Fact]
    public void Load_BucketsOrdersByStatus()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        orderService.Setup(s => s.GetOrders()).Returns([
            NewOrder(1, 1, OrderStatus.Created),
            NewOrder(2, 1, OrderStatus.Packed),
            NewOrder(3, 1, OrderStatus.Shipped),
            NewOrder(4, 1, OrderStatus.Completed),
            NewOrder(5, 1, OrderStatus.Cancelled),
        ]);
        customerService.Setup(s => s.GetAll()).Returns([]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);

        vm.Load();

        Assert.Single(vm.CreatedOrders);
        Assert.Single(vm.PackedOrders);
        Assert.Single(vm.ShippedOrders);
        Assert.Single(vm.CompletedOrders);
        Assert.Single(vm.CancelledOrders);
    }

    [Fact]
    public void CancelOrder_OnCreatedOrder_CallsMoveOrder_AndSetsStatusMessage()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        var order = NewOrder(1, 1, OrderStatus.Created);

        customerService.Setup(s => s.GetAll()).Returns([]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);
        orderService.Setup(s => s.GetOrders()).Returns([NewOrder(1, 1, OrderStatus.Cancelled)]);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.CancelOrder(order);

        orderService.Verify(s => s.SetStatus(1, OrderStatus.Cancelled), Times.Once);
        Assert.Equal("Order moved to Cancelled.", vm.StatusMessage);
    }

    [Fact]
    public void CancelOrder_OnShippedOrder_DoesNotCallService_AndSetsStatusMessage()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Shipped);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.CancelOrder(order);

        orderService.Verify(s => s.SetStatus(It.IsAny<int>(), It.IsAny<OrderStatus>()), Times.Never);
        Assert.Equal("Can't cancel a Shipped order.", vm.StatusMessage);
    }

    [Fact]
    public void DeleteOrder_OnCreatedOrder_CallsService_AndClearsSelectionIfSelected()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService, out var dialogService);
        var order = NewOrder(1, 1, OrderStatus.Created);

        customerService.Setup(s => s.GetAll()).Returns([]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);
        orderService.Setup(s => s.GetOrders()).Returns([]);
        orderService.Setup(s => s.GetLines(1)).Returns([]);
        dialogService.Setup(s => s.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        vm.SelectedOrder = order;
        vm.DeleteOrder(order);

        orderService.Verify(s => s.DeleteOrder(1), Times.Once);
        Assert.Null(vm.SelectedOrder);
        Assert.Equal("Order deleted.", vm.StatusMessage);
    }

    [Fact]
    public void DeleteOrder_WhenConfirmDeclined_DoesNotCallService_AndLeavesStateIntact()
    {
        var vm = MakeVm(out var orderService, out _, out _, out var dialogService);
        var order = NewOrder(1, 1, OrderStatus.Created);
        orderService.Setup(s => s.GetLines(1)).Returns([]);
        dialogService.Setup(s => s.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        vm.SelectedOrder = order;
        vm.DeleteOrder(order);

        orderService.Verify(s => s.DeleteOrder(It.IsAny<int>()), Times.Never);
        Assert.Equal(order, vm.SelectedOrder);
        Assert.NotEqual("Order deleted.", vm.StatusMessage);
    }

    [Fact]
    public void DeleteOrder_OnShippedOrder_DoesNotCallService_AndSetsStatusMessage()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Shipped);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.DeleteOrder(order);

        orderService.Verify(s => s.DeleteOrder(It.IsAny<int>()), Times.Never);
        Assert.Equal("Can't delete a Shipped order.", vm.StatusMessage);
    }

    [Fact]
    public void DeleteOrder_WhenServiceThrows_SetsStatusMessage_AndDoesNotReload()
    {
        var vm = MakeVm(out var orderService, out _, out _, out var dialogService);
        var order = NewOrder(1, 1, OrderStatus.Created);
        orderService.Setup(s => s.GetLines(1)).Returns([]);
        orderService.Setup(s => s.DeleteOrder(1)).Throws(new InvalidOperationException("Can't delete a Shipped order (its sale is recorded and inventory removed)."));
        dialogService.Setup(s => s.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        vm.SelectedOrder = order;
        vm.DeleteOrder(order);

        Assert.Equal("Can't delete a Shipped order (its sale is recorded and inventory removed).", vm.StatusMessage);
        Assert.Equal(order, vm.SelectedOrder);
    }

    [Fact]
    public void SaveOrder_WithSelectedOrder_CallsUpdateOrder()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1);
        orderService.Setup(s => s.GetLines(1)).Returns([]);
        vm.SelectedOrder = order;

        vm.SaveOrder();

        orderService.Verify(s => s.UpdateOrder(order), Times.Once);
        Assert.Equal("Saved.", vm.StatusMessage);
    }

    [Fact]
    public void PrintReceipt_WhenBuildReceiptThrows_DoesNotPropagate_AndSetsStatusMessage()
    {
        var orderService = new Mock<IOrderService>();
        var customerService = new Mock<ICustomerService>();
        var listingService = new Mock<IListingService>();
        var vm = new OrdersViewModel(
            orderService.Object, customerService.Object, listingService.Object,
            new FakeReceiptService(throwOnBuild: true), new FakeReceiptPdfExporter(),
            Mock.Of<ITcgPlayerOrderImportService>(), Mock.Of<IDialogService>());
        var order = NewOrder(1, 1);
        orderService.Setup(s => s.GetLines(1)).Returns([]);
        vm.SelectedOrder = order;

        var ex = Record.Exception(() => vm.PrintReceiptCommand.Execute(null));

        Assert.Null(ex);
        Assert.Equal("Print failed: boom", vm.StatusMessage);
    }

    [Fact]
    public void OrderTotal_SumsQuantityTimesUnitSalePriceAcrossLines()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1);
        orderService.Setup(s => s.GetLines(1)).Returns([Line(1, 1, 2.50m, 2), Line(2, 1, 3.00m, 1)]);

        vm.SelectedOrder = order;

        Assert.Equal(8.00m, vm.OrderTotal);
    }

    [Fact]
    public void ReconciliationHint_ShownForImportedOrder_HiddenOtherwise()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        var plainOrder = NewOrder(1, 1);
        orderService.Setup(s => s.GetOrders()).Returns([plainOrder]);
        customerService.Setup(s => s.GetAll()).Returns([]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.Load();

        // Non-imported order: hint hidden.
        var plain = vm.Orders.First();
        vm.SelectedOrder = plain;
        Assert.False(vm.HasReconciliation);

        // Imported order: hint shown and references the target counts.
        var imported = new Order { Id = 999, ImportedItemCount = 8, ImportedProductValue = 320.00m };
        orderService.Setup(s => s.GetLines(999)).Returns([]);
        vm.Orders.Add(imported);
        vm.SelectedOrder = imported;
        Assert.True(vm.HasReconciliation);
        Assert.Contains("of 8 items", vm.ReconciliationHint);
    }
}
