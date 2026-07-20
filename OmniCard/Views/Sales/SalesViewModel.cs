using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// Backs the Sales tab: a location-grouped pick list sourced from <see cref="IListingService"/>,
/// plus the For-Sale storage location picker (persisted via <see cref="ISalesSettingsService"/>).
/// Data is loaded lazily on first tab activation (see <see cref="Load"/>) and can be recomputed
/// on demand via <see cref="RefreshPickListCommand"/>.
/// </summary>
public partial class SalesViewModel(
    IListingService listingService,
    ISalesSettingsService salesSettings,
    IStorageContainerService storageContainers) : ObservableObject
{
    public ObservableCollection<PickListEntry> PickList { get; } = [];
    public ObservableCollection<StorageContainer> Locations { get; } = [];

    [ObservableProperty]
    public partial StorageContainer? ForSaleLocation { get; set; }

    /// <summary>Loads the storage locations and pick list. Safe to call repeatedly (e.g. on
    /// every tab activation) — always refreshes, unlike Dashboard's once-only lazy load.</summary>
    public void Load()
    {
        Locations.Clear();
        foreach (var c in storageContainers.GetAll())
            Locations.Add(c);
        ForSaleLocation = Locations.FirstOrDefault(c => c.Id == salesSettings.ForSaleLocationId);
        RefreshPickList();
    }

    partial void OnForSaleLocationChanged(StorageContainer? value)
        => salesSettings.SetForSaleLocationId(value?.Id);

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
        var ids = PickList.Select(e => e.LotId).ToList();
        if (ids.Count == 0) return;
        listingService.MarkPicked(ids);
        RefreshPickList();
    }
}
