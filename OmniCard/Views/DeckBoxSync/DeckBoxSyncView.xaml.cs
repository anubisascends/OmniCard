namespace OmniCard.Views.DeckBoxSync;

public partial class DeckBoxSyncView : IView<DeckBoxSyncViewModel>
{
    public DeckBoxSyncView(DeckBoxSyncViewModel viewModel)
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

    public DeckBoxSyncViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
