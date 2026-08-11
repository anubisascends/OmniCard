using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class CardServiceTradeFulfillmentTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;

    public CardServiceTradeFulfillmentTests()
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

    private IDbContextFactory<OmniCardDbContext> CreateOmniFactory() => new MockOmniDbContextFactory(_omniOptions);

    private CardService CreateService() => new(
        new StubHashService(),
        [],
        CreateOmniFactory(),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService(),
        new NullAuditService(),
        new StubScannerSettingsService());

    /// <summary>Seeds a traded-away lot + its Trade row, as TradeImportService would leave them.</summary>
    private (int TradeId, int OriginalLotId) SeedOpenTrade(string cardName = "Traded Card")
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "old", Name = cardName };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();

        var trade = new Trade
        {
            TradeRecordId = Guid.NewGuid(),
            Game = CardGame.Mtg,
            CardName = cardName,
            Note = "traded",
            OriginalLotId = lot.Id,
            CreatedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow,
        };
        ctx.Trades.Add(trade);
        ctx.SaveChanges();
        return (trade.Id, lot.Id);
    }

    private static ScannedCard NewScan(CardGame game, string gameCardId, int? linkedTradeId) => new()
    {
        Hash = 1,
        Game = game,
        TempImagePath = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".jpg"),
        Match = new CardMatch { Name = "Replacement", SetCode = "TST", GameSpecificId = gameCardId },
        LinkedTradeId = linkedTradeId,
    };

    [Fact]
    public void CommitScans_LinkedScan_SetsFulfilledTradeIdOnNewLot()
    {
        var (tradeId, _) = SeedOpenTrade();
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "new", tradeId)]);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var newLot = ctx.Lots.Single(l => l.Product.GameCardId == "new");
        Assert.Equal(tradeId, newLot.FulfilledTradeId);
    }

    [Fact]
    public void CommitScans_LinkedScan_DeletesOriginalLotOnFirstFulfillment()
    {
        var (tradeId, originalLotId) = SeedOpenTrade();
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "new", tradeId)]);

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.False(ctx.Lots.Any(l => l.Id == originalLotId));
    }

    [Fact]
    public void CommitScans_LinkedScan_SetsTradeFirstFulfilledAtAndClearsOriginalLotId()
    {
        var (tradeId, _) = SeedOpenTrade();
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "new", tradeId)]);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var trade = ctx.Trades.Single(t => t.Id == tradeId);
        Assert.NotNull(trade.FirstFulfilledAt);
        Assert.Null(trade.OriginalLotId);
    }

    [Fact]
    public void CommitScans_SecondReplacementForSameTrade_DoesNotErrorOrRedelete()
    {
        var (tradeId, _) = SeedOpenTrade();
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "first-replacement", tradeId)]);
        var firstFulfilledAt = new OmniCardDbContext(_omniOptions).Trades.Single(t => t.Id == tradeId).FirstFulfilledAt;

        // Second commit, same trade — the original lot is already gone; should just link, not throw.
        var exception = Record.Exception(() => svc.CommitScans([NewScan(CardGame.Mtg, "second-replacement", tradeId)]));

        Assert.Null(exception);
        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Equal(2, ctx.Lots.Count(l => l.FulfilledTradeId == tradeId));
        Assert.Equal(firstFulfilledAt, ctx.Trades.Single(t => t.Id == tradeId).FirstFulfilledAt);
    }

    [Fact]
    public void CommitScans_UnlinkedScan_DoesNotSetFulfilledTradeId()
    {
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "plain", null)]);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var lot = ctx.Lots.Single(l => l.Product.GameCardId == "plain");
        Assert.Null(lot.FulfilledTradeId);
    }

    // --- Helpers (mirrors CardServiceCollectionTests) ---

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

    private class StubScannerSettingsService : IScannerSettingsService
    {
        public ScanWorkflowMode WorkflowMode { get; private set; } = ScanWorkflowMode.Store;
        public void SetWorkflowMode(ScanWorkflowMode mode) => WorkflowMode = mode;
    }
}
