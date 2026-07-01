using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class OptcgCorrectionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgCorrectionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed two cards with known hashes
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-001",
            CardName = "Card A",
            SetId = "OP01",
            SetName = "Romance Dawn",
            Rarity = "C",
            CardColor = "Red",
            CardType = "Character",
            ImageHash = 0x0000000000000000UL,
        });
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-002",
            CardName = "Card B",
            SetId = "OP01",
            SetName = "Romance Dawn",
            Rarity = "C",
            CardColor = "Blue",
            CardType = "Character",
            ImageHash = 0x00000000000000FFUL,
        });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        return new OptcgService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(Microsoft.Extensions.Logging.Abstractions.NullLogger<PerceptualHashService>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public void FindClosestMatch_ExactCorrection_OverridesPHash()
    {
        var svc = CreateService();
        svc.RecordCorrection(0x0000000000000001UL, "OP01-002");

        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card B", match.Name);
    }

    [Fact]
    public void FindClosestMatch_FuzzyCorrection_WinsWithTrustBonus()
    {
        var svc = CreateService();
        svc.RecordCorrection(0x0000000000000003UL, "OP01-002");

        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card B", match.Name);
    }

    [Fact]
    public void RecordCorrection_Upserts()
    {
        var svc = CreateService();
        svc.RecordCorrection(0x0000000000000001UL, "OP01-001");
        svc.RecordCorrection(0x0000000000000001UL, "OP01-002");

        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card B", match.Name);
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
