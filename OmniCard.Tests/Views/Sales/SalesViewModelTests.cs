using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Sales;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class SalesViewModelTests
{
    private static StorageContainer Container(int id, string name) =>
        new() { Id = id, Name = name, ContainerType = ContainerType.Binder };

    private static PickListEntry Entry(int lotId) =>
        new(lotId, "Card Name", "Set Name", "SET", "NM", false, "Binder A", null, null, null, 1.00m, 1);

    // SalesViewModel takes OrdersViewModel/CustomersViewModel as direct child VMs (for the Orders/
    // Customers sub-tabs) that these pick-list-focused tests exercise. Real instances are cheap to
    // construct off mocked interfaces and are never invoked here.
    private static OrdersViewModel NewOrdersViewModel() =>
        new(Mock.Of<IOrderService>(), Mock.Of<ICustomerService>(), Mock.Of<IListingService>());

    private static CustomersViewModel NewCustomersViewModel() =>
        new(Mock.Of<ICustomerService>());

    [Fact]
    public async Task Load_PopulatesLocationsAndPickList_AndSelectsSavedForSaleLocation()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        var containers = new List<StorageContainer> { Container(1, "Binder A"), Container(2, "Box B") };
        containerService.Setup(c => c.GetAll()).Returns(containers);
        salesSettings.Setup(s => s.ForSaleLocationId).Returns(2);
        listingService.Setup(l => l.GetPickList(null)).Returns([Entry(10), Entry(11)]);

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object, NewOrdersViewModel(), NewCustomersViewModel());

        await vm.Load();

        Assert.Equal(2, vm.Locations.Count);
        Assert.NotNull(vm.ForSaleLocation);
        Assert.Equal(2, vm.ForSaleLocation!.Id);
        Assert.Equal(2, vm.PickList.Count);
    }

    [Fact]
    public async Task Load_WhenGetPickListThrows_SetsStatusMessage_AndDoesNotThrow()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        containerService.Setup(c => c.GetAll()).Returns([Container(1, "Binder A")]);
        salesSettings.Setup(s => s.ForSaleLocationId).Returns(1);
        listingService.Setup(l => l.GetPickList(null)).Throws(new InvalidOperationException("db is locked"));

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object, NewOrdersViewModel(), NewCustomersViewModel());

        var ex = await Record.ExceptionAsync(() => vm.Load());

        Assert.Null(ex);
        Assert.NotNull(vm.StatusMessage);
        Assert.Contains("db is locked", vm.StatusMessage);
    }

    [Fact]
    public async Task Load_RestoringPersistedLocation_DoesNotRewriteSettings()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        var containers = new List<StorageContainer> { Container(1, "Binder A"), Container(2, "Box B") };
        containerService.Setup(c => c.GetAll()).Returns(containers);
        salesSettings.Setup(s => s.ForSaleLocationId).Returns(2);
        listingService.Setup(l => l.GetPickList(null)).Returns([]);

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object, NewOrdersViewModel(), NewCustomersViewModel());

        await vm.Load();

        // Restoring the saved location during Load must never rewrite (or clobber) settings.
        salesSettings.Verify(s => s.SetForSaleLocationId(It.IsAny<int?>()), Times.Never);

        // A genuine, subsequent user-driven change still persists.
        vm.ForSaleLocation = containers[0];
        salesSettings.Verify(s => s.SetForSaleLocationId(1), Times.Once);
    }

    [Fact]
    public void SettingForSaleLocation_PersistsViaSalesSettingsService()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        listingService.Setup(l => l.GetPickList(null)).Returns([]);

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object, NewOrdersViewModel(), NewCustomersViewModel());
        var location = Container(5, "Bulk");

        vm.ForSaleLocation = location;

        salesSettings.Verify(s => s.SetForSaleLocationId(5), Times.Once);
    }

    [Fact]
    public async Task MarkAllPicked_MarksThenRefreshes_AndNoOpsWhenPickListEmpty()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        containerService.Setup(c => c.GetAll()).Returns([Container(1, "Binder A")]);
        salesSettings.Setup(s => s.ForSaleLocationId).Returns(1);
        listingService.Setup(l => l.GetPickList(null)).Returns([Entry(10), Entry(11)]);

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object, NewOrdersViewModel(), NewCustomersViewModel());
        await vm.Load();

        listingService.Setup(l => l.MarkPicked(It.IsAny<IEnumerable<int>>()))
            .Callback(() => listingService.Setup(l => l.GetPickList(null)).Returns([]))
            .Returns(2);

        vm.MarkAllPicked();

        listingService.Verify(l => l.MarkPicked(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10, 11 }))), Times.Once);
        Assert.Empty(vm.PickList);

        // No-op when the pick list is already empty — MarkPicked must not be called again.
        vm.MarkAllPicked();
        listingService.Verify(l => l.MarkPicked(It.IsAny<IEnumerable<int>>()), Times.Once);
    }

    [Fact]
    public void MarkAllPicked_WithNoForSaleLocationConfigured_DoesNotCallMarkPicked_AndDoesNotThrow()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object, NewOrdersViewModel(), NewCustomersViewModel());
        // ForSaleLocation left at its default (null) — the state before the user has picked one.
        vm.PickList.Add(Entry(10));

        var ex = Record.Exception(() => vm.MarkAllPicked());

        Assert.Null(ex);
        listingService.Verify(l => l.MarkPicked(It.IsAny<IEnumerable<int>>()), Times.Never);
        Assert.Equal("Select a For-Sale location first.", vm.StatusMessage);
    }
}
