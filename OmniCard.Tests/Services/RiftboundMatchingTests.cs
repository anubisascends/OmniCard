using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundMatchingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<RiftboundDbContext> _factory;
    private readonly RiftboundService _svc;

    public RiftboundMatchingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
        _factory = new Factory(options);
        using (var ctx = _factory.CreateDbContext())
        {
            ctx.Database.EnsureCreated();
            ctx.MarkMigrationComplete();
            // Two printings of collector 310 (base + alt art) — OCR gives (OGN,310); pHash must disambiguate.
            ctx.Cards.Add(new RiftboundCard { Id = "base", CollectorNumber = 310, SetId = "OGN", SetName = "Origins",
                Name = "Vex", Rarity = "Epic", CardType = "Legend", ImageHash = 0x0UL, AlternateArt = false, CardImageUri="u" });
            ctx.Cards.Add(new RiftboundCard { Id = "alt", CollectorNumber = 310, SetId = "OGN", SetName = "Origins",
                Name = "Vex", Rarity = "Epic", CardType = "Legend", ImageHash = 0xFFFFFFFFFFFFFFFFUL, AlternateArt = true, CardImageUri="u" });
            // A different card for pure pHash fallback.
            ctx.Cards.Add(new RiftboundCard { Id = "solo", CollectorNumber = 5, SetId = "OGN", SetName = "Origins",
                Name = "Solo", Rarity = "Common", CardType = "Unit", ImageHash = 0x00FF00FF00FF00FFUL, CardImageUri="u" });
            ctx.SaveChanges();
        }
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        _svc = new RiftboundService(new NullFactory(), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object, NullLogger<RiftboundService>.Instance);
    }

    public void Dispose() { _svc.Dispose(); _connection.Dispose(); }

    [Fact]
    public void ParsesOcrCollectorNumber_IgnoringTotal()
    {
        Assert.True(RiftboundService.TryParseOcrCollectorNumber("OGN-310", out var set, out var num));
        Assert.Equal("OGN", set);
        Assert.Equal(310, num);
    }

    [Fact]
    public void Ocr_MultipleCandidates_DisambiguatesByPHash()
    {
        var ocr = new OcrMatchResult { CollectorNumber = "OGN-310", CollectorNumberConfidence = 0.95 };
        // Scan hash all-zero → nearest is the base printing (ImageHash 0x0).
        var match = _svc.FindClosestMatch(0x0UL, ocrResult: ocr);
        Assert.NotNull(match);
        Assert.Equal("base", match!.GameSpecificId);

        // Scan hash all-ones → nearest is the alt art.
        var match2 = _svc.FindClosestMatch(0xFFFFFFFFFFFFFFFFUL, ocrResult: ocr);
        Assert.Equal("alt", match2!.GameSpecificId);
    }

    [Fact]
    public void NoOcr_FallsBackToPHash()
    {
        var match = _svc.FindClosestMatch(0x00FF00FF00FF00FFUL);
        Assert.NotNull(match);
        Assert.Equal("solo", match!.GameSpecificId);
    }

    [Fact]
    public void PHash_BeyondThreshold_ReturnsNull()
    {
        // Far from every stored hash; maxDistance small.
        var match = _svc.FindClosestMatch(0x0123456789ABCDEFUL, maxDistance: 1);
        Assert.Null(match);
    }

    [Fact]
    public void Ocr_SingleCandidate_ReturnsExactMatch()
    {
        var ocr = new OcrMatchResult { CollectorNumber = "OGN-5", CollectorNumberConfidence = 0.95 };
        var match = _svc.FindClosestMatch(0xDEADBEEFUL, ocrResult: ocr);
        Assert.NotNull(match);
        Assert.Equal("solo", match!.GameSpecificId);
        Assert.Equal(100, match.Confidence);
    }

    [Fact]
    public void Ocr_CandidatesWithoutImageHash_FallsBackToFirstCandidate()
    {
        // Phase 0's OCR-collector-number branch queries _readContext fresh on every call
        // (unlike the pHash caches, which are memoized in _hashCache/_edgeHashCache), so a
        // row inserted through a new context sharing the same in-memory Sqlite connection
        // is visible to _svc without reconstructing the service.
        using (var ctx = _factory.CreateDbContext())
        {
            ctx.Cards.Add(new RiftboundCard { Id = "fallback1", CollectorNumber = 99, SetId = "OGN", SetName = "Origins",
                Name = "Nohash1", Rarity = "Common", CardType = "Unit", ImageHash = null, CardImageUri = "u" });
            ctx.Cards.Add(new RiftboundCard { Id = "fallback2", CollectorNumber = 99, SetId = "OGN", SetName = "Origins",
                Name = "Nohash2", Rarity = "Common", CardType = "Unit", ImageHash = null, CardImageUri = "u" });
            ctx.SaveChanges();
        }

        var ocr = new OcrMatchResult { CollectorNumber = "OGN-99", CollectorNumberConfidence = 0.95 };
        var match = _svc.FindClosestMatch(0x0UL, ocrResult: ocr);

        Assert.NotNull(match);
        Assert.Contains(match!.GameSpecificId, new[] { "fallback1", "fallback2" });
        Assert.Equal(100, match.Confidence);
    }

    private class NullFactory : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(); }
    private class Factory(DbContextOptions<RiftboundDbContext> o) : IDbContextFactory<RiftboundDbContext>
    { public RiftboundDbContext CreateDbContext() => new(o); }
}
