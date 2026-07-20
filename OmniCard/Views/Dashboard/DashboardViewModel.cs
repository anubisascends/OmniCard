using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Dashboard;

/// <summary>Period over which realized P&amp;L is computed on the dashboard.</summary>
public enum RealizedPeriod
{
    AllTime,
    ThisYear,
    ThisMonth,
}

/// <summary>
/// Backs the Dashboard tab: a read-only valuation + realized-P&amp;L overview sourced from
/// <see cref="IAnalyticsService"/>. Data is loaded lazily on first tab activation and can be
/// recomputed on demand via <see cref="RefreshCommand"/>.
/// </summary>
public sealed partial class DashboardViewModel : ViewModel
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ILogger<DashboardViewModel> _logger;
    private bool _loaded;

    public DashboardViewModel(IAnalyticsService analyticsService, ILogger<DashboardViewModel> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    [ObservableProperty]
    public partial HoldingsValuation? Holdings { get; set; }

    [ObservableProperty]
    public partial RealizedSummary? Realized { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Selected realized-P&amp;L period filter. Changing this recomputes only the
    /// realized side (holdings are unaffected) off the UI thread.</summary>
    [ObservableProperty]
    public partial RealizedPeriod RealizedPeriod { get; set; } = RealizedPeriod.AllTime;

    /// <summary>Set when the most recent refresh failed (e.g. a price-fetch exception), so the
    /// view can show why the tiles are empty instead of looking like "no data". Cleared at the
    /// start of the next successful refresh.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    // Derived tile properties — recomputed whenever Holdings/Realized are reassigned.
    public decimal TotalCost => Holdings?.TotalCost ?? 0m;
    public decimal TotalMarket => Holdings?.TotalMarket ?? 0m;
    public decimal UnrealizedGain => TotalMarket - TotalCost;
    public decimal RealizedProceeds => Realized?.TotalProceeds ?? 0m;
    public decimal RealizedCost => Realized?.TotalCost ?? 0m;
    public decimal RealizedProfit => RealizedProceeds - RealizedCost;

    /// <summary>True once holdings have loaded and there is nothing in inventory — drives the
    /// breakdown tables' empty-state message (as opposed to the pre-first-load null state).</summary>
    public bool HasNoHoldings => Holdings is { TotalUnits: 0 };

    partial void OnHoldingsChanged(HoldingsValuation? value)
    {
        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(TotalMarket));
        OnPropertyChanged(nameof(UnrealizedGain));
        OnPropertyChanged(nameof(HasNoHoldings));
    }

    partial void OnRealizedChanged(RealizedSummary? value)
    {
        OnPropertyChanged(nameof(RealizedProceeds));
        OnPropertyChanged(nameof(RealizedCost));
        OnPropertyChanged(nameof(RealizedProfit));
    }

    /// <summary>Recomputes only the realized side, off the UI thread, whenever the period
    /// selection changes — holdings are left untouched.</summary>
    partial void OnRealizedPeriodChanged(RealizedPeriod value) => _ = RefreshRealized();

    /// <summary>Derives the <c>since</c> cutoff (UTC) for a given period. Null means all-time.
    /// Kept as a thin, UtcNow-based computation on the VM so the service itself stays a pure
    /// function of the timestamp it's given.</summary>
    private static DateTime? SinceFor(RealizedPeriod period)
    {
        var now = DateTime.UtcNow;
        return period switch
        {
            RealizedPeriod.ThisMonth => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            RealizedPeriod.ThisYear => new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => null,
        };
    }

    [RelayCommand]
    public async Task Refresh()
    {
        if (IsBusy) return; // guard against overlapping runs (e.g. double-click)
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var since = SinceFor(RealizedPeriod);
            var (holdings, realized) = await Task.Run(() => (_analyticsService.GetHoldings(), _analyticsService.GetRealized(since)));

            // Task.Run's continuation resumes on the calling (UI) SynchronizationContext,
            // so these assignments — and the property-changed notifications they raise —
            // happen back on the UI thread.
            Holdings = holdings;
            Realized = realized;
        }
        catch (Exception ex)
        {
            // Leave any previously-loaded Holdings/Realized as-is rather than blanking them —
            // surface the failure via StatusMessage so the view doesn't just look like "no data".
            _logger.LogError(ex, "Dashboard refresh failed");
            StatusMessage = $"Failed to refresh: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Recomputes only <see cref="Realized"/> (and its derived tile properties) for the
    /// currently selected <see cref="RealizedPeriod"/>, off the UI thread. Holdings are untouched,
    /// so this is much cheaper than a full <see cref="Refresh"/> and safe to run on every period
    /// change.</summary>
    private async Task RefreshRealized()
    {
        if (IsBusy) return; // a full refresh is in flight; it will pick up the new period itself
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var since = SinceFor(RealizedPeriod);
            var realized = await Task.Run(() => _analyticsService.GetRealized(since));
            Realized = realized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard realized-period refresh failed");
            StatusMessage = $"Failed to refresh: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Loads the dashboard once, on first activation. Subsequent activations are no-ops;
    /// use <see cref="RefreshCommand"/> to force a recompute.</summary>
    public void Load()
    {
        if (_loaded) return;
        _loaded = true;
        _ = RefreshCommand.ExecuteAsync(null);
    }
}
