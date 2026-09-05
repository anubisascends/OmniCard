using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>
/// Runs the per-game catalog refresh operations (download bulk data / update prices / compute image
/// hashes) server-side as background jobs, so the web app no longer depends on the desktop to keep
/// the SQLite catalog caches current. One job runs at a time (these are heavy, and they write the
/// catalog DBs); progress is captured for the SPA to poll. State lives in memory only — a refresh is
/// re-triggerable, so losing status on restart is harmless.
/// </summary>
public sealed class CatalogRefreshService
{
    public static readonly IReadOnlySet<string> Operations =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "prices", "bulk", "hashes", "images" };

    private readonly Dictionary<CardGame, ICardGameService> _games;
    private readonly CardImageCacheService _imageCache;
    private readonly ILogger<CatalogRefreshService> _logger;
    private readonly object _lock = new();
    private JobState? _running;
    private readonly LinkedList<JobState> _recent = new(); // most-recent-first, capped

    public CatalogRefreshService(
        IEnumerable<ICardGameService> games,
        CardImageCacheService imageCache,
        ILogger<CatalogRefreshService> logger)
    {
        _games = games.ToDictionary(g => g.Game);
        _imageCache = imageCache;
        _logger = logger;
    }

    /// <summary>Immutable snapshot of a refresh job for the API.</summary>
    public sealed record JobSnapshot(
        string Game, string Operation, string State, string Message, string StartedAt, string? FinishedAt);

    public sealed record StatusSnapshot(JobSnapshot? Running, IReadOnlyList<JobSnapshot> Recent);

    public bool TryStart(CardGame game, string operation, out string? error)
    {
        if (!Operations.Contains(operation))
        {
            error = $"Unknown operation '{operation}' (expected prices, bulk, or hashes)";
            return false;
        }
        if (!_games.TryGetValue(game, out var service))
        {
            error = $"Game {game} is not available";
            return false;
        }

        lock (_lock)
        {
            if (_running is not null)
            {
                error = $"A catalog refresh is already running ({_running.Game} {_running.Operation})";
                return false;
            }
            _running = new JobState
            {
                Game = game,
                Operation = operation.ToLowerInvariant(),
                State = "running",
                Message = "Starting…",
                StartedAt = DateTime.UtcNow,
            };
        }

        // Fire-and-forget: the job owns its lifecycle and records completion into state.
        _ = Task.Run(() => RunAsync(service, _running!));
        error = null;
        return true;
    }

    public StatusSnapshot Status()
    {
        lock (_lock)
        {
            return new StatusSnapshot(_running?.ToSnapshot(), _recent.Select(j => j.ToSnapshot()).ToList());
        }
    }

    private async Task RunAsync(ICardGameService service, JobState job)
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
            _logger.LogInformation("Catalog refresh started: {Game} {Operation}", job.Game, job.Operation);
            switch (job.Operation)
            {
                case "prices":
                    await service.UpdatePricesAsync(new Progress<PriceUpdateProgress>(p => SetMessage(p.Message)));
                    break;
                case "bulk":
                    await service.DownloadBulkDataAsync(new Progress<string>(SetMessage));
                    break;
                case "hashes":
                    await service.ComputeImageHashesAsync(forceAll: false, progress: new Progress<string>(SetMessage));
                    break;
                case "images":
                    await DownloadImagesAsync(service, job.Game, SetMessage);
                    break;
            }
            Complete(job, "succeeded", "Completed");
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
    private async Task DownloadImagesAsync(ICardGameService service, CardGame game, Action<string> setMessage)
    {
        var sets = service.GetAvailableSets();
        int setNo = 0, downloaded = 0, total = 0;
        foreach (var set in sets)
        {
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
    }

    private sealed class JobState
    {
        public CardGame Game;
        public string Operation = "";
        public string State = "";
        public string Message = "";
        public DateTime StartedAt;
        public DateTime? FinishedAt;

        public JobSnapshot ToSnapshot() => new(
            Game.ToString(), Operation, State, Message,
            StartedAt.ToString("o"), FinishedAt?.ToString("o"));
    }
}
