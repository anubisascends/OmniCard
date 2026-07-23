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
    private readonly IDbContextFactory<OmniCardDbContext> _factory;

    public WebPageTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
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

    private static Product NewSingle(string gameCardId, string name, string setName, string setCode, string number, string rarity) => new()
    {
        Game = CardGame.Mtg,
        Category = ProductCategory.Single,
        GameCardId = gameCardId,
        Name = name,
        SetName = setName,
        SetCode = setCode,
        CollectorNumber = number,
        Rarity = rarity,
    };

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

            var pa = NewSingle("c1", "A", "Alpha", "LEA", "1", "common");
            var pb = NewSingle("c2", "B", "Alpha", "LEA", "2", "common");
            var pc = NewSingle("c3", "C", "Beta", "LEB", "1", "rare");
            ctx.Products.AddRange(pa, pb, pc);
            ctx.SaveChanges();

            ctx.Lots.AddRange(
                new InventoryLot { ProductId = pa.Id, LocationId = containerId },
                new InventoryLot { ProductId = pb.Id, LocationId = containerId },
                new InventoryLot { ProductId = pc.Id, LocationId = containerId });
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
        int lotId;
        using (var ctx = _factory.CreateDbContext())
        {
            var container = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();

            var product = NewSingle("test-id", "Lightning Bolt", "Alpha", "LEA", "161", "common");
            product.ImageUri = "https://img/bolt.jpg";
            ctx.Products.Add(product);
            ctx.SaveChanges();

            var lot = new InventoryLot { ProductId = product.Id, LocationId = container.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;
        }

        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(lotId);

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
        int lotId;
        using (var ctx = _factory.CreateDbContext())
        {
            var product = NewSingle("scan-card", "Scanned Card", "Set", "SET", "1", "common");
            product.ImageUri = "https://api.example.com/card.jpg";
            ctx.Products.Add(product);
            ctx.SaveChanges();

            var lot = new InventoryLot { ProductId = product.Id, ScanImagePath = "scans/12345.jpg" };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;
        }

        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        model.OnGet(lotId);

        // ScanImagePath takes precedence: extracts filename → /scans/12345.jpg
        Assert.Equal("/scans/12345.jpg", model.ImageUrl);
    }

    // --- IndexModel game filter (mtg/optcg/riftbound + new pokemon/yugioh/fftcg) ---

    [Theory]
    [InlineData("pokemon", CardGame.Pokemon)]
    [InlineData("yugioh", CardGame.YuGiOh)]
    [InlineData("fftcg", CardGame.FinalFantasy)]
    public void IndexModel_OnGet_FiltersContainersByNewGameCodes(string gameCode, CardGame expectedGame)
    {
        int matchingContainerId, otherContainerId;
        using (var ctx = _factory.CreateDbContext())
        {
            var matchingContainer = new StorageContainer { Name = "Matching", ContainerType = ContainerType.Box };
            var otherContainer = new StorageContainer { Name = "Other", ContainerType = ContainerType.Box };
            ctx.StorageContainers.AddRange(matchingContainer, otherContainer);
            ctx.SaveChanges();
            matchingContainerId = matchingContainer.Id;
            otherContainerId = otherContainer.Id;

            var matchingProduct = NewSingle("m1", "Matching Card", "Set", "SET", "1", "common");
            matchingProduct.Game = expectedGame;
            var otherProduct = NewSingle("o1", "Other Card", "Set", "SET", "1", "common");
            otherProduct.Game = CardGame.Mtg;
            ctx.Products.AddRange(matchingProduct, otherProduct);
            ctx.SaveChanges();

            ctx.Lots.AddRange(
                new InventoryLot { ProductId = matchingProduct.Id, LocationId = matchingContainerId },
                new InventoryLot { ProductId = otherProduct.Id, LocationId = otherContainerId });
            ctx.SaveChanges();
        }

        var model = new IndexModel(_factory) { PageContext = CreatePageContext(), Game = gameCode };
        model.OnGet();

        Assert.Single(model.Containers);
        Assert.Equal("Matching", model.Containers[0].Name);
    }

    // --- CardModel ExtendedData rendering ---

    [Fact]
    public void CardModel_OnGet_PopulatesExtendedDataJson_ForPokemonCard()
    {
        const string extendedJson = """[{"name":"HP","value":"90"}]""";

        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(connection).Options;
            var pokemonFactory = new TestPokemonDbContextFactory(options);
            using (var pokemonCtx = pokemonFactory.CreateDbContext())
            {
                pokemonCtx.Database.EnsureCreated();
                pokemonCtx.Cards.Add(new TcgCsvCard
                {
                    ProductId = 12345,
                    Game = CardGame.Pokemon,
                    Name = "Pikachu",
                    ExtendedDataJson = extendedJson,
                });
                pokemonCtx.SaveChanges();
            }

            int lotId;
            using (var ctx = _factory.CreateDbContext())
            {
                var product = NewSingle("12345", "Pikachu", "Base Set", "BS", "58", "common");
                product.Game = CardGame.Pokemon;
                ctx.Products.Add(product);
                ctx.SaveChanges();

                var lot = new InventoryLot { ProductId = product.Id };
                ctx.Lots.Add(lot);
                ctx.SaveChanges();
                lotId = lot.Id;
            }

            var model = new CardModel(_factory, pokemonFactory) { PageContext = CreatePageContext() };
            var result = model.OnGet(lotId);

            Assert.IsType<PageResult>(result);
            Assert.Equal(extendedJson, model.ExtendedDataJson);
            var parsed = ExtendedDataParser.Parse(model.ExtendedDataJson);
            Assert.Single(parsed);
            Assert.Equal("HP", parsed[0].Key);
            Assert.Equal("90", parsed[0].Value);
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public void CardModel_OnGet_ExtendedDataJson_NullForNonTcgCsvGame()
    {
        int lotId;
        using (var ctx = _factory.CreateDbContext())
        {
            var product = NewSingle("mtg-1", "Lightning Bolt", "Alpha", "LEA", "161", "common");
            ctx.Products.Add(product);
            ctx.SaveChanges();

            var lot = new InventoryLot { ProductId = product.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;
        }

        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        model.OnGet(lotId);

        Assert.Null(model.ExtendedDataJson);
    }

    [Fact]
    public void CardModel_OnGet_MissingCatalogDb_DoesNotThrow_ExtendedDataJsonNull()
    {
        // Simulate a missing pokemon.db: a read-only connection string pointing at a file
        // that doesn't exist. SQLite refuses to create the file in Mode=ReadOnly, so opening
        // it throws SqliteException (SQLITE_CANTOPEN) — this is what QueryExtendedData must
        // catch instead of letting bubble up into a 500.
        var missingDbPath = Path.Combine(Path.GetTempPath(), $"omnicard-missing-{Guid.NewGuid():N}.db");
        Assert.False(File.Exists(missingDbPath));

        var options = new DbContextOptionsBuilder<PokemonDbContext>()
            .UseSqlite($"Data Source={missingDbPath};Mode=ReadOnly")
            .Options;
        var pokemonFactory = new TestPokemonDbContextFactory(options);

        int lotId;
        using (var ctx = _factory.CreateDbContext())
        {
            var product = NewSingle("12345", "Pikachu", "Base Set", "BS", "58", "common");
            product.Game = CardGame.Pokemon;
            ctx.Products.Add(product);
            ctx.SaveChanges();

            var lot = new InventoryLot { ProductId = product.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;
        }

        var model = new CardModel(_factory, pokemonFactory) { PageContext = CreatePageContext() };

        var exception = Record.Exception(() => model.OnGet(lotId));

        Assert.Null(exception);
        Assert.Null(model.ExtendedDataJson);
    }

    private class TestDbContextFactory(DbContextOptions<OmniCardDbContext> options)
        : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private class TestPokemonDbContextFactory(DbContextOptions<PokemonDbContext> options)
        : IDbContextFactory<PokemonDbContext>
    {
        public PokemonDbContext CreateDbContext() => new(options);
    }
}
