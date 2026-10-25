using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection.Settings;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Sets;
using OmniCard.Shared.Settings;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>The automatic catalog update schedule: slot arithmetic (days, times, DST, catch-up), the
/// file-backed settings, and the background runner driving <see cref="CatalogRefreshService"/>.</summary>
public class CatalogScheduleTests : IDisposable
{
    // Windows zone ids (the app is Windows-only). US Eastern: DST starts 2026-03-08, ends 2026-11-01.
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "catalog-schedule-" + Guid.NewGuid().ToString("N"));

    public CatalogScheduleTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static DateTime EasternToUtc(int y, int mo, int d, int h, int mi) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Unspecified), Eastern);

    // === Slot arithmetic ===

    [Fact]
    public void SlotsBetween_HonorsDaysAndTimes_InLocalTime()
    {
        var s = new CatalogScheduleSettings
        {
            Enabled = true,
            Times = ["06:00", "18:30"],
            Days = [DayOfWeek.Monday, DayOfWeek.Wednesday],
        };
        // Sun 2026-10-18 00:00 → Thu 2026-10-22 00:00 (Eastern).
        var slots = s.SlotsBetween(EasternToUtc(2026, 10, 18, 0, 0), EasternToUtc(2026, 10, 22, 0, 0), Eastern).ToList();

        Assert.Equal(
            [
                EasternToUtc(2026, 10, 19, 6, 0), EasternToUtc(2026, 10, 19, 18, 30),
                EasternToUtc(2026, 10, 21, 6, 0), EasternToUtc(2026, 10, 21, 18, 30),
            ],
            slots);
    }

    [Fact]
    public void SlotsBetween_TimeSkippedBySpringForward_RunsAnHourLater()
    {
        var s = new CatalogScheduleSettings { Enabled = true, Times = ["02:30"] };
        // 2026-03-08 02:30 doesn't exist in US Eastern; 03:30 EDT = 07:30 UTC.
        var slot = s.SlotsBetween(new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 8, 12, 0, 0, DateTimeKind.Utc), Eastern).Single();
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), slot);
    }

    [Fact]
    public void DueSlot_LatestSlotSinceLastRun_ButNotBeforeSaving()
    {
        var s = new CatalogScheduleSettings
        {
            Enabled = true,
            Times = ["06:00"],
            SavedAtUtc = EasternToUtc(2026, 10, 19, 7, 0), // saved after Monday's 06:00
        };

        // Monday's slot predates saving: not due.
        Assert.Null(s.DueSlot(EasternToUtc(2026, 10, 19, 12, 0), lastRunUtc: null, Eastern));

        // Tuesday 06:00 is due; once it has run, it isn't again.
        var tuesday = EasternToUtc(2026, 10, 20, 6, 0);
        Assert.Equal(tuesday, s.DueSlot(EasternToUtc(2026, 10, 20, 6, 0).AddSeconds(30), null, Eastern));
        Assert.Null(s.DueSlot(EasternToUtc(2026, 10, 20, 9, 0), lastRunUtc: tuesday.AddSeconds(5), Eastern));
    }

    [Fact]
    public void DueSlot_AfterDowntime_CatchesUpWithOneRun_AndOffMeansNever()
    {
        var s = new CatalogScheduleSettings { Enabled = true, Times = ["06:00"], SavedAtUtc = EasternToUtc(2026, 10, 1, 0, 0) };
        var lastRun = EasternToUtc(2026, 10, 15, 6, 0);
        var now = EasternToUtc(2026, 10, 19, 12, 0); // four days of missed slots

        Assert.Equal(EasternToUtc(2026, 10, 19, 6, 0), s.DueSlot(now, lastRun, Eastern));

        s.Enabled = false;
        Assert.Null(s.DueSlot(now, lastRun, Eastern));
    }

    [Fact]
    public void NextSlotAfter_SkipsUnselectedDays()
    {
        var s = new CatalogScheduleSettings { Enabled = true, Times = ["06:00"], Days = [DayOfWeek.Saturday] };
        Assert.Equal(EasternToUtc(2026, 10, 24, 6, 0), s.NextSlotAfter(EasternToUtc(2026, 10, 19, 12, 0), Eastern));
        s.Times = [];
        Assert.Null(s.NextSlotAfter(EasternToUtc(2026, 10, 19, 12, 0), Eastern));
    }

    // === Settings service ===

    private CatalogScheduleSettingsService SettingsService(TimeProvider? clock = null)
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dir);
        return new CatalogScheduleSettingsService(dataPath.Object, clock);
    }

    [Fact]
    public void Settings_DefaultOff_SaveNormalizesStampsAndPersists()
    {
        var clock = new TestClock(new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc), Eastern);
        var svc = SettingsService(clock);
        Assert.False(svc.Get().Enabled);

        var changed = 0;
        svc.Changed += () => changed++;
        var errors = svc.Save(new CatalogScheduleSettings
        {
            Enabled = true,
            Times = ["18:30", "6:00", "06:00", " "],
            Days = [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Friday],
            Games = [CardGame.Riftbound, CardGame.Mtg],
            UpdateCards = false,
        });

        Assert.Empty(errors);
        Assert.Equal(1, changed);
        var saved = SettingsService().Get();
        Assert.True(saved.Enabled);
        Assert.Equal(["06:00", "18:30"], saved.Times);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], saved.Days);
        Assert.Equal([CardGame.Mtg, CardGame.Riftbound], saved.Games);
        Assert.False(saved.UpdateCards);
        Assert.Equal(clock.UtcNow, saved.SavedAtUtc);
    }

    [Fact]
    public void Settings_InvalidSchedule_IsRejected_AndNothingSaved()
    {
        var svc = SettingsService();
        var errors = svc.Save(new CatalogScheduleSettings
        {
            Enabled = true,
            Times = ["25:00"],
            Days = [],
            Games = [],
            UpdateCards = false,
            UpdatePrices = false,
        });

        Assert.Equal(4, errors.Count); // bad time, no day, no game, nothing to update
        Assert.False(svc.Get().Enabled);
        Assert.False(File.Exists(Path.Combine(_dir, "catalog-schedule.json")));
    }

    [Fact]
    public void Settings_RunState_RoundTrips_AndCorruptFilesFallBackToDefaults()
    {
        var svc = SettingsService();
        svc.SaveRunState(new CatalogScheduleRunState { JobCount = 4, FailedCount = 1, Failures = ["Mtg bulk: boom"] });
        Assert.Equal(["Mtg bulk: boom"], SettingsService().GetRunState().Failures);

        File.WriteAllText(Path.Combine(_dir, "catalog-schedule.json"), "{ nope");
        File.WriteAllText(Path.Combine(_dir, "catalog-schedule-state.json"), "{ nope");
        Assert.False(svc.Get().Enabled);
        Assert.Equal(0, svc.GetRunState().JobCount);
    }

    // === Background runner ===

    private (CatalogScheduleHostedService Scheduler, CatalogScheduleSettingsService Settings, TestClock Clock, List<string> Log)
        Scheduler(params RecordingGameService[] games)
    {
        var log = new List<string>();
        foreach (var g in games) g.Log = log;
        var clock = new TestClock(EasternToUtc(2026, 10, 19, 5, 0), Eastern);
        var settings = SettingsService(clock);
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dir);
        var imageCache = new CardImageCacheService(dataPath.Object, new StubHttpClientFactory(),
            NullLogger<CardImageCacheService>.Instance);
        var refresh = new CatalogRefreshService(games, imageCache, NullLogger<CatalogRefreshService>.Instance);
        var scheduler = new CatalogScheduleHostedService(settings, refresh, clock, NullLogger<CatalogScheduleHostedService>.Instance);
        return (scheduler, settings, clock, log);
    }

    [Fact]
    public async Task Runner_AtDueSlot_UpdatesEachGameCardsThenPrices_AndRecordsTheRun()
    {
        var (scheduler, settings, clock, log) = Scheduler(
            new RecordingGameService(CardGame.Mtg), new RecordingGameService(CardGame.Pokemon) { FailPrices = true });
        Assert.Empty(settings.Save(new CatalogScheduleSettings
        {
            Enabled = true,
            Times = ["06:00"],
            // OnePiece isn't loaded on this server: skipped, not failed.
            Games = [CardGame.Mtg, CardGame.OnePiece, CardGame.Pokemon],
        }));

        Assert.False(await scheduler.CheckOnceAsync(CancellationToken.None)); // 05:00: nothing due
        Assert.Empty(log);

        clock.UtcNow = EasternToUtc(2026, 10, 19, 6, 0).AddSeconds(10);
        Assert.True(await scheduler.CheckOnceAsync(CancellationToken.None));
        Assert.Equal(["Mtg bulk", "Mtg prices", "Pokemon bulk", "Pokemon prices"], log);

        var state = settings.GetRunState();
        Assert.Equal(clock.UtcNow, state.LastRunStartedUtc);
        Assert.Equal(4, state.JobCount);
        Assert.Equal(1, state.FailedCount);
        Assert.Equal(["Pokemon prices: price feed down"], state.Failures);

        // Same slot doesn't run twice.
        clock.UtcNow = clock.UtcNow.AddMinutes(30);
        Assert.False(await scheduler.CheckOnceAsync(CancellationToken.None));
        Assert.Equal(4, log.Count);
    }

    [Fact]
    public async Task Runner_RunNow_RunsEvenWhenScheduleIsOff_OnlyTheChosenOperations()
    {
        var (scheduler, settings, _, log) = Scheduler(new RecordingGameService(CardGame.Riftbound));
        Assert.Empty(settings.Save(new CatalogScheduleSettings
        {
            Enabled = false,
            Games = [CardGame.Riftbound],
            UpdateCards = false,
        }));

        Assert.False(await scheduler.CheckOnceAsync(CancellationToken.None));
        Assert.True(scheduler.RequestRun());
        Assert.True(await scheduler.CheckOnceAsync(CancellationToken.None));
        Assert.Equal(["Riftbound prices"], log);
        Assert.False(scheduler.IsRunning);
    }

    private sealed class TestClock(DateTime utcNow, TimeZoneInfo zone) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>Records "Game operation" for each catalog job it runs.</summary>
    private sealed class RecordingGameService(CardGame game) : ICardGameService
    {
        public List<string> Log { get; set; } = [];
        public bool FailPrices { get; init; }

        public CardGame Game => game;

        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            lock (Log) Log.Add($"{game} bulk");
            return Task.CompletedTask;
        }

        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
        {
            lock (Log) Log.Add($"{game} prices");
            return FailPrices ? Task.FromException(new InvalidOperationException("price feed down")) : Task.CompletedTask;
        }

        // Unused members
        public MatchDiagnostics? LastMatchDiagnostics => null;
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
}
