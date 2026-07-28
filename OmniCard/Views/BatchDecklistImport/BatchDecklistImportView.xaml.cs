using System.Windows;

namespace OmniCard.Views.BatchDecklistImport;

public partial class BatchDecklistImportView : Window, IView<BatchDecklistImportViewModel>
{
    public BatchDecklistImportView(BatchDecklistImportViewModel viewModel)
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

    public BatchDecklistImportViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
