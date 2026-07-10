using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Web.Pages;

namespace OmniCard.Tests.Web;

public class WebPageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _factory;

    public WebPageTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection).Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static PageContext CreatePageContext()
    {
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        return new PageContext(actionContext);
    }

    // --- IndexModel ---

    [Fact]
    public void IndexModel_OnGet_ReturnsContainersOrdered()
    {
        using (var ctx = _factory.CreateDbContext())
        {
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Zebra Box",
                ContainerType = ContainerType.Box,
                SortOrder = 2,
            });
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Alpha Binder",
                ContainerType = ContainerType.Binder,
                SortOrder = 1,
            });
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Beta Binder",
                ContainerType = ContainerType.Binder,
                SortOrder = 1,
            });
            ctx.SaveChanges();
        }

        var model = new IndexModel(_factory) { PageContext = CreatePageContext() };
        model.OnGet();

        Assert.Equal(3, model.Containers.Count);
        // SortOrder=1 first (Alpha < Beta by Name), then SortOrder=2
        Assert.Equal("Alpha Binder", model.Containers[0].Name);
        Assert.Equal("Beta Binder", model.Containers[1].Name);
        Assert.Equal("Zebra Box", model.Containers[2].Name);
    }

    // --- LocationModel ---

    [Fact]
    public void LocationModel_OnGet_ReturnsCardsGroupedBySet()
    {
        int containerId;
        using (var ctx = _factory.CreateDbContext())
        {
            var container = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
            containerId = container.Id;

            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "c1", Name = "A", SetName = "Alpha", SetCode = "LEA", Number = "1", Rarity = "common", ContainerId = containerId });
            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "c2", Name = "B", SetName = "Alpha", SetCode = "LEA", Number = "2", Rarity = "common", ContainerId = containerId });
            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "c3", Name = "C", SetName = "Beta", SetCode = "LEB", Number = "1", Rarity = "rare", ContainerId = containerId });
            ctx.SaveChanges();
        }

        var model = new LocationModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(containerId);

        Assert.IsType<PageResult>(result);
        Assert.Equal(3, model.CardCount);
        Assert.Equal(2, model.Sets.Count);
        Assert.Contains(model.Sets, s => s.SetCode == "LEA" && s.Count == 2);
        Assert.Contains(model.Sets, s => s.SetCode == "LEB" && s.Count == 1);
    }

    [Fact]
    public void LocationModel_OnGet_NonexistentContainer_Returns404()
    {
        var model = new LocationModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(99999);
        Assert.IsType<NotFoundResult>(result);
    }

    // --- CardModel ---

    [Fact]
    public void CardModel_OnGet_ReturnsCardWithContainer()
    {
        int cardId;
        using (var ctx = _factory.CreateDbContext())
        {
            var container = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();

            var card = new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "test-id",
                Name = "Lightning Bolt",
                SetName = "Alpha",
                SetCode = "LEA",
                Number = "161",
                Rarity = "common",
                ContainerId = container.Id,
                ImageUri = "https://img/bolt.jpg",
            };
            ctx.Cards.Add(card);
            ctx.SaveChanges();
            cardId = card.Id;
        }

        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(cardId);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Lightning Bolt", model.Card.Name);
        Assert.NotNull(model.Card.Container);
        Assert.Equal("Box", model.Card.Container!.Name);
    }

    [Fact]
    public void CardModel_OnGet_NonexistentCard_Returns404()
    {
        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(99999);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void CardModel_ImageUrl_ResolvesScanPathOverApiUri()
    {
        int cardId;
        using (var ctx = _factory.CreateDbContext())
        {
            var card = new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "scan-card",
                Name = "Scanned Card",
                SetName = "Set",
                SetCode = "SET",
                Number = "1",
                Rarity = "common",
                ScanImagePath = "scans/12345.jpg",
                ImageUri = "https://api.example.com/card.jpg",
            };
            ctx.Cards.Add(card);
            ctx.SaveChanges();
            cardId = card.Id;
        }

        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        model.OnGet(cardId);

        // ScanImagePath takes precedence: extracts filename → /scans/12345.jpg
        Assert.Equal("/scans/12345.jpg", model.ImageUrl);
    }

    private class TestDbContextFactory(DbContextOptions<CollectionDbContext> options)
        : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
