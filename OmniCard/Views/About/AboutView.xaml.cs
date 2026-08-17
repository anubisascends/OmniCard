using System.Windows;

namespace OmniCard.Views.About;

public partial class AboutView : Window
{
    public AboutViewModel ViewModel { get; }

    public AboutView(AboutViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
