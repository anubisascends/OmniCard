using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Binder;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Storage;

namespace OmniCard.Tests.Services.Inventory;

public class BinderDividerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public BinderDividerServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private BinderDividerService Dividers() => new(new Factory(_options));
    private StorageContainerService Containers() => new(new Factory(_options));

    private int SeedBinder(string sheetSides, ContainerType type = ContainerType.Binder)
    {
        using var ctx = new OmniCardDbContext(_options);
        var layout = BinderSheetLayout.Parse(sheetSides, 0);
        var container = new StorageContainer
        {
            Name = $"Binder {Guid.NewGuid():N}",
            ContainerType = type,
            SheetSides = layout.Serialize(),
            TotalPages = layout.TotalPages,
        };
        ctx.StorageContainers.Add(container);
        ctx.SaveChanges();
        return container.Id;
    }

    private void PlaceLot(int containerId, int page, int slot)
    {
        using var ctx = new OmniCardDbContext(_options);
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Card" };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = containerId, Page = page, Slot = slot });
        ctx.SaveChanges();
    }

    private Dictionary<string, int> PagesByName(int containerId)
        => Dividers().GetForBinder(containerId).ToDictionary(d => d.Name, d => d.Page);

    [Fact]
    public void Add_StoresTrimmedName_KnownColor_AndListsInPageOrder()
    {
        var id = SeedBinder("2,2,2");
        var svc = Dividers();

        svc.Add(id, 5, "  Showcase  ", "BLUE");
        svc.Add(id, 3, "Borderless", "not-a-color");

        var list = svc.GetForBinder(id);
        Assert.Equal(["Borderless", "Showcase"], list.Select(d => d.Name));
        Assert.Equal([3, 5], list.Select(d => d.Page));
        Assert.Null(list[0].Color);
        Assert.Equal("blue", list[1].Color);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(7, "A")]
    [InlineData(1, "   ")]
    public void Add_RejectsPagesOutsideTheBinder_AndBlankNames(int page, string name)
    {
        var id = SeedBinder("2,2,2"); // pages 1-6
        Assert.Throws<InvalidOperationException>(() => Dividers().Add(id, page, name, null));
    }

    [Fact]
    public void Add_OnlyBinders()
    {
        var id = SeedBinder("2", ContainerType.Box);
        Assert.Throws<InvalidOperationException>(() => Dividers().Add(id, 1, "A", null));
    }

    [Fact]
    public void Update_RenamesRecolorsAndMoves_NullKeepsField_EmptyColorClears()
    {
        var id = SeedBinder("2,2,2");
        var svc = Dividers();
        var d = svc.Add(id, 2, "Red", "red");

        svc.Update(id, d.Id, name: "White", color: null, page: null);
        Assert.Equal(("White", "red", 2), Snapshot(svc, id));

        svc.Update(id, d.Id, name: null, color: "", page: 4);
        Assert.Equal(("White", (string?)null, 4), Snapshot(svc, id));
    }

    private static (string, string?, int) Snapshot(BinderDividerService svc, int id)
    {
        var d = svc.GetForBinder(id).Single();
        return (d.Name, d.Color, d.Page);
    }

    [Fact]
    public void Update_And_Remove_OnlyTouchTheirOwnBinder()
    {
        var a = SeedBinder("2");
        var b = SeedBinder("2");
        var svc = Dividers();
        var d = svc.Add(a, 1, "A", null);

        Assert.Throws<KeyNotFoundException>(() => svc.Update(b, d.Id, "X", null, null));
        Assert.False(svc.Remove(b, d.Id));
        Assert.True(svc.Remove(a, d.Id));
        Assert.Empty(svc.GetForBinder(a));
    }

    [Fact]
    public void InsertSheet_BeforeADividersPage_CarriesItAlong()
    {
        var id = SeedBinder("2,2,2"); // [1,2][3,4][5,6]
        var svc = Dividers();
        svc.Add(id, 1, "Start", null);
        svc.Add(id, 3, "Borderless", null);

        Containers().InsertBinderSheet(id, insertIndex: 1, doubleSided: true); // after page 2

        Assert.Equal(1, PagesByName(id)["Start"]);
        Assert.Equal(5, PagesByName(id)["Borderless"]); // the new sheet extends the section before it
    }

    [Fact]
    public void RemoveSheet_HoldingADivider_HandsItToTheNextPage()
    {
        var id = SeedBinder("2,2,2"); // [1,2][3,4][5,6]
        var svc = Dividers();
        svc.Add(id, 3, "Borderless", null);
        svc.Add(id, 5, "Showcase", null);

        Containers().RemoveBinderSheet(id, page: 3); // removes pages 3-4

        Assert.Equal(3, PagesByName(id)["Borderless"]); // what was page 5 is now page 3
        Assert.Equal(3, PagesByName(id)["Showcase"]);
    }

    [Fact]
    public void RemoveLastSheet_HoldingADivider_KeepsItOnTheNewLastPage()
    {
        var id = SeedBinder("2,2"); // [1,2][3,4]
        var svc = Dividers();
        svc.Add(id, 4, "End", null);

        Containers().RemoveBinderSheet(id, page: 4);

        Assert.Equal(2, PagesByName(id)["End"]);
    }

    [Fact]
    public void MoveSheet_DividerFollowsItsPage()
    {
        var id = SeedBinder("2,2,2"); // [1,2][3,4][5,6]
        Dividers().Add(id, 1, "First sheet", null);

        Containers().MoveBinderSheet(id, fromPage: 1, toIndex: 2); // to the end

        Assert.Equal(5, PagesByName(id)["First sheet"]);
    }

    [Fact]
    public void ShiftPage_ThisAndAfter_CarriesDividersOnShiftedPages()
    {
        var id = SeedBinder("2,2,2,2"); // pages 1-8
        PlaceLot(id, 3, 0);
        PlaceLot(id, 5, 0);
        var svc = Dividers();
        svc.Add(id, 2, "Before", null);
        svc.Add(id, 5, "Showcase", null);

        Containers().ShiftPage(id, page: 3, deltaPages: 1, BinderShiftScope.ThisAndAfter);

        Assert.Equal(2, PagesByName(id)["Before"]);
        Assert.Equal(6, PagesByName(id)["Showcase"]);
    }

    [Fact]
    public void DeletingTheBinder_DeletesItsDividers()
    {
        var id = SeedBinder("2");
        Dividers().Add(id, 1, "A", null);

        Containers().Delete(id, moveCardsToBulk: false); // no Bulk location is seeded here

        using var ctx = new OmniCardDbContext(_options);
        Assert.Empty(ctx.BinderDividers);
    }

    [Theory]
    [InlineData(2, 2)]  // before the removed sheet: unchanged (not in the map)
    [InlineData(3, 3)]  // removed: next surviving page (old 5 → 3)
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 4)]
    public void Remap_RemovedMiddleSheet(int page, int expected)
    {
        var (_, remap) = BinderSheetLayout.Parse("2,2,2", 0).RemoveSheet(1);
        Assert.Equal(expected, BinderDivider.Remap(page, remap, newTotalPages: 4));
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
