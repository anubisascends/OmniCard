using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class StorageContainerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;

    public StorageContainerServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed Bulk container
        ctx.StorageContainers.Add(new StorageContainer
        {
            Name = "Bulk",
            ContainerType = ContainerType.Bulk,
            IsSystem = true,
            SortOrder = 0,
        });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private StorageContainerService CreateService() => new(_factory);

    private static Product NewSingle(string gameCardId, string name) => new()
    {
        Game = CardGame.Mtg,
        Category = ProductCategory.Single,
        GameCardId = gameCardId,
        Name = name,
        SetName = "S",
        SetCode = "S",
        CollectorNumber = "1",
        Rarity = "common",
    };

    [Fact]
    public void GetAll_ReturnsBulkFirst()
    {
        var svc = CreateService();
        var all = svc.GetAll();
        Assert.NotEmpty(all);
        Assert.Equal("Bulk", all[0].Name);
    }

    [Fact]
    public void GetBulk_ReturnsSystemContainer()
    {
        var svc = CreateService();
        var bulk = svc.GetBulk();
        Assert.Equal("Bulk", bulk.Name);
        Assert.True(bulk.IsSystem);
        Assert.Equal(ContainerType.Bulk, bulk.ContainerType);
    }

    [Fact]
    public void Create_AddsContainer()
    {
        var svc = CreateService();
        var container = svc.Create("Red Binder", ContainerType.Binder);
        Assert.True(container.Id > 0);
        Assert.Equal("Red Binder", container.Name);
        Assert.Equal(ContainerType.Binder, container.ContainerType);
        Assert.False(container.IsSystem);

        var all = svc.GetAll();
        Assert.Equal(2, all.Count); // Bulk + Red Binder
    }

    [Fact]
    public void Create_SetsIncrementingSortOrder()
    {
        var svc = CreateService();
        var c1 = svc.Create("Binder A", ContainerType.Binder);
        var c2 = svc.Create("Binder B", ContainerType.Binder);
        Assert.True(c2.SortOrder > c1.SortOrder);
    }

    [Fact]
    public void Rename_UpdatesName()
    {
        var svc = CreateService();
        var container = svc.Create("Old Name", ContainerType.Box);
        svc.Rename(container.Id, "New Name");

        var all = svc.GetAll();
        Assert.Contains(all, c => c.Name == "New Name");
    }

    [Fact]
    public void Rename_ThrowsForSystemContainer()
    {
        var svc = CreateService();
        var bulk = svc.GetBulk();
        Assert.Throws<InvalidOperationException>(() => svc.Rename(bulk.Id, "Not Bulk"));
    }

    [Fact]
    public void Delete_RemovesContainer()
    {
        var svc = CreateService();
        var container = svc.Create("To Delete", ContainerType.DeckBox);
        svc.Delete(container.Id);

        var all = svc.GetAll();
        Assert.DoesNotContain(all, c => c.Name == "To Delete");
    }

    [Fact]
    public void Delete_ThrowsForSystemContainer()
    {
        var svc = CreateService();
        var bulk = svc.GetBulk();
        Assert.Throws<InvalidOperationException>(() => svc.Delete(bulk.Id));
    }

    [Fact]
    public void Delete_WithMoveCardsToBulk_MovesCardsToSystem()
    {
        var svc = CreateService();
        var container = svc.Create("Container With Cards", ContainerType.Box);
        var bulk = svc.GetBulk();

        // Add a card to this container
        using (var ctx = _factory.CreateDbContext())
        {
            var product = NewSingle("test", "Test");
            ctx.Products.Add(product);
            ctx.SaveChanges();
            ctx.Lots.Add(new InventoryLot { ProductId = product.Id, LocationId = container.Id });
            ctx.SaveChanges();
        }

        // Delete with moveCardsToBulk = true
        svc.Delete(container.Id, moveCardsToBulk: true);

        // Verify container is deleted
        var all = svc.GetAll();
        Assert.DoesNotContain(all, c => c.Name == "Container With Cards");

        // Verify card was moved to bulk
        using (var ctx = _factory.CreateDbContext())
        {
            var lot = ctx.Lots.Include(l => l.Product).First(l => l.Product.Name == "Test");
            Assert.Equal(bulk.Id, lot.LocationId);
            Assert.Null(lot.Page);
            Assert.Null(lot.Slot);
            Assert.Null(lot.Section);
        }
    }

    [Fact]
    public void Delete_WithDeleteCards_RemovesCards()
    {
        var svc = CreateService();
        var container = svc.Create("Container With Cards", ContainerType.Box);

        // Add a card to this container
        using (var ctx = _factory.CreateDbContext())
        {
            var product = NewSingle("test", "Test");
            ctx.Products.Add(product);
            ctx.SaveChanges();
            ctx.Lots.Add(new InventoryLot { ProductId = product.Id, LocationId = container.Id });
            ctx.SaveChanges();
        }

        // Delete with moveCardsToBulk = false
        svc.Delete(container.Id, moveCardsToBulk: false);

        // Verify container is deleted
        var all = svc.GetAll();
        Assert.DoesNotContain(all, c => c.Name == "Container With Cards");

        // Verify card was deleted
        using (var ctx = _factory.CreateDbContext())
        {
            Assert.Empty(ctx.Lots.Include(l => l.Product).Where(l => l.Product.Name == "Test"));
        }
    }

    [Fact]
    public void GetCardCount_ReturnsCorrectCount()
    {
        var svc = CreateService();
        var container = svc.Create("Box", ContainerType.Box);

        using var ctx = _factory.CreateDbContext();
        var a = NewSingle("a", "A");
        var b = NewSingle("b", "B");
        ctx.Products.AddRange(a, b);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = a.Id, LocationId = container.Id });
        ctx.Lots.Add(new InventoryLot { ProductId = b.Id, LocationId = container.Id });
        ctx.SaveChanges();

        Assert.Equal(2, svc.GetCardCount(container.Id));
    }

    [Fact]
    public void SetCoverCard_UpdatesCoverCardId()
    {
        var svc = CreateService();
        var container = svc.Create("Box", ContainerType.Box);

        // Add a card
        int cardId;
        using (var ctx = _factory.CreateDbContext())
        {
            var product = NewSingle("cover", "Cover Card");
            ctx.Products.Add(product);
            ctx.SaveChanges();
            var lot = new InventoryLot { ProductId = product.Id, LocationId = container.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            cardId = lot.Id;
        }

        // Set cover card
        svc.SetCoverCard(container.Id, cardId);

        // Verify
        using (var ctx = _factory.CreateDbContext())
        {
            var updated = ctx.StorageContainers.Find(container.Id);
            Assert.NotNull(updated);
            Assert.Equal(cardId, updated.CoverCardId);
        }
    }

    [Fact]
    public void SetCoverCard_CanClearCoverCard()
    {
        var svc = CreateService();
        var container = svc.Create("Box", ContainerType.Box);

        // Add a card
        int cardId;
        using (var ctx = _factory.CreateDbContext())
        {
            var product = NewSingle("cover", "Cover Card");
            ctx.Products.Add(product);
            ctx.SaveChanges();
            var lot = new InventoryLot { ProductId = product.Id, LocationId = container.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            cardId = lot.Id;
        }

        // Set then clear cover card
        svc.SetCoverCard(container.Id, cardId);
        svc.SetCoverCard(container.Id, null);

        // Verify
        using (var ctx = _factory.CreateDbContext())
        {
            var updated = ctx.StorageContainers.Find(container.Id);
            Assert.NotNull(updated);
            Assert.Null(updated.CoverCardId);
        }
    }

    [Fact]
    public void GetCardsInContainer_ReturnsAllCards()
    {
        var svc = CreateService();
        var container = svc.Create("Box", ContainerType.Box);

        using (var ctx = _factory.CreateDbContext())
        {
            var bCard = NewSingle("a", "B Card");
            var aCard = NewSingle("b", "A Card");
            ctx.Products.AddRange(bCard, aCard);
            ctx.SaveChanges();
            ctx.Lots.Add(new InventoryLot { ProductId = bCard.Id, LocationId = container.Id });
            ctx.Lots.Add(new InventoryLot { ProductId = aCard.Id, LocationId = container.Id });
            ctx.SaveChanges();
        }

        var cards = svc.GetCardsInContainer(container.Id);
        Assert.Equal(2, cards.Count);
        // Should be ordered by name
        Assert.Equal("A Card", cards[0].Name);
        Assert.Equal("B Card", cards[1].Name);
    }

    [Fact]
    public void GetCardsInContainer_ReturnsEmptyForEmptyContainer()
    {
        var svc = CreateService();
        var container = svc.Create("Box", ContainerType.Box);

        var cards = svc.GetCardsInContainer(container.Id);
        Assert.Empty(cards);
    }

    private class TestDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
