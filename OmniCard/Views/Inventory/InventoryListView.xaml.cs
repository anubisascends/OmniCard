using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OmniCard.Views.Inventory;

public partial class InventoryListView : UserControl
{
    public InventoryListView()
    {
        InitializeComponent();
        // Focus the scan box whenever the Inventory view becomes visible, so a barcode scanned
        // immediately after switching tabs lands in the right place.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Dispatcher.BeginInvoke(() => ScanBox.Focus());
        };
    }

    public void WireUp(InventoryViewModel vm)
    {
        DataContext = vm;
        // Let the ViewModel return focus to the scan box after each scan completes, so the next
        // barcode can be scanned without touching the mouse.
        vm.FocusScanBox = () => Dispatcher.BeginInvoke(() => ScanBox.Focus());
    }

    // Chevron click: toggle the product's lot sub-list. We flip the model's IsExpanded (drives the
    // chevron direction) and set the owning row's DetailsVisibility directly — the most reliable way
    // to expand DataGrid row-details, independent of selection/virtualization quirks.
    private void ToggleLotDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InventoryRow row }) return;

        row.IsExpanded = !row.IsExpanded;

        if (FindAncestor<DataGridRow>((DependencyObject)sender) is { } dataGridRow)
            dataGridRow.DetailsVisibility = row.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    // A recycled row (virtualization) must show the details state of whatever item it now hosts.
    private void InventoryGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is InventoryRow row)
            e.Row.DetailsVisibility = row.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var current = start; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match)
                return match;
        return null;
    }
}
