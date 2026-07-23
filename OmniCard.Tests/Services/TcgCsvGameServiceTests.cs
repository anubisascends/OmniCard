using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TcgCsvGameServiceTests
{
    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    [Fact]
    public void Product_Deserializes_WithExtendedData()
    {
        const string json = """
        {"results":[{"productId":132375,"name":"Auron (Hero)","cleanName":"Auron Hero",
          "imageUrl":"https://cdn/132375_200w.jpg","groupId":1939,
          "url":"https://tcgplayer.com/132375",
          "extendedData":[
            {"name":"Rarity","displayName":"Rarity","value":"Hero"},
            {"name":"Number","displayName":"Number","value":"1-001H"},
            {"name":"CardType","displayName":"Card Type","value":"Forward"}]}],
          "success":true,"errors":[]}
        """;

        var resp = JsonSerializer.Deserialize<TcgCsvProductsResponse>(json, Camel);

        Assert.NotNull(resp);
        var p = Assert.Single(resp!.Results);
        Assert.Equal(132375, p.ProductId);
        Assert.Equal("Auron (Hero)", p.Name);
        Assert.Equal(1939, p.GroupId);
        Assert.Equal(3, p.ExtendedData.Count);
        Assert.Equal("1-001H", p.ExtendedData.Single(e => e.Name == "Number").Value);
    }

    [Fact]
    public void Prices_Deserialize_WithSubTypeNames()
    {
        const string json = """
        {"results":[
          {"productId":1,"marketPrice":1.50,"subTypeName":"Normal"},
          {"productId":1,"marketPrice":3.00,"subTypeName":"Holofoil"}],
          "success":true,"errors":[]}
        """;

        var resp = JsonSerializer.Deserialize<TcgCsvPricesResponse>(json, Camel);

        Assert.Equal(2, resp!.Results.Count);
        Assert.Equal("Holofoil", resp.Results[1].SubTypeName);
        Assert.Equal(3.00m, resp.Results[1].MarketPrice);
    }
}

