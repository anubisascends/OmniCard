using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OptcgVariantMatchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgVariantMatchTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-001_p1", CardNumber = "OP01-001", VariantIndex = 1,
            CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn", Rarity = "SEC",
            ImageHash = 0x0UL, MarketPrice = 40m,
        });
        ctx.SaveChanges();
        ctx.MarkMigrationComplete();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        return new OptcgService(new StubHttpClientFactory(), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object, NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public void FindClosestMatch_AltArt_UsesPrintedNumberAndVariantUid()
    {
        var svc = CreateService();
        var match = svc.FindClosestMatch(0x0UL);

        Assert.NotNull(match);
        Assert.Equal("OP01-001", match.CollectorNumber);   // printed number
        Assert.Equal("OP01-001_p1", match.GameSpecificId);  // variant uid
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }
    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
