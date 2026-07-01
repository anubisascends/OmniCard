using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class ScryfallCorrectionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ScryfallDbContext> _factory;
    private readonly ScryfallDbContext _readCtx;

    public ScryfallCorrectionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestScryfallDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed two cards with known hashes
        ctx.Cards.Add(new Card
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            OracleId = Guid.NewGuid(),
            Name = "Card A",
            Lang = "en",
            Layout = "normal",
            TypeLine = "Creature",
            SetCode = "TST",
            SetName = "Test Set",
            CollectorNumber = "001",
            Rarity = "common",
            ImageHash = 0x0000000000000000UL, // hash = 0
        });
        ctx.Cards.Add(new Card
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            OracleId = Guid.NewGuid(),
            Name = "Card B",
            Lang = "en",
            Layout = "normal",
            TypeLine = "Creature",
            SetCode = "TST",
            SetName = "Test Set",
            CollectorNumber = "002",
            Rarity = "common",
            ImageHash = 0x00000000000000FFUL, // hash = 0xFF (8 bits set)
        });
        ctx.SaveChanges();

        _readCtx = _factory.CreateDbContext();
    }

    public void Dispose()
    {
        _readCtx.Dispose();
        _connection.Dispose();
    }

    private ScryfallService CreateService()
    {
        return new ScryfallService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            new SetSymbolCache(new StubHttpClientFactory(), new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
            Options.Create(new ScryfallSettings()),
            NullLogger<ScryfallService>.Instance,
            new DataPathService(Path.GetTempPath()));
    }

    [Fact]
    public void FindClosestMatch_WithoutCorrection_ReturnsNearestHash()
    {
        var svc = CreateService();
        // Hash 0x0000000000000001 is distance 1 from Card A (0x0), distance 7 from Card B (0xFF)
        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card A", match.Name);
    }

    [Fact]
    public void FindClosestMatch_ExactCorrection_OverridesPHash()
    {
        var svc = CreateService();
        var cardBId = "00000000-0000-0000-0000-000000000002";

        // Record correction: hash 1 → Card B (even though pHash says Card A)
        svc.RecordCorrection(0x0000000000000001UL, cardBId);

        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card B", match.Name);
    }

    [Fact]
    public void FindClosestMatch_ConfidentHash_IgnoresFuzzyCorrection()
    {
        var svc = CreateService();
        var cardBId = "00000000-0000-0000-0000-000000000002";

        // Record correction: hash 0x03 → Card B
        // Scan hash 0x01: distance to Card A (0x00) = 1, which is within confident threshold (<=6)
        // Confident hash match returns Card A, fuzzy corrections are not consulted
        svc.RecordCorrection(0x0000000000000003UL, cardBId);

        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card A", match.Name);
    }

    [Fact]
    public void FindClosestMatch_PHashWins_WhenCorrectionTooFar()
    {
        var svc = CreateService();
        var cardBId = "00000000-0000-0000-0000-000000000002";

        // Record correction at hash far from scan (distance > maxDistance)
        svc.RecordCorrection(0xFFFFFFFFFFFFFFFFUL, cardBId);

        // Should still return Card A via normal pHash
        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card A", match.Name);
    }

    [Fact]
    public void RecordCorrection_Upserts()
    {
        var svc = CreateService();
        var cardAId = "00000000-0000-0000-0000-000000000001";
        var cardBId = "00000000-0000-0000-0000-000000000002";

        svc.RecordCorrection(0x0000000000000001UL, cardAId);
        svc.RecordCorrection(0x0000000000000001UL, cardBId); // Overwrite

        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card B", match.Name); // Latest correction wins
    }

    [Fact]
    public void FindClosestMatch_CorrectionForDeletedCard_FallsThrough()
    {
        var svc = CreateService();
        // Record correction pointing to a card that doesn't exist
        svc.RecordCorrection(0x0000000000000001UL, "00000000-0000-0000-0000-999999999999");

        // Should fall through to pHash match (Card A)
        var match = svc.FindClosestMatch(0x0000000000000001UL);
        Assert.NotNull(match);
        Assert.Equal("Card A", match.Name);
    }

    private class TestScryfallDbFactory(DbContextOptions<ScryfallDbContext> options) : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
