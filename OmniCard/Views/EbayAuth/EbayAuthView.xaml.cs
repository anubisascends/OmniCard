using System.Windows;
using Microsoft.Web.WebView2.Core;
using OmniCard.Views;

namespace OmniCard.Views.EbayAuth;

public partial class EbayAuthView : Window, IView<EbayAuthViewModel>
{
    public EbayAuthView(EbayAuthViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = result =>
        {
            DialogResult = result;
            Close();
        };
        DataContext = this;

        WebView.NavigationStarting += WebView_NavigationStarting;
        WebView.CoreWebView2InitializationCompleted += WebView_Initialized;

        // Navigate to the auth URL once the component is loaded
        Loaded += (_, _) => WebView.Source = new Uri(ViewModel.AuthUrl);
    }

    public EbayAuthViewModel ViewModel { get; }
    IViewModel IView.ViewModel => ViewModel;

    private void WebView_Initialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ViewModel.ErrorMessage = "Failed to initialize browser. Please ensure WebView2 Runtime is installed.";
        }
        else
        {
            ViewModel.IsLoading = false;
        }
    }

    private async void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Check if navigating to the redirect URI
        if (!string.IsNullOrEmpty(ViewModel.RedirectUri)
            && e.Uri.StartsWith(ViewModel.RedirectUri, StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true; // Don't actually navigate to the redirect URI
            await ViewModel.HandleRedirectAsync(new Uri(e.Uri));
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
