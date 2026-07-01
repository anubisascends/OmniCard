namespace OmniCard.Views.SealedProductEditor;

public partial class CrackProductView : IView<CrackProductViewModel>
{
    public CrackProductView(CrackProductViewModel viewModel)
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

    public CrackProductViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
