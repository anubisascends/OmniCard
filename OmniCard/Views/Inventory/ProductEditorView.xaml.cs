namespace OmniCard.Views.Inventory;

public partial class ProductEditorView : IView<ProductEditorViewModel>
{
    public ProductEditorView(ProductEditorViewModel viewModel)
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

    public ProductEditorViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
