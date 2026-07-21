using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Views.DataLocation;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings tab. Composes the section view-models. Display prefs are bound directly
/// to the RootViewModel (one source of truth) and so are not represented here.
/// </summary>
public partial class SettingsViewModel(
    SalesSettingsViewModel sales,
    DataLocationViewModel dataLocation) : ObservableObject
{
    public SalesSettingsViewModel Sales { get; } = sales;
    public DataLocationViewModel DataLocation { get; } = dataLocation;

    /// <summary>Loads section data. Called when the Settings tab is activated.</summary>
    public async Task Load()
    {
        Sales.Load();
        await DataLocation.LoadAsync();
    }
}
