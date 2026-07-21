using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Views.DataLocation;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings dialog. Composes the section view-models and tracks which section the
/// left-hand nav has selected. Display prefs are bound directly to the RootViewModel (one source
/// of truth) and so are not represented here.
/// </summary>
public partial class SettingsViewModel(
    SalesSettingsViewModel sales,
    DataLocationViewModel dataLocation) : ObservableObject
{
    public SalesSettingsViewModel Sales { get; } = sales;
    public DataLocationViewModel DataLocation { get; } = dataLocation;

    /// <summary>Index of the section selected in the dialog's left-hand nav
    /// (0 = Display, 1 = Data Location, 2 = Sales &amp; Receipts).</summary>
    [ObservableProperty]
    public partial int SelectedSectionIndex { get; set; }

    public bool ShowDisplay => SelectedSectionIndex == 0;
    public bool ShowDataLocation => SelectedSectionIndex == 1;
    public bool ShowSales => SelectedSectionIndex == 2;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowDisplay));
        OnPropertyChanged(nameof(ShowDataLocation));
        OnPropertyChanged(nameof(ShowSales));
    }

    /// <summary>Loads section data. Called when the Settings dialog opens.</summary>
    public async Task Load()
    {
        Sales.Load();
        await DataLocation.LoadAsync();
    }
}
