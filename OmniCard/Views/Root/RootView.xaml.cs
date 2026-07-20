using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using OmniCard.Models;
using OmniCard.Controls.Themes;

namespace OmniCard.Views.Root;

public partial class RootView : IView<RootViewModel>, IHostedService
{
    private readonly ILogger<RootView> _logger;

    public RootView(RootViewModel viewModel, ILogger<RootView> logger)
    {
        _logger = logger;
        _logger.LogInformation("OmniCard application starting");
        InitializeComponent();
        ViewModel = viewModel;

        CollectionTab.ViewModel = viewModel;
        CollectionTab.WireUp(viewModel.Collection);
        CollectionTab.WireUpInventory(viewModel.Inventory);
        ScannerTab.ViewModel = viewModel;
        ScannerTab.WireUpAutoScroll();
        DashboardTab.WireUp(viewModel.Dashboard);

        // Lazy-load the Dashboard tab's data the first time it's selected.
        MainTabControl.SelectionChanged += (_, _) =>
        {
            if (MainTabControl.SelectedItem == tabItemDashboard)
                viewModel.Dashboard.Load();
        };

        ViewModel.Collection.GetSelectedCards = () => CollectionTab.GetSelectedCards();
        ViewModel.Collection.FocusSearch = () =>
        {
            MainTabControl.SelectedIndex = MainTabControl.Items.IndexOf(tabItemCollection);
            CollectionTab.FocusSearchBox();
        };
        ViewModel.FocusManualSearch = () =>
        {
            MainTabControl.SelectedIndex = MainTabControl.Items.IndexOf(tabItemScanner);
            ScannerTab.FocusManualSearchBox();
        };
        ViewModel.SelectAllInActiveTab = () =>
        {
            switch (MainTabControl.SelectedIndex)
            {
                case 1: CollectionTab.SelectAll(); break;
                case 2: ScannerTab.SelectAll(); break;
            }
        };
        DataContext = this;
    }

    public RootViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Application window opening");
        Show();

        // Provide the window handle to the scanner service so TWAIN drivers
        // that require a valid HWND for message pumping (e.g., network scanners)
        // don't crash with STATUS_STACK_BUFFER_OVERRUN.
        var hwnd = new WindowInteropHelper(this).Handle;
        ViewModel.ScannerService.WindowHandle = hwnd;

        // Apply saved theme with custom palette
        OmniCardTheme.Apply(ViewModel.IsDarkTheme);

        ViewModel.Initialize();
        _logger.LogInformation("Application initialized and ready");
        return Task.CompletedTask;
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void DeleteSelectedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        switch (MainTabControl.SelectedIndex)
        {
            case 1: // Collection tab
                ViewModel.Collection.BulkDeleteCollection();
                break;
            case 2: // Scanner tab
                ViewModel.RemoveScannedCard();
                break;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel)
            Application.Current.Shutdown();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Application shutting down");
        Close();
        return Task.CompletedTask;
    }
}
