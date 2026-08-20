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
    public void AddBinderSheet_DoubleSided_AddsTwoPages()
    {
        var (containerId, _, _) = SeedBinderWithTwoLots(); // legacy binder: TotalPages=1, SheetSides null -> [1]
        var service = CreateService();

        service.AddBinderSheet(containerId, doubleSided: true);

        var layout = service.GetBinderLayout(containerId);
        Assert.Equal(3, layout.TotalPages);          // 1 (backfilled) + 2 (front + back)
        Assert.Equal(new[] { 1, 2 }, layout.SheetSides);
    }

    [Fact]
    public void AddBinderSheet_SingleSided_AddsOnePage()
    {
        var (containerId, _, _) = SeedBinderWithTwoLots();
        var service = CreateService();

        service.AddBinderSheet(containerId, doubleSided: false);

        var layout = service.GetBinderLayout(containerId);
        Assert.Equal(2, layout.TotalPages);
        Assert.Equal(new[] { 1, 1 }, layout.SheetSides);
    }

    [Fact]
    public void Create_Binder_StartsWithOneDoubleSidedSheet()
    {
        var service = CreateService();

        var binder = service.Create("Fresh Binder", ContainerType.Binder);

        var layout = service.GetBinderLayout(binder.Id);
        Assert.Equal(2, layout.TotalPages);
        Assert.Equal(new[] { 2 }, layout.SheetSides);
    }

    [Fact]
    public void GetBinderLayout_LegacyBinder_BackfillsSheetsFromTotalPages()
    {
        // A binder created before the sheet model: only TotalPages is set, SheetSides is null.
        using (var ctx = new OmniCardDbContext(_options))
        {
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Legacy", ContainerType = ContainerType.Binder, SlotsPerPage = 9, TotalPages = 5,
            });
            ctx.SaveChanges();
        }
        var legacyId = new OmniCardDbContext(_options).StorageContainers.Single(c => c.Name == "Legacy").Id;
        var service = CreateService();

        var layout = service.GetBinderLayout(legacyId);

        // 5 logical pages group into double-sided sheets with a trailing single-sided sheet,
        // preserving every existing card's page number.
        Assert.Equal(5, layout.TotalPages);
        Assert.Equal(new[] { 2, 2, 1 }, layout.SheetSides);
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


    private (int ContainerId, int ProductId) SeedBinderWithSheets(string name, string sheetSides)
    {
        using var ctx = new OmniCardDbContext(_options);
        var total = sheetSides.Split(',').Sum(int.Parse);
        var container = new StorageContainer
        {
            Name = name, ContainerType = ContainerType.Binder, SlotsPerPage = 9, Columns = 3,
            TotalPages = total, SheetSides = sheetSides,
        };
        ctx.StorageContainers.Add(container);
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = name + " Card" };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        return (container.Id, product.Id);
    }

    private int PlaceLot(int containerId, int productId, int page, int slot)
    {
        using var ctx = new OmniCardDbContext(_options);
        var lot = new InventoryLot { ProductId = productId, Quantity = 1, LocationId = containerId, Page = page, Slot = slot };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void RemoveBinderSheet_UnplacesItsCards_AndShiftsLaterPagesDown()
    {
        var (containerId, productId) = SeedBinderWithSheets("Remove A", "2,2"); // pages 1-4
        var onRemoved = PlaceLot(containerId, productId, page: 1, slot: 0); // sheet 0
        var onLater = PlaceLot(containerId, productId, page: 3, slot: 5);   // sheet 1 (front)
        var service = CreateService();

        service.RemoveBinderSheet(containerId, page: 1); // removes sheet 0 (pages 1,2)

        using var ctx = new OmniCardDbContext(_options);
        var removed = ctx.Lots.Single(l => l.Id == onRemoved);
        var later = ctx.Lots.Single(l => l.Id == onLater);

        Assert.Null(removed.Page);
        Assert.Null(removed.Slot);
        Assert.Equal(containerId, removed.LocationId); // stays in the binder, just unplaced

        Assert.Equal(1, later.Page); // page 3 shifted down by the removed sheet's 2 sides
        Assert.Equal(5, later.Slot); // slot preserved

        var layout = service.GetBinderLayout(containerId);
        Assert.Equal(new[] { 2 }, layout.SheetSides);
        Assert.Equal(2, layout.TotalPages);
    }

    [Fact]
    public void RemoveBinderSheet_OnlySheet_Throws()
    {
        var (containerId, _) = SeedBinderWithSheets("Remove B", "2");
        var service = CreateService();

        Assert.Throws<InvalidOperationException>(() => service.RemoveBinderSheet(containerId, page: 1));
    }

    [Fact]
    public void InsertBinderSheet_ShiftsLaterCardsUp_AndAddsEmptyPages()
    {
        var (containerId, productId) = SeedBinderWithSheets("Insert A", "2,2"); // pages 1-4
        var early = PlaceLot(containerId, productId, page: 1, slot: 0); // sheet 0, before insert
        var late = PlaceLot(containerId, productId, page: 3, slot: 2);  // sheet 1, after insert
        var service = CreateService();

        // Insert a double-sided sheet before sheet 1 (pages 3-4).
        service.InsertBinderSheet(containerId, insertIndex: 1, doubleSided: true);

        using var ctx = new OmniCardDbContext(_options);
        var earlyLot = ctx.Lots.Single(l => l.Id == early);
        var lateLot = ctx.Lots.Single(l => l.Id == late);

        Assert.Equal(1, earlyLot.Page); // untouched
        Assert.Equal(0, earlyLot.Slot);
        Assert.Equal(5, lateLot.Page);  // shifted up by 2 (the new sheet's sides)
        Assert.Equal(2, lateLot.Slot);  // slot preserved

        var layout = service.GetBinderLayout(containerId);
        Assert.Equal(new[] { 2, 2, 2 }, layout.SheetSides);
        Assert.Equal(6, layout.TotalPages);
    }

    [Fact]
    public void MoveBinderSheet_RenumbersEveryShiftedCard_SlotsPreserved()
    {
        var (containerId, productId) = SeedBinderWithSheets("Move A", "2,2,2"); // [1,2][3,4][5,6]
        var a = PlaceLot(containerId, productId, page: 1, slot: 3); // sheet 0
        var b = PlaceLot(containerId, productId, page: 3, slot: 4); // sheet 1
        var c = PlaceLot(containerId, productId, page: 5, slot: 5); // sheet 2
        var service = CreateService();

        // Move the first sheet (page 1) to the end (insertion index 2 among the other two sheets).
        service.MoveBinderSheet(containerId, fromPage: 1, toIndex: 2);

        using var ctx = new OmniCardDbContext(_options);
        var lotA = ctx.Lots.Single(l => l.Id == a);
        var lotB = ctx.Lots.Single(l => l.Id == b);
        var lotC = ctx.Lots.Single(l => l.Id == c);

        // New order B,C,A -> pages B=1-2, C=3-4, A=5-6.
        Assert.Equal(5, lotA.Page);
        Assert.Equal(3, lotA.Slot); // slot preserved
        Assert.Equal(1, lotB.Page);
        Assert.Equal(4, lotB.Slot);
        Assert.Equal(3, lotC.Page);
        Assert.Equal(5, lotC.Slot);

        var layout = service.GetBinderLayout(containerId);
        Assert.Equal(new[] { 2, 2, 2 }, layout.SheetSides);
        Assert.Equal(6, layout.TotalPages);
    }

    [Fact]
    public void GetSheets_ReturnsAllSheetsWithCounts()
    {
        var (containerId, productId) = SeedBinderWithSheets("Sheets", "2,1,2"); // pages [1,2][3][4,5]
        PlaceLot(containerId, productId, page: 1, slot: 0);
        PlaceLot(containerId, productId, page: 2, slot: 1);
        PlaceLot(containerId, productId, page: 4, slot: 0);
        var service = CreateService();

        var sheets = service.GetSheets(containerId);

        Assert.Equal(3, sheets.Count);
        Assert.Equal(new[] { 1, 2 }, sheets[0].Pages);
        Assert.Equal(2, sheets[0].CardCount);
        Assert.Equal(new[] { 3 }, sheets[1].Pages);
        Assert.Equal(0, sheets[1].CardCount);
        Assert.Equal(new[] { 4, 5 }, sheets[2].Pages);
        Assert.Equal(1, sheets[2].CardCount);
    }

    [Fact]
    public void GetSheetForPage_ReturnsRangeAndCardCount()
    {
        var (containerId, productId) = SeedBinderWithSheets("Sheet Info", "2,2"); // pages 1-4
        PlaceLot(containerId, productId, page: 3, slot: 0); // front of sheet 1
        PlaceLot(containerId, productId, page: 4, slot: 1); // back of sheet 1
        var service = CreateService();

        var info = service.GetSheetForPage(containerId, page: 4);

        Assert.Equal(1, info.SheetIndex);
        Assert.Equal(3, info.FirstPage);
        Assert.Equal(2, info.Sides);
        Assert.Equal(2, info.TotalSheets);
        Assert.Equal(2, info.CardCount); // both sides of the sheet counted
        Assert.Equal(new[] { 3, 4 }, info.Pages);
    }

    [Fact]
    public void SetAlwaysAvailable_TogglesFlag_AndIsAlwaysAvailableReflectsIt()
    {
        using (var ctx = new OmniCardDbContext(_options))
        {
            ctx.StorageContainers.Add(new StorageContainer { Name = "Trade Box", ContainerType = ContainerType.Box });
            ctx.SaveChanges();
        }
        var id = new OmniCardDbContext(_options).StorageContainers.Single(c => c.Name == "Trade Box").Id;
        var service = CreateService();

        service.SetAlwaysAvailable(id, true);

        var updated = new OmniCardDbContext(_options).StorageContainers.Single(c => c.Id == id);
        Assert.True(updated.AlwaysAvailable);
        Assert.True(updated.IsAlwaysAvailable);

        service.SetAlwaysAvailable(id, false);
        var reverted = new OmniCardDbContext(_options).StorageContainers.Single(c => c.Id == id);
        Assert.False(reverted.AlwaysAvailable);
        Assert.False(reverted.IsAlwaysAvailable);
    }

    [Fact]
    public void SetAlwaysAvailable_SystemBulk_IsNoOp_ButStillAlwaysAvailable()
    {
        using (var ctx = new OmniCardDbContext(_options))
        {
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true, SortOrder = 0,
            });
            ctx.SaveChanges();
        }
        var bulkId = new OmniCardDbContext(_options).StorageContainers.Single(c => c.IsSystem).Id;
        var service = CreateService();

        // Attempting to turn always-available off on the system location does nothing.
        service.SetAlwaysAvailable(bulkId, false);

        var bulk = new OmniCardDbContext(_options).StorageContainers.Single(c => c.Id == bulkId);
        Assert.False(bulk.AlwaysAvailable);   // stored flag untouched
        Assert.True(bulk.IsAlwaysAvailable);  // but always available intrinsically
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
