using System.Windows;

namespace OmniCard.Views.LogViewer;

public partial class LogViewerView : Window
{
    public LogViewerViewModel ViewModel { get; }

    public LogViewerView(LogViewerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
