using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class StorageContainerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public StorageContainerServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IStorageContainerService CreateService() =>
        new StorageContainerService(new MockFactory(_options));

    private (int ContainerId, int LotAId, int LotBId) SeedBinderWithTwoLots()
    {
        using var ctx = new OmniCardDbContext(_options);
        var container = new StorageContainer { Name = "Binder A", ContainerType = ContainerType.Binder, SlotsPerPage = 9, TotalPages = 1 };
        ctx.StorageContainers.Add(container);
        ctx.SaveChanges();

        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Test Card" };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lotA = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = container.Id, Page = null, Slot = null };
        var lotB = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = container.Id, Page = null, Slot = null };
        ctx.Lots.AddRange(lotA, lotB);
        ctx.SaveChanges();

        return (container.Id, lotA.Id, lotB.Id);
    }

    [Fact]
    public void AssignCardToSlot_EmptySlot_PlacesCard()
    {
        var (containerId, lotAId, _) = SeedBinderWithTwoLots();
        var service = CreateService();

        service.AssignCardToSlot(lotAId, containerId, page: 1, slot: 0);

        using var ctx = new OmniCardDbContext(_options);
        var lotA = ctx.Lots.Single(l => l.Id == lotAId);
        Assert.Equal(1, lotA.Page);
        Assert.Equal(0, lotA.Slot);
    }

    [Fact]
    public void AssignCardToSlot_FromUnplacedPool_DisplacesOccupantBackToPool()
    {
        var (containerId, lotAId, lotBId) = SeedBinderWithTwoLots();
        var service = CreateService();

        // A takes slot 0.
        service.AssignCardToSlot(lotAId, containerId, page: 1, slot: 0);
        // B (from the unplaced pool) takes the same slot, displacing A.
        service.AssignCardToSlot(lotBId, containerId, page: 1, slot: 0);

        using var ctx = new OmniCardDbContext(_options);
        var lotA = ctx.Lots.Single(l => l.Id == lotAId);
        var lotB = ctx.Lots.Single(l => l.Id == lotBId);

        Assert.Null(lotA.Page);
        Assert.Null(lotA.Slot);
        Assert.Equal(containerId, lotA.LocationId); // stays in the binder, just unplaced

        Assert.Equal(1, lotB.Page);
        Assert.Equal(0, lotB.Slot);
    }

    [Fact]
    public void AssignCardToSlot_BetweenTwoSlots_Swaps()
    {
        var (containerId, lotAId, lotBId) = SeedBinderWithTwoLots();
        var service = CreateService();

        service.AssignCardToSlot(lotAId, containerId, page: 1, slot: 0);
        service.AssignCardToSlot(lotBId, containerId, page: 1, slot: 1);

        // Drag A (currently at slot 0) onto B's slot (1) -> swap.
        service.AssignCardToSlot(lotAId, containerId, page: 1, slot: 1);

        using var ctx = new OmniCardDbContext(_options);
        var lotA = ctx.Lots.Single(l => l.Id == lotAId);
        var lotB = ctx.Lots.Single(l => l.Id == lotBId);

        Assert.Equal(1, lotA.Page);
        Assert.Equal(1, lotA.Slot);
        Assert.Equal(1, lotB.Page);
        Assert.Equal(0, lotB.Slot);
    }

    [Fact]
    public void AddBinderPage_IncrementsTotalPages()
    {
        var (containerId, _, _) = SeedBinderWithTwoLots();
        var service = CreateService();

        service.AddBinderPage(containerId);

        var layout = service.GetBinderLayout(containerId);
        Assert.Equal(2, layout.TotalPages);
    }

    [Fact]
    public void SetColumns_UpdatesBinderLayout()
    {
        var (containerId, _, _) = SeedBinderWithTwoLots();
        var service = CreateService();

        service.SetColumns(containerId, 5);

        var layout = service.GetBinderLayout(containerId);
        Assert.Equal(5, layout.Columns);
    }


    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
