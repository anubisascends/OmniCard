namespace OmniCard.Views.Sales;

public partial class RequireReasonView : IView<RequireReasonViewModel>
{
    public RequireReasonView(RequireReasonViewModel viewModel)
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

    public RequireReasonViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
