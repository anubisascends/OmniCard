namespace OmniCard.Views.SealedProductEditor;

public partial class SealedProductTemplateEditorView : IView<SealedProductTemplateEditorViewModel>
{
    public SealedProductTemplateEditorView(SealedProductTemplateEditorViewModel viewModel)
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

    public SealedProductTemplateEditorViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
