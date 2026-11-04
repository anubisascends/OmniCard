using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Web.Services.Dashboard;

namespace OmniCard.Tests.Web.Dashboard;

/// <summary>
/// Widget data: totals and breakdowns reuse the classic Dashboard's numbers; card searches name their
/// own game, skip traded-away cards and only see the sites they're given.
/// </summary>
public class DashboardDataServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly Mock<IAnalyticsService> _analytics = new();
    private readonly DashboardDataService _data;
    private readonly int _homeBinder;
    private readonly int _shopBox;
    private readonly int _shop;

    public DashboardDataServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        var prices = new Mock<ICardGameService>();
        prices.Setup(p => p.GetCurrentPrices(It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
            .Returns((IEnumerable<string> ids, bool _) => ids.ToDictionary(id => id, id => id.Length * 1m));
        var cards = new Mock<ICardService>();
        cards.Setup(c => c.GetGameService(It.IsAny<CardGame>())).Returns(prices.Object);

        _analytics.Setup(a => a.GetHoldings()).Returns(new HoldingsValuation(7, 10m, 25m,
            [new ValuationLine("Mtg", 7, 10m, 25m)], [new ValuationLine("Single", 7, 10m, 25m)], []));
        _analytics.Setup(a => a.GetRealized(null)).Returns(new RealizedSummary(2, 50m, 20m,
            [new RealizedLine("Pokemon", 2, 50m, 20m)], TotalFees: 5m));

        var sql = new DashboardSqlRunner([new DashboardSqlTarget(DashboardSqlRunner.MainDatabase, "OmniCard", () => _conn)]);
        _data = new DashboardDataService(_analytics.Object, new Factory(_opts), cards.Object, [], sql, TimeProvider.System);

        _shop = AddSite("Shop");
        _homeBinder = AddLocation("Binder", Site.DefaultSiteId);
        _shopBox = AddLocation("Case", _shop);
        AddLot("Pikachu", "ab", CardGame.Pokemon, _homeBinder, qty: 2, rarity: "Common");   // $2 each
        AddLot("Charizard", "abcde", CardGame.Pokemon, _homeBinder, rarity: "Rare");       // $5
        AddLot("Mewtwo", "abcdefgh", CardGame.Pokemon, _shopBox, rarity: "Rare");          // $8, other site
        AddLot("Bolt", "abc", CardGame.Mtg, _homeBinder, rarity: "Common");                // $3, other game
        AddLot("Traded", "abcdefghij", CardGame.Pokemon, _homeBinder, traded: true);
    }

    public void Dispose() => _conn.Dispose();

    private OmniCardDbContext Db() => new(_opts);

    private int AddSite(string name)
    {
        using var db = Db();
        var s = new Site { Name = name };
        db.Sites.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    private int AddLocation(string name, int siteId)
    {
        using var db = Db();
        var c = new StorageContainer { Name = name, ContainerType = ContainerType.Box, SiteId = siteId };
        db.StorageContainers.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private void AddLot(string name, string cardId, CardGame game, int location, int qty = 1, string rarity = "", bool traded = false)
    {
        using var db = Db();
        var p = new Product { Game = game, Category = ProductCategory.Single, GameCardId = cardId, Name = name, SetCode = "SET", Rarity = rarity };
        db.Products.Add(p);
        db.SaveChanges();
        db.Lots.Add(new InventoryLot { ProductId = p.Id, Quantity = qty, UnitCost = 1m, LocationId = location, IsTraded = traded });
        db.SaveChanges();
    }

    private Task<DashboardDataDto> Get(DashboardWidgetDto widget, IReadOnlyCollection<int>? sites = null) =>
        _data.GetAsync(DashboardLayoutService.Sanitize(new DashboardLayoutDto { Widgets = [widget] }).Widgets.Single(), sites, default);

    [Fact]
    public async Task Metrics_MatchTheClassicDashboard()
    {
        var unrealized = await Get(new() { Type = "metric", Metric = "unrealized" });
        var profit = await Get(new() { Type = "metric", Metric = "realizedProfit" });

        Assert.Equal(("unrealized", "money", 15m), (unrealized.Columns[0].Key, unrealized.Columns[0].Kind, unrealized.Rows[0][0]));
        Assert.Equal(25m, profit.Rows[0][0]); // proceeds − cost − fees
    }

    [Fact]
    public async Task Analytics_AreComputedOncePerFewSeconds()
    {
        await Get(new() { Type = "metric", Metric = "totalUnits" });
        await Get(new() { Type = "metric", Metric = "totalMarket" });
        await Get(new() { Type = "breakdown", GroupBy = "game" });

        _analytics.Verify(a => a.GetHoldings(), Times.Once);
    }

    [Fact]
    public async Task Breakdowns_UseGameDisplayNames()
    {
        var byGame = await Get(new() { Type = "breakdown", GroupBy = "game" });
        var sales = await Get(new() { Type = "breakdown", GroupBy = "salesGame" });

        Assert.Equal(["group", "units", "cost", "market"], byGame.Columns.Select(c => c.Key));
        Assert.Equal("Magic: The Gathering", byGame.Rows.Single()[0]);
        Assert.Equal(30m, sales.Rows.Single()[4]); // profit = proceeds − cost
    }

    [Fact]
    public async Task SearchSummary_UsesTheWidgetsGame_AndSkipsTradedCards()
    {
        var pokemon = await Get(new() { Type = "search", Game = "Pokemon", Output = "summary" });
        var everything = await Get(new() { Type = "search", Output = "summary" });

        // Pikachu ×2 ($4), Charizard ($5), Mewtwo ($8) — not Bolt (MTG) or the traded card.
        Assert.Equal(new List<object?> { 3, 4, 4m, 17m }, pokemon.Rows.Single());
        Assert.Equal(4, everything.Rows.Single()[0]);
    }

    [Fact]
    public async Task Search_OnlySeesTheGivenSites()
    {
        var home = await Get(new() { Type = "search", Game = "Pokemon", Output = "summary" }, [Site.DefaultSiteId]);

        Assert.Equal(2, home.Rows.Single()[0]);
    }

    [Fact]
    public async Task Search_AppliesTheQuery_AndListsTopCardsByValue()
    {
        var rares = await Get(new() { Type = "search", Game = "Pokemon", Query = "r:rare", Output = "cards", Limit = 1 });

        Assert.Equal("Mewtwo", rares.Rows.Single()[0]);
        Assert.True(rares.Truncated);
    }

    [Fact]
    public async Task SearchGroups_TotalEachGroup()
    {
        var byRarity = await Get(new() { Type = "search", Game = "Pokemon", Output = "groups", GroupBy = "rarity" });

        Assert.Equal(["group", "cards", "units", "market", "cost"], byRarity.Columns.Select(c => c.Key));
        Assert.Equal(new List<object?> { "Rare", 2, 2, 13m, 2m }, byRarity.Rows[0]);
        Assert.Equal(new List<object?> { "Common", 1, 2, 4m, 2m }, byRarity.Rows[1]);
    }

    [Fact]
    public async Task Sql_RunsThroughTheRunner()
    {
        var result = await Get(new() { Type = "sql", Sql = "SELECT COUNT(*) AS Lots FROM Lots" });

        Assert.Equal(5L, result.Rows.Single()[0]);
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
