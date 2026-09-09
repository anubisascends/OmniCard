using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

/// <summary>
/// FFTCG's Element lives only in the ExtendedDataJson blob. These tests verify the in-memory blob
/// search path: element:fire / e:fire / element:f (value alias f→Fire) all resolve, plus numeric
/// cost ordering, against a live (in-memory SQLite) FinalFantasy catalog.
/// </summary>
public class FinalFantasyElementSearchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<FinalFantasyDbContext> _factory;
    private readonly string _dataDir;

    public FinalFantasyElementSearchTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<FinalFantasyDbContext>().UseSqlite(_connection).Options;
        _factory = new FfFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();
        _dataDir = Path.Combine(Path.GetTempPath(), "fftcg-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private FinalFantasyService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new FinalFantasyService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<FinalFantasyService>.Instance);
    }

    private void Seed()
    {
        using var ctx = _factory.CreateDbContext();
        ctx.Cards.AddRange(
            Card(1, "Auron", "Fire", cost: 6),
            Card(2, "Shiva", "Ice", cost: 3),
            Card(3, "Ifrit", "Fire", cost: 8));
        ctx.SaveChanges();
    }

    private static TcgCsvCard Card(int id, string name, string element, int cost) => new()
    {
        ProductId = id, Game = CardGame.FinalFantasy, Name = name, SetCode = "OP1", SetName = "Opus I",
        CollectorNumber = $"1-00{id}H", Rarity = "Hero", CardType = "Forward",
        ExtendedDataJson =
            $"[{{\"name\":\"Element\",\"displayName\":\"Element\",\"value\":\"{element}\"}}," +
            $"{{\"name\":\"Cost\",\"displayName\":\"Cost\",\"value\":\"{cost}\"}}]",
    };

    [Theory]
    [InlineData("element:fire")]
    [InlineData("e:fire")]
    [InlineData("element:f")]
    [InlineData("e:f")]
    public void ElementAliases_AllMatchFireCards(string query)
    {
        Seed();
        var svc = CreateService();
        var results = svc.SearchCards(query);
        Assert.Equal(new[] { "Auron", "Ifrit" }, results.Select(r => r.Name).OrderBy(x => x));
    }

    [Fact]
    public void Element_NonFire_MatchesIce()
    {
        Seed();
        var svc = CreateService();
        Assert.Equal("Shiva", Assert.Single(svc.SearchCards("element:ice")).Name);
    }

    [Fact]
    public void Cost_NumericOrdering_Works()
    {
        Seed();
        var svc = CreateService();
        var results = svc.SearchCards("cost>=6");
        Assert.Equal(new[] { "Auron", "Ifrit" }, results.Select(r => r.Name).OrderBy(x => x));
    }

    [Fact]
    public void ElementCombinedWithName_Ands()
    {
        Seed();
        var svc = CreateService();
        Assert.Equal("Ifrit", Assert.Single(svc.SearchCards("element:fire ifrit")).Name);
    }

    [Fact]
    public void ResolveFieldCardIds_ReturnsMatchingProductIds()
    {
        Seed();
        var svc = (IGameFieldResolver)CreateService();
        var ids = svc.ResolveFieldCardIds("element", ComparisonOp.Contains, "f"); // f -> Fire
        Assert.NotNull(ids);
        Assert.Equal(new[] { "1", "3" }, ids!.OrderBy(x => x));
    }

    [Fact]
    public void ResolveFieldCardIds_UnknownField_ReturnsNull()
    {
        Seed();
        var svc = (IGameFieldResolver)CreateService();
        Assert.Null(svc.ResolveFieldCardIds("set", ComparisonOp.Contains, "OP1")); // core field, not game-specific
    }

    private sealed class FfFactory(DbContextOptions<FinalFantasyDbContext> options) : IDbContextFactory<FinalFantasyDbContext>
    {
        public FinalFantasyDbContext CreateDbContext() => new(options);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());
        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
