using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;
using OmniCard.Shared.Games;
using OmniCard.Shared.Settings;
using OmniCard.CardMatching.Games;
using OmniCard.Data.Catalogs;

namespace OmniCard.Tests.Services.Games;

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

    // TCGCSV knows OGN (one product matches riftcodex card c2, which has no tcgplayer_id) and a
    // brand-new set RAD that riftcodex hasn't published: one card, one battlefield, one sealed pack.
    private const string FallbackGroupsJson = """
    {"results":[{"groupId":24344,"name":"Origins","abbreviation":"OGN"},
      {"groupId":24819,"name":"Radiance","abbreviation":"RAD"}],"success":true,"errors":[]}
    """;

    private const string OgnProductsJson = """
    {"results":[
      {"productId":653002,"name":"Cull the Weak","groupId":24344,"extendedData":[{"name":"Number","value":"209/298"}]},
      {"productId":653310,"name":"Vex (Overnumbered)","groupId":24344,"extendedData":[{"name":"Number","value":"310*/298"}]}
    ],"success":true,"errors":[]}
    """;

    private const string RadProductsJson = """
    {"results":[
      {"productId":711362,"name":"Radiance - Booster Pack","groupId":24819,
        "imageUrl":"https://tcgplayer-cdn.tcgplayer.com/product/711362_200w.jpg","extendedData":[]},
      {"productId":721337,"name":"Kai'Sa, Rebel","cleanName":"KaiSa Rebel","groupId":24819,
        "imageUrl":"https://tcgplayer-cdn.tcgplayer.com/product/721337_200w.jpg",
        "extendedData":[
          {"name":"Rarity","value":"Rare"},{"name":"Number","value":"063a/167"},
          {"name":"Description","value":"[DISARM] <em>(When I attack.)</em>\r\n<br>Your spells have [REACTION]."},
          {"name":"Energy Cost","value":"5"},{"name":"Power Cost","value":"1"},{"name":"Might","value":"5"},
          {"name":"Card Type","value":"Champion Unit"},{"name":"Domain","value":"Calm;Mind"}]},
      {"productId":721400,"name":"Shimmering Grotto","groupId":24819,
        "imageUrl":"https://tcgplayer-cdn.tcgplayer.com/product/721400_200w.jpg",
        "extendedData":[{"name":"Number","value":"170/167"},{"name":"Card Type","value":"Battlefield"},{"name":"Domain","value":"None"},
          {"name":"Might","value":"0"},{"name":"Rarity","value":"Uncommon"}]}
    ],"success":true,"errors":[]}
    """;

    private RiftboundService CreateServiceWithTcgCsv(Func<string, string?>? riftcodexCards = null)
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.Contains("/sets")) return SetListJson;
            if (uri.Contains("/cards"))
                return riftcodexCards?.Invoke(uri) ?? CardsPage(uri.Contains("page=2") ? 2 : 1);
            if (uri.EndsWith("/89/groups")) return FallbackGroupsJson;
            if (uri.EndsWith("/24344/products")) return OgnProductsJson;
            if (uri.EndsWith("/24819/products")) return RadProductsJson;
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
    public async Task DownloadBulkData_AddsTcgCsvOnlySets_AndBackfillsMissingTcgplayerIds()
    {
        var svc = CreateServiceWithTcgCsv();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        // 2 riftcodex + 2 RAD cards; the sealed booster pack is skipped and OGN isn't duplicated.
        Assert.Equal(4, ctx.Cards.Count());

        // Riftcodex card without a tcgplayer_id picks it up from the TCGCSV product with its riftbound id.
        Assert.Equal("653310", ctx.Cards.Single(c => c.Id == "c2").TcgplayerId);

        var kaisa = ctx.Cards.Single(c => c.Id == "tcgcsv-721337");
        Assert.Equal("RAD", kaisa.SetId);
        Assert.Equal("Radiance", kaisa.SetName);
        Assert.Equal("rad-063a-167", kaisa.RiftboundId);
        Assert.Equal("721337", kaisa.TcgplayerId);
        Assert.Equal(63, kaisa.CollectorNumber);
        Assert.Equal("Kai'Sa, Rebel", kaisa.Name);
        Assert.Equal("Unit", kaisa.CardType);
        Assert.Equal("Champion", kaisa.Supertype);
        Assert.Equal("Calm/Mind", kaisa.Domain);
        Assert.Equal(5, kaisa.Energy);
        Assert.Equal(5, kaisa.Might);
        Assert.Equal(1, kaisa.Power);
        Assert.Equal("[DISARM] (When I attack.)\nYour spells have [REACTION].", kaisa.CardText);
        Assert.True(kaisa.AlternateArt);
        Assert.False(kaisa.Overnumbered);
        Assert.Equal("portrait", kaisa.Orientation);
        Assert.Equal("https://tcgplayer-cdn.tcgplayer.com/product/721337_400w.jpg", kaisa.CardImageUri);

        var grotto = ctx.Cards.Single(c => c.Id == "tcgcsv-721400");
        Assert.Equal("Battlefield", grotto.CardType);
        Assert.Equal("landscape", grotto.Orientation);
        Assert.Equal("", grotto.Domain);
        Assert.Null(grotto.Might);
        Assert.True(grotto.Overnumbered);

        Assert.Contains(svc.GetAvailableSets(), s => s.SetCode == "RAD");
    }

    [Fact]
    public async Task DownloadBulkData_RiftcodexCatchesUp_TakesOverTcgCsvRowInPlace()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.Add(new RiftboundCard
            {
                Id = "tcgcsv-721337", RiftboundId = "rad-063a-167", TcgplayerId = "721337",
                Name = "Kai'Sa, Rebel", SetId = "RAD", SetName = "Radiance", CollectorNumber = 63,
                CardImageUri = "https://tcgplayer-cdn.tcgplayer.com/product/721337_400w.jpg",
                ImageHash = 42UL, EdgeHash = 43UL, LocalImagePath = "riftbound-art/tcgcsv-721337.png",
            });
            seed.SaveChanges();
        }

        // Riftcodex now serves RAD, including Kai'Sa under its own id.
        const string radCards = """
        {"items":[{"id":"rc-kaisa","name":"Kai'Sa - Rebel","riftbound_id":"rad-063a-167","tcgplayer_id":"721337",
          "collector_number":63,"attributes":{"energy":5,"might":5,"power":1},
          "classification":{"type":"Unit","supertype":"Champion","rarity":"Rare","domain":["Calm","Mind"]},
          "text":{"rich":null,"plain":"disarm","flavour":null},"set":{"set_id":"RAD","label":"Radiance"},
          "media":{"image_url":"https://cdn/rc-kaisa.png","artist":"A","accessibility_text":null},
          "tags":[],"orientation":"portrait",
          "metadata":{"clean_name":"Kai'Sa Rebel","updated_on":null,"alternate_art":true,
            "overnumbered":false,"signature":false},"new":false}],
          "total":1,"page":1,"size":100,"pages":1}
        """;
        var svc = CreateServiceWithTcgCsv(_ => radCards);
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        Assert.False(ctx.Cards.Any(c => c.Id == "rc-kaisa"));
        var row = ctx.Cards.Single(c => c.TcgplayerId == "721337");
        Assert.Equal("tcgcsv-721337", row.Id);          // owned copies keep resolving
        Assert.Equal("Kai'Sa - Rebel", row.Name);        // metadata now from riftcodex
        Assert.Equal("https://cdn/rc-kaisa.png", row.CardImageUri);
        Assert.Null(row.EdgeHash);                       // art changed → re-hashed from riftcodex art
                                                         // (the fake CDN 404s, so it stays unhashed)
    }

    [Theory]
    [InlineData("020a/167", 20, "rad-020a-167")]
    [InlineData("R01", 1, "rad-r01")]
    [InlineData("SP2/005", 2, "rad-sp2-005")]
    [InlineData("T1A 003/005", 3, "rad-t1a 003-005")]
    [InlineData("190*/166", 190, "rad-190*-166")]
    public void MapTcgCsvProduct_ParsesCollectorNumbers(string number, int expected, string riftboundId)
    {
        var card = RiftboundService.MapTcgCsvProduct(new TcgCsvProduct
        {
            ProductId = 1, Name = "X",
            ExtendedData = [new TcgCsvExtendedData { Name = "Number", Value = number }],
        }, "RAD", "Radiance");

        Assert.NotNull(card);
        Assert.Equal(expected, card.CollectorNumber);
        Assert.Equal(riftboundId, card.RiftboundId);
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
