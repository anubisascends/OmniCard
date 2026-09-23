using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Settings;
using OmniCard.Web.Mcp.Tools;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers the read-only MCP collection tools. They delegate to the same query builder / paging /
/// hydration internals the SPA's CollectionController uses (those are covered by
/// <see cref="CollectionStackingTests"/> / <see cref="CollectionSortingTests"/>); this locks in that
/// the MCP tool wiring returns the expected DTOs. No game catalogs are registered, so price/art
/// hydration is a graceful no-op (both hydrators swallow a missing catalog) — exactly as in production
/// when a catalog hasn't been downloaded.
/// </summary>
public class CollectionToolsTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly CollectionTools _tools;

    public CollectionToolsTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        var factory = new MockFactory(_opts);
        ICardService cardService = new WebCardService([]);                 // no catalogs → hydration no-ops
        var binderCards = new WebBinderCardService(factory, new StubDataPath());
        var imageCache = new CardImageCacheService(new StubDataPath(), new StubHttpClientFactory(),
            NullLogger<CardImageCacheService>.Instance);

        // collectionQuery/analytics back top_value_cards/collection_dashboard, which these tests don't
        // exercise; SearchCollection + GetCard never touch them.
        _tools = new CollectionTools(factory, cardService, binderCards, imageCache,
            collectionQuery: null!, analytics: null!, gameServices: []);
    }

    public void Dispose() => _conn.Dispose();

    private int AddLot(string name, string setCode = "SET", string number = "1", bool foil = false,
        string condition = "NM", int quantity = 1)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Pokemon,
            Category = ProductCategory.Single,
            GameCardId = $"{name}-{setCode}-{number}-{foil}".ToLowerInvariant(),
            Foil = foil,
            Name = name,
            SetCode = setCode,
            SetName = setCode + " Set",
            CollectorNumber = number,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, Condition = condition, Quantity = quantity };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void SearchCollection_ReturnsMatchingCards()
    {
        AddLot("Pikachu", "BASE", "58");
        AddLot("Charizard", "BASE", "4");

        var result = _tools.SearchCollection();

        Assert.Equal(2, result.Total);
        Assert.Contains(result.Items, c => c.Name == "Pikachu");
        Assert.Contains(result.Items, c => c.Name == "Charizard");
    }

    [Fact]
    public void SearchCollection_HonorsScryfallQuery()
    {
        AddLot("Pikachu", "BASE", "58");
        AddLot("Charizard", "BASE", "4");

        var result = _tools.SearchCollection(query: "Charizard");

        var card = Assert.Single(result.Items);
        Assert.Equal("Charizard", card.Name);
    }

    [Fact]
    public void SearchCollection_Stacked_CollapsesIdenticalPrintings()
    {
        AddLot("Squirtle", "BASE", "63", quantity: 2);
        AddLot("Squirtle", "BASE", "63", quantity: 3);

        var result = _tools.SearchCollection(stacked: true);

        Assert.Equal(1, result.Total);
        var card = Assert.Single(result.Items);
        Assert.Equal(5, card.Quantity);
    }

    [Fact]
    public void SearchCollection_ClampsTake()
    {
        var result = _tools.SearchCollection(take: 100_000);
        Assert.True(result.Take <= 500);
    }

    [Fact]
    public void GetCard_ReturnsCard_ForKnownId()
    {
        var id = AddLot("Bulbasaur", "BASE", "44", quantity: 4);

        var card = _tools.GetCard(id);

        Assert.NotNull(card);
        Assert.Equal("Bulbasaur", card!.Name);
        Assert.Equal(4, card.Quantity);
    }

    [Fact]
    public void GetCard_ReturnsNull_ForUnknownId()
    {
        Assert.Null(_tools.GetCard(999_999));
    }

    // --- Local test doubles (mirrors the other web tests) ---

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private sealed class StubDataPath : IDataPathService
    {
        public string DataDirectory => Path.GetTempPath();
        public string ScansDirectory => Path.GetTempPath();
        public string TempScansDirectory => Path.GetTempPath();
        public string SymbolsCacheDirectory => Path.GetTempPath();
        public string LogsDirectory => Path.GetTempPath();
        public string TradesDirectory => Path.GetTempPath();
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
