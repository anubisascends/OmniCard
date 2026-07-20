namespace OmniCard.Views.Inventory;

public partial class AddLotView : IView<AddLotViewModel>
{
    public AddLotView(AddLotViewModel viewModel)
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

    public AddLotViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
