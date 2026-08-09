using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// Backs the "Listings" tab: every Listed/Picked <see cref="Listing"/>, editable in place via
/// <see cref="IListingService.UpdateListing"/>. Exists because once a card is picked (or even
/// while still just listed), the only prior way to change its price was to Unlist and re-list it
/// — losing the picked state — with no way to see what's currently listed/picked at all once it
/// dropped off the (Listed-only) pick list.
/// </summary>
public partial class ManageListingsViewModel(IListingService listingService) : ObservableObject
{
    public ObservableCollection<ListingDetail> Listings { get; } = [];

    public SalesChannel[] Channels { get; } = Enum.GetValues<SalesChannel>();

    [ObservableProperty]
    public partial ListingDetail? SelectedListing { get; set; }

    [ObservableProperty]
    public partial decimal EditPrice { get; set; }

    [ObservableProperty]
    public partial SalesChannel EditChannel { get; set; }

    [ObservableProperty]
    public partial int EditQuantity { get; set; } = 1;

    [ObservableProperty]
    public partial string? EditNote { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    partial void OnSelectedListingChanged(ListingDetail? value)
    {
        EditPrice = value?.ListedPrice ?? 0m;
        EditChannel = value?.Channel ?? SalesChannel.Manual;
        EditQuantity = value?.Quantity ?? 1;
        EditNote = value?.Note;
        StatusMessage = null;
    }

    /// <summary>Loads all active listings. Safe to call repeatedly (e.g. on every tab activation).</summary>
    public void Load()
    {
        var selectedId = SelectedListing?.Id;

        Listings.Clear();
        foreach (var l in listingService.GetListingDetails())
            Listings.Add(l);

        SelectedListing = selectedId is int id ? Listings.FirstOrDefault(l => l.Id == id) : null;
    }

    [RelayCommand]
    public void Refresh() => Load();

    [RelayCommand]
    public void SaveChanges()
    {
        if (SelectedListing is not { } listing) return;

        if (EditPrice < 0)
        {
            StatusMessage = "Price cannot be negative.";
            return;
        }

        if (EditQuantity < 1)
        {
            StatusMessage = "Quantity must be at least 1.";
            return;
        }

        try
        {
            listingService.UpdateListing(listing.Id, EditPrice, EditChannel, EditQuantity, EditNote);
            StatusMessage = "Saved.";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            return;
        }

        Load();
    }

    [RelayCommand]
    public void Unlist()
    {
        if (SelectedListing is not { } listing) return;

        listingService.Unlist([listing.LotId]);
        StatusMessage = "Unlisted.";
        SelectedListing = null;
        Load();
    }
}
