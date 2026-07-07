using System.Windows;
using OmniCard.Views;

namespace OmniCard.Views.EbayListing;

public partial class EbayListingView : Window, IView<EbayListingViewModel>
{
    public EbayListingView(EbayListingViewModel viewModel)
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

    public EbayListingViewModel ViewModel { get; }
    IViewModel IView.ViewModel => ViewModel;
}
