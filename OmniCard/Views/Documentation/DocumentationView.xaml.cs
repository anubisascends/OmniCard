using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace OmniCard.Views.Documentation;

public partial class DocumentationView : Window
{
    public DocumentationViewModel ViewModel { get; }

    public DocumentationView(DocumentationViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        if (ViewModel.DocsAvailable)
            Loaded += async (_, _) => await InitializeWebViewAsync();
        else
            MissingDocsMessage.Visibility = Visibility.Visible;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();

            var core = WebView.CoreWebView2;

            // Lock the embedded browser down to a read-only local help viewer.
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // Serve the bundled Docs/ folder over a virtual host so relative asset
            // references (css/js/images) resolve correctly and no real web access is needed.
            core.SetVirtualHostNameToFolderMapping(
                DocumentationViewModel.VirtualHost,
                ViewModel.DocsFolder!,
                CoreWebView2HostResourceAccessKind.Allow);

            // External links (e.g. Scryfall, eBay) should open in the user's real browser,
            // never inside the help window.
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationStarting += OnNavigationStarting;

            WebView.Source = new Uri(ViewModel.StartUrl);
        }
        catch
        {
            MissingDocsMessage.Text =
                "The WebView2 runtime is required to display documentation but could not be initialized. "
                + "Please ensure the Microsoft Edge WebView2 Runtime is installed.";
            MissingDocsMessage.Visibility = Visibility.Visible;
            WebView.Visibility = Visibility.Collapsed;
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternally(e.Uri);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Allow navigation within the local help host; send anything else to the OS browser.
        if (e.Uri.StartsWith($"https://{DocumentationViewModel.VirtualHost}/", StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        OpenExternally(e.Uri);
    }

    private static void OpenExternally(string uri)
    {
        if (!uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch
        {
            // Ignore — failing to launch a browser shouldn't break the help window.
        }
    }
}
