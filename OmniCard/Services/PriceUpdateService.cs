using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using OmniCard.Helpers;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Services;

/// <summary>Orchestrates background price refreshes across all game services. Throttled per
/// game (unless forced), single-run guarded, and surfaces bindable progress for the status bar.</summary>
public sealed class PriceUpdateService : INotifyPropertyChanged
{
    private readonly IEnumerable<ICardGameService> _gameServices;
    private readonly IDataPathService _dataPath;
    private readonly ILogger<PriceUpdateService> _logger;
    private readonly object _gate = new();
    private Task? _current;

    public PriceUpdateService(
        IEnumerable<ICardGameService> gameServices,
        IDataPathService dataPath,
        ILogger<PriceUpdateService> logger)
    {
        _gameServices = gameServices;
        _dataPath = dataPath;
        _logger = logger;
    }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private int _completed;
    public int Completed { get => _completed; private set => Set(ref _completed, value); }

    private int _total;
    public int Total { get => _total; private set => Set(ref _total, value); }

    public event EventHandler? PricesUpdated;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task RunAsync(bool force, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_current is { IsCompleted: false })
                return _current;              // single-run guard
            _current = RunCoreAsync(force, ct);
            return _current;
        }
    }

    private async Task RunCoreAsync(bool force, CancellationToken ct)
    {
        IsRunning = true;
        var anyUpdated = false;
        try
        {
            foreach (var svc in _gameServices)
            {
                ct.ThrowIfCancellationRequested();

                if (!force && PriceRefreshCooldownHelper.IsCooldownActive(_dataPath.DataDirectory, svc.Game, out _))
                {
                    _logger.LogInformation("Price refresh skipped for {Game} (within 24h cooldown)", svc.Game);
                    continue;
                }

                try
                {
                    StatusText = $"Updating {svc.Game} prices...";
                    var progress = new Progress<PriceUpdateProgress>(OnProgress);
                    await svc.UpdatePricesAsync(progress, ct);
                    PriceRefreshCooldownHelper.RecordRefresh(_dataPath.DataDirectory, svc.Game);
                    anyUpdated = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Price refresh failed for {Game}", svc.Game);
                    StatusText = $"{svc.Game} price update failed";
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Price refresh cancelled");
        }
        finally
        {
            IsRunning = false;
            Completed = 0;
            Total = 0;
            if (anyUpdated)
            {
                StatusText = "Prices updated";
                RaisePricesUpdated();
            }
        }
    }

    private void OnProgress(PriceUpdateProgress p)
    {
        Completed = p.Completed;
        Total = p.Total;
        StatusText = p.Message;
    }

    private void RaisePricesUpdated() => RunOnUi(() => PricesUpdated?.Invoke(this, EventArgs.Empty));

    // Marshal to the UI thread when a WPF app is running; run inline under unit tests.
    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        RunOnUi(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
    }
}
