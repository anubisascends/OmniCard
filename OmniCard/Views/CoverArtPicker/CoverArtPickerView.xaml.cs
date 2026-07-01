using System.Windows;
using OmniCard.Views;

namespace OmniCard.Views.CoverArtPicker;

public partial class CoverArtPickerView : Window, IView<CoverArtPickerViewModel>
{
    public CoverArtPickerView(CoverArtPickerViewModel viewModel)
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

    public CoverArtPickerViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
