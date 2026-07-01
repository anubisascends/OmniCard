namespace OmniCard.Views.HashPreview;

public partial class HashPreviewView : IView<HashPreviewViewModel>
{
    public HashPreviewView(HashPreviewViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = this;
    }

    public HashPreviewViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
