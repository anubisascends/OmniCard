using System.Windows;

namespace OmniCard.Views.DecklistImport;

public partial class DecklistImportView : Window, IView<DecklistImportViewModel>
{
    public DecklistImportView(DecklistImportViewModel viewModel)
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

    public DecklistImportViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
