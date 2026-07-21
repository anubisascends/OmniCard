using System.Windows.Controls;

namespace OmniCard.Views.Sales;

public partial class CustomersView : UserControl
{
    public CustomersView() => InitializeComponent();

    private void CustomersView_OnLoaded(object sender, System.Windows.RoutedEventArgs e) =>
        (DataContext as CustomersViewModel)?.Load();
}
