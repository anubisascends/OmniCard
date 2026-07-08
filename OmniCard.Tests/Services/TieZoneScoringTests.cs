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

public class TieZoneScoringTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ScryfallDbContext> _factory;

    public TieZoneScoringTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestScryfallDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

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

    /// <summary>
    /// Multiple cards tie on pHash distance. Art hash should break the tie
    /// even when art hash distances are moderate (not within the old gate).
    /// Simulates: Spider-Woman correction (#2) — cards tied at pHash dist=6,
    /// art hash at dist=12 correctly identifies the card while others are worse.
    /// </summary>
    [Fact]
    public void FindClosestMatch_PHashTie_ArtHashBreaksTie()
    {
        using var ctx = _factory.CreateDbContext();

        // Three cards all at pHash distance 2 from scan hash 0x00
        // Art hash distances vary: 14, 8, 14 — card B should win via combined scoring
        ctx.Cards.AddRange(
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                OracleId = Guid.NewGuid(),
                Name = "Wrong Card A",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "001", Rarity = "common",
                ImageHash = 0x0000_0000_0000_0003, // dist 2 from scan 0x00
                ArtHash =   0x0000_0000_0000_7F00, // art dist ~14 from scan art
            },
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                OracleId = Guid.NewGuid(),
                Name = "Correct Card",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "002", Rarity = "common",
                ImageHash = 0x0000_0000_0000_0005, // dist 2 from scan 0x00 (tied)
                ArtHash =   0x0000_0000_0000_00F0, // art dist ~4 from scan art 0xF1
            },
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                OracleId = Guid.NewGuid(),
                Name = "Wrong Card C",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "003", Rarity = "common",
                ImageHash = 0x0000_0000_0000_0006, // dist 2 from scan 0x00 (tied)
                ArtHash =   0x0000_0000_0000_3F00, // art dist ~12 from scan art
            }
        );
        ctx.SaveChanges();

        var service = CreateService();
        // Scan art hash 0xF1: dist to card A art = ~14, dist to card B art = ~4, dist to card C art = ~12
        var match = service.FindClosestMatch(
            0x0000_0000_0000_0000,
            artHashes: [0x0000_0000_0000_00F1, 0, 0]);

        Assert.NotNull(match);
        Assert.Equal("Correct Card", match.Name);
    }

    /// <summary>
    /// Correct card is slightly farther on pHash (within tie zone) but much
    /// closer on art hash. Combined scoring should rescue it.
    /// Simulates: Sourbread Auntie correction (#5) — correct card at pHash dist=8
    /// vs wrong card at pHash dist=6, but art hash strongly favors the correct card.
    /// </summary>
    [Fact]
    public void FindClosestMatch_ArtHashRescuesWithinTieZone()
    {
        using var ctx = _factory.CreateDbContext();

        // Wrong card: closer on pHash (dist=2) but far on art hash
        // Correct card: farther on pHash (dist=4, within TieZone=4) but close on art hash
        ctx.Cards.AddRange(
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                OracleId = Guid.NewGuid(),
                Name = "Wrong Card",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "001", Rarity = "common",
                ImageHash = 0x0000_0000_0000_0003, // dist 2 from scan 0x00
                ArtHash =   0xFFFF_FFFF_FFFF_FFFF, // far from scan art (dist ~64)
            },
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                OracleId = Guid.NewGuid(),
                Name = "Correct Card",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "002", Rarity = "common",
                ImageHash = 0x0000_0000_0000_000F, // dist 4 from scan 0x00 (within tie zone)
                ArtHash =   0x0000_0000_0000_0010, // close to scan art (dist 0)
            }
        );
        ctx.SaveChanges();

        var service = CreateService();
        // Scan art hash matches Correct Card's art hash exactly
        var match = service.FindClosestMatch(
            0x0000_0000_0000_0000,
            artHashes: [0x0000_0000_0000_0010, 0, 0]);

        Assert.NotNull(match);
        Assert.Equal("Correct Card", match.Name);
    }

    /// <summary>
    /// Card outside the tie zone should not be considered, even if its art
    /// hash is better. The tie zone limits how far we reach.
    /// </summary>
    [Fact]
    public void FindClosestMatch_OutsideTieZone_NotRescuedByArtHash()
    {
        using var ctx = _factory.CreateDbContext();

        ctx.Cards.AddRange(
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                OracleId = Guid.NewGuid(),
                Name = "Close pHash Card",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "001", Rarity = "common",
                ImageHash = 0x0000_0000_0000_0001, // dist 1 from scan
                ArtHash =   0xFFFF_FFFF_FFFF_FFFF, // far art
            },
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                OracleId = Guid.NewGuid(),
                Name = "Far pHash Card",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "002", Rarity = "common",
                ImageHash = 0x0000_0000_0000_FFFF, // dist 16 from scan (way outside TZ=4)
                ArtHash =   0x0000_0000_0000_0010, // perfect art match
            }
        );
        ctx.SaveChanges();

        var service = CreateService();
        var match = service.FindClosestMatch(
            0x0000_0000_0000_0000,
            artHashes: [0x0000_0000_0000_0010, 0, 0]);

        Assert.NotNull(match);
        // Close pHash Card should win because Far pHash Card is outside tie zone
        Assert.Equal("Close pHash Card", match.Name);
    }

    /// <summary>
    /// Without art hashes, pHash-only matching should work as before.
    /// No regression on the basic path.
    /// </summary>
    [Fact]
    public void FindClosestMatch_NoArtHashes_PHashWins()
    {
        using var ctx = _factory.CreateDbContext();

        ctx.Cards.AddRange(
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                OracleId = Guid.NewGuid(),
                Name = "Closest Card",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "001", Rarity = "common",
                ImageHash = 0x0000_0000_0000_0001, // dist 1
                ArtHash =   0xAAAA_AAAA_AAAA_AAAA,
            },
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                OracleId = Guid.NewGuid(),
                Name = "Farther Card",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "002", Rarity = "common",
                ImageHash = 0x0000_0000_0000_00FF, // dist 8
                ArtHash =   0xBBBB_BBBB_BBBB_BBBB,
            }
        );
        ctx.SaveChanges();

        var service = CreateService();
        // No art hashes — pure pHash matching
        var match = service.FindClosestMatch(0x0000_0000_0000_0000);

        Assert.NotNull(match);
        Assert.Equal("Closest Card", match.Name);
    }

    /// <summary>
    /// When a single candidate has the best pHash with no ties and no
    /// art hashes close, it should still win (no regression).
    /// </summary>
    [Fact]
    public void FindClosestMatch_ClearPHashWinner_StillWins()
    {
        using var ctx = _factory.CreateDbContext();

        ctx.Cards.AddRange(
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                OracleId = Guid.NewGuid(),
                Name = "Clear Winner",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "001", Rarity = "common",
                ImageHash = 0x0000_0000_0000_0000, // exact pHash match
                ArtHash =   0x0000_0000_0000_FF00, // moderate art distance
            },
            new Card
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                OracleId = Guid.NewGuid(),
                Name = "Far Card",
                Lang = "en", Layout = "normal", TypeLine = "Creature",
                SetCode = "TST", SetName = "Test Set",
                CollectorNumber = "002", Rarity = "common",
                ImageHash = 0x0000_0000_FFFF_FFFF, // dist 32
                ArtHash =   0x0000_0000_0000_0010, // close art but irrelevant
            }
        );
        ctx.SaveChanges();

        var service = CreateService();
        var match = service.FindClosestMatch(
            0x0000_0000_0000_0000,
            artHashes: [0x0000_0000_0000_0010, 0, 0]);

        Assert.NotNull(match);
        Assert.Equal("Clear Winner", match.Name);
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
