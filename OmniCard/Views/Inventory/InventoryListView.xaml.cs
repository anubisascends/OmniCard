using System.Windows.Controls;

namespace OmniCard.Views.Inventory;

public partial class InventoryListView : UserControl
{
    public InventoryListView()
    {
        InitializeComponent();
    }

    public void WireUp(InventoryViewModel vm)
    {
        DataContext = vm;
    }
}
