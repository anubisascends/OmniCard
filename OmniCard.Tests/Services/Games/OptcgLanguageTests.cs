using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching.Games;
using OmniCard.Data.Catalogs;
using OmniCard.Imaging;
using OmniCard.Shared.Games;
using OmniCard.Shared.Settings;

namespace OmniCard.Tests.Services.Games;

/// <summary>Japanese/French One Piece rows (downloaded earlier from poneglyph; TCGCSV is English-only):
/// keyed "{uid}@{lang}", English price fallback, variant lookup, English-only set views.</summary>
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

    private OptcgService CreateService()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new OptcgCard
                {
                    CardSetId = "OP01-001", CardNumber = "OP01-001", CardName = "Zoro", SetId = "OP01",
                    SetName = "Romance Dawn", Rarity = "L", CardColor = "Red", CardType = "Leader",
                    MarketPrice = 6.00m, CardImageUri = "https://cdn/en/0.png",
                },
                new OptcgCard
                {
                    CardSetId = "OP01-001@ja", Lang = "ja", CardNumber = "OP01-001", CardName = "Zoro", SetId = "OP01",
                    SetName = "Romance Dawn", Rarity = "L", CardColor = "Red", CardType = "Leader",
                    CardImageUri = "https://cdn/ja/0.png",
                });
            seed.SaveChanges();
        }
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new OptcgService(
            new FakeHttpClientFactory(new RoutingHandler(_ => null)),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public void LanguageRows_PriceFromEnglish_VariantLookup_AndSetViewsStayEnglish()
    {
        var svc = CreateService();

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

    [Fact]
    public void CatalogLanguages_AreEnglishOnly_SinceTcgCsvHasNoOtherLanguages()
    {
        var svc = CreateService();
        svc.CatalogLanguages = ["en", "ja", "fr"];
        Assert.Equal(["en"], svc.CatalogLanguages);
        Assert.Equal(["en"], svc.DownloadableLanguages);
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