public class TcgCsvDownloadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<PokemonDbContext> _factory;
    private readonly string _dataDir;

    public TcgCsvDownloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(_connection).Options;
        _factory = new TestFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();
        _dataDir = Path.Combine(Path.GetTempPath(), "tcgcsv-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private const string GroupsJson = """
    {"results":[{"groupId":1939,"name":"Opus I","abbreviation":"OP1"}],"success":true,"errors":[]}
    """;

    private const string ProductsJson = """
    {"results":[
      {"productId":132375,"name":"Auron (Hero)","cleanName":"Auron Hero","imageUrl":"https://cdn/132375_200w.jpg","groupId":1939,"url":"https://tcg/132375",
        "extendedData":[{"name":"Number","displayName":"Number","value":"1-001H"},{"name":"Rarity","displayName":"Rarity","value":"Hero"},{"name":"CardType","displayName":"Card Type","value":"Forward"},{"name":"Element","displayName":"Element","value":"Fire"}]},
      {"productId":132376,"name":"Auron (Rare)","cleanName":"Auron Rare","imageUrl":"https://cdn/132376_200w.jpg","groupId":1939,"url":"https://tcg/132376",
        "extendedData":[{"name":"Number","displayName":"Number","value":"1-002R"},{"name":"Rarity","displayName":"Rarity","value":"Rare"},{"name":"CardType","displayName":"Card Type","value":"Forward"}]}
    ],"success":true,"errors":[]}
    """;

    private const string PricesJson = """
    {"results":[
      {"productId":132375,"marketPrice":1.50,"subTypeName":"Normal"},
      {"productId":132375,"marketPrice":3.00,"subTypeName":"Holofoil"},
      {"productId":132376,"marketPrice":0.12,"subTypeName":"Reverse Holofoil"}
    ],"success":true,"errors":[]}
    """;

    // Note: return type is the public base (not the `file`-scoped TestTcgCsvService) because a
    // file-local type cannot appear in a member signature of this non-file-local test class
    // (CS9051), even for private members. Callers only need base/interface members.
    private TcgCsvGameService<PokemonDbContext> CreateService() => CreateService(uri =>
    {
        if (uri.Contains("/groups")) return GroupsJson;
        if (uri.Contains("/products")) return ProductsJson;
        if (uri.Contains("/prices")) return PricesJson;
        return null;
    });

    private TcgCsvGameService<PokemonDbContext> CreateService(Func<string, string?> route)
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new TestTcgCsvService(
            new FakeHttpClientFactory(new RoutingHandler(route)),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<TestTcgCsvService>.Instance);
    }

    [Fact]
    public async Task DownloadBulkData_MapsProducts_AndStoresExtendedDataJson()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var rows = ctx.Cards.OrderBy(c => c.ProductId).ToList();
        Assert.Equal(2, rows.Count);

        var hero = rows.Single(c => c.ProductId == 132375);
        Assert.Equal("Auron (Hero)", hero.Name);
        Assert.Equal(1939, hero.GroupId);
        Assert.Equal("OP1", hero.SetCode);
        Assert.Equal("Opus I", hero.SetName);
        Assert.Equal("1-001H", hero.CollectorNumber);
        Assert.Equal("Hero", hero.Rarity);
        Assert.Equal("Forward", hero.CardType);
        Assert.Equal(CardGame.Pokemon, hero.Game); // TestTcgCsvService reports Pokemon
        Assert.Contains("Element", hero.ExtendedDataJson);   // full extendedData retained
        Assert.Contains("Fire", hero.ExtendedDataJson);
    }

    [Fact]
    public async Task DownloadBulkData_ExistingRow_RefreshesMetadata_PreservesHashAndPrice()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.Add(new TcgCsvCard { ProductId = 132375, Game = CardGame.Pokemon, Name = "Stale",
                CollectorNumber = "999", ImageHash = 12345UL, LocalImagePath = "pokemon-art/132375.png", MarketPrice = 7.77m });
            seed.SaveChanges();
        }
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var row = ctx.Cards.Single(c => c.ProductId == 132375);
        Assert.Equal("Auron (Hero)", row.Name);         // refreshed
        Assert.Equal("1-001H", row.CollectorNumber);
        Assert.Equal(12345UL, row.ImageHash);           // preserved
        Assert.Equal("pokemon-art/132375.png", row.LocalImagePath);
        Assert.Equal(7.77m, row.MarketPrice);           // price not clobbered by catalog re-download
    }
}

file class RoutingHandler(Func<string, string?> route) : System.Net.Http.HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = route(request.RequestUri!.ToString());
        var resp = body is null
            ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        return Task.FromResult(resp);
    }
}

file class FakeHttpClientFactory(System.Net.Http.HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}

file class TestFactory(Microsoft.EntityFrameworkCore.DbContextOptions<PokemonDbContext> options)
    : Microsoft.EntityFrameworkCore.IDbContextFactory<PokemonDbContext>
{
    public PokemonDbContext CreateDbContext() => new(options);
}

// Minimal concrete subclass exercising the base service against the Pokemon context.
file class TestTcgCsvService : TcgCsvGameService<PokemonDbContext>
{
    public TestTcgCsvService(IHttpClientFactory h, Microsoft.EntityFrameworkCore.IDbContextFactory<PokemonDbContext> f,
        IPerceptualHashService ph, IDataPathService dp, Microsoft.Extensions.Logging.ILogger l) : base(h, f, ph, dp, l) { }

    protected override int CategoryId => 3;
    public override CardGame Game => CardGame.Pokemon;
    protected override string GameKey => "pokemon";

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows)
    {
        decimal? Price(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        return (Price("Normal"), Price("Holofoil") ?? Price("Reverse Holofoil"));
    }
}
