using System.Windows;
using System.Windows.Controls;

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
}
