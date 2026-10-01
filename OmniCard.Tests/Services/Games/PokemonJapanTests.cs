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

/// <summary>Pokémon Japan = TCGCSV category 85 downloaded alongside category 3 when "ja" is ticked:
/// its own sets (prefixed "JP-" because abbreviations collide), language-tagged rows, real prices.</summary>
public class PokemonJapanTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<PokemonDbContext> _factory;

    public PokemonJapanTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(_connection).Options;
        _factory = new PkFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();
    }

    public void Dispose() => _connection.Dispose();

    private static string Products(int productId, int groupId, string number) => $$"""
        {"results":[{"productId":{{productId}},"name":"Pikachu","cleanName":"Pikachu","imageUrl":null,
          "groupId":{{groupId}},"url":"https://tcgplayer/{{productId}}",
          "extendedData":[{"name":"Number","value":"{{number}}"},{"name":"Rarity","value":"Common"}]}]}
        """;

    private static readonly Dictionary<string, string> Routes = new()
    {
        ["https://tcgcsv.com/tcgplayer/3/groups"] = """{"results":[{"groupId":100,"name":"Scarlet & Violet","abbreviation":"SV1"}]}""",
        ["https://tcgcsv.com/tcgplayer/3/100/products"] = Products(1001, 100, "025/198"),
        ["https://tcgcsv.com/tcgplayer/3/100/prices"] = """{"results":[{"productId":1001,"subTypeName":"Normal","marketPrice":0.25}]}""",
        // Same abbreviation as the English group — the "JP-" prefix keeps the sets apart.
        ["https://tcgcsv.com/tcgplayer/85/groups"] = """{"results":[{"groupId":200,"name":"SV1: Scarlet ex","abbreviation":"SV1"}]}""",
        ["https://tcgcsv.com/tcgplayer/85/200/products"] = Products(2001, 200, "025/078"),
        ["https://tcgcsv.com/tcgplayer/85/200/prices"] = """{"results":[{"productId":2001,"subTypeName":"Normal","marketPrice":1.10}]}""",
    };

    private PokemonService Create()
    {
        var dp = new Moq.Mock<IDataPathService>();
        dp.Setup(d => d.DataDirectory).Returns(Path.Combine(Path.GetTempPath(), "pkjp-" + Guid.NewGuid().ToString("N")));
        return new PokemonService(new FakeHttpClientFactory(new RoutingHandler(Routes)), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance), dp.Object,
            NullLogger<PokemonService>.Instance);
    }

    [Fact]
    public void DownloadableLanguages_AreEnglishAndJapanese()
    {
        Assert.Equal(["en", "ja"], Create().DownloadableLanguages);
    }

    [Fact]
    public async Task Download_EnglishOnly_SkipsJapanCategory()
    {
        var svc = Create();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var row = Assert.Single(ctx.Cards.ToList());
        Assert.Equal(1001, row.ProductId);
        Assert.Equal("en", row.Lang);
    }

    [Fact]
    public async Task Download_WithJapanese_AddsPrefixedJapaneseSets_PricesBoth_PrunesWhenUnticked()
    {
        var svc = Create();
        svc.CatalogLanguages = ["en", "ja"];
        await svc.DownloadBulkDataAsync();

        using (var ctx = _factory.CreateDbContext())
        {
            var jp = ctx.Cards.Single(c => c.ProductId == 2001);
            Assert.Equal("ja", jp.Lang);
            Assert.Equal("JP-SV1", jp.SetCode);
            Assert.Equal("SV1: Scarlet ex (JP)", jp.SetName);
            Assert.Equal("SV1", ctx.Cards.Single(c => c.ProductId == 1001).SetCode);
        }
        Assert.Equal(["JP-SV1", "SV1"], svc.GetAvailableSets().Select(s => s.SetCode).OrderBy(s => s).ToList());
        Assert.Equal("ja", svc.GetCardLanguage("2001"));
        // A Japan-category product is its own printing — there's no English row to remap to.
        Assert.Null(svc.FindLanguageVariant("1001", "ja"));

        await svc.UpdatePricesAsync();
        Assert.Equal(1.10m, svc.GetCurrentPrice("2001", isFoil: false));
        Assert.Equal(0.25m, svc.GetCurrentPrice("1001", isFoil: false));

        svc.CatalogLanguages = ["en"];
        await svc.DownloadBulkDataAsync();
        using (var ctx = _factory.CreateDbContext())
            Assert.Equal([1001], ctx.Cards.Select(c => c.ProductId).ToList());
    }

    private sealed class RoutingHandler(Dictionary<string, string> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(routes.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class PkFactory(DbContextOptions<PokemonDbContext> o) : IDbContextFactory<PokemonDbContext>
    {
        public PokemonDbContext CreateDbContext() => new(o);
    }
}
