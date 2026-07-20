using System.Windows;

namespace OmniCard.Views.MovementHistory;

public partial class MovementHistoryView : Window
{
    public MovementHistoryViewModel ViewModel { get; }

    public MovementHistoryView(MovementHistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
