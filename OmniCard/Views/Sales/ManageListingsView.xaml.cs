using System.Windows.Controls;

namespace OmniCard.Views.Sales;

public partial class ManageListingsView : UserControl
{
    public ManageListingsView() => InitializeComponent();

    private void ManageListingsView_OnLoaded(object sender, System.Windows.RoutedEventArgs e) =>
        (DataContext as ManageListingsViewModel)?.Load();
}
