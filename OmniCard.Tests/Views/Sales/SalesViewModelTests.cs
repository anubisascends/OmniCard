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
        new(lotId, "Card Name", "Set Name", "NM", false, "Binder A", null, null, null, 1.00m, 1);

    [Fact]
    public void Load_PopulatesLocationsAndPickList_AndSelectsSavedForSaleLocation()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        var containers = new List<StorageContainer> { Container(1, "Binder A"), Container(2, "Box B") };
        containerService.Setup(c => c.GetAll()).Returns(containers);
        salesSettings.Setup(s => s.ForSaleLocationId).Returns(2);
        listingService.Setup(l => l.GetPickList(null)).Returns([Entry(10), Entry(11)]);

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object);

        vm.Load();

        Assert.Equal(2, vm.Locations.Count);
        Assert.NotNull(vm.ForSaleLocation);
        Assert.Equal(2, vm.ForSaleLocation!.Id);
        Assert.Equal(2, vm.PickList.Count);
    }

    [Fact]
    public void SettingForSaleLocation_PersistsViaSalesSettingsService()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        listingService.Setup(l => l.GetPickList(null)).Returns([]);

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object);
        var location = Container(5, "Bulk");

        vm.ForSaleLocation = location;

        salesSettings.Verify(s => s.SetForSaleLocationId(5), Times.Once);
    }

    [Fact]
    public void MarkAllPicked_MarksThenRefreshes_AndNoOpsWhenPickListEmpty()
    {
        var listingService = new Mock<IListingService>();
        var salesSettings = new Mock<ISalesSettingsService>();
        var containerService = new Mock<IStorageContainerService>();

        containerService.Setup(c => c.GetAll()).Returns([]);
        listingService.Setup(l => l.GetPickList(null)).Returns([Entry(10), Entry(11)]);

        var vm = new SalesViewModel(listingService.Object, salesSettings.Object, containerService.Object);
        vm.Load();

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
}
