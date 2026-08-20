using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web;
using OmniCard.Web.Pages;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

public class WebPageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;

    // No game catalogs are registered in these tests, so art hydration is a no-op (GetGameService
    // throws → skipped). Cards keep whatever ImageUri the test set explicitly.
    private static readonly ICardService NoGameServices = new WebCardService([]);
    private readonly IDataPathService _dataPaths =
        new WebDataPathService(Path.Combine(Path.GetTempPath(), "omnicard-web-tests"));

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

        var model = new IndexModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
        model.OnGet();

        Assert.Equal(3, model.Containers.Count);
        // SortOrder=1 first (Alpha < Beta by Name), then SortOrder=2
        Assert.Equal("Alpha Binder", model.Containers[0].Name);
        Assert.Equal("Beta Binder", model.Containers[1].Name);
        Assert.Equal("Zebra Box", model.Containers[2].Name);
    }

    [Fact]
    public void IndexModel_Search_ProjectsTileFields_GroupedByNameAndSet()
    {
        using (var ctx = _factory.CreateDbContext())
        {
            // Two printings of the same Name+SetCode. The representative (lowest lot Id)
            // supplies SetName, image, and price; Quantity is the group count.
            var rep = NewSingle("rep", "Lightning Bolt", "Alpha", "LEA", "161", "common");
            rep.ImageUri = "https://img/bolt-rep.jpg";
            rep.LastMarketPrice = 12.50m;
            rep.Foil = true;
            var other = NewSingle("other", "Lightning Bolt", "Alpha", "LEA", "161", "common");
            other.ImageUri = "https://img/bolt-other.jpg";
            other.LastMarketPrice = 99.00m;
            ctx.Products.AddRange(rep, other);
            ctx.SaveChanges();

            ctx.Lots.AddRange(
                new InventoryLot { ProductId = rep.Id, Condition = "LP" },
                new InventoryLot { ProductId = other.Id });
            ctx.SaveChanges();
        }

        var model = new IndexModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext(), Q = "bolt" };
        model.OnGet();

        var result = Assert.Single(model.SearchResults);
        Assert.Equal("Lightning Bolt", result.Name);
        Assert.Equal("Alpha", result.SetName);
        Assert.Equal("LEA", result.SetCode);
        Assert.Equal(2, result.Quantity);
        Assert.Equal("https://img/bolt-rep.jpg", result.ImageUrl);
        Assert.Equal(12.50m, result.MarketPrice);
        // Sort/filter fields projected from the representative copy.
        Assert.Equal("LP", result.Condition);
        Assert.True(result.IsFoil);
    }

    [Fact]
    public void IndexModel_Search_NoPrice_YieldsNullMarketPrice()
    {
        using (var ctx = _factory.CreateDbContext())
        {
            var p = NewSingle("np", "Counterspell", "Alpha", "LEA", "54", "common");
            ctx.Products.Add(p);
            ctx.SaveChanges();
            ctx.Lots.Add(new InventoryLot { ProductId = p.Id });
            ctx.SaveChanges();
        }

        var model = new IndexModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext(), Q = "counterspell" };
        model.OnGet();

        var result = Assert.Single(model.SearchResults);
        Assert.Null(result.MarketPrice);
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

        var model = new LocationModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
        var result = model.OnGet(containerId);

        Assert.IsType<PageResult>(result);
        Assert.Equal(3, model.CardCount);
        Assert.Equal(2, model.Sets.Count);
        Assert.Contains(model.Sets, s => s.SetCode == "LEA" && s.Count == 2);
        Assert.Contains(model.Sets, s => s.SetCode == "LEB" && s.Count == 1);
    }

    [Fact]
    public void LocationModel_OnGet_ProjectsTileFields()
    {
        int containerId;
        using (var ctx = _factory.CreateDbContext())
        {
            var container = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
            containerId = container.Id;

            var bolt = NewSingle("b1", "Lightning Bolt", "Alpha", "LEA", "161", "common");
            bolt.ImageUri = "https://img/bolt.jpg";
            bolt.LastMarketPrice = 12.50m;
            bolt.Foil = true;
            var counter = NewSingle("c1", "Counterspell", "Alpha", "LEA", "54", "common");
            ctx.Products.AddRange(bolt, counter);
            ctx.SaveChanges();

            ctx.Lots.AddRange(
                new InventoryLot { ProductId = bolt.Id, LocationId = containerId },
                new InventoryLot { ProductId = bolt.Id, LocationId = containerId },
                new InventoryLot { ProductId = counter.Id, LocationId = containerId });
            ctx.SaveChanges();
        }

        var model = new LocationModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
        model.OnGet(containerId);

        var boltCard = Assert.Single(model.Cards, c => c.Name == "Lightning Bolt");
        Assert.Equal("Alpha", boltCard.SetName);
        Assert.Equal(2, boltCard.Quantity);
        Assert.Equal("https://img/bolt.jpg", boltCard.ImageUrl);
        Assert.Equal(12.50m, boltCard.MarketPrice);
        Assert.True(boltCard.IsFoil);
        Assert.Equal("NM", boltCard.Condition);

        var counterCard = Assert.Single(model.Cards, c => c.Name == "Counterspell");
        Assert.Null(counterCard.ImageUrl);
        Assert.Null(counterCard.MarketPrice);
    }

    [Fact]
    public void LocationModel_OnGet_NonexistentContainer_Returns404()
    {
        var model = new LocationModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
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

        var model = new CardModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
        var result = model.OnGet(lotId);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Lightning Bolt", model.Card.Name);
        Assert.NotNull(model.Card.Container);
        Assert.Equal("Box", model.Card.Container!.Name);
    }

    [Fact]
    public void CardModel_OnGet_NonexistentCard_Returns404()
    {
        var model = new CardModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
        var result = model.OnGet(99999);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void CardModel_ImageUrl_ResolvesApiUriOverScanPath()
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

        var model = new CardModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
        model.OnGet(lotId);

        // Catalog art takes precedence over the scan, matching the desktop (CardArtCandidateResolver).
        // A stored scan path can be stale, so it must not shadow good catalog art.
        Assert.Equal("https://api.example.com/card.jpg", model.ImageUrl);
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

        var model = new IndexModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext(), Game = gameCode };
        model.OnGet();

        Assert.Single(model.Containers);
        Assert.Equal("Matching", model.Containers[0].Name);
    }

    [Fact]
    public void IndexModel_OnGet_GameFilter_KeepsAlwaysAvailableLocations_EvenWithNoMatchingCards()
    {
        using (var ctx = _factory.CreateDbContext())
        {
            // System Bulk and a user-marked always-available box, both holding only MTG cards.
            var bulk = new StorageContainer { Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true, SortOrder = 0 };
            var trade = new StorageContainer { Name = "Trade Box", ContainerType = ContainerType.Box, AlwaysAvailable = true, SortOrder = 1 };
            var plain = new StorageContainer { Name = "Plain Box", ContainerType = ContainerType.Box, SortOrder = 2 };
            ctx.StorageContainers.AddRange(bulk, trade, plain);
            ctx.SaveChanges();

            var mtg = NewSingle("m1", "Bolt", "Set", "SET", "1", "common");
            ctx.Products.Add(mtg);
            ctx.SaveChanges();
            ctx.Lots.AddRange(
                new InventoryLot { ProductId = mtg.Id, LocationId = bulk.Id },
                new InventoryLot { ProductId = mtg.Id, LocationId = trade.Id },
                new InventoryLot { ProductId = mtg.Id, LocationId = plain.Id });
            ctx.SaveChanges();
        }

        // Filter to a game none of the cards belong to.
        var model = new IndexModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext(), Game = "pokemon" };
        model.OnGet();

        // Plain box is dropped (no Pokémon cards); the two always-available ones survive.
        var names = model.Containers.Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Bulk", "Trade Box" }, names);

        // Both land in the "Always Available" group, none in the type groups.
        Assert.Equal(new[] { "Bulk", "Trade Box" },
            model.AlwaysAvailableContainers.Select(c => c.Name).ToList());
        Assert.Empty(model.ContainersByType);
    }

    [Fact]
    public void IndexModel_OnGet_NoFilter_AlwaysAvailableSplitFromTypeGroups()
    {
        using (var ctx = _factory.CreateDbContext())
        {
            ctx.StorageContainers.AddRange(
                new StorageContainer { Name = "Bulk", ContainerType = ContainerType.Bulk, IsSystem = true, SortOrder = 0 },
                new StorageContainer { Name = "Trade Box", ContainerType = ContainerType.Box, AlwaysAvailable = true, SortOrder = 1 },
                new StorageContainer { Name = "Plain Box", ContainerType = ContainerType.Box, SortOrder = 2 });
            ctx.SaveChanges();
        }

        var model = new IndexModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
        model.OnGet();

        Assert.Equal(new[] { "Bulk", "Trade Box" },
            model.AlwaysAvailableContainers.Select(c => c.Name).ToList());
        var grouped = model.ContainersByType.SelectMany(g => g).Select(c => c.Name).ToList();
        Assert.Equal(new[] { "Plain Box" }, grouped);
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

            var model = new CardModel(_factory, NoGameServices, _dataPaths, pokemonFactory) { PageContext = CreatePageContext() };
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

        var model = new CardModel(_factory, NoGameServices, _dataPaths) { PageContext = CreatePageContext() };
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

        var model = new CardModel(_factory, NoGameServices, _dataPaths, pokemonFactory) { PageContext = CreatePageContext() };

        var exception = Record.Exception(() => model.OnGet(lotId));

        Assert.Null(exception);
        Assert.Null(model.ExtendedDataJson);
    }

    // --- TcgPlayerLink ---

    [Fact]
    public void TcgPlayerLink_NumericGameCardId_DeepLinksToProduct()
    {
        // TCGCSV games (Pokémon/Yu-Gi-Oh!/FFTCG/Riftbound) store the real TCGplayer product id.
        var url = TcgPlayerLink.Build(CardGame.Pokemon, "12345", "Pikachu", "Base Set");
        Assert.Equal("https://www.tcgplayer.com/product/12345", url);
    }

    [Fact]
    public void TcgPlayerLink_ResolvedProductId_DeepLinksToProduct()
    {
        // MTG stores a Scryfall GUID; the real product id is resolved and passed in explicitly.
        var url = TcgPlayerLink.Build(
            CardGame.Mtg, "9e1a...-guid", "Lightning Bolt", "Alpha", resolvedProductId: 987);
        Assert.Equal("https://www.tcgplayer.com/product/987", url);
    }

    [Fact]
    public void TcgPlayerLink_NonNumericId_NoResolution_FallsBackToScopedSearch()
    {
        // One Piece stores a set code (e.g. OP01-001) and has no TCGplayer id → search.
        var url = TcgPlayerLink.Build(CardGame.OnePiece, "OP01-001", "Monkey D. Luffy", "Romance Dawn");

        Assert.StartsWith("https://www.tcgplayer.com/search/one-piece-card-game/product?q=", url);
        Assert.Contains(Uri.EscapeDataString("Monkey D. Luffy Romance Dawn"), url);
    }

    [Fact]
    public void TcgPlayerLink_SearchWithoutSet_UsesNameOnly()
    {
        var url = TcgPlayerLink.Build(CardGame.OnePiece, "OP01-001", "Nami", setName: null);
        Assert.EndsWith("product?q=" + Uri.EscapeDataString("Nami"), url);
    }

    // --- MarketPriceHydrator ---

    [Fact]
    public void MarketPriceHydrator_Populate_SetsLivePricePerFoilGroup()
    {
        // MTG catalog returns different prices for foil vs non-foil printings of the same id.
        var game = new OmniCard.Tests.Fakes.ConfigurableGameService
        {
            OnGetCurrentPrices = (ids, foil) => ids.ToDictionary(id => id, _ => foil ? 20m : 5m),
        };
        var cardService = new WebCardService([game]);

        var plain = new CollectionCard { Game = CardGame.Mtg, GameCardId = "bolt", IsFoil = false };
        var foilCard = new CollectionCard { Game = CardGame.Mtg, GameCardId = "bolt", IsFoil = true };
        var traded = new CollectionCard { Game = CardGame.Mtg, GameCardId = "bolt", IsFoil = false, IsTraded = true };

        MarketPriceHydrator.Populate(cardService, [plain, foilCard, traded]);

        Assert.Equal(5m, plain.MarketPrice);
        Assert.Equal(20m, foilCard.MarketPrice);
        Assert.Equal(0m, traded.MarketPrice); // traded cards are excluded from live pricing
    }

    [Fact]
    public void MarketPriceHydrator_Populate_UnregisteredGame_LeavesPriceZero()
    {
        // No game services registered → GetGameService throws → cards keep 0 rather than blowing up.
        var card = new CollectionCard { Game = CardGame.Mtg, GameCardId = "x", MarketPrice = 0m };
        MarketPriceHydrator.Populate(NoGameServices, [card]);
        Assert.Equal(0m, card.MarketPrice);
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
