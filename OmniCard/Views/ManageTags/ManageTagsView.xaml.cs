namespace OmniCard.Views.ManageTags;

public partial class ManageTagsView : IView<ManageTagsViewModel>
{
    public ManageTagsView(ManageTagsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = Close;
        DataContext = this;
    }

    public ManageTagsViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
