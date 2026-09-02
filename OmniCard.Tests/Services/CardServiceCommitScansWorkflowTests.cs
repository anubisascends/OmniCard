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

/// <summary>Covers the Store-vs-Discard scan workflow branch in CardService.CommitScans,
/// including the transactional guarantee that Discard-mode temp-file deletion only happens
/// after a successful DB commit.</summary>
public class CardServiceCommitScansWorkflowTests : IDisposable
{
    private readonly SqliteConnection _omniConnection;
    private readonly DbContextOptions<OmniCardDbContext> _omniOptions;
    private readonly string _tempDataDir;
    private readonly StubDataPathService _dataPath;
    private readonly StubScannerSettingsService _scannerSettings = new();

    public CardServiceCommitScansWorkflowTests()
    {
        _omniConnection = new SqliteConnection("Data Source=:memory:");
        _omniConnection.Open();
        _omniOptions = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_omniConnection).Options;
        using var ctx = new OmniCardDbContext(_omniOptions);
        ctx.Database.EnsureCreated();

        _tempDataDir = Path.Combine(Path.GetTempPath(), "OmniCardTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDataDir);
        _dataPath = new StubDataPathService(_tempDataDir);
    }

    public void Dispose()
    {
        _omniConnection.Dispose();
        if (Directory.Exists(_tempDataDir)) Directory.Delete(_tempDataDir, recursive: true);
    }

    private CardService CreateService() => new(
        new StubHashService(),
        [],
        new MockOmniDbContextFactory(_omniOptions),
        new StubOcrService(),
        new ScanImageCache(_dataPath, NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance,
        _dataPath,
        new NullScanDiagnosticService(),
        new NullAuditService(),
        _scannerSettings);

    private static ScannedCard NewScan(string gameCardId, byte[]? imageBytes = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"scan-{Guid.NewGuid()}.jpg");
        File.WriteAllBytes(tempPath, imageBytes ?? MinimalJpegBytes());
        return new ScannedCard
        {
            Hash = 1,
            Game = CardGame.Mtg,
            TempImagePath = tempPath,
            Match = new CardMatch { Name = "Test Card", SetCode = "TST", GameSpecificId = gameCardId },
        };
    }

    // A tiny, real, decodable 1x1 JPEG so System.Drawing's decoder (used by ConvertToJpeg) succeeds.
    private static byte[] MinimalJpegBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
        0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x03, 0x02, 0x02, 0x02, 0x02, 0x02, 0x03,
        0x02, 0x02, 0x02, 0x03, 0x03, 0x03, 0x03, 0x04, 0x06, 0x04, 0x04, 0x04, 0x04, 0x04, 0x08, 0x06,
        0x06, 0x05, 0x06, 0x09, 0x08, 0x0A, 0x0A, 0x09, 0x08, 0x09, 0x09, 0x0A, 0x0C, 0x0F, 0x0C, 0x0A,
        0x0B, 0x0E, 0x0B, 0x09, 0x09, 0x0D, 0x11, 0x0D, 0x0E, 0x0F, 0x10, 0x10, 0x11, 0x10, 0x0A, 0x0C,
        0x12, 0x13, 0x12, 0x10, 0x13, 0x0F, 0x10, 0x10, 0x10, 0xFF, 0xC9, 0x00, 0x0B, 0x08, 0x00, 0x01,
        0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xCC, 0x00, 0x06, 0x00, 0x10, 0x10, 0x05, 0xFF, 0xDA,
        0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0xD2, 0xCF, 0x20, 0xFF, 0xD9,
    ];

    private int SeedLotFromCommit(string gameCardId)
    {
        using var ctx = new OmniCardDbContext(_omniOptions);
        return ctx.Lots.Include(l => l.Product).Single(l => l.Product.GameCardId == gameCardId).Id;
    }

    [Fact]
    public void CommitScans_StoreMode_CopiesImageAndSetsScanImagePath_DeletesTemp()
    {
        _scannerSettings.SetWorkflowMode(ScanWorkflowMode.Store);
        var scan = NewScan("original-mode");
        var tempPath = scan.TempImagePath;

        CreateService().CommitScans([scan]);

        var lotId = SeedLotFromCommit("original-mode");
        using var ctx = new OmniCardDbContext(_omniOptions);
        var lot = ctx.Lots.Single(l => l.Id == lotId);

        Assert.Equal($"scans/{lotId}.jpg", lot.ScanImagePath);
        Assert.True(File.Exists(Path.Combine(_dataPath.ScansDirectory, $"{lotId}.jpg")));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void CommitScans_DiscardMode_DeletesTempFile_NoScanImagePath_NoScansDirEntry()
    {
        _scannerSettings.SetWorkflowMode(ScanWorkflowMode.Discard);
        var scan = NewScan("new-mode");
        var tempPath = scan.TempImagePath;

        CreateService().CommitScans([scan]);

        var lotId = SeedLotFromCommit("new-mode");
        using var ctx = new OmniCardDbContext(_omniOptions);
        var lot = ctx.Lots.Single(l => l.Id == lotId);

        Assert.Null(lot.ScanImagePath);
        Assert.False(File.Exists(tempPath));
        Assert.False(Directory.Exists(_dataPath.ScansDirectory) &&
                     File.Exists(Path.Combine(_dataPath.ScansDirectory, $"{lotId}.jpg")));
    }

    [Fact]
    public void CommitScans_DiscardMode_TempDeleteFailureIsLoggedNotThrown()
    {
        _scannerSettings.SetWorkflowMode(ScanWorkflowMode.Discard);
        var scan = NewScan("missing-temp-file");
        File.Delete(scan.TempImagePath); // simulate a file that's already gone / locked away

        var exception = Record.Exception(() => CreateService().CommitScans([scan]));

        Assert.Null(exception);
        var lotId = SeedLotFromCommit("missing-temp-file");
        using var ctx = new OmniCardDbContext(_omniOptions);
        Assert.Null(ctx.Lots.Single(l => l.Id == lotId).ScanImagePath);
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

    private class MockOmniDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private class StubDataPathService(string dataDirectory) : IDataPathService
    {
        public string DataDirectory => dataDirectory;
        public string ScansDirectory => Path.Combine(dataDirectory, "scans");
        public string TempScansDirectory => Path.Combine(dataDirectory, "temp_scans");
        public string SymbolsCacheDirectory => Path.Combine(dataDirectory, "symbols", "sets");
        public string LogsDirectory => Path.Combine(dataDirectory, "logs");
        public string TradesDirectory => Path.Combine(dataDirectory, "trades");
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
