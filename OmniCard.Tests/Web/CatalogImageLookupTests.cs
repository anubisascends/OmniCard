using System.IO;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web;

namespace OmniCard.Tests.Web;

public class CatalogImageLookupTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StubDataPathService _dataPath;

    public CatalogImageLookupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_tempDir, "scans"));
        _dataPath = new StubDataPathService(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private void WriteFakeScanFile(string fileName)
        => File.WriteAllBytes(Path.Combine(_dataPath.ScansDirectory, fileName), [0xFF, 0xD8, 0xFF]);

    [Fact]
    public void Resolve_ScanFileExistsOnDisk_ReturnsScanUrl_NoCatalogLookup()
    {
        WriteFakeScanFile("123.jpg");
        var lookup = new CatalogImageLookup([new StubGameService(CardGame.Mtg, "unused")], _dataPath);

        var url = lookup.Resolve(CardGame.Mtg, "abc", "scans/123.jpg", "https://img/api.jpg");

        Assert.Equal("/scans/123.jpg", url);
    }

    [Fact]
    public void Resolve_ScanPathSetButFileMissingOnDisk_FallsBackToImageUri()
    {
        // Regression: a lot can carry a stale ScanImagePath whose file is gone (e.g. archived
        // and deleted after switching to the Discard workflow) — must fall through, not 404.
        var lookup = new CatalogImageLookup([], _dataPath);

        var url = lookup.Resolve(CardGame.Mtg, "abc", "scans/123.jpg", "https://img/api.jpg");

        Assert.Equal("https://img/api.jpg", url);
    }

    [Fact]
    public void Resolve_NoScanNoImageUri_FallsBackToCatalogLookup()
    {
        var lookup = new CatalogImageLookup([new StubGameService(CardGame.Mtg, "https://catalog/original.jpg")], _dataPath);

        var url = lookup.Resolve(CardGame.Mtg, "abc", null, null);

        Assert.Equal("https://catalog/original.jpg", url);
    }

    [Fact]
    public void Resolve_NoMatchingGameService_ReturnsNull()
    {
        var lookup = new CatalogImageLookup([new StubGameService(CardGame.OnePiece, "https://catalog/other.jpg")], _dataPath);

        var url = lookup.Resolve(CardGame.Mtg, "abc", null, null);

        Assert.Null(url);
    }

    [Fact]
    public void Resolve_EmptyGameCardId_ReturnsNull_DoesNotCallGameService()
    {
        var lookup = new CatalogImageLookup([new StubGameService(CardGame.Mtg, "https://catalog/original.jpg")], _dataPath);

        var url = lookup.Resolve(CardGame.Mtg, "", null, null);

        Assert.Null(url);
    }

    private class StubGameService(CardGame game, string imageUri) : ICardGameService
    {
        public CardGame Game { get; } = game;
        public object? FindCardById(string gameCardId) => new Card { ImageUris = new ImageUris { Normal = imageUri } };

        // Unused members
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => new();
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
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
