using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings page's "eBay Selling" section: the seller's ship-from address and
/// shipping/return policy inputs, plus the one-click "Run eBay Setup" flow that provisions
/// the inventory location and business policies via <see cref="IEbaySellerSetupService"/>.
/// </summary>
public partial class EbaySellingSettingsViewModel(
    IEbaySellingSettingsService sellingSettings,
    IEbaySellerSetupService setupService) : ObservableObject
{
    [ObservableProperty]
    public partial EbaySellingSettings Settings { get; set; } = new();

    public IReadOnlyList<ReturnShippingPayer> ReturnShippingPayerValues { get; } = Enum.GetValues<ReturnShippingPayer>();

    /// <summary>
    /// Proxies <see cref="EbaySellingSettings.FreeShipping"/>. <see cref="EbaySellingSettings"/> is a
    /// plain POCO with no change notification, so the shipping-cost TextBox's IsEnabled binding
    /// (bound to <see cref="NotFreeShipping"/>) wouldn't live-update if the "Free shipping" CheckBox
    /// bound straight to Settings.FreeShipping — this wrapper raises PropertyChanged on toggle.
    /// </summary>
    public bool FreeShipping
    {
        get => Settings.FreeShipping;
        set
        {
            if (Settings.FreeShipping == value) return;
            Settings.FreeShipping = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotFreeShipping));
        }
    }

    public bool NotFreeShipping => !Settings.FreeShipping;

    partial void OnSettingsChanged(EbaySellingSettings value)
    {
        OnPropertyChanged(nameof(FreeShipping));
        OnPropertyChanged(nameof(NotFreeShipping));
    }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool NotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(NotBusy));

    public ObservableCollection<string> StatusLog { get; } = [];

    /// <summary>Loads the persisted settings. Safe to call on every activation.</summary>
    public void Load() => Settings = sellingSettings.Get();

    [RelayCommand]
    public void Save()
    {
        sellingSettings.Save(Settings);
        StatusLog.Add("Saved.");
    }

    [RelayCommand]
    public async Task RunSetup()
    {
        sellingSettings.Save(Settings); // persist form before setup reads it
        IsBusy = true;
        StatusLog.Clear();
        var progress = new System.Progress<string>(m => StatusLog.Add(m));
        try
        {
            var result = await setupService.RunSetupAsync(progress);
            foreach (var step in result.Steps)
            {
                var status = step.Status switch
                {
                    EbaySetupStepStatus.Ok => "OK",
                    EbaySetupStepStatus.SkippedExisting => "already set up",
                    _ => "FAILED",
                };
                StatusLog.Add($"{step.Name}: {status}{(step.Message is null ? "" : " — " + step.Message)}");
            }
            StatusLog.Add(result.Success ? "eBay setup complete." : "Setup finished with errors — see above.");
            Settings = sellingSettings.Get(); // reflect stored IDs/flags
        }
        finally { IsBusy = false; }
    }
}
