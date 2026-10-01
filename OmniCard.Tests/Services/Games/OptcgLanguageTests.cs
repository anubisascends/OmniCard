using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching.Games;
using OmniCard.Data.Catalogs;
using OmniCard.Imaging;
using OmniCard.Shared.Settings;

namespace OmniCard.Tests.Services.Games;

/// <summary>Japanese/French One Piece rows fetched via poneglyph <c>/v1/search?lang=</c>: keyed
/// "{uid}@{lang}", English metadata overlaid, own art, English price fallback, pruned when un-ticked.</summary>
public class OptcgLanguageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;
    private readonly string _dataDir;

    public OptcgLanguageTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();

        _dataDir = Path.Combine(Path.GetTempPath(), "optcg-lang-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private const string SetListJson = """{"data":[{"code":"OP01","name":"Romance Dawn","released_at":null,"card_count":1}]}""";

    private const string SetDetailJson = """
    {"data":{"code":"OP01","name":"Romance Dawn","card_count":1,"cards":[
      {"card_number":"OP01-001","name":"Zoro","set":"OP01","set_name":"One Piece 1","card_type":"Leader",
       "rarity":"L","color":["Red"],"power":5000,"life":5,"attribute":["Slash"],"types":["Straw Hat Crew"],
       "effect":"Text.","variants":[
         {"index":0,"label":"Standard","images":{"stock":{"full":"https://cdn/en/0.png"},"scan":{}},
          "market":{"market_price":"6.00","low_price":"1.46"}}
       ]}
    ]}}
    """;

    // Japanese search page: localized name/colour (overridden by the English card), own art, no price.
    private const string JapaneseSearchJson = """
    {"data":[
      {"card_number":"OP01-001","name":"ロロノア・ゾロ","language":"ja","set":"OP01","set_name":"One Piece 1",
       "card_type":"Leader","rarity":"L","color":["赤"],"power":5000,"life":5,"types":["麦わらの一味"],
       "variants":[
         {"index":0,"label":"Standard","images":{"stock":{"full":"https://cdn/ja/0.png"},"scan":{}},
          "market":{"market_price":null,"low_price":null}}
       ]}
    ],"pagination":{"page":1,"limit":100,"total":1,"has_more":false}}
    """;

    private OptcgService CreateService()
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.EndsWith("/v1/sets")) return SetListJson;
            if (uri.EndsWith("/v1/sets/OP01")) return SetDetailJson;
            if (uri.Contains("/v1/search?set=OP01&lang=ja")) return JapaneseSearchJson;
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

    [Fact]
    public async Task Download_WithJapanese_AddsSuffixedRowsWithEnglishMetadataAndOwnArt()
    {
        var svc = CreateService();
        svc.CatalogLanguages = ["en", "ja"];
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var ja = ctx.Cards.Single(c => c.CardSetId == "OP01-001@ja");
        Assert.Equal("ja", ja.Lang);
        Assert.Equal("OP01-001", ja.CardNumber);
        Assert.Equal("Zoro", ja.CardName);
        Assert.Equal("Red", ja.CardColor);
        Assert.Equal("Romance Dawn", ja.SetName);
        Assert.Equal("https://cdn/ja/0.png", ja.CardImageUri);
        Assert.Null(ja.MarketPrice);
        Assert.Equal("en", ctx.Cards.Single(c => c.CardSetId == "OP01-001").Lang);
    }

    [Fact]
    public async Task Download_AfterUntickingJapanese_PrunesJapaneseRows()
    {
        var svc = CreateService();
        svc.CatalogLanguages = ["en", "ja"];
        await svc.DownloadBulkDataAsync();

        svc.CatalogLanguages = ["en"];
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        Assert.Equal(["OP01-001"], ctx.Cards.Select(c => c.CardSetId).ToList());
    }

    [Fact]
    public async Task LanguageRows_PriceFromEnglish_VariantLookup_AndSetViewsStayEnglish()
    {
        var svc = CreateService();
        svc.CatalogLanguages = ["en", "ja"];
        await svc.DownloadBulkDataAsync();

        Assert.Equal(6.00m, svc.GetCurrentPrice("OP01-001@ja", isFoil: false));
        Assert.Equal(6.00m, svc.GetCurrentPrices(["OP01-001@ja"], isFoil: false)["OP01-001@ja"]);

        var ja = svc.FindLanguageVariant("OP01-001", "ja");
        Assert.Equal("OP01-001@ja", ja?.GameSpecificId);
        Assert.Equal("ja", ja?.Language);
        Assert.Equal("OP01-001", svc.FindLanguageVariant("OP01-001@ja", "en")?.GameSpecificId);
        Assert.Null(svc.FindLanguageVariant("OP01-001", "fr"));
        Assert.Equal("ja", svc.GetCardLanguage("OP01-001@ja"));

        var setCard = Assert.Single(svc.GetSetCards("OP01"));
        Assert.Equal("OP01-001", setCard.GameCardId);
        Assert.Single(svc.GetAvailableSets());
        Assert.Contains(svc.SearchCards("lang:ja"), m => m.GameSpecificId == "OP01-001@ja");
    }

    [Theory]
    [InlineData("OP01-001", "en", "OP01-001")]
    [InlineData("OP01-001_p2", "ja", "OP01-001_p2@ja")]
    public void LanguageCardSetId_RoundTrips(string englishUid, string language, string expected)
    {
        var id = OptcgService.LanguageCardSetId(englishUid, language);
        Assert.Equal(expected, id);
        Assert.Equal(englishUid, OptcgService.EnglishCardSetId(id));
    }

    private sealed class RoutingHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = route(request.RequestUri!.ToString());
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }
}
