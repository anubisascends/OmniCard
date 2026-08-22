using System.Windows;

namespace OmniCard.Views.Trades;

public partial class TradesView : Window
{
    public TradesViewModel ViewModel { get; }

    public TradesView(TradesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        ViewModel.CloseDialog = Close;
        InitializeComponent();
    }
}
