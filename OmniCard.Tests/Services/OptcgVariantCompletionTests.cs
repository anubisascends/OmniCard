using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OptcgVariantCompletionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgVariantCompletionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        // Set OP01: two printed numbers, one with an extra alt-art variant (3 rows total).
        ctx.Cards.AddRange(
            new OptcgCard { CardSetId = "OP01-001", CardNumber = "OP01-001", VariantIndex = 0, CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn" },
            new OptcgCard { CardSetId = "OP01-001_p1", CardNumber = "OP01-001", VariantIndex = 1, CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn" },
            new OptcgCard { CardSetId = "OP01-002", CardNumber = "OP01-002", VariantIndex = 0, CardName = "Nami", SetId = "OP01", SetName = "Romance Dawn" });
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
    public async Task GetSetCompletion_CountsDistinctCardNumbers_NotVariants()
    {
        var svc = CreateService();
        var results = await svc.GetSetCompletionAsync([]);

        var op01 = results.Single(r => r.SetCode == "OP01");
        Assert.Equal(2, op01.TotalCount);  // two printed numbers, not three rows
        Assert.Equal(0, op01.OwnedCount);
    }

    [Fact]
    public void GetMissingCards_ReturnsOneEntryPerPrintedNumber()
    {
        var svc = CreateService();
        var missing = svc.GetMissingCards("OP01", []);

        Assert.Equal(2, missing.Count);
        Assert.Contains(missing, m => m.CollectorNumber == "OP01-001");
        Assert.Contains(missing, m => m.CollectorNumber == "OP01-002");
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
