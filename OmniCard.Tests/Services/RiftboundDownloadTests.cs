using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundDownloadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<RiftboundDbContext> _factory;
    private readonly string _dataDir;

    public RiftboundDownloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
        _factory = new TestFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();

        _dataDir = Path.Combine(Path.GetTempPath(), "rift-dl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private const string SetListJson = """
    {"items":[{"id":"s1","name":"Origins","set_id":"OGN","card_count":2,
      "tcgplayer_id":"24344","cardmarket_id":"6286","published_on":"2025-10-31T00:00:00"}],
      "total":1,"page":1,"size":100,"pages":1}
    """;

    // OGN has 2 cards spread over 2 pages (size=1) to exercise paging.
    private static string CardsPage(int page) => page switch
    {
        1 => """
        {"items":[{"id":"c1","name":"Cull the Weak","riftbound_id":"ogn-209-298","tcgplayer_id":"653002",
          "collector_number":209,"attributes":{"energy":2,"might":null,"power":1},
          "classification":{"type":"Spell","supertype":null,"rarity":"Common","domain":["Order"]},
          "text":{"rich":null,"plain":"kill","flavour":null},"set":{"set_id":"OGN","label":"Origins"},
          "media":{"image_url":"https://cdn/c1.png","artist":"A","accessibility_text":null},
          "tags":[],"orientation":"portrait",
          "metadata":{"clean_name":"Cull the Weak","updated_on":null,"alternate_art":false,
            "overnumbered":false,"signature":false},"new":false}],
          "total":2,"page":1,"size":1,"pages":2}
        """,
        _ => """
        {"items":[{"id":"c2","name":"Vex","riftbound_id":"ogn-310*-298","tcgplayer_id":null,
          "collector_number":310,"attributes":{"energy":4,"might":null,"power":4},
          "classification":{"type":"Legend","supertype":null,"rarity":"Epic","domain":["Body","Order"]},
          "text":{"rich":null,"plain":null,"flavour":null},"set":{"set_id":"OGN","label":"Origins"},
          "media":{"image_url":"https://cdn/c2.png","artist":"B","accessibility_text":null},
          "tags":[],"orientation":"landscape",
          "metadata":{"clean_name":"Vex","updated_on":null,"alternate_art":true,
            "overnumbered":true,"signature":false},"new":false}],
          "total":2,"page":2,"size":1,"pages":2}
        """
    };

    // TCGCSV: one group (24344). Prices cover:
    //   653002 -> Normal 1.50 + Foil 3.00 (both subtypes)
    //   685522 -> Foil 542.31 only (foil-only)
    //   999999 -> present but matches no seeded card
    private const string TcgCsvGroupsJson = """
    {"results":[{"groupId":24344,"name":"Origins","abbreviation":"OGN"}],"success":true,"errors":[]}
    """;

    private const string TcgCsvPricesJson = """
    {"results":[
      {"productId":653002,"lowPrice":1.0,"midPrice":1.4,"highPrice":2.0,"marketPrice":1.50,"directLowPrice":null,"subTypeName":"Normal"},
      {"productId":653002,"lowPrice":2.5,"midPrice":3.1,"highPrice":5.0,"marketPrice":3.00,"directLowPrice":null,"subTypeName":"Foil"},
      {"productId":685522,"lowPrice":600.0,"midPrice":625.0,"highPrice":1299.0,"marketPrice":542.31,"directLowPrice":null,"subTypeName":"Foil"},
      {"productId":999999,"lowPrice":1.0,"midPrice":1.0,"highPrice":1.0,"marketPrice":9.99,"directLowPrice":null,"subTypeName":"Normal"}
    ],"success":true,"errors":[]}
    """;

    private RiftboundService CreateService()
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.Contains("/sets")) return SetListJson;
            if (uri.Contains("/cards"))
            {
                var page = uri.Contains("page=2") ? 2 : 1;
                return CardsPage(page);
            }
            return null;
        });
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new RiftboundService(
            new FakeHttpClientFactory(handler),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<RiftboundService>.Instance);
    }

    private RiftboundService CreateServiceWithPricing()
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.Contains("/sets")) return SetListJson;
            if (uri.Contains("/cards"))
                return CardsPage(uri.Contains("page=2") ? 2 : 1);
            if (uri.Contains("/prices")) return TcgCsvPricesJson;
            if (uri.Contains("/groups")) return TcgCsvGroupsJson;
            return null;
        });
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new RiftboundService(
            new FakeHttpClientFactory(handler),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<RiftboundService>.Instance);
    }

    [Fact]
    public async Task DownloadBulkData_PagesThroughAllCards_AndMaps()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var rows = ctx.Cards.OrderBy(c => c.CollectorNumber).ToList();
        Assert.Equal(2, rows.Count);

        var cull = rows.Single(c => c.Id == "c1");
        Assert.Equal(209, cull.CollectorNumber);
        Assert.Equal("Cull the Weak", cull.Name);
        Assert.Equal("OGN", cull.SetId);
        Assert.Equal("Origins", cull.SetName);
        Assert.Equal("Spell", cull.CardType);
        Assert.Equal("Order", cull.Domain);
        Assert.Equal("portrait", cull.Orientation);
        Assert.Equal("https://cdn/c1.png", cull.CardImageUri);
        Assert.False(cull.AlternateArt);

        var vex = rows.Single(c => c.Id == "c2");
        Assert.Equal("Body/Order", vex.Domain);
        Assert.Equal("landscape", vex.Orientation);
        Assert.True(vex.AlternateArt);
        Assert.True(vex.Overnumbered);

        Assert.Equal(RiftboundDbContext.RiftboundSchemaVersion, ctx.GetSchemaVersion());
    }

    [Fact]
    public async Task DownloadBulkData_ExistingRow_RefreshesMetadata_PreservesHashAndImagePath()
    {
        using (var seedCtx = _factory.CreateDbContext())
        {
            seedCtx.Cards.Add(new RiftboundCard
            {
                Id = "c1",
                Name = "Stale",
                SetId = "OGN",
                SetName = "Origins (stale)",
                CollectorNumber = 999,
                CardType = "Spell",
                CardImageUri = "https://stale/url",
                ImageHash = 12345UL,
                LocalImagePath = "riftbound-art/c1.png",
            });
            seedCtx.SaveChanges();
        }

        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var row = ctx.Cards.Single(c => c.Id == "c1");

        // Metadata refreshed from the API.
        Assert.Equal("Cull the Weak", row.Name);
        Assert.Equal(209, row.CollectorNumber);
        Assert.Equal("https://cdn/c1.png", row.CardImageUri);

        // Computed/backfilled fields preserved, not nulled by the update.
        Assert.Equal(12345UL, row.ImageHash);
        Assert.Equal("riftbound-art/c1.png", row.LocalImagePath);
    }

    [Fact]
    public async Task UpdatePrices_WritesMarketPrices_FromTcgCsv()
    {
        // Seed three cards: both-subtypes, foil-only, and one with no matching price row.
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new RiftboundCard { Id = "c1", Name = "Cull the Weak", SetId = "OGN", TcgplayerId = "653002" },
                new RiftboundCard { Id = "c2", Name = "Vi", SetId = "UNL", TcgplayerId = "685522" },
                new RiftboundCard { Id = "c3", Name = "Orphan", SetId = "OGN", TcgplayerId = "111111" },
                new RiftboundCard { Id = "c4", Name = "NoTcg", SetId = "OGN", TcgplayerId = null });
            seed.SaveChanges();
        }

        var svc = CreateServiceWithPricing();
        await svc.UpdatePricesAsync();

        using var ctx = _factory.CreateDbContext();
        var c1 = ctx.Cards.Single(c => c.Id == "c1");
        Assert.Equal(1.50m, c1.MarketPrice);
        Assert.Equal(3.00m, c1.FoilMarketPrice);
        Assert.NotNull(c1.PriceUpdatedAt);

        var c2 = ctx.Cards.Single(c => c.Id == "c2");
        Assert.Null(c2.MarketPrice);
        Assert.Equal(542.31m, c2.FoilMarketPrice);

        var c3 = ctx.Cards.Single(c => c.Id == "c3");
        Assert.Null(c3.MarketPrice);
        Assert.Null(c3.FoilMarketPrice);
        Assert.Null(c3.PriceUpdatedAt);

        var c4 = ctx.Cards.Single(c => c.Id == "c4");
        Assert.Null(c4.MarketPrice);
        Assert.Null(c4.FoilMarketPrice);
    }

    // Two groups: group 1's /prices endpoint fails (simulated 500/non-OK), group 2's succeeds.
    // A working price refresh must not abort on the first group's failure — it should log
    // and continue, still applying prices from the second (good) group.
    private const string TwoGroupsJson = """
    {"results":[{"groupId":1,"name":"BadSet","abbreviation":"BAD"},{"groupId":2,"name":"GoodSet","abbreviation":"GOOD"}],"success":true,"errors":[]}
    """;

    private const string GoodGroupPricesJson = """
    {"results":[
      {"productId":700001,"lowPrice":4.0,"midPrice":5.0,"highPrice":6.0,"marketPrice":5.25,"directLowPrice":null,"subTypeName":"Normal"}
    ],"success":true,"errors":[]}
    """;

    [Fact]
    public async Task UpdatePrices_OneGroupFails_StillAppliesOtherGroupPrices()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.Add(new RiftboundCard { Id = "good", Name = "GoodCard", SetId = "GOOD", TcgplayerId = "700001" });
            seed.SaveChanges();
        }

        var handler = new RoutingHandler(uri =>
        {
            if (uri.Contains("/groups")) return TwoGroupsJson;
            if (uri.Contains("/89/1/prices")) return null; // simulated failure -> NotFound -> throws on GetFromJsonAsync
            if (uri.Contains("/89/2/prices")) return GoodGroupPricesJson;
            return null;
        });
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        var svc = new RiftboundService(
            new FakeHttpClientFactory(handler),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<RiftboundService>.Instance);

        await svc.UpdatePricesAsync();

        using var ctx = _factory.CreateDbContext();
        var good = ctx.Cards.Single(c => c.Id == "good");
        Assert.Equal(5.25m, good.MarketPrice);
    }

    [Fact]
    public void GetCurrentPrice_IsFoilAware_WithFallback()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new RiftboundCard { Id = "both", Name = "Both", SetId = "OGN", MarketPrice = 1.50m, FoilMarketPrice = 3.00m },
                new RiftboundCard { Id = "foilonly", Name = "FoilOnly", SetId = "OGN", MarketPrice = null, FoilMarketPrice = 9.00m },
                new RiftboundCard { Id = "normalonly", Name = "NormalOnly", SetId = "OGN", MarketPrice = 2.00m, FoilMarketPrice = null },
                new RiftboundCard { Id = "none", Name = "None", SetId = "OGN", MarketPrice = null, FoilMarketPrice = null });
            seed.SaveChanges();
        }

        var svc = CreateService(); // read path uses the DB, not HTTP

        // Foil requested
        Assert.Equal(3.00m, svc.GetCurrentPrice("both", isFoil: true));
        Assert.Equal(9.00m, svc.GetCurrentPrice("foilonly", isFoil: true));
        Assert.Equal(2.00m, svc.GetCurrentPrice("normalonly", isFoil: true));   // falls back to normal
        Assert.Null(svc.GetCurrentPrice("none", isFoil: true));

        // Non-foil requested
        Assert.Equal(1.50m, svc.GetCurrentPrice("both", isFoil: false));
        Assert.Equal(9.00m, svc.GetCurrentPrice("foilonly", isFoil: false));    // falls back to foil
        Assert.Equal(2.00m, svc.GetCurrentPrice("normalonly", isFoil: false));
        Assert.Null(svc.GetCurrentPrice("none", isFoil: false));

        // Bulk (foil requested)
        var prices = svc.GetCurrentPrices(new[] { "both", "foilonly", "none" }, isFoil: true);
        Assert.Equal(3.00m, prices["both"]);
        Assert.Equal(9.00m, prices["foilonly"]);
        Assert.False(prices.ContainsKey("none"));   // no value for either subtype

        // Bulk (non-foil requested) — symmetric with the foil-direction assertion above.
        var nonFoilPrices = svc.GetCurrentPrices(new[] { "both", "normalonly", "none" }, isFoil: false);
        Assert.Equal(1.50m, nonFoilPrices["both"]);
        Assert.Equal(2.00m, nonFoilPrices["normalonly"]);
        Assert.False(nonFoilPrices.ContainsKey("none"));
    }

    private class RoutingHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = route(request.RequestUri!.ToString());
            var resp = body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(resp);
        }
    }

    private class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class TestFactory(DbContextOptions<RiftboundDbContext> options) : IDbContextFactory<RiftboundDbContext>
    {
        public RiftboundDbContext CreateDbContext() => new(options);
    }
}
