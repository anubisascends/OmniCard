using System.Windows.Controls;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class SealedProductListView : UserControl
{
    public SealedProductListView()
    {
        InitializeComponent();
    }

    public void WireUp(SealedProductViewModel vm)
    {
        DataContext = vm;
    }

    private void SealedProductGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not SealedProductInstance instance) return;
        if (e.EditingElement is not TextBox textBox) return;

        // Parse the new price and persist it
        decimal? newPrice = decimal.TryParse(textBox.Text, out var parsed) ? parsed : null;
        if (DataContext is SealedProductViewModel vm)
            vm.SaveInstancePrice(instance.Id, newPrice);
    }
}
