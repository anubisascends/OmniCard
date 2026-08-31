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

    /// <summary>Seeds a trade session with one or more traded-away lots + their Trade rows, as
    /// TradeImportService would leave them. Returns the session id and the outgoing lot ids.</summary>
    private (int SessionId, int[] OriginalLotIds) SeedOpenTrade(int outgoingCards = 1)
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        var session = new TradeSession
        {
            SessionRecordId = Guid.NewGuid(),
            Note = "traded",
            CreatedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow,
        };
        ctx.TradeSessions.Add(session);
        ctx.SaveChanges();

        var lotIds = new List<int>();
        for (var i = 0; i < outgoingCards; i++)
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, GameCardId = "old" + i, Name = "Traded Card " + i };
            ctx.Products.Add(product);
            ctx.SaveChanges();
            var lot = new InventoryLot { ProductId = product.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotIds.Add(lot.Id);

            ctx.Trades.Add(new Trade
            {
                TradeSessionId = session.Id,
                TradeRecordId = session.SessionRecordId,
                Game = CardGame.Mtg,
                CardName = product.Name,
                OriginalLotId = lot.Id,
                CreatedAt = DateTime.UtcNow,
                ImportedAt = DateTime.UtcNow,
            });
        }
        ctx.SaveChanges();
        return (session.Id, lotIds.ToArray());
    }

    private static ScannedCard NewScan(CardGame game, string gameCardId, int? linkedSessionId) => new()
    {
        Hash = 1,
        Game = game,
        TempImagePath = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".jpg"),
        Match = new CardMatch { Name = "Replacement", SetCode = "TST", GameSpecificId = gameCardId },
        LinkedTradeSessionId = linkedSessionId,
    };

    [Fact]
    public void CommitScans_LinkedScan_SetsFulfilledTradeSessionIdOnNewLot()
    {
        var (sessionId, _) = SeedOpenTrade();
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "new", sessionId)]);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var newLot = ctx.Lots.Single(l => l.Product.GameCardId == "new");
        Assert.Equal(sessionId, newLot.FulfilledTradeSessionId);
    }

    [Fact]
    public void CommitScans_LinkedScan_DeletesAllOutgoingLotsOnFirstFulfillment()
    {
        var (sessionId, originalLotIds) = SeedOpenTrade(outgoingCards: 3);
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "new", sessionId)]);

        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.False(ctx.Lots.Any(l => originalLotIds.Contains(l.Id)));
    }

    [Fact]
    public void CommitScans_LinkedScan_SetsSessionFirstFulfilledAtAndClearsOriginalLotIds()
    {
        var (sessionId, _) = SeedOpenTrade(outgoingCards: 2);
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "new", sessionId)]);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var session = ctx.TradeSessions.Single(s => s.Id == sessionId);
        Assert.NotNull(session.FirstFulfilledAt);
        Assert.All(ctx.Trades.Where(t => t.TradeSessionId == sessionId), t => Assert.Null(t.OriginalLotId));
    }

    [Fact]
    public void CommitScans_SecondReplacementForSameSession_DoesNotErrorOrRedelete()
    {
        var (sessionId, _) = SeedOpenTrade();
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "first-replacement", sessionId)]);
        var firstFulfilledAt = new OmniCardDbContext(_omniOptions).TradeSessions.Single(s => s.Id == sessionId).FirstFulfilledAt;

        // Second commit, same session — the original lots are already gone; should just link, not throw.
        var exception = Record.Exception(() => svc.CommitScans([NewScan(CardGame.Mtg, "second-replacement", sessionId)]));

        Assert.Null(exception);
        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Equal(2, ctx.Lots.Count(l => l.FulfilledTradeSessionId == sessionId));
        Assert.Equal(firstFulfilledAt, ctx.TradeSessions.Single(s => s.Id == sessionId).FirstFulfilledAt);
    }

    [Fact]
    public void CommitScans_UnlinkedScan_DoesNotSetFulfilledTradeSessionId()
    {
        var svc = CreateService();

        svc.CommitScans([NewScan(CardGame.Mtg, "plain", null)]);

        using var ctx = new OmniCardDbContext(_omniOptions);
        var lot = ctx.Lots.Single(l => l.Product.GameCardId == "plain");
        Assert.Null(lot.FulfilledTradeSessionId);
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
    }

    private class StubScannerSettingsService : IScannerSettingsService
    {
        public ScanWorkflowMode WorkflowMode { get; private set; } = ScanWorkflowMode.Store;
        public void SetWorkflowMode(ScanWorkflowMode mode) => WorkflowMode = mode;
    }
}
