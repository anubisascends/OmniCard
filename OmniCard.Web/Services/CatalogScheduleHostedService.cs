using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services;

/// <summary>
/// Runs the automatic catalog updates (<see cref="CatalogScheduleSettings"/>, Settings ▸ Catalog data).
/// Every <see cref="CheckInterval"/> (and whenever the schedule is saved or an admin clicks Run now) it
/// checks for a due slot; a due run refreshes each selected game's card data and then its prices, one
/// job at a time through <see cref="CatalogRefreshService"/> — queued behind any job already running.
/// A slot missed while the app was down runs once on the next check (within a week). Under IIS this only
/// runs while the app does: the app pool needs AlwaysRunning + preload (see the README).
/// </summary>
public sealed class CatalogScheduleHostedService(
    ICatalogScheduleSettingsService settingsService,
    CatalogRefreshService refresh,
    TimeProvider clock,
    ILogger<CatalogScheduleHostedService> logger) : BackgroundService
{
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _wake = new(0, 1);
    private volatile bool _runRequested;
    private volatile bool _running;

    /// <summary>True while a scheduled (or Run now) update is in progress.</summary>
    public bool IsRunning => _running;

    /// <summary>Starts a run on the next check, regardless of the schedule. False when one is already running.</summary>
    public bool RequestRun()
    {
        if (_running) return false;
        _runRequested = true;
        Wake();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settingsService.Changed += Wake;
        await Task.Yield(); // let host startup finish before the first check

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled catalog update check failed");
            }

            try
            {
                await _wake.WaitAsync(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs the update when one is due or requested. Returns whether it ran.</summary>
    internal async Task<bool> CheckOnceAsync(CancellationToken ct)
    {
        var settings = settingsService.Get();
        var now = clock.GetUtcNow().UtcDateTime;
        var requested = _runRequested;
        var due = settings.DueSlot(now, settingsService.GetRunState().LastRunStartedUtc, clock.LocalTimeZone);
        if (!requested && due is null)
            return false;

        _runRequested = false;
        if (requested)
            logger.LogInformation("Catalog update requested by an administrator");
        else
            logger.LogInformation("Scheduled catalog update due (slot {Slot:u})", due);
        await RunAsync(settings, ct);
        return true;
    }

    private async Task RunAsync(CatalogScheduleSettings settings, CancellationToken ct)
    {
        _running = true;
        try
        {
            // Stamp the start first: a crash mid-run must not make the same slot due again forever.
            var state = new CatalogScheduleRunState { LastRunStartedUtc = clock.GetUtcNow().UtcDateTime };
            settingsService.SaveRunState(state);

            var operations = new List<string>();
            if (settings.UpdateCards) operations.Add("bulk");
            if (settings.UpdatePrices) operations.Add("prices");

            foreach (var game in settings.Games)
            {
                foreach (var operation in operations)
                {
                    CatalogRefreshService.JobSnapshot job;
                    try
                    {
                        job = await refresh.RunAndWaitAsync(game, operation, scheduled: true, ct);
                    }
                    catch (ArgumentException ex)
                    {
                        // The game isn't loaded on this server: nothing to update.
                        logger.LogDebug(ex, "Skipping scheduled {Operation} for {Game}", operation, game);
                        continue;
                    }

                    state.JobCount++;
                    if (job.State != "succeeded")
                    {
                        state.FailedCount++;
                        state.Failures.Add($"{game} {operation}: {job.Message}");
                    }
                }
            }

            state.LastRunFinishedUtc = clock.GetUtcNow().UtcDateTime;
            settingsService.SaveRunState(state);
            logger.LogInformation("Catalog update finished: {Jobs} jobs, {Failed} failed", state.JobCount, state.FailedCount);
        }
        finally
        {
            _running = false;
        }
    }

    public override void Dispose()
    {
        settingsService.Changed -= Wake;
        _wake.Dispose();
        base.Dispose();
    }

    private void Wake()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { } // already signalled
        catch (ObjectDisposedException) { }
    }
}
