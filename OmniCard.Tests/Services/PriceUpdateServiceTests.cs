using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;
using Xunit;

namespace OmniCard.Tests.Services;

public class PriceUpdateServiceTests : IDisposable
{
    private readonly string _dir;

    public PriceUpdateServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"priceupd-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class FakePathService : IDataPathService
    {
        public FakePathService(string dir) => DataDirectory = dir;
        public string DataDirectory { get; }
        public string ScansDirectory => throw new NotImplementedException();
        public string TempScansDirectory => throw new NotImplementedException();
        public string SymbolsCacheDirectory => throw new NotImplementedException();
        public string LogsDirectory => throw new NotImplementedException();
        public string? PendingDataDirectory => throw new NotImplementedException();
        public bool IsMigrationPending => throw new NotImplementedException();
        public void SetPendingDataDirectory(string path) => throw new NotImplementedException();
        public void CommitMigration() => throw new NotImplementedException();
        public void CancelPendingMigration() => throw new NotImplementedException();
    }

    private sealed class FakeGameService : ICardGameService
    {
        public FakeGameService(CardGame game) => Game = game;
        public CardGame Game { get; }
        public int UpdateCalls { get; private set; }
        public bool ShouldThrow { get; set; }
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
        {
            UpdateCalls++;
            if (ShouldThrow) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }

        public MatchDiagnostics? LastMatchDiagnostics => throw new NotImplementedException();
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => throw new NotImplementedException();
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => throw new NotImplementedException();
        public List<CardMatch> GetPrintings(string cardName) => throw new NotImplementedException();
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => throw new NotImplementedException();
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => throw new NotImplementedException();
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) => throw new NotImplementedException();
        public IReadOnlyList<SetInfo> GetAvailableSets() => throw new NotImplementedException();
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => throw new NotImplementedException();
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => throw new NotImplementedException();
        public object? FindCardById(string gameCardId) => throw new NotImplementedException();
    }

    private PriceUpdateService Create(params FakeGameService[] games) =>
        new(games, new FakePathService(_dir), NullLogger<PriceUpdateService>.Instance);

    [Fact]
    public async Task RunAsync_Force_InvokesEveryGame_AndRaisesPricesUpdated()
    {
        var mtg = new FakeGameService(CardGame.Mtg);
        var op = new FakeGameService(CardGame.OnePiece);
        var svc = Create(mtg, op);
        var raised = 0;
        svc.PricesUpdated += (_, _) => raised++;

        await svc.RunAsync(force: true);

        Assert.Equal(1, mtg.UpdateCalls);
        Assert.Equal(1, op.UpdateCalls);
        Assert.Equal(1, raised);
        Assert.False(svc.IsRunning);
    }

    [Fact]
    public async Task RunAsync_RespectsCooldown_WhenNotForced()
    {
        var mtg = new FakeGameService(CardGame.Mtg);
        var svc = Create(mtg);
        await svc.RunAsync(force: true);   // records timestamp
        await svc.RunAsync(force: false);  // within cooldown -> skipped

        Assert.Equal(1, mtg.UpdateCalls);
    }

    [Fact]
    public async Task RunAsync_ForceBypassesCooldown()
    {
        var mtg = new FakeGameService(CardGame.Mtg);
        var svc = Create(mtg);
        await svc.RunAsync(force: true);
        await svc.RunAsync(force: true);

        Assert.Equal(2, mtg.UpdateCalls);
    }

    [Fact]
    public async Task RunAsync_IsolatesPerGameFailure()
    {
        var mtg = new FakeGameService(CardGame.Mtg) { ShouldThrow = true };
        var op = new FakeGameService(CardGame.OnePiece);
        var svc = Create(mtg, op);

        await svc.RunAsync(force: true);   // must not throw

        Assert.Equal(1, op.UpdateCalls);   // second game still ran
    }

    [Fact]
    public async Task RunAsync_FailedGame_NotMarkedCooldown_SoRetryRuns()
    {
        var mtg = new FakeGameService(CardGame.Mtg) { ShouldThrow = true };
        var svc = Create(mtg);
        await svc.RunAsync(force: false);  // fails, no timestamp recorded
        await svc.RunAsync(force: false);  // not in cooldown -> runs again

        Assert.Equal(2, mtg.UpdateCalls);
    }
}
