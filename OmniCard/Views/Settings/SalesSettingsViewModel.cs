using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings page's "Sales &amp; Receipts" section: the For-Sale storage location,
/// the company profile, and receipt settings, all persisted via <see cref="ISalesSettingsService"/>.
/// </summary>
public partial class SalesSettingsViewModel(
    ISalesSettingsService salesSettings,
    IStorageContainerService storageContainers) : ObservableObject
{
    public ObservableCollection<StorageContainer> Locations { get; } = [];

    [ObservableProperty]
    public partial StorageContainer? ForSaleLocation { get; set; }

    [ObservableProperty]
    public partial CompanyProfile Company { get; set; } = new();

    [ObservableProperty]
    public partial ReceiptSettings Receipt { get; set; } = new();

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>Loads locations + persisted company/receipt. Safe to call on every activation.</summary>
    public void Load()
    {
        Locations.Clear();
        foreach (var c in storageContainers.GetAll())
            Locations.Add(c);

        ForSaleLocation = Locations.FirstOrDefault(c => c.Id == salesSettings.ForSaleLocationId);
        Company = salesSettings.GetCompany();
        Receipt = salesSettings.GetReceipt();
    }

    [RelayCommand]
    public void Save()
    {
        salesSettings.SetForSaleLocationId(ForSaleLocation?.Id);
        salesSettings.SaveCompany(Company);
        salesSettings.SaveReceipt(Receipt);
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    public void PickLogo()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select company logo",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
        };
        if (dialog.ShowDialog() != true) return;

        Company.LogoPath = salesSettings.SetLogo(dialog.FileName);
        OnPropertyChanged(nameof(Company));
        StatusMessage = "Logo set (remember to Save).";
    }
}
