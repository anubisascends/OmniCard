using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sets;
using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services;

/// <summary>
/// Runs the per-game catalog refresh operations (download bulk data / update prices / compute image
/// hashes) server-side as background jobs, so the web app no longer depends on the desktop to keep
/// the SQLite catalog caches current. One job runs at a time (these are heavy, and they write the
/// catalog DBs): a manual start is refused while another job runs, while a scheduled one
/// (<see cref="RunAndWaitAsync"/>, used by <see cref="CatalogScheduleHostedService"/>) waits its turn.
/// Progress is captured for the SPA to poll. State lives in memory only — a refresh is re-triggerable,
/// so losing status on restart is harmless.
/// </summary>
public sealed class CatalogRefreshService
{
    public static readonly IReadOnlySet<string> Operations =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "prices", "bulk", "hashes", "images" };

    private readonly Dictionary<CardGame, ICardGameService> _games;
    private readonly CardImageCacheService _imageCache;
    private readonly ICatalogLanguageSettingsService? _languages;
    private readonly ILogger<CatalogRefreshService> _logger;
    private readonly object _lock = new();
    // Held by the running job (released when it completes).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JobState? _running;
    private readonly LinkedList<JobState> _recent = new(); // most-recent-first, capped

    public CatalogRefreshService(
        IEnumerable<ICardGameService> games,
        CardImageCacheService imageCache,
        ILogger<CatalogRefreshService> logger,
        ICatalogLanguageSettingsService? languages = null)
    {
        _games = games.ToDictionary(g => g.Game);
        _imageCache = imageCache;
        _logger = logger;
        _languages = languages;
    }

    /// <summary>Immutable snapshot of a refresh job for the API.</summary>
    public sealed record JobSnapshot(
        string Game, string Operation, string State, string Message, string StartedAt, string? FinishedAt,
        bool Scheduled = false);

    public sealed record StatusSnapshot(JobSnapshot? Running, IReadOnlyList<JobSnapshot> Recent);

    public bool TryStart(CardGame game, string operation, out string? error)
    {
        if (!TryResolve(game, operation, out var service, out error))
            return false;

        if (!_gate.Wait(0))
        {
            lock (_lock)
                error = _running is { } r
                    ? $"A catalog refresh is already running ({r.Game} {r.Operation})"
                    : "A catalog refresh is already running";
            return false;
        }

        var job = Begin(game, operation, scheduled: false);
        // Fire-and-forget: the job owns its lifecycle and records completion into state.
        _ = Task.Run(() => RunAsync(service, job, CancellationToken.None));
        error = null;
        return true;
    }

    /// <summary>Runs one job to completion, first waiting for any running job to finish. Returns the
    /// finished job (state succeeded/failed); throws <see cref="ArgumentException"/> for an unknown
    /// operation or unavailable game.</summary>
    public async Task<JobSnapshot> RunAndWaitAsync(CardGame game, string operation, bool scheduled, CancellationToken ct)
    {
        if (!TryResolve(game, operation, out var service, out var error))
            throw new ArgumentException(error);

        await _gate.WaitAsync(ct);
        var job = Begin(game, operation, scheduled);
        await RunAsync(service, job, ct);
        lock (_lock)
            return job.ToSnapshot();
    }

    private bool TryResolve(CardGame game, string operation, out ICardGameService service, out string? error)
    {
        service = null!;
        if (!Operations.Contains(operation))
        {
            error = $"Unknown operation '{operation}' (expected prices, bulk, hashes, or images)";
            return false;
        }
        if (!_games.TryGetValue(game, out service!))
        {
            error = $"Game {game} is not available";
            return false;
        }
        error = null;
        return true;
    }

    // Caller holds the gate.
    private JobState Begin(CardGame game, string operation, bool scheduled)
    {
        var job = new JobState
        {
            Game = game,
            Operation = operation.ToLowerInvariant(),
            State = "running",
            Message = "Starting…",
            StartedAt = DateTime.UtcNow,
            Scheduled = scheduled,
        };
        lock (_lock)
            _running = job;
        return job;
    }

    public StatusSnapshot Status()
    {
        lock (_lock)
        {
            return new StatusSnapshot(_running?.ToSnapshot(), _recent.Select(j => j.ToSnapshot()).ToList());
        }
    }

    private async Task RunAsync(ICardGameService service, JobState job, CancellationToken ct)
    {
        void SetMessage(string message)
        {
            // Progress<T> reports asynchronously, so a late callback can arrive after the job has
            // completed — don't let it clobber the terminal "succeeded"/"failed" message.
            lock (_lock)
            {
                if (job.State == "running")
                    job.Message = message;
            }
        }

        try
        {
            // Apply the saved language selection so a bulk download fetches (and prunes) exactly it.
            if (_languages is not null && service is ICatalogLanguageAware languageAware)
                languageAware.CatalogLanguages = _languages.GetLanguages(job.Game);

            _logger.LogInformation("Catalog refresh started: {Game} {Operation}", job.Game, job.Operation);
            switch (job.Operation)
            {
                case "prices":
                    await service.UpdatePricesAsync(new Progress<PriceUpdateProgress>(p => SetMessage(p.Message)), ct);
                    break;
                case "bulk":
                    await service.DownloadBulkDataAsync(new Progress<string>(SetMessage), ct);
                    break;
                case "hashes":
                    await service.ComputeImageHashesAsync(forceAll: false, progress: new Progress<string>(SetMessage), ct: ct);
                    break;
                case "images":
                    await DownloadImagesAsync(service, job.Game, SetMessage, ct);
                    break;
            }
            // Keep the job's last progress line (e.g. "prices updated (N cards)") as its result.
            string message;
            lock (_lock)
                message = job.Message is "Starting…" or "" ? "Completed" : job.Message;
            Complete(job, "succeeded", message);
            _logger.LogInformation("Catalog refresh finished: {Game} {Operation}", job.Game, job.Operation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Catalog refresh failed: {Game} {Operation}", job.Game, job.Operation);
            Complete(job, "failed", ex.Message);
        }
    }

    /// <summary>Downloads every printing's artwork for a game into the server image cache
    /// (skip-if-present), walking the catalog set-by-set so progress is meaningful.</summary>
    private async Task DownloadImagesAsync(ICardGameService service, CardGame game, Action<string> setMessage, CancellationToken ct)
    {
        var sets = service.GetAvailableSets();
        int setNo = 0, downloaded = 0, total = 0;
        foreach (var set in sets)
        {
            ct.ThrowIfCancellationRequested();
            setNo++;
            setMessage($"Set {setNo}/{sets.Count} ({set.SetCode}) — {downloaded} images cached");
            List<SetCatalogCard> cards;
            try { cards = service.GetSetCards(set.SetCode); }
            catch { continue; }

            foreach (var card in cards)
            {
                total++;
                if (await _imageCache.EnsureCachedAsync(game, card.GameCardId, card.ImageUri))
                    downloaded++;
            }
        }
        setMessage($"Cached {downloaded}/{total} images across {sets.Count} sets");
    }

    private void Complete(JobState job, string state, string message)
    {
        lock (_lock)
        {
            job.State = state;
            job.Message = message;
            job.FinishedAt = DateTime.UtcNow;
            _recent.AddFirst(job);
            while (_recent.Count > 10)
                _recent.RemoveLast();
            if (ReferenceEquals(_running, job))
                _running = null;
        }
        _gate.Release();
    }

    private sealed class JobState
    {
        public CardGame Game;
        public string Operation = "";
        public string State = "";
        public string Message = "";
        public DateTime StartedAt;
        public DateTime? FinishedAt;
        public bool Scheduled;

        public JobSnapshot ToSnapshot() => new(
            Game.ToString(), Operation, State, Message,
            StartedAt.ToString("o"), FinishedAt?.ToString("o"), Scheduled);
    }
}
