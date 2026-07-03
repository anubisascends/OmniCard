// OmniCard/Views/SetFilterBuilder/SetFilterBuilderView.xaml.cs
using System.Windows;
using System.Windows.Input;

namespace OmniCard.Views.SetFilterBuilder;

public partial class SetFilterBuilderView : Window
{
    public SetFilterBuilderViewModel ViewModel { get; }

    public SetFilterBuilderView(SetFilterBuilderViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmSelection();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void AvailableList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedAvailableItem is not null)
            ViewModel.Add();
    }

    private void SelectedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedSelectedItem is not null)
            ViewModel.Remove();
    }
}
