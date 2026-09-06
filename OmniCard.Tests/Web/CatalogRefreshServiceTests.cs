using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;
using Xunit;

namespace OmniCard.Tests.Web;

/// <summary>Covers the catalog-refresh job runner: validation, the single-job concurrency guard, and
/// success/failure state transitions. A gated stub game service keeps the running job pending so the
/// guard can be asserted deterministically.</summary>
public class CatalogRefreshServiceTests
{
    // Image cache isn't exercised by these tests (they use the "prices" op); a stub instance suffices.
    private static CardImageCacheService ImageCache() =>
        new(new StubDataPath(), new StubHttpClientFactory(), NullLogger<CardImageCacheService>.Instance);

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("Condition not met in time");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void TryStart_UnknownOperation_Fails()
    {
        var svc = new CatalogRefreshService([new GatedGameService(CardGame.Mtg)], ImageCache(), NullLogger<CatalogRefreshService>.Instance);
        Assert.False(svc.TryStart(CardGame.Mtg, "nonsense", out var error));
        Assert.Contains("operation", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryStart_UnavailableGame_Fails()
    {
        var svc = new CatalogRefreshService([new GatedGameService(CardGame.Mtg)], ImageCache(), NullLogger<CatalogRefreshService>.Instance);
        Assert.False(svc.TryStart(CardGame.Pokemon, "prices", out var error));
        Assert.Contains("not available", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryStart_WhileRunning_IsRejected_ThenCompletes()
    {
        var stub = new GatedGameService(CardGame.Mtg);
        var svc = new CatalogRefreshService([stub], ImageCache(), NullLogger<CatalogRefreshService>.Instance);

        Assert.True(svc.TryStart(CardGame.Mtg, "prices", out _));
        Assert.NotNull(svc.Status().Running);
        Assert.Equal("running", svc.Status().Running!.State);

        // Second start is rejected while the first is gated open.
        Assert.False(svc.TryStart(CardGame.Mtg, "prices", out var error));
        Assert.Contains("already running", error, StringComparison.OrdinalIgnoreCase);

        stub.Release();
        await WaitUntil(() => svc.Status().Running is null);

        var status = svc.Status();
        Assert.Null(status.Running);
        Assert.Single(status.Recent);
        Assert.Equal("succeeded", status.Recent[0].State);
        Assert.Equal("prices", status.Recent[0].Operation);
    }

    [Fact]
    public async Task Job_Failure_IsRecorded()
    {
        var stub = new GatedGameService(CardGame.Mtg) { Throw = true };
        var svc = new CatalogRefreshService([stub], ImageCache(), NullLogger<CatalogRefreshService>.Instance);

        Assert.True(svc.TryStart(CardGame.Mtg, "prices", out _));
        stub.Release();
        await WaitUntil(() => svc.Status().Running is null);

        var recent = svc.Status().Recent;
        Assert.Single(recent);
        Assert.Equal("failed", recent[0].State);
        Assert.Contains("boom", recent[0].Message);
    }

    /// <summary>Game service whose price refresh blocks until <see cref="Release"/> is called, so the
    /// job's running window is controllable from the test.</summary>
    private sealed class GatedGameService(CardGame game) : ICardGameService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Throw { get; init; }
        public void Release() => _gate.TrySetResult();

        public CardGame Game => game;
        public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new PriceUpdateProgress(game, null, 0, 0, "working"));
            await _gate.Task;
            if (Throw) throw new InvalidOperationException("boom");
        }

        // Unused members
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
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
        public List<SetCatalogCard> GetSetCards(string setCode) => [];
        public object? FindCardById(string gameCardId) => null;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubDataPath : IDataPathService
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
