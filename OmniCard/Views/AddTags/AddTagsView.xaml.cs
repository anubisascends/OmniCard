namespace OmniCard.Views.AddTags;

public partial class AddTagsView : IView<AddTagsViewModel>
{
    public AddTagsView(AddTagsViewModel viewModel)
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

    public AddTagsViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
