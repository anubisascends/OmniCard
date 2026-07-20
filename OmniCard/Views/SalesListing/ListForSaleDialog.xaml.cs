using System.Windows;

namespace OmniCard.Views.SalesListing;

public partial class ListForSaleDialog : Window
{
    public ListForSaleDialog(ListForSaleViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnList(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
