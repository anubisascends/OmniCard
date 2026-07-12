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
        rootView.ViewModel.StartAudit(summary.Container.Id);
    }

    private void ChangeCoverArt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.Collection.SetCoverArt(summary.Container.Id);
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
        rootView.ViewModel.ExportLocationManabox(summary.Container.Id, summary.Container.Name);
    }

    private void ToggleDeckCheckExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        var rootView = (RootView)Window.GetWindow(this)!;
        rootView.ViewModel.Collection.ToggleDeckCheckExclusion(summary.Container.Id);
    }

    private void DeleteLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.DataContext is not LocationTileSummary summary) return;

        // Show delete confirmation dialog with checkbox
        var dialog = new Window
        {
            Title = "Delete Location",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("MaterialDesign.Brush.Background"),
            Foreground = (System.Windows.Media.Brush)FindResource("MaterialDesign.Brush.Foreground"),
        };

        var moveCheckBox = new CheckBox
        {
            Content = "Move cards to Bulk",
            IsChecked = true,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var yesButton = new Button { Content = "Yes", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var noButton = new Button { Content = "No", Padding = new Thickness(16, 6, 16, 6), IsCancel = true };

        yesButton.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        noButton.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttonPanel.Children.Add(yesButton);
        buttonPanel.Children.Add(noButton);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Are you sure you want to delete \"{summary.Container.Name}\"?", FontSize = 14 });
        panel.Children.Add(moveCheckBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        if (dialog.ShowDialog() == true)
        {
            var rootView = (RootView)Window.GetWindow(this)!;
            rootView.ViewModel.Collection.DeleteLocationWithOptions(
                summary.Container.Id,
                moveCheckBox.IsChecked == true);
        }
    }
}
