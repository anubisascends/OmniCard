using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class ArtHashMatchingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ScryfallDbContext> _factory;

    public ArtHashMatchingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestScryfallDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        SeedCards(ctx);
    }

    private static void SeedCards(ScryfallDbContext ctx)
    {
        ctx.Cards.AddRange(
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                OracleId = Guid.NewGuid(),
                Name = "Lightning Bolt",
                Lang = "en",
                Layout = "normal",
                TypeLine = "Instant",
                SetCode = "m10",
                SetName = "M10",
                CollectorNumber = "001",
                Rarity = "common",
                ImageHash = 0x1000_0000_0000_0000,
                ArtHash = 0xAAAA_BBBB_CCCC_0001,
            },
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                OracleId = Guid.NewGuid(),
                Name = "Shock",
                Lang = "en",
                Layout = "normal",
                TypeLine = "Instant",
                SetCode = "m10",
                SetName = "M10",
                CollectorNumber = "002",
                Rarity = "common",
                ImageHash = 0x1000_0000_0000_0001,
                ArtHash = 0xAAAA_BBBB_CCCC_0002,
            },
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                OracleId = Guid.NewGuid(),
                Name = "Lightning Bolt",
                Lang = "en",
                Layout = "normal",
                TypeLine = "Instant",
                SetCode = "m11",
                SetName = "M11",
                CollectorNumber = "001",
                Rarity = "common",
                ImageHash = 0x1000_0000_0000_0002,
                ArtHash = 0xAAAA_BBBB_CCCC_0003,
            }
        );
        ctx.SaveChanges();
    }

    [Fact]
    public void FindClosestMatch_ArtHash_MatchesCorrectCard()
    {
        var service = CreateService();

        // Art hash very close to "Lightning Bolt" m10 (distance 1)
        var scanArtHashes = new ulong[] { 0xAAAA_BBBB_CCCC_0001 ^ 0x01, 0, 0 };
        var match = service.FindClosestMatch(0xFFFF_FFFF_FFFF_FFFF, scanArtHashes);

        Assert.NotNull(match);
        Assert.Equal("Lightning Bolt", match.Name);
        Assert.Equal("m10", match.SetCode);
    }

    [Fact]
    public void FindClosestMatch_ArtHash_BeatsFullCardHash()
    {
        var service = CreateService();

        // Full-card hash is closest to "Shock" but art hash is closest to "Lightning Bolt m10"
        var scanImageHash = 0x1000_0000_0000_0001UL; // Exact match for Shock's ImageHash
        var scanArtHashes = new ulong[] { 0xAAAA_BBBB_CCCC_0001, 0, 0 }; // Exact match for Lightning Bolt m10's ArtHash

        var match = service.FindClosestMatch(scanImageHash, scanArtHashes);

        Assert.NotNull(match);
        Assert.Equal("Lightning Bolt", match.Name);
        Assert.Equal("m10", match.SetCode);
    }

    [Fact]
    public void FindClosestMatch_NoArtHash_FallsBackToImageHash()
    {
        var service = CreateService();

        // No art hashes provided — should fall back to ImageHash matching
        var match = service.FindClosestMatch(0x1000_0000_0000_0001UL);

        Assert.NotNull(match);
        Assert.Equal("Shock", match.Name);
    }

    private ScryfallService CreateService()
    {
        return new ScryfallService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            new SetSymbolCache(new StubHttpClientFactory(), new DataPathService(Path.GetTempPath()), NullLogger<SetSymbolCache>.Instance),
            Options.Create(new ScryfallSettings { Languages = ["en"] }),
            NullLogger<ScryfallService>.Instance,
            new DataPathService(Path.GetTempPath()));
    }

    public void Dispose()
    {
        _connection.Dispose();
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
