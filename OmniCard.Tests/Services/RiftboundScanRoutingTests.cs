using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

/// <summary>
/// Regression guard: a scan with <see cref="CardService.SelectedGame"/> set to
/// <see cref="CardGame.Riftbound"/> must dispatch to the registered Riftbound
/// <see cref="ICardGameService"/> via <see cref="CardService.FindBestMatch"/>.
/// Mirrors the CardService construction pattern in FallbackMatchingTests.cs, since
/// ScanMatchingIntegrationTests.cs exercises ScryfallService directly and never
/// constructs a CardService.
/// </summary>
public class RiftboundScanRoutingTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public RiftboundScanRoutingTests()
    {
        _omniConnection = new SqliteConnection("Data Source=:memory:");
        _omniConnection.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_omniConnection)
            .Options;
        using var omniCtx = new OmniCardDbContext(_omniOptions);
        omniCtx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _omniConnection.Dispose();
    }

    [Fact]
    public void FindBestMatch_RoutesRiftboundGame_ToRiftboundService()
    {
        var riftMatch = new CardMatch
        {
            Name = "Vex",
            SetCode = "OGN",
            SetName = "Origins",
            CollectorNumber = "310",
            Rarity = "common",
            GameSpecificId = "base",
            Source = new object(),
        };

        var riftService = new StubGameService(CardGame.Riftbound, match: riftMatch);
        var otherService = new StubGameService(CardGame.Mtg, match: null);

        var service = CreateCardService([riftService, otherService]);
        service.SelectedGame = CardGame.Riftbound;

        var (match, game) = service.FindBestMatch(123UL, null, null, null, null, null);

        Assert.Equal(CardGame.Riftbound, game);
        Assert.NotNull(match);
        Assert.Equal("base", match!.GameSpecificId);
    }

    private CardService CreateCardService(ICardGameService[] gameServices)
    {
        var omniFactory = new MockOmniDbContextFactory(_omniOptions);
        return new CardService(
            new StubHashService(),
            gameServices,
            omniFactory,
            new StubOcrService(),
            new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
            NullLogger<CardService>.Instance,
            new DataPathService(Path.GetTempPath()),
            new NullScanDiagnosticService(),
            new NullAuditService());
    }

    // --- Helpers (mirrors FallbackMatchingTests.cs) ---

    private class StubGameService(CardGame game, CardMatch? match) : ICardGameService
    {
        public CardGame Game => game;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public ulong? LastScanEdgeHash { get; private set; }
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        {
            LastScanEdgeHash = scanEdgeHash;
            return match;
        }
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => [];
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public object? FindCardById(string gameCardId) => null;
    }

    private class StubHashService : IPerceptualHashService
    {
        public ulong ComputeHash(Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong ComputeEdgeHash(Stream imageStream, Action<HashStageResult>? onStage = null) => 0;
        public ulong[] ComputeArtHash(Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<HashStageResult>? onStage = null) => new ulong[cropRegions.Length];
    }

    private class StubOcrService : IOcrMatchingService
    {
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] imageData) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] imageData) => Task.FromResult<(string?, double)>((null, 0));
    }

    private class NullScanDiagnosticService : IScanDiagnosticService
    {
        public void LogScanCompleted(string sessionId, ulong scanHash, CardMatch? match, MatchDiagnostics? diagnostics, ulong[]? artHashes, OcrMatchResult? ocrResult, FlagReason autoFlagReason) { }
        public void LogUserFlagged(ulong scanHash, ScannedCard card) { }
        public void LogUserConfirmed(ulong scanHash, ScannedCard card) { }
        public void LogUserCorrected(ulong scanHash, ScannedCard card, CardMatch newMatch) { }
        public void LogUserUnflagged(ulong scanHash, ScannedCard card, FlagReason previousReason) { }
        public void ExportDiagnostics(string filePath) { }
        public void ClearDiagnostics() { }
        public int GetEventCount() => 0;
    }

    private class MockOmniDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private class NullAuditService : IAuditService
    {
        public bool IsAuditActive => false;
        public int? AuditLocationId => null;
        public string? AuditLocationName => null;
        public void StartAudit(int containerId) { }
        public void EndAudit() { }
        public CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes) => null;
        public AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards) => throw new NotImplementedException();
    }
}
