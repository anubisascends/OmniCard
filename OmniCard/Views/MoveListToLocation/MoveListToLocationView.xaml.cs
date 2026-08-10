namespace OmniCard.Views.MoveListToLocation;

public partial class MoveListToLocationView : IView<MoveListToLocationViewModel>
{
    public MoveListToLocationView(MoveListToLocationViewModel viewModel)
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

    public MoveListToLocationViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
