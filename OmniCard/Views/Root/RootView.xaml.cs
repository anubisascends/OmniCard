using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using OmniCard.Models;

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
        CollectionTab.WireUpSealed(viewModel.Sealed);
        ScannerTab.ViewModel = viewModel;
        ScannerTab.WireUpAutoScroll();

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
        viewModel.Sealed.LaunchScanner = () =>
        {
            MainTabControl.SelectedIndex = MainTabControl.Items.IndexOf(tabItemScanner);
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

        // Apply saved theme
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(ViewModel.IsDarkTheme ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(theme);

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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Application shutting down");
        Close();
        return Task.CompletedTask;
    }
}
