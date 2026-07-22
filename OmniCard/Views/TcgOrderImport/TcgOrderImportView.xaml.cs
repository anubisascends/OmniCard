using System.Windows;

namespace OmniCard.Views.TcgOrderImport;

public partial class TcgOrderImportView : Window, IView<TcgOrderImportViewModel>
{
    public TcgOrderImportView(TcgOrderImportViewModel viewModel)
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

    public TcgOrderImportViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
