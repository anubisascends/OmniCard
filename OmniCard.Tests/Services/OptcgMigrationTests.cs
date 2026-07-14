using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OptcgMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;
    private readonly string _dataDir;

    public OptcgMigrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);

        _dataDir = Path.Combine(Path.GetTempPath(), "optcg-migration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dataDir, "optcg-art"));
        File.WriteAllText(Path.Combine(_dataDir, "optcg-art", "OP01-001.jpg"), "stale");
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private void SeedLegacy(int userVersion)
    {
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.Cards.Add(new OptcgCard { CardSetId = "OP01-001", CardNumber = "OP01-001", CardName = "Zoro", SetId = "OP01" });
        ctx.HashCorrections.Add(new HashCorrection { ScanHash = 123, CorrectCardId = "OP01-001", CreatedAt = DateTime.Parse("2024-01-01T00:00:00Z") });
        ctx.SaveChanges();
        if (userVersion > 0) ctx.MarkMigrationComplete();
    }

    private OptcgService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new OptcgService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public void Constructor_StaleVersion_WipesCardsCorrectionsAndArt()
    {
        SeedLegacy(userVersion: 0);

        _ = CreateService();

        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.Cards);
        Assert.Empty(ctx.HashCorrections);
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "optcg-art")));
    }

    [Fact]
    public void Constructor_CurrentVersion_PreservesData()
    {
        SeedLegacy(userVersion: OptcgDbContext.PoneglyphSchemaVersion);

        _ = CreateService();

        using var ctx = _factory.CreateDbContext();
        Assert.Single(ctx.Cards);
        Assert.Single(ctx.HashCorrections);
        Assert.True(File.Exists(Path.Combine(_dataDir, "optcg-art", "OP01-001.jpg")));
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
