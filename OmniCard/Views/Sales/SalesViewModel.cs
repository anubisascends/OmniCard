using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// Backs the Sales tab: a location-grouped pick list sourced from <see cref="IListingService"/>,
/// plus a read-only view of the For-Sale storage location (configured and persisted via
/// <see cref="ISalesSettingsService"/> from Settings ▸ Sales &amp; Receipts, see
/// <c>SalesSettingsViewModel</c>). Data is loaded lazily on first tab activation (see
/// <see cref="Load"/>) and can be recomputed on demand via <see cref="RefreshPickListCommand"/>.
/// </summary>
public partial class SalesViewModel(
    IListingService listingService,
    ISalesSettingsService salesSettings,
    IStorageContainerService storageContainers,
    OrdersViewModel orders,
    CustomersViewModel customers) : ObservableObject
{
    public ObservableCollection<PickListEntry> PickList { get; } = [];
    public ObservableCollection<StorageContainer> Locations { get; } = [];

    public OrdersViewModel Orders { get; } = orders;
    public CustomersViewModel Customers { get; } = customers;

    [ObservableProperty]
    public partial StorageContainer? ForSaleLocation { get; set; }

    /// <summary>Surfaces guard/failure outcomes from <see cref="MarkAllPicked"/> (no For-Sale
    /// location configured, or the underlying <see cref="IListingService.MarkPicked"/> call
    /// throwing) since there is no global unhandled-exception handler to fall back on.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>Loads the storage locations and pick list. Safe to call repeatedly (e.g. on
    /// every tab activation) — always refreshes, unlike Dashboard's once-only lazy load. Both
    /// queries run off the UI thread so tab activation never blocks on synchronous DB I/O.</summary>
    public async Task Load()
    {
        try
        {
            var (containers, pickList) = await Task.Run(() => (storageContainers.GetAll(), listingService.GetPickList()));

            Locations.Clear();
            foreach (var c in containers)
                Locations.Add(c);

            ForSaleLocation = Locations.FirstOrDefault(c => c.Id == salesSettings.ForSaleLocationId);

            PickList.Clear();
            foreach (var e in pickList)
                PickList.Add(e);
        }
        catch (Exception ex)
        {
            // Leave any previously-loaded Locations/PickList as-is rather than blanking them —
            // surface the failure via StatusMessage instead of crashing (there is no global
            // unhandled-exception handler to fall back on).
            StatusMessage = $"Failed to load pick list: {ex.Message}";
        }
    }

    [RelayCommand]
    public void RefreshPickList()
    {
        PickList.Clear();
        foreach (var e in listingService.GetPickList())
            PickList.Add(e);
    }

    [RelayCommand]
    public void MarkAllPicked()
    {
        if (ForSaleLocation is null)
        {
            StatusMessage = "Select a For-Sale location first.";
            return;
        }

        var ids = PickList.Select(e => e.LotId).ToList();
        if (ids.Count == 0) return;

        try
        {
            var count = listingService.MarkPicked(ids);
            StatusMessage = $"Marked {count} card(s) picked.";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            return;
        }

        RefreshPickList();
    }

    [RelayCommand]
    public void PrintPickList() => PickListPrinter.Print(PickList.ToList());
}
