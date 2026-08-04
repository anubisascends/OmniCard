namespace OmniCard.Views.PickTrade;

public partial class PickTradeView : IView<PickTradeViewModel>
{
    public PickTradeView(PickTradeViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = result =>
        {
            DialogResult = result;
            Close();
        };
        DataContext = this;
    }

    public PickTradeViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
