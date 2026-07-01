using System.Windows.Controls;

namespace OmniCard.Views.Root;

public partial class ScannerDetailPanelView : UserControl
{
    public ScannerDetailPanelView()
    {
        InitializeComponent();
    }

    public void FocusSearchBox() => CardSearch.FocusSearchBox();
}
