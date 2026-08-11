namespace OmniCard.Views.TopValueCards;

public partial class TopValueCardsView : IView<TopValueCardsViewModel>
{
    public TopValueCardsView(TopValueCardsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = Close;
        DataContext = this;
    }

    public TopValueCardsViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
}
