namespace OmniCard.Views.SealedProductEditor;

public partial class AddSealedProductView : IView<AddSealedProductViewModel>
{
    public AddSealedProductView(AddSealedProductViewModel viewModel)
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

    public AddSealedProductViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
