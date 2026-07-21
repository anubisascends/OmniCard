using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Sales;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class OrdersViewModelTests
{
    private static Customer Cust(int id, string name) => new() { Id = id, Name = name };

    private static Order NewOrder(int id, int customerId, OrderStatus status = OrderStatus.Open) =>
        new() { Id = id, CustomerId = customerId, Status = status, OrderNumber = $"ORD-{id}" };

    private static OrderLine Line(int id, int orderId, decimal price, int qty = 1) =>
        new() { Id = id, OrderId = orderId, NameSnapshot = "Card", Quantity = qty, UnitSalePrice = price };

    private static ActiveListing Listing(int lotId, decimal price) =>
        new(lotId, "Card", "Set", "SET", "NM", false, price, ListingStatus.Listed);

    private static OrdersViewModel MakeVm(
        out Mock<IOrderService> orderService,
        out Mock<ICustomerService> customerService,
        out Mock<IListingService> listingService)
    {
        orderService = new Mock<IOrderService>();
        customerService = new Mock<ICustomerService>();
        listingService = new Mock<IListingService>();
        return new OrdersViewModel(orderService.Object, customerService.Object, listingService.Object);
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
        var order = NewOrder(1, 1, OrderStatus.Open);
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
        Assert.Equal("Can only edit an Open order.", vm.StatusMessage);
    }

    [Fact]
    public void RemoveLine_OnOpenOrder_RemovesLine_AndRefreshes()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Open);
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
        Assert.Equal("Can only edit an Open order.", vm.StatusMessage);
    }

    [Fact]
    public void SetStatus_CallsService_ThenReloadsAndReselectsOrderById()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        var order = NewOrder(1, 1, OrderStatus.Open);

        customerService.Setup(s => s.GetAll()).Returns([]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);
        orderService.Setup(s => s.GetOrders()).Returns([NewOrder(1, 1, OrderStatus.Shipped)]);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.SetStatus(OrderStatus.Shipped);

        orderService.Verify(s => s.SetStatus(1, OrderStatus.Shipped), Times.Once);
        Assert.NotNull(vm.SelectedOrder);
        Assert.Equal(1, vm.SelectedOrder!.Id);
        Assert.Equal(OrderStatus.Shipped, vm.SelectedOrder.Status);
        Assert.Equal("Order marked Shipped.", vm.StatusMessage);
    }

    [Fact]
    public void SetStatus_Completed_OnOpenOrder_DoesNotCallService_AndSetsStatusMessage()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1, OrderStatus.Open);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.SetStatus(OrderStatus.Completed);

        orderService.Verify(s => s.SetStatus(It.IsAny<int>(), It.IsAny<OrderStatus>()), Times.Never);
        Assert.Equal("Can't mark Completed from Open.", vm.StatusMessage);
    }

    [Fact]
    public void SetStatus_Shipped_OnOpenOrder_CallsService()
    {
        var vm = MakeVm(out var orderService, out var customerService, out var listingService);
        var order = NewOrder(1, 1, OrderStatus.Open);

        customerService.Setup(s => s.GetAll()).Returns([]);
        listingService.Setup(s => s.GetActiveListings(null)).Returns([]);
        orderService.Setup(s => s.GetOrders()).Returns([NewOrder(1, 1, OrderStatus.Shipped)]);
        orderService.Setup(s => s.GetLines(1)).Returns([]);

        vm.SelectedOrder = order;
        vm.SetStatus(OrderStatus.Shipped);

        orderService.Verify(s => s.SetStatus(1, OrderStatus.Shipped), Times.Once);
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
    public void OrderTotal_SumsQuantityTimesUnitSalePriceAcrossLines()
    {
        var vm = MakeVm(out var orderService, out _, out _);
        var order = NewOrder(1, 1);
        orderService.Setup(s => s.GetLines(1)).Returns([Line(1, 1, 2.50m, 2), Line(2, 1, 3.00m, 1)]);

        vm.SelectedOrder = order;

        Assert.Equal(8.00m, vm.OrderTotal);
    }
}
