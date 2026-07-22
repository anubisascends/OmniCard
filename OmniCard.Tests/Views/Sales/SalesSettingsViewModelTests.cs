using System.Collections.Generic;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Settings;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class SalesSettingsViewModelTests
{
    private sealed class FakeContainers : IStorageContainerService
    {
        public List<StorageContainer> Containers { get; } =
            [new StorageContainer { Id = 1, Name = "Bulk" }, new StorageContainer { Id = 2, Name = "For Sale" }];
        public List<StorageContainer> GetAll() => Containers;
        public StorageContainer GetBulk() => Containers[0];
        public StorageContainer Create(string name, ContainerType type) => throw new System.NotImplementedException();
        public void Rename(int id, string newName) { }
        public void Delete(int id, bool moveCardsToBulk = true) { }
        public int GetCardCount(int containerId) => 0;
        public void SetCoverCard(int containerId, int? cardId) { }
        public List<CollectionCard> GetCardsInContainer(int containerId) => [];
        public void SetExcludeFromDeckCheck(int containerId, bool exclude) { }
    }

    private sealed class FakeSettings : ISalesSettingsService
    {
        public int? StoredLocation { get; set; } = 2;
        public CompanyProfile StoredCompany { get; set; } = new() { Name = "Existing Co" };
        public ReceiptSettings StoredReceipt { get; set; } = new() { WidthMm = 80 };
        public string? LastLogoSource { get; private set; }

        public int? ForSaleLocationId => StoredLocation;
        public void SetForSaleLocationId(int? id) => StoredLocation = id;
        public CompanyProfile GetCompany() => StoredCompany;
        public void SaveCompany(CompanyProfile company) => StoredCompany = company;
        public ReceiptSettings GetReceipt() => StoredReceipt;
        public void SaveReceipt(ReceiptSettings receipt) => StoredReceipt = receipt;
        public string SetLogo(string sourcePath) { LastLogoSource = sourcePath; return "company-logo.png"; }
        public double? OrdersEditorWidth => null;
        public void SetOrdersEditorWidth(double width) { }
        public bool OrdersEditorCollapsed => false;
        public void SetOrdersEditorCollapsed(bool collapsed) { }
    }

    [Fact]
    public void Load_PopulatesLocations_SelectsSavedLocation_AndCopiesCompanyReceipt()
    {
        var settings = new FakeSettings();
        var vm = new SalesSettingsViewModel(settings, new FakeContainers());

        vm.Load();

        Assert.Equal(2, vm.Locations.Count);
        Assert.NotNull(vm.ForSaleLocation);
        Assert.Equal(2, vm.ForSaleLocation!.Id);
        Assert.Equal("Existing Co", vm.Company.Name);
        Assert.Equal(80, vm.Receipt.WidthMm);
    }

    [Fact]
    public void Save_PersistsForSaleLocation_Company_AndReceipt()
    {
        var settings = new FakeSettings();
        var vm = new SalesSettingsViewModel(settings, new FakeContainers());
        vm.Load();

        vm.ForSaleLocation = vm.Locations[0];       // Bulk (Id 1)
        vm.Company.Name = "New Name";
        vm.Receipt.WidthMm = 58;
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, settings.StoredLocation);
        Assert.Equal("New Name", settings.StoredCompany.Name);
        Assert.Equal(58, settings.StoredReceipt.WidthMm);
        Assert.Equal("Saved.", vm.StatusMessage);
    }
}
