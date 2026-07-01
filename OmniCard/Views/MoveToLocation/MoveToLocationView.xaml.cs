namespace OmniCard.Views.MoveToLocation;

public partial class MoveToLocationView : IView<MoveToLocationViewModel>
{
    public MoveToLocationView(MoveToLocationViewModel viewModel)
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

    public MoveToLocationViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
