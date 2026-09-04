using System.Windows;
using System.Windows.Controls;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class LocationOverviewView : UserControl
{
    public LocationOverviewView()
    {
        InitializeComponent();
    }

    private void AuditLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;

        // Single entry point: binders prompt mark-vs-import; other locations prompt scan-vs-import.
        rootView.ViewModel.AuditContainer(summary.Container.Id);
    }

    private void ChangeCoverArt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.Collection.SetCoverArt(summary.Container.Id);
    }

    private void OpenBinderView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.Collection.NavigateToLocation(summary.Container.Id);
    }

    private void UpgradeDeck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.Collection.UpgradeDeck(summary.Container.Id);
    }

    private void AddCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.Collection.OpenManualAdd(summary.Container);
    }

    private void ExportLocationManabox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.ExportLocationManabox(summary.Container.Id);
    }

    private async void CreatePriceSheet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        await rootView.ViewModel.CreatePriceSheet(summary.Container.Id);
    }

    private void ToggleDeckCheckExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.Collection.ToggleDeckCheckExclusion(summary.Container.Id);
    }

    private void ToggleAlwaysAvailable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.Collection.ToggleAlwaysAvailable(summary.Container.Id);
    }

    private void DeleteLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        if (Helpers.DeleteLocationPrompt.Confirm(Window.GetWindow(this), summary.Container.Name, out var moveToBulk))
        {
            var rootView = (RootView)Window.GetWindow(this)!;
            rootView.ViewModel.Collection.DeleteLocationWithOptions(summary.Container.Id, moveToBulk);
        }
    }
}
