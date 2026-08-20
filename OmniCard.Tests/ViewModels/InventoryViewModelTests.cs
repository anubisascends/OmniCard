using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Inventory;

namespace OmniCard.Tests.ViewModels;

public class InventoryViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;
    private readonly IInventoryService _inventory;
    private readonly Mock<IDialogService> _dialog = new();
    private readonly Mock<ISealedPriceUpdateService> _sealedPrices = new();
    private readonly Mock<IUpcLookupService> _upc = new();
    private readonly Mock<IStorageContainerService> _containers = new();

    public InventoryViewModelTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        using (var ctx = new OmniCardDbContext(_options))
            ctx.Database.EnsureCreated();

        _inventory = new InventoryService(new MockFactory(_options));
        _containers.Setup(c => c.GetAll()).Returns(new List<StorageContainer>());
    }

    public void Dispose() => _connection.Dispose();

    private InventoryViewModel CreateVm() =>
        new(_inventory, _dialog.Object, _sealedPrices.Object, _upc.Object, _containers.Object);

    private Product SeedProduct(CardGame game, string name, ProductCategory category = ProductCategory.Box) =>
        _inventory.CreateProduct(new Product { Game = game, Category = category, Name = name });

    [Fact]
    public void LoadInventory_GroupsLotsUnderProducts()
    {
        var product = SeedProduct(CardGame.Mtg, "Bloomburrow Box");
        _inventory.AddLot(product.Id, 2, 40m, null, "eBay", DateTime.Today);
        _inventory.AddLot(product.Id, 1, 42m, null, "Store", DateTime.Today);

        var vm = CreateVm();
        vm.LoadInventory();

        var row = Assert.Single(vm.Rows);
        Assert.Equal(3, row.OwnedQuantity);
        Assert.Equal(2, row.Lots.Count);
        Assert.Equal(122m, row.TotalCost); // 2*40 + 1*42
    }

    [Fact]
    public void LoadInventory_ExcludesSingles()
    {
        SeedProduct(CardGame.Mtg, "A Single", ProductCategory.Single);
        SeedProduct(CardGame.Mtg, "A Box", ProductCategory.Box);

        var vm = CreateVm();
        vm.LoadInventory();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("A Box", row.Name);
    }

    [Fact]
    public void SetGame_FiltersVisibleRowsByGame()
    {
        SeedProduct(CardGame.Mtg, "MTG Box");
        SeedProduct(CardGame.Pokemon, "Pokemon Box");

        var vm = CreateVm();

        vm.SetGame(CardGame.Mtg);
        Assert.Equal("MTG Box", Assert.Single(vm.Rows).Name);

        vm.SetGame(CardGame.Pokemon);
        Assert.Equal("Pokemon Box", Assert.Single(vm.Rows).Name);

        vm.SetGame(null); // All Games
        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public void AddProduct_SeedsGameFromFilter()
    {
        Product? seedSeen = null;
        _dialog.Setup(d => d.EditProduct(It.IsAny<Product?>()))
            .Callback<Product?>(p => seedSeen = p)
            .Returns<Product?>(p => p); // user accepts the seeded product

        var vm = CreateVm();
        vm.SetGame(CardGame.Pokemon);
        vm.AddProductCommand.Execute(null);

        Assert.NotNull(seedSeen);
        Assert.Equal(CardGame.Pokemon, seedSeen!.Game);
        Assert.Equal(CardGame.Pokemon, Assert.Single(_inventory.GetProducts()).Game);
    }

    [Fact]
    public void AddProduct_AllGames_PassesNoSeed()
    {
        Product? seedSeen = new Product { Name = "sentinel" };
        _dialog.Setup(d => d.EditProduct(It.IsAny<Product?>()))
            .Callback<Product?>(p => seedSeen = p)
            .Returns((Product?)null); // user cancels

        var vm = CreateVm();
        vm.SetGame(null);
        vm.AddProductCommand.Execute(null);

        Assert.Null(seedSeen); // no seed passed when All Games
    }

    [Fact]
    public void EditLot_AppliesDialogValues_AndPreservesOtherFields()
    {
        var product = SeedProduct(CardGame.Mtg, "Box");
        var lot = _inventory.AddLot(product.Id, 1, 40m, null, "eBay", DateTime.Today);
        // A field the edit dialog doesn't touch — must survive the edit.
        lot.Condition = "NM";
        _inventory.UpdateLot(lot);

        var vm = CreateVm();
        vm.LoadInventory();
        var row = vm.Rows.Single().Lots.Single();

        _dialog.Setup(d => d.EditLotDialog(It.IsAny<InventoryLot>()))
            .Returns((5, 99m, (int?)null, "Store", new DateTime(2026, 1, 2)));

        vm.EditLotCommand.Execute(row);

        var updated = _inventory.GetLots(product.Id).Single();
        Assert.Equal(5, updated.Quantity);
        Assert.Equal(99m, updated.UnitCost);
        Assert.Equal("Store", updated.Source);
        Assert.Equal(new DateTime(2026, 1, 2), updated.AcquisitionDate);
        Assert.Equal("NM", updated.Condition); // preserved
    }

    [Fact]
    public void EditLot_Cancelled_NoChange()
    {
        var product = SeedProduct(CardGame.Mtg, "Box");
        _inventory.AddLot(product.Id, 1, 40m, null, "eBay", DateTime.Today);

        var vm = CreateVm();
        vm.LoadInventory();
        var row = vm.Rows.Single().Lots.Single();

        _dialog.Setup(d => d.EditLotDialog(It.IsAny<InventoryLot>()))
            .Returns(((int, decimal?, int?, string?, DateTime)?)null);

        vm.EditLotCommand.Execute(row);

        Assert.Equal(40m, _inventory.GetLots(product.Id).Single().UnitCost);
    }

    [Fact]
    public void DeleteLot_RemovesTheLot()
    {
        var product = SeedProduct(CardGame.Mtg, "Box");
        _inventory.AddLot(product.Id, 1, 40m, null, null, DateTime.Today);
        _inventory.AddLot(product.Id, 1, 50m, null, null, DateTime.Today);

        var vm = CreateVm();
        vm.LoadInventory();
        // Note: DeleteLot shows a confirmation MessageBox; that path can't run headless, so we
        // exercise the service directly to confirm the wiring target. The command itself is covered
        // by the manual GUI smoke.
        var lotId = vm.Rows.Single().Lots.First().LotId;
        _inventory.DeleteLot(lotId);

        Assert.Single(_inventory.GetLots(product.Id));
    }

    [Fact]
    public void MoveLot_UpdatesLotLocation()
    {
        var product = SeedProduct(CardGame.Mtg, "Box");
        _inventory.AddLot(product.Id, 1, 40m, null, null, DateTime.Today);

        // The lot→location FK requires the container to exist in the DB.
        StorageContainer container;
        using (var ctx = new OmniCardDbContext(_options))
        {
            container = new StorageContainer { Name = "Shelf B" };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
        }

        _dialog.Setup(d => d.PickMoveToLocation())
            .Returns(new MoveToLocationResult { Container = container });

        var vm = CreateVm();
        vm.LoadInventory();
        var row = vm.Rows.Single().Lots.Single();

        vm.MoveLotCommand.Execute(row);

        Assert.Equal(container.Id, _inventory.GetLots(product.Id).Single().LocationId);
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
