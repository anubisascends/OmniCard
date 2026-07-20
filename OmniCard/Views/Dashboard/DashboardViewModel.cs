using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Dashboard;

/// <summary>
/// Backs the Dashboard tab: a read-only valuation + realized-P&amp;L overview sourced from
/// <see cref="IAnalyticsService"/>. Data is loaded lazily on first tab activation and can be
/// recomputed on demand via <see cref="RefreshCommand"/>.
/// </summary>
public sealed partial class DashboardViewModel : ViewModel
{
    private readonly IAnalyticsService _analyticsService;
    private bool _loaded;

    public DashboardViewModel(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [ObservableProperty]
    public partial HoldingsValuation? Holdings { get; set; }

    [ObservableProperty]
    public partial RealizedSummary? Realized { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

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

    [RelayCommand]
    public async Task Refresh()
    {
        if (IsBusy) return; // guard against overlapping runs (e.g. double-click)
        IsBusy = true;
        try
        {
            var (holdings, realized) = await Task.Run(() => (_analyticsService.GetHoldings(), _analyticsService.GetRealized()));

            // Task.Run's continuation resumes on the calling (UI) SynchronizationContext,
            // so these assignments — and the property-changed notifications they raise —
            // happen back on the UI thread.
            Holdings = holdings;
            Realized = realized;
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
