using System.Windows;

namespace OmniCard.Views.ManualAdd;

public partial class ManualAddView : Window, IView<ManualAddViewModel>
{
    public ManualAddView(ManualAddViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = this;
    }

    public ManualAddViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = ViewModel.HasAdded;
        Close();
    }
}
