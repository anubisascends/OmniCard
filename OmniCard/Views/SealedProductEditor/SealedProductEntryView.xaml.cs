namespace OmniCard.Views.SealedProductEditor;

public partial class SealedProductEntryView : IView<SealedProductEntryViewModel>
{
    public SealedProductEntryView(SealedProductEntryViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = result =>
        {
            DialogResult = result;
            Close();
        };
        ViewModel.FocusUpcField = () => UpcField.Focus();
        ViewModel.FocusPriceField = () => PriceField.Focus();
        DataContext = this;
    }

    public SealedProductEntryViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
