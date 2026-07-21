using System.Windows.Controls;

namespace OmniCard.Views.Sales;

public partial class OrdersView : UserControl
{
    public OrdersView() => InitializeComponent();

    private void OrdersView_OnLoaded(object sender, System.Windows.RoutedEventArgs e) =>
        (DataContext as OrdersViewModel)?.Load();
}
