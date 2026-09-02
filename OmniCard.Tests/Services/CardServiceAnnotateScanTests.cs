using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class CardServiceAnnotateScanTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;
    private readonly StubGameService _gameService = new();

    public CardServiceAnnotateScanTests()
    {
        _omniConnection = new SqliteConnection("Data Source=:memory:");
        _omniConnection.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_omniConnection)
            .Options;
        using var ctx = new OmniCardDbContext(_omniOptions);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _omniConnection.Dispose();

    private CardService CreateService() => new(
        new StubHashService(),
        [_gameService],
        new MockOmniDbContextFactory(_omniOptions),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService(),
        new StubScannerSettingsService());

    private void SeedLot(string gameCardId, bool foil)
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Single,
            GameCardId = gameCardId,
            Foil = foil,
            Name = "Test Card",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id });
        ctx.SaveChanges();
    }

    private static CardMatch NewMatch(string gameCardId) => new()
    {
        Name = "Test Card",
        SetCode = "TST",
        GameSpecificId = gameCardId,
    };

    [Fact]
    public void IsFirstCopy_ReturnsTrue_WhenNoLotsExist()
    {
        var svc = CreateService();
        Assert.True(svc.IsFirstCopy(CardGame.Mtg, "a", isFoil: false));
    }

    [Fact]
    public void IsFirstCopy_ReturnsFalse_WhenMatchingLotExists()
    {
        SeedLot("a", foil: false);
        var svc = CreateService();
        Assert.False(svc.IsFirstCopy(CardGame.Mtg, "a", isFoil: false));
    }

    [Fact]
    public void IsFirstCopy_TreatsFoilAndNonFoilAsSeparateVariants()
    {
        SeedLot("a", foil: false);
        var svc = CreateService();

        Assert.False(svc.IsFirstCopy(CardGame.Mtg, "a", isFoil: false));
        Assert.True(svc.IsFirstCopy(CardGame.Mtg, "a", isFoil: true));
    }

    [Fact]
    public void AnnotateScan_SetsIsFirstCopyAndCurrentPrice_WhenMatched()
    {
        _gameService.Prices[("a", false)] = 5.00m;
        var svc = CreateService();
        var scan = new ScannedCard { Hash = 1, Game = CardGame.Mtg, Match = NewMatch("a"), IsFoil = false };

        svc.AnnotateScan(scan);

        Assert.True(scan.IsFirstCopy);
        Assert.Equal(5.00m, scan.CurrentPrice);
    }

    [Fact]
    public void AnnotateScan_IsFirstCopyFalse_WhenAlreadyOwned()
    {
        SeedLot("a", foil: false);
        var svc = CreateService();
        var scan = new ScannedCard { Hash = 1, Game = CardGame.Mtg, Match = NewMatch("a"), IsFoil = false };

        svc.AnnotateScan(scan);

        Assert.False(scan.IsFirstCopy);
    }

    [Fact]
    public void AnnotateScan_ClearsBothFields_WhenMatchIsNull()
    {
        var svc = CreateService();
        var scan = new ScannedCard { Hash = 1, Game = CardGame.Mtg, Match = null, IsFirstCopy = true, CurrentPrice = 5m };

        svc.AnnotateScan(scan);

        Assert.False(scan.IsFirstCopy);
        Assert.Null(scan.CurrentPrice);
    }

    // --- Helpers ---

    private sealed class StubGameService : ICardGameService
    {
        public Dictionary<(string GameCardId, bool IsFoil), decimal> Prices { get; } = new();

        public CardGame Game => CardGame.Mtg;
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) =>
            Prices.TryGetValue((gameCardId, isFoil), out var price) ? price : null;

        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => new();
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public List<SetCatalogCard> GetSetCards(string setCode) => [];
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
        public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] imageData, OcrCollectorSpec spec) => Task.FromResult<(string?, double)>((null, 0));
        public Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] imageData) => Task.FromResult<(string?, string?, double)>((null, null, 0));
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
        public AuditReport GenerateFileAuditReport(int containerId, IEnumerable<CollectionCard> importedCards) => throw new NotImplementedException();
    }

    private class StubScannerSettingsService : IScannerSettingsService
    {
        public ScanWorkflowMode WorkflowMode { get; private set; } = ScanWorkflowMode.Store;
        public void SetWorkflowMode(ScanWorkflowMode mode) => WorkflowMode = mode;
    }
}
