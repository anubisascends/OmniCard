using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.MovementHistory;

/// <summary>Period filter for the movement history browser — mirrors the Dashboard's
/// RealizedPeriod convention (<see cref="Views.Dashboard.RealizedPeriod"/>).</summary>
public enum MovementPeriod
{
    AllTime,
    ThisMonth,
    ThisYear,
}

/// <summary>
/// Backs the Movement History dialog: a read-only, filterable browser over the raw inventory
/// ledger (<see cref="IAnalyticsService.GetMovements"/>). Loads once on open via <see
/// cref="Load"/>; filter changes and <see cref="RefreshCommand"/> re-query off the UI thread.
/// </summary>
public sealed partial class MovementHistoryViewModel(
    IAnalyticsService analyticsService,
    ILogger<MovementHistoryViewModel> logger) : ViewModel
{
    /// <summary>Type filter options for the combo box; "All" (index 0) means no Type restriction.</summary>
    public IReadOnlyList<string> TypeOptions { get; } = ["All", .. Enum.GetNames<MovementType>()];

    public ObservableCollection<MovementView> Movements { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string SelectedTypeOption { get; set; } = "All";

    partial void OnSelectedTypeOptionChanged(string value) => _ = RefreshCommand.ExecuteAsync(null);

    [ObservableProperty]
    public partial MovementPeriod Period { get; set; } = MovementPeriod.AllTime;

    partial void OnPeriodChanged(MovementPeriod value) => _ = RefreshCommand.ExecuteAsync(null);

    [ObservableProperty]
    public partial string ProductQuery { get; set; } = "";

    private MovementType? SelectedType =>
        Enum.TryParse<MovementType>(SelectedTypeOption, out var parsed) ? parsed : null;

    /// <summary>Derives the <c>Since</c> cutoff (UTC) for a given period. Null means all-time.</summary>
    private static DateTime? SinceFor(MovementPeriod period)
    {
        var now = DateTime.UtcNow;
        return period switch
        {
            MovementPeriod.ThisMonth => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            MovementPeriod.ThisYear => new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => null,
        };
    }

    [RelayCommand]
    public async Task Refresh()
    {
        if (IsBusy) return; // guard against overlapping runs
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var filter = new MovementFilter(SelectedType, SinceFor(Period), ProductQuery);
            var results = await Task.Run(() => analyticsService.GetMovements(filter));

            Movements.Clear();
            foreach (var m in results)
                Movements.Add(m);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Movement history refresh failed");
            StatusMessage = $"Failed to load movement history: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Loads the ledger once, when the dialog opens.</summary>
    public void Load() => _ = RefreshCommand.ExecuteAsync(null);
}
