using System.Windows;

namespace OmniCard.Views.CollectionCardEditor;

public partial class CollectionCardEditorView : IView<CollectionCardEditorViewModel>
{
    public CollectionCardEditorView(CollectionCardEditorViewModel viewModel)
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

    public CollectionCardEditorViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = null;
        Close();
    }
}
