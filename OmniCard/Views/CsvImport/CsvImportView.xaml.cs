using System.Windows;

namespace OmniCard.Views.CsvImport;

public partial class CsvImportView : Window, IView<CsvImportViewModel>
{
    public CsvImportView(CsvImportViewModel viewModel)
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

    public CsvImportViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
