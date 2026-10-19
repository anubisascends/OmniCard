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

    private RiftboundService CreateService(Func<string, string?> route)
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.Contains("riftcodex")) throw new InvalidOperationException("Riftcodex is no longer a source");
            return route(uri);
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

    // === Catalog download (TCGCSV) ===

    // TCGCSV: OGN (two printings that may already exist as Riftcodex-era rows) and RAD (one card, one
    // battlefield, one sealed pack).
    private const string CatalogGroupsJson = """
    {"results":[{"groupId":24344,"name":"Origins","abbreviation":"OGN"},
      {"groupId":24819,"name":"Radiance","abbreviation":"RAD"}],"success":true,"errors":[]}
    """;

    private const string OgnProductsJson = """
    {"results":[
      {"productId":653002,"name":"Cull the Weak","groupId":24344,
        "imageUrl":"https://tcgplayer-cdn.tcgplayer.com/product/653002_200w.jpg",
        "extendedData":[{"name":"Number","value":"209/298"},{"name":"Rarity","value":"Common"},
          {"name":"Card Type","value":"Spell"},{"name":"Description","value":"Kill a unit."}]},
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

    private RiftboundService CreateCatalogService() => CreateService(uri =>
    {
        if (uri.EndsWith("/89/groups")) return CatalogGroupsJson;
        if (uri.EndsWith("/24344/products")) return OgnProductsJson;
        if (uri.EndsWith("/24819/products")) return RadProductsJson;
        return null;
    });

    [Fact]
    public async Task DownloadBulkData_EmptyCatalog_ImportsEveryTcgCsvCard_SkippingSealed()
    {
        var svc = CreateCatalogService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        Assert.Equal(["tcgcsv-653002", "tcgcsv-653310", "tcgcsv-721337", "tcgcsv-721400"],
            ctx.Cards.Select(c => c.Id).OrderBy(id => id).ToList());

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

        // Variant qualifiers stay out of the card name (decklists name the card, not the printing).
        var vex = ctx.Cards.Single(c => c.Id == "tcgcsv-653310");
        Assert.Equal("Vex", vex.Name);
        Assert.True(vex.Overnumbered);

        Assert.Contains(svc.GetAvailableSets(), s => s.SetCode == "RAD");
        Assert.Equal(RiftboundDbContext.RiftboundSchemaVersion, ctx.GetSchemaVersion());
    }

    [Fact]
    public async Task DownloadBulkData_LinksEarlierRiftcodexRowsInPlace_KeepingTheirDataArtAndHashes()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                // Matched by TcgplayerId.
                new RiftboundCard
                {
                    Id = "c1", RiftboundId = "ogn-209-298", TcgplayerId = "653002", Name = "Cull the Weak",
                    SetId = "OGN", SetName = "Origins (stale)", CollectorNumber = 209, CardType = "Spell",
                    Rarity = "Uncommon", Artist = "A", CardImageUri = "https://cdn/c1.png",
                    ImageHash = 12345UL, EdgeHash = 678UL, LocalImagePath = "riftbound-art/c1.png",
                },
                // No TcgplayerId yet: matched by riftbound id and given one.
                new RiftboundCard
                {
                    Id = "c2", RiftboundId = "ogn-310*-298", TcgplayerId = null, Name = "Vex",
                    SetId = "OGN", SetName = "Origins", CollectorNumber = 310, Orientation = "landscape",
                    CardImageUri = "https://cdn/c2.png",
                });
            seed.SaveChanges();
        }

        var svc = CreateCatalogService();
        await svc.DownloadBulkDataAsync();
        await svc.DownloadBulkDataAsync(); // a second run must not duplicate anything

        using var ctx = _factory.CreateDbContext();
        Assert.Equal(["c1", "c2", "tcgcsv-721337", "tcgcsv-721400"],
            ctx.Cards.Select(c => c.Id).OrderBy(id => id).ToList());

        var c1 = ctx.Cards.Single(c => c.Id == "c1");
        Assert.Equal("Origins (stale)", c1.SetName);         // Riftcodex data is kept...
        Assert.Equal("Uncommon", c1.Rarity);
        Assert.Equal("Kill a unit.", c1.CardText);           // ...and only gaps are filled
        Assert.Equal("https://cdn/c1.png", c1.CardImageUri); // art + hashes kept
        Assert.Equal(12345UL, c1.ImageHash);
        Assert.Equal(678UL, c1.EdgeHash);
        Assert.Equal("riftbound-art/c1.png", c1.LocalImagePath);
        Assert.Equal("A", c1.Artist);                        // TCGCSV has no artist

        var c2 = ctx.Cards.Single(c => c.Id == "c2");
        Assert.Equal("653310", c2.TcgplayerId);
        Assert.Equal("Vex", c2.Name);
        Assert.Equal("landscape", c2.Orientation);
        Assert.Equal("https://cdn/c2.png", c2.CardImageUri);
    }

    [Fact]
    public async Task DownloadBulkData_RowsCreatedFromTcgCsv_AreFullyRefreshed()
    {
        using (var seed = _factory.CreateDbContext())
        {
            // An earlier import (name with its variant qualifier, presale rarity).
            seed.Cards.Add(new RiftboundCard
            {
                Id = "tcgcsv-653310", RiftboundId = "ogn-310*-298", TcgplayerId = "653310", Name = "Vex (Overnumbered)",
                SetId = "OGN", SetName = "Origins", CollectorNumber = 310, Rarity = "Rare", Domain = "Order",
                CardImageUri = "https://cdn/vex.png", ImageHash = 1UL,
            });
            seed.SaveChanges();
        }

        await CreateCatalogService().DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var vex = ctx.Cards.Single(c => c.Id == "tcgcsv-653310");
        Assert.Equal("Vex", vex.Name);
        Assert.Equal("Rare", vex.Rarity);                     // TCGCSV lists none: kept
        Assert.True(vex.Overnumbered);
        Assert.Equal("https://cdn/vex.png", vex.CardImageUri); // hashed art kept
        Assert.Equal(1UL, vex.ImageHash);
    }

    [Fact]
    public async Task DownloadBulkData_KeepsRowsTcgCsvDoesNotList()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.Add(new RiftboundCard { Id = "ven-1", RiftboundId = "ven-001", Name = "Old", SetId = "VEN" });
            seed.SaveChanges();
        }

        await CreateCatalogService().DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        Assert.True(ctx.Cards.Any(c => c.Id == "ven-1"));
    }

    [Fact]
    public async Task DownloadBulkData_SetListUnavailable_Throws_AndChangesNothing()
    {
        var svc = CreateService(_ => null);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => svc.DownloadBulkDataAsync());
        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.Cards);
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

    // === Prices (TCGCSV) ===

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

    private RiftboundService CreatePricingService() => CreateService(uri =>
    {
        if (uri.Contains("/prices")) return TcgCsvPricesJson;
        if (uri.Contains("/groups")) return TcgCsvGroupsJson;
        return null;
    });

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

        var svc = CreatePricingService();
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

        var svc = CreateService(uri =>
        {
            if (uri.Contains("/groups")) return TwoGroupsJson;
            if (uri.Contains("/89/1/prices")) return null; // simulated failure -> NotFound -> throws on GetFromJsonAsync
            if (uri.Contains("/89/2/prices")) return GoodGroupPricesJson;
            return null;
        });

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

        var svc = CreatePricingService(); // read path uses the DB, not HTTP

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
