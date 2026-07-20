namespace OmniCard.Views.Inventory;

public partial class OpenUnitsView : IView<OpenUnitsViewModel>
{
    public OpenUnitsView(OpenUnitsViewModel viewModel)
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

    public OpenUnitsViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
