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

/// <summary>One Piece catalog + prices from TCGCSV (category 68), including the one-time link of a
/// catalog downloaded earlier from poneglyph (via each variant's tcgplayer_url).</summary>
public class OptcgDownloadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;
    private readonly string _dataDir;
    private readonly List<string> _requests = [];

    public OptcgDownloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete(); // already migrated so the ctor does not wipe

        _dataDir = Path.Combine(Path.GetTempPath(), "optcg-dl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    // TCGCSV: OP01 with a base Zoro, its alternate art, a new manga art and a sealed box; a release-event
    // group with a stamped Zoro.
    private const string GroupsJson = """
    {"results":[{"groupId":3188,"name":"Romance Dawn","abbreviation":"OP01"},
      {"groupId":3300,"name":"Romance Dawn Release Event Cards","abbreviation":"OP01 RE"}],"success":true,"errors":[]}
    """;

    private const string Op01ProductsJson = """
    {"results":[
      {"productId":454512,"name":"Roronoa Zoro (001)","groupId":3188,
        "imageUrl":"https://tcgplayer-cdn.tcgplayer.com/product/454512_200w.jpg",
        "extendedData":[{"name":"Rarity","value":"L"},{"name":"Number","value":"OP01-001"},
          {"name":"Description","value":"[DON!! x1] <strong>[Your Turn]</strong> All of your Characters gain +1000 power."},
          {"name":"Color","value":"Red;Green"},{"name":"CardType","value":"Leader"},{"name":"Life","value":"5"},
          {"name":"Power","value":"5000"},{"name":"Subtypes","value":"Supernovas;Straw Hat Crew"},
          {"name":"Attribute","value":"Slash"}]},
      {"productId":454513,"name":"Roronoa Zoro (001) (Alternate Art)","groupId":3188,
        "imageUrl":"https://tcgplayer-cdn.tcgplayer.com/product/454513_200w.jpg",
        "extendedData":[{"name":"Rarity","value":"L"},{"name":"Number","value":"OP01-001"},{"name":"CardType","value":"Leader"}]},
      {"productId":499999,"name":"Roronoa Zoro (001) (Manga)","groupId":3188,
        "extendedData":[{"name":"Rarity","value":"L"},{"name":"Number","value":"OP01-001"}]},
      {"productId":453000,"name":"Romance Dawn Booster Box","groupId":3188,"extendedData":[]},
      {"productId":454600,"name":"Nami","groupId":3188,"imageCount":0,
        "imageUrl":"https://tcgplayer-cdn.tcgplayer.com/product/454600_200w.jpg",
        "extendedData":[{"name":"Rarity","value":"C"},{"name":"Number","value":"OP01-016"},{"name":"Cost","value":"1"},
          {"name":"Counterplus","value":"+2000"},{"name":"Color","value":"Red"},{"name":"CardType","value":"Character"}]}
    ],"success":true,"errors":[]}
    """;

    private const string ReleaseEventProductsJson = """
    {"results":[
      {"productId":470001,"name":"Nami","groupId":3300,
        "extendedData":[{"name":"Rarity","value":"C"},{"name":"Number","value":"OP01-016"}]}
    ],"success":true,"errors":[]}
    """;

    private const string Op01PricesJson = """
    {"results":[
      {"productId":454512,"lowPrice":3.50,"marketPrice":4.86,"subTypeName":"Normal"},
      {"productId":454513,"lowPrice":780.00,"marketPrice":595.68,"subTypeName":"Foil"},
      {"productId":454600,"lowPrice":0.05,"marketPrice":0.10,"subTypeName":"Normal"},
      {"productId":454600,"lowPrice":0.50,"marketPrice":0.75,"subTypeName":"Foil"}
    ],"success":true,"errors":[]}
    """;

    // poneglyph (only consulted for the one-time link): base + alt-art Zoro with TCGplayer URLs, and a
    // third variant TCGplayer doesn't list.
    private const string PoneglyphSetListJson = """{"data":[{"code":"OP01","name":"Romance Dawn","released_at":null,"card_count":1}]}""";

    private const string PoneglyphSetDetailJson = """
    {"data":{"code":"OP01","name":"Romance Dawn","card_count":1,"cards":[
      {"card_number":"OP01-001","name":"Roronoa Zoro","set":"OP01","card_type":"Leader","rarity":"L","color":["Red","Green"],
       "variants":[
         {"index":0,"label":"Standard","images":{"stock":{"full":"https://cdn/0.png"},"scan":{}},
          "market":{"tcgplayer_url":"https://www.tcgplayer.com/product/454512/one-piece-card-game-romance-dawn-roronoa-zoro-001","market_price":"4.00"}},
         {"index":1,"label":"Alternate Art","images":{"stock":{"full":"https://cdn/1.png"},"scan":{}},
          "market":{"tcgplayer_url":"https://www.tcgplayer.com/product/454513/one-piece-card-game-romance-dawn-roronoa-zoro-001-parallel","market_price":"500.00"}},
         {"index":2,"label":"Alternate Art","images":{"stock":{"full":"https://cdn/2.png"},"scan":{}},
          "market":{"tcgplayer_url":null,"market_price":null}}
       ]}
    ]}}
    """;

    private OptcgService CreateService(bool poneglyphUp = true)
    {
        var handler = new RoutingHandler(uri =>
        {
            lock (_requests) _requests.Add(uri);
            if (uri.EndsWith("/68/groups")) return GroupsJson;
            if (uri.EndsWith("/68/3188/products")) return Op01ProductsJson;
            if (uri.EndsWith("/68/3300/products")) return ReleaseEventProductsJson;
            if (uri.EndsWith("/68/3188/prices")) return Op01PricesJson;
            if (uri.EndsWith("/68/3300/prices")) return """{"results":[]}""";
            if (poneglyphUp && uri.EndsWith("/v1/sets")) return PoneglyphSetListJson;
            if (poneglyphUp && uri.EndsWith("/v1/sets/OP01")) return PoneglyphSetDetailJson;
            return null;
        });
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new OptcgService(
            new FakeHttpClientFactory(handler),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<OptcgService>.Instance);
    }

    private void SeedPoneglyphCatalog()
    {
        using var seed = _factory.CreateDbContext();
        seed.Cards.AddRange(
            new OptcgCard
            {
                CardSetId = "OP01-001", CardNumber = "OP01-001", VariantIndex = 0, VariantLabel = "Standard",
                CardName = "Roronoa Zoro", SetId = "OP01", SetName = "Romance Dawn", Rarity = "L", CardColor = "Red/Green",
                CardType = "Leader", MarketPrice = 1.00m, CardImageUri = "https://cdn/0.png",
                ImageHash = 12345UL, LocalImagePath = "optcg-art/OP01-001.jpg",
            },
            new OptcgCard
            {
                CardSetId = "OP01-001_p1", CardNumber = "OP01-001", VariantIndex = 1, VariantLabel = "Alternate Art",
                CardName = "Roronoa Zoro", SetId = "OP01", SetName = "Romance Dawn", Rarity = "L", CardImageUri = "https://cdn/1.png",
            },
            new OptcgCard
            {
                CardSetId = "OP01-001_p2", CardNumber = "OP01-001", VariantIndex = 2, VariantLabel = "Alternate Art",
                CardName = "Roronoa Zoro", SetId = "OP01", SetName = "Romance Dawn", Rarity = "L", MarketPrice = 9.99m,
            },
            new OptcgCard
            {
                CardSetId = "OP01-001@ja", Lang = "ja", CardNumber = "OP01-001", CardName = "Roronoa Zoro",
                SetId = "OP01", SetName = "Romance Dawn", CardImageUri = "https://cdn/ja.png",
            });
        seed.SaveChanges();
    }

    [Fact]
    public async Task DownloadBulkData_EmptyCatalog_BuildsVariantsFromTcgCsv()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        Assert.DoesNotContain(_requests, r => r.Contains("poneglyph")); // nothing to link
        using var ctx = _factory.CreateDbContext();
        var rows = ctx.Cards.OrderBy(c => c.CardNumber).ThenBy(c => c.VariantIndex).ToList();
        Assert.Equal(["OP01-001", "OP01-001_t454513", "OP01-001_t499999", "OP01-016", "OP01-016_t470001"],
            rows.Select(c => c.CardSetId).ToList());

        var zoro = rows[0];
        Assert.Equal(454512, zoro.TcgplayerId);
        Assert.True(zoro.FromTcgCsv);
        Assert.Equal(0, zoro.VariantIndex);
        Assert.Equal("Standard", zoro.VariantLabel);
        Assert.Equal("Roronoa Zoro", zoro.CardName);
        Assert.Equal("OP01", zoro.SetId);
        Assert.Equal("Romance Dawn", zoro.SetName);
        Assert.Equal("L", zoro.Rarity);
        Assert.Equal("Red/Green", zoro.CardColor);
        Assert.Equal("Leader", zoro.CardType);
        Assert.Equal("5000", zoro.CardPower);
        Assert.Equal("5", zoro.Life);
        Assert.Null(zoro.CardCost);
        Assert.Equal("Supernovas/Straw Hat Crew", zoro.SubTypes);
        Assert.Equal("Slash", zoro.Attribute);
        Assert.Equal("[DON!! x1] [Your Turn] All of your Characters gain +1000 power.", zoro.CardText);
        Assert.Equal("https://tcgplayer-cdn.tcgplayer.com/product/454512_400w.jpg", zoro.CardImageUri);
        Assert.Equal(4.86m, zoro.MarketPrice);
        Assert.Equal(3.50m, zoro.InventoryPrice);

        var alt = rows[1];
        Assert.Equal(1, alt.VariantIndex);
        Assert.Equal("Alternate Art", alt.VariantLabel);
        Assert.Equal("Roronoa Zoro", alt.CardName);
        Assert.Equal(595.68m, alt.MarketPrice); // foil-only price

        Assert.Equal("Manga Art", rows[2].VariantLabel);
        Assert.Null(rows[2].MarketPrice);

        var nami = rows[3];
        Assert.Equal("1", nami.CardCost);
        Assert.Equal(2000, nami.CounterAmount);
        Assert.Equal(0.10m, nami.MarketPrice);  // Normal preferred over Foil
        Assert.Null(nami.CardImageUri);          // imageCount 0: TCGplayer has no picture yet

        var stamped = rows[4];
        Assert.Equal("Release Event", stamped.VariantLabel);
        Assert.Equal("Romance Dawn", stamped.SetName); // the set's name, not the event group's
    }

    [Fact]
    public async Task DownloadBulkData_PoneglyphCatalog_LinksOnce_UpdatesInPlace_KeepsIdentityArtAndHashes()
    {
        SeedPoneglyphCatalog();

        var svc = CreateService();
        await svc.DownloadBulkDataAsync();
        Assert.Contains(_requests, r => r.EndsWith("/v1/sets/OP01"));

        _requests.Clear();
        await svc.DownloadBulkDataAsync(); // linked now: poneglyph isn't consulted again, nothing duplicates
        Assert.DoesNotContain(_requests, r => r.Contains("poneglyph"));

        using var ctx = _factory.CreateDbContext();
        Assert.Equal(
            ["OP01-001", "OP01-001@ja", "OP01-001_p1", "OP01-001_p2", "OP01-001_t499999", "OP01-016", "OP01-016_t470001"],
            ctx.Cards.Select(c => c.CardSetId).ToList().OrderBy(id => id, StringComparer.Ordinal).ToList());

        var zoro = ctx.Cards.Single(c => c.CardSetId == "OP01-001");
        Assert.Equal(454512, zoro.TcgplayerId);
        Assert.False(zoro.FromTcgCsv);
        Assert.Equal(4.86m, zoro.MarketPrice);            // prices from TCGCSV
        Assert.Equal("5000", zoro.CardPower);             // missing fields filled
        Assert.Equal("Leader", zoro.CardType);
        Assert.Equal("Roronoa Zoro", zoro.CardName);
        Assert.Equal("https://cdn/0.png", zoro.CardImageUri); // art + hashes kept
        Assert.Equal(12345UL, zoro.ImageHash);
        Assert.Equal("optcg-art/OP01-001.jpg", zoro.LocalImagePath);

        Assert.Equal(454513, ctx.Cards.Single(c => c.CardSetId == "OP01-001_p1").TcgplayerId);

        // TCGplayer doesn't list _p2: unlinked and untouched.
        var p2 = ctx.Cards.Single(c => c.CardSetId == "OP01-001_p2");
        Assert.Null(p2.TcgplayerId);
        Assert.Equal(9.99m, p2.MarketPrice);

        // The new manga art follows the existing variants.
        Assert.Equal(3, ctx.Cards.Single(c => c.CardSetId == "OP01-001_t499999").VariantIndex);

        // Japanese rows from poneglyph are kept (TCGCSV is English-only).
        Assert.Equal("ja", ctx.Cards.Single(c => c.CardSetId == "OP01-001@ja").Lang);
    }

    [Fact]
    public async Task DownloadBulkData_PoneglyphUnavailableForLink_Throws_AndAddsNothing()
    {
        SeedPoneglyphCatalog();

        var svc = CreateService(poneglyphUp: false);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DownloadBulkDataAsync());
        Assert.Contains("Nothing was changed", ex.Message);

        using var ctx = _factory.CreateDbContext();
        Assert.Equal(4, ctx.Cards.Count());
        Assert.False(ctx.Cards.Any(c => c.TcgplayerId != null));
    }

    [Fact]
    public async Task UpdatePrices_LinksThenAppliesTcgCsvPrices()
    {
        SeedPoneglyphCatalog();

        var svc = CreateService();
        await svc.UpdatePricesAsync();

        using var ctx = _factory.CreateDbContext();
        var zoro = ctx.Cards.Single(c => c.CardSetId == "OP01-001");
        Assert.Equal(4.86m, zoro.MarketPrice);
        Assert.Equal(3.50m, zoro.InventoryPrice);
        Assert.Equal(595.68m, ctx.Cards.Single(c => c.CardSetId == "OP01-001_p1").MarketPrice);
        Assert.Equal(9.99m, ctx.Cards.Single(c => c.CardSetId == "OP01-001_p2").MarketPrice); // unlinked: untouched
        Assert.Equal(4, ctx.Cards.Count()); // prices never add printings
    }

    [Fact]
    public void ApplyTcgCsvFields_PoneglyphRowsKeepTheirData_TcgCsvRowsAreRefreshed()
    {
        OptcgCard Incoming() => new()
        {
            TcgplayerId = 1, CardName = "Nami", Rarity = "P", CardType = "Character", CardText = "[DON!! x1][Your Turn]",
            CardImageUri = "https://tcg/1.jpg", MarketPrice = 2m, InventoryPrice = 1m, FromTcgCsv = true,
        };

        var poneglyph = new OptcgCard { CardName = "Nami", Rarity = "C", CardType = "Event", CardText = "[DON!! x1] [Your Turn]" };
        Assert.True(OptcgService.ApplyTcgCsvFields(poneglyph, Incoming())); // gained art
        Assert.Equal(("C", "Event", "[DON!! x1] [Your Turn]"), (poneglyph.Rarity, poneglyph.CardType, poneglyph.CardText));
        Assert.Equal((1, 2m, 1m), (poneglyph.TcgplayerId, poneglyph.MarketPrice, poneglyph.InventoryPrice));
        Assert.Equal("https://tcg/1.jpg", poneglyph.CardImageUri);
        Assert.False(poneglyph.FromTcgCsv);

        var fromTcgCsv = new OptcgCard { CardName = "Nami?", Rarity = "C", CardType = "Event", CardImageUri = "https://tcg/old.jpg", FromTcgCsv = true };
        Assert.False(OptcgService.ApplyTcgCsvFields(fromTcgCsv, Incoming())); // art kept
        Assert.Equal(("Nami", "P", "Character"), (fromTcgCsv.CardName, fromTcgCsv.Rarity, fromTcgCsv.CardType));
        Assert.Equal("https://tcg/old.jpg", fromTcgCsv.CardImageUri);
    }

    [Fact]
    public void MapTcgCsvProduct_PromoRarity_UsesTheCatalogsP()
    {
        var card = OptcgService.MapTcgCsvProduct(new TcgCsvProduct
        {
            ProductId = 5, Name = "Monkey.D.Luffy (P-001)",
            ExtendedData = [new() { Name = "Number", Value = "P-001" }, new() { Name = "Rarity", Value = "PR" }],
        }, new TcgCsvGroup { GroupId = 1, Name = "One Piece Promotion Cards", Abbreviation = "P" });
        Assert.Equal(("P", "P", "Monkey.D.Luffy"), (card!.Rarity, card.SetId, card.CardName));
    }

    [Theory]
    [InlineData("Roronoa Zoro (001)", "Romance Dawn", "Standard")]
    [InlineData("Shanks (020) (Alternate Art)", "x", "Alternate Art")]
    [InlineData("Roronoa Zoro (Parallel)", "x", "Alternate Art")]
    [InlineData("Shanks (112) (Manga)", "x", "Manga Art")]
    [InlineData("Roronoa Zoro (EB04-007) (SP)", "x", "SP")]
    [InlineData("Usopp (Pandaman Art)", "x", "Pandaman Art")]
    [InlineData("Nami", "Romance Dawn Release Event Cards", "Release Event")]
    public void VariantLabelOf_MapsTcgPlayerQualifiers(string name, string group, string expected) =>
        Assert.Equal(expected, OptcgService.VariantLabelOf(name, group));

    [Theory]
    [InlineData("https://www.tcgplayer.com/product/454512/one-piece-card-game-romance-dawn-roronoa-zoro-001", 454512)]
    [InlineData(null, null)]
    [InlineData("https://example.com/", null)]
    public void TcgplayerProductId_ParsesProductUrls(string? url, int? expected) =>
        Assert.Equal(expected, OptcgService.TcgplayerProductId(url));

    private class RoutingHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = route(request.RequestUri!.ToString());
            var resp = body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            return Task.FromResult(resp);
        }
    }

    private class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }
}
