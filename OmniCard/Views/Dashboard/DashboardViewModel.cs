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

/// <summary>A single bar-chart row on the Dashboard's Charts section: a breakdown key (raw enum
/// name or location name, as in <see cref="OmniCard.Models.ValuationLine"/>/<see
/// cref="OmniCard.Models.RealizedLine"/>) paired with the charted value.</summary>
public sealed record ChartRow(string Key, decimal Value);

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

    /// <summary>Set when <see cref="RealizedPeriod"/> changes while a refresh (full <see
    /// cref="Refresh"/> or a realized-only <see cref="RefreshRealized"/>) is already in flight.
    /// Consumed by <see cref="RunPendingRealizedRefreshIfAny"/> once that run's <c>finally</c>
    /// clears <see cref="IsBusy"/>, so the realized figures always converge on whichever period
    /// is selected once the dust settles. Only ever touched on the UI thread.</summary>
    private bool _realizedRefreshPending;

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
    public decimal RealizedFees => Realized?.TotalFees ?? 0m;
    public decimal RealizedShippingCost => Realized?.TotalShippingCost ?? 0m;
    public decimal RealizedShippingCharged => Realized?.TotalShippingCharged ?? 0m;

    /// <summary>Realized profit after fees and net shipping (cost paid to ship minus what was
    /// charged to the buyer) — the bottom-line take-home from realized sales.</summary>
    public decimal RealizedNet => RealizedProfit - RealizedFees - RealizedShippingCost + RealizedShippingCharged;

    /// <summary>True once holdings have loaded and there is nothing in inventory — drives the
    /// breakdown tables' empty-state message (as opposed to the pre-first-load null state).</summary>
    public bool HasNoHoldings => Holdings is { TotalUnits: 0 };

    // --- Chart rows (Charts section) ---------------------------------------------------------
    // Each bar chart is driven by a small (Key, Value) projection, sorted descending by the
    // charted value, plus a section-max used to scale bar widths via ShareBarWidthConverter.
    // Recomputed (not cached) so they always reflect the latest Holdings/Realized.

    /// <summary>Market value by game, sorted descending by market value.</summary>
    public IReadOnlyList<ChartRow> MarketByGameRows =>
        Holdings?.ByGame.Select(l => new ChartRow(l.Key, l.Market)).OrderByDescending(r => r.Value).ToList()
        ?? [];

    /// <summary>Largest value in <see cref="MarketByGameRows"/> (0 if empty); the chart's bar-scale denominator.</summary>
    public decimal MarketByGameMax => MarketByGameRows.Count > 0 ? MarketByGameRows[0].Value : 0m;

    /// <summary>Market value by category, sorted descending by market value.</summary>
    public IReadOnlyList<ChartRow> MarketByCategoryRows =>
        Holdings?.ByCategory.Select(l => new ChartRow(l.Key, l.Market)).OrderByDescending(r => r.Value).ToList()
        ?? [];

    /// <summary>Largest value in <see cref="MarketByCategoryRows"/> (0 if empty); the chart's bar-scale denominator.</summary>
    public decimal MarketByCategoryMax => MarketByCategoryRows.Count > 0 ? MarketByCategoryRows[0].Value : 0m;

    /// <summary>Realized profit (Proceeds-Cost) by game, sorted descending by profit — largest
    /// gains first, largest losses last. Losses (negative values) render a zero-width bar via
    /// <c>ShareBarWidthConverter</c> (which clamps non-positive amounts to 0) but keep their
    /// signed value label, so a loss is never misread as "no data".</summary>
    public IReadOnlyList<ChartRow> RealizedProfitByGameRows =>
        Realized?.ByGame.Select(l => new ChartRow(l.Key, l.Proceeds - l.Cost)).OrderByDescending(r => r.Value).ToList()
        ?? [];

    /// <summary>Largest (most positive) value in <see cref="RealizedProfitByGameRows"/>, clamped to
    /// ≥0 — if every game lost money this is 0, which zeroes every bar's width while the signed
    /// labels remain the only (correct) signal.</summary>
    public decimal RealizedProfitByGameMax => RealizedProfitByGameRows.Count > 0
        ? Math.Max(0m, RealizedProfitByGameRows[0].Value)
        : 0m;

    partial void OnHoldingsChanged(HoldingsValuation? value)
    {
        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(TotalMarket));
        OnPropertyChanged(nameof(UnrealizedGain));
        OnPropertyChanged(nameof(HasNoHoldings));
        OnPropertyChanged(nameof(MarketByGameRows));
        OnPropertyChanged(nameof(MarketByGameMax));
        OnPropertyChanged(nameof(MarketByCategoryRows));
        OnPropertyChanged(nameof(MarketByCategoryMax));
    }

    partial void OnRealizedChanged(RealizedSummary? value)
    {
        OnPropertyChanged(nameof(RealizedProceeds));
        OnPropertyChanged(nameof(RealizedCost));
        OnPropertyChanged(nameof(RealizedProfit));
        OnPropertyChanged(nameof(RealizedFees));
        OnPropertyChanged(nameof(RealizedShippingCost));
        OnPropertyChanged(nameof(RealizedShippingCharged));
        OnPropertyChanged(nameof(RealizedNet));
        OnPropertyChanged(nameof(RealizedProfitByGameRows));
        OnPropertyChanged(nameof(RealizedProfitByGameMax));
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
            RunPendingRealizedRefreshIfAny();
        }
    }

    /// <summary>Recomputes only <see cref="Realized"/> (and its derived tile properties) for the
    /// currently selected <see cref="RealizedPeriod"/>, off the UI thread. Holdings are untouched,
    /// so this is much cheaper than a full <see cref="Refresh"/> and safe to run on every period
    /// change. If a refresh (full or realized-only) is already in flight, this does NOT block
    /// waiting for it — it records the request via <see cref="_realizedRefreshPending"/> and
    /// returns immediately; the in-flight run's <c>finally</c> will re-dispatch a realized-only
    /// recompute for whatever period is current by then, so <see cref="Realized"/> always
    /// converges on the currently selected <see cref="RealizedPeriod"/> instead of silently
    /// keeping stale data from before the period changed.</summary>
    private async Task RefreshRealized()
    {
        if (IsBusy)
        {
            _realizedRefreshPending = true;
            return;
        }
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
            RunPendingRealizedRefreshIfAny();
        }
    }

    /// <summary>If <see cref="RealizedPeriod"/> changed while a refresh was in flight (recorded in
    /// <see cref="_realizedRefreshPending"/>), kicks off one more realized-only recompute now that
    /// <see cref="IsBusy"/> has just cleared. Called from the <c>finally</c> of both <see
    /// cref="Refresh"/> and <see cref="RefreshRealized"/> so the realized figures never get stuck
    /// showing a stale period. The flag is cleared before re-dispatching, so a stable period
    /// converges after at most one extra run and this can't loop forever.</summary>
    private void RunPendingRealizedRefreshIfAny()
    {
        if (!_realizedRefreshPending) return;
        _realizedRefreshPending = false;
        _ = RefreshRealized();
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
