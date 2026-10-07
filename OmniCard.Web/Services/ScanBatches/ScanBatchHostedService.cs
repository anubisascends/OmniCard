using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services.ScanBatches;

/// <summary>
/// The background loop behind watched scan folders. Each pass picks up new files
/// (<see cref="ScanFolderIngestor"/>), matches pending batch items (<see cref="ScanBatchProcessor"/>)
/// and, hourly, purges expired batches. A <see cref="FileSystemWatcher"/> per folder only wakes the
/// loop early; a full rescan every minute (and at startup) is what guarantees nothing is missed, since
/// watcher events can be dropped.
///
/// Under IIS this only runs while the app is running: the app pool needs AlwaysRunning + preload, or
/// nothing is watched until someone opens the site (see the README).
/// </summary>
public sealed class ScanBatchHostedService(
    IScanFolderSettingsService settingsService,
    ScanFolderIngestor ingestor,
    ScanBatchProcessor processor,
    ScanBatchService batches,
    TimeProvider clock,
    ILogger<ScanBatchHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);
    // Cap one matching burst so new files keep getting picked up during a long batch.
    private static readonly TimeSpan MatchBurst = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _watcherLock = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private int _fileEvent;
    private volatile bool _reconfigure = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settingsService.Changed += OnSettingsChanged;
        await Task.Yield(); // let host startup finish before the first scan

        var nextScan = DateTimeOffset.MinValue;
        var nextPurge = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var moreWork = false;
            try
            {
                var settings = settingsService.Get();
                if (_reconfigure)
                {
                    _reconfigure = false;
                    ConfigureWatchers(settings);
                    nextScan = DateTimeOffset.MinValue;
                }

                var now = clock.GetUtcNow();
                var fileEvent = Interlocked.Exchange(ref _fileEvent, 0) == 1;
                if (settings.Enabled && (fileEvent || now >= nextScan || ingestor.HasUnsettledFiles))
                {
                    ingestor.RunOnce(settings);
                    if (now >= nextScan)
                        nextScan = now + RescanInterval;
                }

                var quiet = TimeSpan.FromSeconds(settings.QuietPeriodSeconds);
                var burstEnd = clock.GetUtcNow() + MatchBurst;
                while (await processor.ProcessNextAsync(quiet, stoppingToken))
                {
                    if (clock.GetUtcNow() >= burstEnd)
                    {
                        moreWork = true;
                        break;
                    }
                }

                if (now >= nextPurge)
                {
                    batches.PurgeExpired(settings.RetentionDays);
                    nextPurge = now + PurgeInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scan batch background pass failed");
            }

            if (moreWork)
                continue;
            try
            {
                await _wake.WaitAsync(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override void Dispose()
    {
        settingsService.Changed -= OnSettingsChanged;
        DisposeWatchers();
        _wake.Dispose();
        base.Dispose();
    }

    private void OnSettingsChanged()
    {
        _reconfigure = true;
        Wake();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Our own moves into _processed fire events too; they need no rescan.
        if (e.FullPath.Contains(Path.DirectorySeparatorChar + ScanFolderIngestor.ProcessedFolderName, StringComparison.OrdinalIgnoreCase))
            return;
        Interlocked.Exchange(ref _fileEvent, 1);
        Wake();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Usually a buffer overflow — events were lost, so force a full rescan.
        logger.LogDebug(e.GetException(), "Scan folder watcher error; rescanning");
        Interlocked.Exchange(ref _fileEvent, 1);
        Wake();
    }

    private void Wake()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { } // already signalled
        catch (ObjectDisposedException) { }
    }

    private void ConfigureWatchers(ScanFolderSettings settings)
    {
        DisposeWatchers();
        if (!settings.Enabled)
            return;
        lock (_watcherLock)
        {
            foreach (var folder in settings.Folders.Where(f => f.Enabled && Directory.Exists(f.Path)))
            {
                try
                {
                    var watcher = new FileSystemWatcher(folder.Path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                        InternalBufferSize = 64 * 1024,
                    };
                    watcher.Created += OnFileEvent;
                    watcher.Changed += OnFileEvent;
                    watcher.Renamed += OnFileEvent;
                    watcher.Error += OnWatcherError;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    // The minute rescan still covers this folder.
                    logger.LogWarning(ex, "Couldn't watch the {Game} scan folder {Path}", folder.Game, folder.Path);
                }
            }
        }
        logger.LogInformation("Watching {Count} scan folder(s)", _watchers.Count);
    }

    private void DisposeWatchers()
    {
        lock (_watcherLock)
        {
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
        }
    }
}
