using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

/// <summary>
/// Exercises the fuzzy OCR collector-number path used by Yu-Gi-Oh! (TcgCsvGameService.FuzzyOcrMatch):
/// exact canonical match, conservative distance-1 gated by pHash, and the set-prefix fallback.
/// </summary>
public class YugiohFuzzyMatchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<PokemonDbContext> _factory;

    // Three catalog cards. card1/card2 share art (near pHash hashes) — the reprint case OCR must
    // disambiguate; card3 is an unrelated art (far hash) and different set.
    private const ulong Hash1 = 0x0UL;                        // card1 art
    private const ulong Hash2 = 0xFUL;                        // card2 art: 4 bits from card1
    private const ulong Hash3 = 0xFFFFFFFFFFFFFFFFUL;         // card3 art: far from both

    public YugiohFuzzyMatchTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(_connection).Options;
        _factory = new Factory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete(); // else the service ctor wipes Cards as "pre-schema"
        ctx.Cards.AddRange(
            new TcgCsvCard { ProductId = 1, Game = CardGame.YuGiOh, Name = "Card One",
                SetCode = "GRCR", SetName = "Genesis", CollectorNumber = "GRCR-EN060", ImageHash = Hash1 },
            new TcgCsvCard { ProductId = 2, Game = CardGame.YuGiOh, Name = "Card Two",
                SetCode = "GRCR", SetName = "Genesis", CollectorNumber = "GRCR-EN033", ImageHash = Hash2 },
            new TcgCsvCard { ProductId = 3, Game = CardGame.YuGiOh, Name = "Card Three",
                SetCode = "PHHY", SetName = "Phantom", CollectorNumber = "PHHY-EN060", ImageHash = Hash3 });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private FuzzyService CreateService() => new(_factory);

    private static OcrMatchResult Ocr(string token) => new() { CollectorNumber = token, CollectorNumberConfidence = 0.9 };

    [Fact]
    public void ExactCanonicalMatch_ResolvesAcrossOcrConfusions()
    {
        var svc = CreateService();
        // "GRCR-ENO60" (O→0) canonicalizes equal to the stored "GRCR-EN060".
        var match = svc.FindClosestMatch(Hash1, ocrResult: Ocr("GRCR-ENO60"));
        Assert.NotNull(match);
        Assert.Equal("GRCR-EN060", match!.CollectorNumber);
        Assert.Equal("OcrFuzzyExact", svc.LastMatchDiagnostics.DecisionPhase);
    }

    [Fact]
    public void Distance1Match_TrustedWhenPHashAgrees()
    {
        var svc = CreateService();
        // "GRCR-EN032" is edit-distance 1 from card2 ("...033"); scan art hashes to card2.
        var match = svc.FindClosestMatch(Hash2, ocrResult: Ocr("GRCR-EN032"));
        Assert.NotNull(match);
        Assert.Equal("GRCR-EN033", match!.CollectorNumber);
        Assert.Equal("OcrFuzzyDist1", svc.LastMatchDiagnostics.DecisionPhase);
    }

    [Fact]
    public void Distance1Match_RejectedWhenNotAPHashCandidate()
    {
        var svc = CreateService();
        // "PHHY-EN061" is distance 1 from card3 ("PHHY-EN060") — but the scanned art hashes to
        // card1, and card3's art is far away, so the mis-read must NOT hijack the match to card3.
        var match = svc.FindClosestMatch(Hash1, ocrResult: Ocr("PHHY-EN061"));
        Assert.NotNull(match);
        Assert.NotEqual("PHHY-EN060", match!.CollectorNumber);      // did not jump to the unrelated card
        Assert.NotEqual("OcrFuzzyDist1", svc.LastMatchDiagnostics.DecisionPhase);
    }

    [Fact]
    public void SetPrefixFallback_NarrowsReprintsWhenNumberUnreadable()
    {
        var svc = CreateService();
        // Number too garbled to match, but the "GRCR" prefix reads: among the pHash candidates
        // (card1 & card2, both GRCR), pick the pHash-nearest — card1 for a card1 scan.
        var match = svc.FindClosestMatch(Hash1, ocrResult: Ocr("GRCR-EN"));
        Assert.NotNull(match);
        Assert.Equal("GRCR-EN060", match!.CollectorNumber);
        Assert.Equal("OcrSetPrefix", svc.LastMatchDiagnostics.DecisionPhase);
    }

    private sealed class Factory(DbContextOptions<PokemonDbContext> options) : IDbContextFactory<PokemonDbContext>
    {
        public PokemonDbContext CreateDbContext() => new(options);
    }

    // Minimal TcgCsv service that reports Yu-Gi-Oh! behaviour (fuzzy OCR matching enabled).
    private sealed class FuzzyService : TcgCsvGameService<PokemonDbContext>
    {
        public FuzzyService(IDbContextFactory<PokemonDbContext> factory)
            : base(new NoHttp(), factory, new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
                   new DataPath(), NullLogger<FuzzyService>.Instance) { }

        protected override int CategoryId => 2;
        public override CardGame Game => CardGame.YuGiOh;
        protected override string GameKey => "yugioh";
        protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => (null, null);
        protected override bool UseFuzzyOcrMatch => true;
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
