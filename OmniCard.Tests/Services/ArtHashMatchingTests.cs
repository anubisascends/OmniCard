using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Imaging;
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

    [Fact]
    public void BlendedConfidence_WithArtHash_HigherThanPHashAlone()
    {
        // Seed a card with a distinctive hash so we control distances precisely
        // Use Guid 4 with ImageHash = 0x8000_0000_0000_0000
        // Flip exactly 8 bits of ImageHash to get pHash distance = 8
        // Flip exactly 4 bits of ArtHash to get art hash distance = 4
        //
        // With maxDistance=14:
        //   pHashConfidence = (1 - 8/14) * 100 ≈ 42.9%
        //   artConfidence   = (1 - 4/20) * 100  = 80%
        //   blended         = 0.5 * 42.9 + 0.5 * 80 ≈ 61.4%
        //   pHashOnly       ≈ 42.9%
        //
        // Test asserts blended confidence > pHash-only confidence.

        using var ctx = _factory.CreateDbContext();
        ctx.Cards.Add(new Card
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
            OracleId = Guid.NewGuid(),
            Name = "Counterspell",
            Lang = "en",
            Layout = "normal",
            TypeLine = "Instant",
            SetCode = "lea",
            SetName = "LEA",
            CollectorNumber = "055",
            Rarity = "common",
            // Distinctly far from all existing seed hashes (which start 0x1000...)
            ImageHash = 0x8000_0000_0000_0000UL,
            ArtHash  = 0xDEAD_BEEF_0000_0000UL,
        });
        ctx.SaveChanges();

        var service = CreateService();

        // Scan pHash: flip 8 bits from card 4's ImageHash (bits 0-7)
        ulong scanImageHash = 0x8000_0000_0000_0000UL ^ 0x0000_0000_0000_00FFUL;

        // Scan artHash: flip 4 bits from card 4's ArtHash (bits 0-3)
        ulong scanArtHash = 0xDEAD_BEEF_0000_0000UL ^ 0x0000_0000_0000_000FUL;

        var match = service.FindClosestMatch(scanImageHash, new ulong[] { scanArtHash, 0, 0 });

        Assert.NotNull(match);
        Assert.Equal("Counterspell", match.Name);

        // pHash-only confidence would be (1 - 8/14) * 100 ≈ 42.86%
        // Blended with art hash distance 4: (1 - 4/20) * 100 = 80% → blended ≈ 61.43%
        double pHashOnly = Math.Max(0, (1.0 - 8.0 / 14.0)) * 100;
        Assert.True(match.Confidence > pHashOnly,
            $"Blended confidence {match.Confidence:F2} should exceed pHash-only {pHashOnly:F2}");
        Assert.True(match.Confidence > 50,
            $"Blended confidence {match.Confidence:F2} should be above 50%");
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
