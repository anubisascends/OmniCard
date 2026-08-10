namespace OmniCard.Views.CreateListFromScans;

public partial class CreateListFromScansView : IView<CreateListFromScansViewModel>
{
    public CreateListFromScansView(CreateListFromScansViewModel viewModel)
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

    public CreateListFromScansViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
