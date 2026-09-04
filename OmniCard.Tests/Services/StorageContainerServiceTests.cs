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
    public void ShiftPage_ThisAndAfter_TowardBack_MovesThisPageAndTrailingPages_SlotsPreserved()
    {
        var (containerId, productId) = SeedBinderWithSheets("Shift A", "2,2,2"); // pages 1-6
        var before = PlaceLot(containerId, productId, page: 1, slot: 0); // stays
        var here = PlaceLot(containerId, productId, page: 3, slot: 2);
        var after = PlaceLot(containerId, productId, page: 5, slot: 4);
        var service = CreateService();

        // From page 3, push this page and everything after one page toward the back.
        service.ShiftPage(containerId, page: 3, deltaPages: 1, scope: BinderShiftScope.ThisAndAfter);

        using var ctx = new OmniCardDbContext(_options);
        Assert.Equal(1, ctx.Lots.Single(l => l.Id == before).Page); // untouched (before the block)
        Assert.Equal(4, ctx.Lots.Single(l => l.Id == here).Page);   // 3 -> 4
        Assert.Equal(2, ctx.Lots.Single(l => l.Id == here).Slot);   // slot preserved
        Assert.Equal(6, ctx.Lots.Single(l => l.Id == after).Page);  // 5 -> 6
    }

    [Fact]
    public void ShiftPage_OnlyThisPage_MovesJustThatPage()
    {
        var (containerId, productId) = SeedBinderWithSheets("Shift B", "2,2"); // pages 1-4
        var here = PlaceLot(containerId, productId, page: 2, slot: 1);
        var neighbor = PlaceLot(containerId, productId, page: 4, slot: 0); // empty slot 1 on page 3 target? no, target is page 3
        var service = CreateService();

        // Shift only page 2's cards to page 3 (page 3 is empty -> no collision).
        service.ShiftPage(containerId, page: 2, deltaPages: 1, scope: BinderShiftScope.OnlyThisPage);

        using var ctx = new OmniCardDbContext(_options);
        Assert.Equal(3, ctx.Lots.Single(l => l.Id == here).Page);     // 2 -> 3
        Assert.Equal(1, ctx.Lots.Single(l => l.Id == here).Slot);
        Assert.Equal(4, ctx.Lots.Single(l => l.Id == neighbor).Page); // unrelated page, untouched
    }

    [Fact]
    public void ShiftPage_ThisAndBefore_TowardFront_MovesHeadBlock()
    {
        var (containerId, productId) = SeedBinderWithSheets("Shift C", "2,2"); // pages 1-4
        // Pages 2-3 are one too far back; page 1 is empty. Pull the head block (pages <=3) forward.
        var p2 = PlaceLot(containerId, productId, page: 2, slot: 0);
        var p3 = PlaceLot(containerId, productId, page: 3, slot: 1);
        var tail = PlaceLot(containerId, productId, page: 4, slot: 2); // stays
        var service = CreateService();

        service.ShiftPage(containerId, page: 3, deltaPages: -1, scope: BinderShiftScope.ThisAndBefore);

        using var ctx = new OmniCardDbContext(_options);
        Assert.Equal(1, ctx.Lots.Single(l => l.Id == p2).Page); // 2 -> 1
        Assert.Equal(2, ctx.Lots.Single(l => l.Id == p3).Page); // 3 -> 2
        Assert.Equal(4, ctx.Lots.Single(l => l.Id == tail).Page); // after the block, untouched
    }

    [Fact]
    public void ShiftPage_WouldFallOffEnd_Throws_AndLeavesEverythingUnchanged()
    {
        var (containerId, productId) = SeedBinderWithSheets("Shift D", "2,2"); // pages 1-4
        var onLast = PlaceLot(containerId, productId, page: 4, slot: 0);
        var here = PlaceLot(containerId, productId, page: 3, slot: 1);
        var service = CreateService();

        // Page 4 (part of the "this and after" block from page 3) + 1 = 5 runs off the binder.
        Assert.Throws<InvalidOperationException>(() =>
            service.ShiftPage(containerId, page: 3, deltaPages: 1, scope: BinderShiftScope.ThisAndAfter));

        using var ctx = new OmniCardDbContext(_options);
        Assert.Equal(4, ctx.Lots.Single(l => l.Id == onLast).Page); // unchanged
        Assert.Equal(3, ctx.Lots.Single(l => l.Id == here).Page);   // unchanged
    }

    [Fact]
    public void ShiftPage_OnlyThisPage_OntoOccupiedNeighbor_Throws_Collision()
    {
        var (containerId, productId) = SeedBinderWithSheets("Shift E", "2,2"); // pages 1-4
        var here = PlaceLot(containerId, productId, page: 2, slot: 0);
        var blocker = PlaceLot(containerId, productId, page: 3, slot: 0); // occupies the target slot, not moving
        var service = CreateService();

        Assert.Throws<InvalidOperationException>(() =>
            service.ShiftPage(containerId, page: 2, deltaPages: 1, scope: BinderShiftScope.OnlyThisPage));

        using var ctx = new OmniCardDbContext(_options);
        Assert.Equal(2, ctx.Lots.Single(l => l.Id == here).Page);    // unchanged
        Assert.Equal(3, ctx.Lots.Single(l => l.Id == blocker).Page); // unchanged
    }

    [Fact]
    public void ShiftPage_LeavesUnplacedCardsUntouched()
    {
        var (containerId, productId) = SeedBinderWithSheets("Shift F", "2,2"); // pages 1-4
        var placed = PlaceLot(containerId, productId, page: 1, slot: 0);
        var unplaced = PlaceLot(containerId, productId, page: 1, slot: 1);
        using (var seed = new OmniCardDbContext(_options))
        {
            var u = seed.Lots.Single(l => l.Id == unplaced);
            u.Page = null; u.Slot = null;
            seed.SaveChanges();
        }
        var service = CreateService();

        service.ShiftPage(containerId, page: 1, deltaPages: 1, scope: BinderShiftScope.ThisAndAfter);

        using var ctx = new OmniCardDbContext(_options);
        Assert.Equal(2, ctx.Lots.Single(l => l.Id == placed).Page); // shifted
        Assert.Null(ctx.Lots.Single(l => l.Id == unplaced).Page);   // still unplaced
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

    [Fact]
    public void Create_DuplicateName_CaseInsensitive_Throws()
    {
        var service = CreateService();
        service.Create("Trades", ContainerType.Box);

        var ex = Assert.Throws<InvalidOperationException>(() => service.Create("trades", ContainerType.Binder));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void Create_ReservedBulkName_Throws()
    {
        using (var ctx = new OmniCardDbContext(_options))
        {
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true, SortOrder = 0,
            });
            ctx.SaveChanges();
        }
        var service = CreateService();

        Assert.Throws<InvalidOperationException>(() => service.Create("bulk", ContainerType.Box));
    }

    [Fact]
    public void Rename_ToExistingName_Throws()
    {
        var service = CreateService();
        service.Create("Box One", ContainerType.Box);
        var two = service.Create("Box Two", ContainerType.Box);

        Assert.Throws<InvalidOperationException>(() => service.Rename(two.Id, "Box One"));
    }

    [Fact]
    public void Rename_ToOwnName_Allowed()
    {
        var service = CreateService();
        var box = service.Create("Keep Me", ContainerType.Box);

        service.Rename(box.Id, "Keep Me"); // same name, only case/whitespace could differ

        var updated = new OmniCardDbContext(_options).StorageContainers.Single(c => c.Id == box.Id);
        Assert.Equal("Keep Me", updated.Name);
    }

    [Fact]
    public void NameExists_RespectsExcludeId_AndCaseInsensitivity()
    {
        var service = CreateService();
        var box = service.Create("Shelf", ContainerType.Box);

        Assert.True(service.NameExists("SHELF"));
        Assert.False(service.NameExists("Shelf", excludeId: box.Id));
        Assert.False(service.NameExists("Other"));
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
