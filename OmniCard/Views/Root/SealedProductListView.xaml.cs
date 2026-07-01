using System.Windows.Controls;

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
}
