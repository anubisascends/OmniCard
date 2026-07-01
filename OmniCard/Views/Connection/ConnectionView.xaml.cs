namespace OmniCard.Views.Connection;

/// <summary>
/// Interaction logic for ConnectionView.xaml
/// </summary>
public partial class ConnectionView : IView<ConnectionViewModel>
{
    public ConnectionView(ConnectionViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = this;
    }

    public ConnectionViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
