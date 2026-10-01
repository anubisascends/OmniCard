using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching.Games;
using OmniCard.Data.Catalogs;
using OmniCard.Imaging;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Settings;

namespace OmniCard.Tests.Services.Games;

/// <summary>
/// OptcgService Phase 0: raw collector-line reads are snapped to catalog numbers, the scan's image hash
/// settles disagreeing reads, and it picks the alt-art variant only when that art is clearly the scan's —
/// the catalog's base images carry a "SAMPLE" watermark, so same-art variants hash a little nearer a
/// plain scan than the base does.
/// </summary>
public class OptcgOcrResolutionTests : IDisposable
{
    private const ulong Scan = 0UL;
    private static ulong Bits(int n) => n == 0 ? 0UL : ulong.MaxValue >> (64 - n); // Hamming distance n from Scan

    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgOcrResolutionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _factory = new TestOptcgDbFactory(new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        // OP09-110: the scan's card. OP11-088: what a read that drops "09" from "OP09-110" hits exactly.
        Add(ctx, "OP09-110", "OP09-110", 0, "OP09", "Pierre", Bits(9));
        Add(ctx, "OP11-088", "OP11-088", 0, "OP11", "Wrong Card", Bits(30));
        // OP14-056: a same-art Release Event variant hashing 4 bits nearer than the watermarked base.
        Add(ctx, "OP14-056", "OP14-056", 0, "OP14", "Wadatsumi", Bits(14));
        Add(ctx, "OP14-056_p1", "OP14-056", 1, "OP14", "Wadatsumi", Bits(10));
        // OP12-093: the scan is a genuinely different alt-art (base 24 vs variant 10), plus an unhashed variant.
        Add(ctx, "OP12-093", "OP12-093", 0, "OP12", "Morley", Bits(24));
        Add(ctx, "OP12-093_p2", "OP12-093", 2, "OP12", "Morley", Bits(10));
        Add(ctx, "OP12-093_p3", "OP12-093", 3, "OP12", "Morley", null);
        ctx.SaveChanges();
        ctx.MarkMigrationComplete();
    }

    private static void Add(OptcgDbContext ctx, string id, string number, int variant, string set, string name, ulong? hash) =>
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = id, CardNumber = number, VariantIndex = variant, SetId = set, SetName = set,
            CardName = name, Rarity = "C", ImageHash = hash,
        });

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        return new OptcgService(new StubHttpClientFactory(), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object, NullLogger<OptcgService>.Instance);
    }

    private static OcrMatchResult Reads(params string[] texts) => new() { CollectorTexts = texts };

    [Fact]
    public void GarbledRead_ResolvesToCatalogNumber()
    {
        var svc = CreateService();

        var match = svc.FindClosestMatch(Scan, ocrResult: Reads("OPO9-110"));

        Assert.Equal("OP09-110", match?.GameSpecificId);
        Assert.Equal("OcrCollectorNumber", svc.LastMatchDiagnostics!.DecisionPhase);
        Assert.Equal(100, match!.Confidence);
    }

    [Fact]
    public void FuzzyOnlyRead_IsReportedWithLowerConfidence()
    {
        var match = CreateService().FindClosestMatch(Scan, ocrResult: Reads("P09-110")); // dropped "O": one edit

        Assert.Equal("OP09-110", match?.GameSpecificId);
        Assert.True(match!.Confidence < 100);
    }

    [Fact]
    public void DisagreeingReads_ImagePrefersTheLowerVotedNumber_WhenClearlyNearer()
    {
        // Two passes read the exact-but-wrong OP11-088 (pHash 30); one reads OP09-110 (pHash 9).
        var match = CreateService().FindClosestMatch(Scan, ocrResult: Reads("OP-11088", "OP-11088", "OPO9-110"));

        Assert.Equal("OP09-110", match?.GameSpecificId);
    }

    [Fact]
    public void SameArtVariant_SlightlyNearer_StaysOnBasePrinting()
    {
        var match = CreateService().FindClosestMatch(Scan, ocrResult: Reads("OP14-056"));

        Assert.Equal("OP14-056", match?.GameSpecificId);
    }

    [Fact]
    public void AltArtVariant_ClearlyNearer_IsPicked()
    {
        var match = CreateService().FindClosestMatch(Scan, ocrResult: Reads("OP12-093"));

        Assert.Equal("OP12-093_p2", match?.GameSpecificId);
        Assert.Equal("OP12-093", match!.CollectorNumber);
    }

    [Fact]
    public void ReadOutsideSetFilter_FallsThroughToPHash()
    {
        var svc = CreateService();
        var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OP14" };

        var match = svc.FindClosestMatch(Scan, ocrResult: Reads("OP09-110"), setFilter: filter);

        Assert.NotEqual("OcrCollectorNumber", svc.LastMatchDiagnostics!.DecisionPhase);
        Assert.Equal("OP14", match?.SetCode);
    }

    [Fact]
    public void UnresolvableRead_FallsThroughToPHash()
    {
        var svc = CreateService();

        var match = svc.FindClosestMatch(Scan, ocrResult: Reads("KP", "W"));

        Assert.Equal("PHashConfident", svc.LastMatchDiagnostics!.DecisionPhase);
        Assert.Equal("OP09-110", match?.GameSpecificId); // the nearest hash (9)
    }

    [Fact]
    public void StrictCollectorNumber_StillResolves()
    {
        var ocr = new OcrMatchResult { CollectorNumber = "OP12-093", CollectorNumberConfidence = 0.9 };

        Assert.Equal("OP12-093_p2", CreateService().FindClosestMatch(Scan, ocrResult: ocr)?.GameSpecificId);
    }

    [Fact]
    public void PHashPath_SameArtVariantNearest_ReturnsBasePrinting()
    {
        // Only OP14-056's rows in play: the stamped variant (10) out-hashes the watermarked base (14).
        var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OP14" };

        var match = CreateService().FindClosestMatch(Scan, setFilter: filter);

        Assert.Equal("OP14-056", match?.GameSpecificId);
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
