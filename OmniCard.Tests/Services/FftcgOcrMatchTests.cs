using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

/// <summary>
/// Exercises FFTCG's strict OCR collector-number path (TcgCsvGameService.FindClosestMatch Phase 0
/// with UseFuzzyOcrMatch=false): exact set-code lookup, and the reprint '/'-split tolerance that
/// lets a scanned single ("11-072R") resolve to a catalog Number that joins several printings
/// ("Re-103C/11-072R"). Also pins that the split is gated off for other games.
/// </summary>
public class FftcgOcrMatchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<FinalFantasyDbContext> _factory;

    public FftcgOcrMatchTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<FinalFantasyDbContext>().UseSqlite(_connection).Options;
        _factory = new Factory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete(); // else the service ctor wipes Cards as "pre-schema"
        ctx.Cards.AddRange(
            new TcgCsvCard { ProductId = 1, Game = CardGame.FinalFantasy, Name = "Cloud",
                SetCode = "9", SetName = "Opus IX", CollectorNumber = "9-085C", ImageHash = 0x1UL },
            new TcgCsvCard { ProductId = 2, Game = CardGame.FinalFantasy, Name = "Reprint Card",
                SetCode = "RE", SetName = "Reprint", CollectorNumber = "Re-103C/11-072R", ImageHash = 0x2UL },
            // Shares the "072R" tail with the reprint's second part but is a distinct number — proves
            // the '/'-boundary match doesn't substring-collide.
            new TcgCsvCard { ProductId = 3, Game = CardGame.FinalFantasy, Name = "Other",
                SetCode = "29", SetName = "Blissful Eternity", CollectorNumber = "29-072R", ImageHash = 0x3UL });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private static OcrMatchResult Ocr(string token) => new() { CollectorNumber = token, CollectorNumberConfidence = 0.9 };

    [Fact]
    public void ExactNumber_Resolves()
    {
        var svc = new FftcgLikeService(_factory);
        var match = svc.FindClosestMatch(0x1UL, ocrResult: Ocr("9-085C"));
        Assert.NotNull(match);
        Assert.Equal("9-085C", match!.CollectorNumber);
        Assert.Equal("OcrCollectorNumber", svc.LastMatchDiagnostics!.DecisionPhase);
    }

    [Theory]
    [InlineData("11-072R")]  // second '/'-part of "Re-103C/11-072R"
    [InlineData("Re-103C")]  // first '/'-part
    public void ReprintPart_ResolvesToJoinedCatalogNumber(string token)
    {
        var svc = new FftcgLikeService(_factory);
        var match = svc.FindClosestMatch(0x2UL, ocrResult: Ocr(token));
        Assert.NotNull(match);
        Assert.Equal("Re-103C/11-072R", match!.CollectorNumber);
        Assert.Equal("OcrCollectorNumber", svc.LastMatchDiagnostics!.DecisionPhase);
    }

    [Fact]
    public void ReprintSplit_DoesNotSubstringCollide()
    {
        // "11-072R" must resolve to the reprint (card 2), never to card 3 ("29-072R") which merely
        // shares the "072R" suffix — the '/'-boundary LIKE excludes non-part substrings.
        var svc = new FftcgLikeService(_factory);
        var match = svc.FindClosestMatch(0x2UL, ocrResult: Ocr("11-072R"));
        Assert.NotNull(match);
        Assert.NotEqual("29-072R", match!.CollectorNumber);
    }

    [Fact]
    public void Split_IsGatedOff_ForNonSplitGames()
    {
        // A game that doesn't opt into SplitReprintNumbers must NOT match a '/'-part; the read
        // falls through to pHash (here there's no confident hash match, so no OCR resolution).
        var svc = new NonSplitService(_factory);
        var match = svc.FindClosestMatch(0x2UL, ocrResult: Ocr("11-072R"));
        Assert.NotEqual("OcrCollectorNumber", svc.LastMatchDiagnostics!.DecisionPhase);
    }

    private sealed class Factory(DbContextOptions<FinalFantasyDbContext> options) : IDbContextFactory<FinalFantasyDbContext>
    {
        public FinalFantasyDbContext CreateDbContext() => new(options);
    }

    // Reports FFTCG behaviour: strict exact lookup + reprint '/'-split.
    private sealed class FftcgLikeService(IDbContextFactory<FinalFantasyDbContext> factory)
        : TcgCsvGameService<FinalFantasyDbContext>(new NoHttp(), factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance), new DataPath(),
            NullLogger<FftcgLikeService>.Instance)
    {
        protected override int CategoryId => 24;
        public override CardGame Game => CardGame.FinalFantasy;
        protected override string GameKey => "fftcg";
        protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => (null, null);
        protected override bool SplitReprintNumbers => true;
    }

    // Same catalog but split disabled (default) — models Pokémon/other games.
    private sealed class NonSplitService(IDbContextFactory<FinalFantasyDbContext> factory)
        : TcgCsvGameService<FinalFantasyDbContext>(new NoHttp(), factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance), new DataPath(),
            NullLogger<NonSplitService>.Instance)
    {
        protected override int CategoryId => 24;
        public override CardGame Game => CardGame.FinalFantasy;
        protected override string GameKey => "fftcg";
        protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => (null, null);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class DataPath : IDataPathService
    {
        public string DataDirectory => Path.GetTempPath();
        public string ScansDirectory => Path.GetTempPath();
        public string TempScansDirectory => Path.GetTempPath();
        public string SymbolsCacheDirectory => Path.GetTempPath();
        public string LogsDirectory => Path.GetTempPath();
        public string TradesDirectory => Path.GetTempPath();
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
