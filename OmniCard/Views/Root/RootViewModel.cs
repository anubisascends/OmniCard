using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using OmniCard.CardMatching;
using OmniCard.Helpers;
using OmniCard.Controls.Converters;
using OmniCard.Controls.Themes;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;
using NTwain;
using OmniCard.Scanner;
using OmniCard.Views.HashPreview;

namespace OmniCard.Views.Root;

public sealed partial class RootViewModel(
    ScannerService scannerService,
    IDialogService dialogService,
    ICardService cardService,
    IOptions<DisplaySettings> displaySettings,
    IStorageContainerService containerService,
    IEbayAuthService ebayAuthService,
    ICsvExportImportService csvService,
    CollectionViewModel collection,
    SealedProductViewModel sealedVm,
    IMismatchLogService mismatchLogService,
    SetSymbolCache setSymbolCache,
    IScanDiagnosticService diagnosticService,
    IAuditService auditService,
    IDataPathService dataPathService,
    IOptionsMonitor<WebCompanionSettings> webCompanionSettings,
    ILogger<RootViewModel> logger) : ViewModel
{
    private readonly ILogger<RootViewModel> _logger = logger;
    private readonly IScanDiagnosticService _diagnosticService = diagnosticService;
    private NotifyCollectionChangedEventHandler? _scannedCardsHandler;
    private System.Windows.Threading.DispatcherTimer? _ebaySyncTimer;
    private bool _suppressGameChangeHandler;
    private CardGame _previousGame;

    public string PhoneScanUrl
    {
        get
        {
            var baseUrl = webCompanionSettings.CurrentValue.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl)) return "";

            // Replace localhost with the machine's LAN IP so the phone can reach it
            var uri = new Uri(baseUrl.TrimEnd('/'));
            if (uri.Host is "localhost" or "127.0.0.1")
            {
                var lanIp = GetLanIp();
                if (lanIp is not null)
                {
                    var builder = new UriBuilder(uri) { Host = lanIp };
                    return $"{builder.Uri.ToString().TrimEnd('/')}/scan";
                }
            }

            return $"{baseUrl.TrimEnd('/')}/scan";
        }
    }

    private static string? GetLanIp()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);
            socket.Connect("8.8.8.8", 53);
            return ((System.Net.IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    public void ShowPhoneScanQr()
    {
        var url = PhoneScanUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            Message = "Set the WebCompanion BaseUrl in Settings first.";
            return;
        }

        using var qrGenerator = new QRCoder.QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qrCode = new QRCoder.PngByteQRCode(qrData);
        var pngBytes = qrCode.GetGraphic(10, new byte[] { 255, 255, 255 }, new byte[] { 30, 30, 46 });

        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(pngBytes);
        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        var window = new Window
        {
            Title = "Phone Scanner",
            Width = 380,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 46)),
            Content = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new System.Windows.Controls.Image
                    {
                        Source = bitmap,
                        Width = 300,
                        Height = 300,
                        Margin = new Thickness(0, 0, 0, 16)
                    },
                    new System.Windows.Controls.TextBlock
                    {
                        Text = "Scan this QR code with your phone",
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    new System.Windows.Controls.TextBlock
                    {
                        Text = url,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)),
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        window.ShowDialog();
    }

    /// <summary>The nested CollectionViewModel that owns all collection-specific state.</summary>
    public CollectionViewModel Collection { get; } = collection;

    /// <summary>The nested SealedProductViewModel that owns all sealed product state.</summary>
    public SealedProductViewModel Sealed { get; } = sealedVm;

    /// <summary>Set by the View to focus and select the manual search box.</summary>
    public Action? FocusManualSearch { get; set; }

    public ScannerService ScannerService { get; } = scannerService;
    public IDialogService DialogService { get; } = dialogService;
    public ICardService CardService { get; } = cardService;

    // Scan defaults — applied to each newly scanned card
    [ObservableProperty]
    public partial bool DefaultIsFoil { get; set; }
    partial void OnDefaultIsFoilChanged(bool value) => CardService.DefaultIsFoil = value;

    [ObservableProperty]
    public partial decimal? DefaultPurchasePrice { get; set; }
    partial void OnDefaultPurchasePriceChanged(decimal? value) => CardService.DefaultPurchasePrice = value;

    // Bulk edit properties for multi-selected scanned cards
    [ObservableProperty]
    public partial string BulkCondition { get; set; } = "NM";

    [ObservableProperty]
    public partial bool BulkIsFoil { get; set; }

    [ObservableProperty]
    public partial decimal? BulkPurchasePrice { get; set; }

    [RelayCommand]
    public void ApplyBulkEdit()
    {
        foreach (var card in SelectedScannedCards)
        {
            card.Condition = BulkCondition;
            card.IsFoil = BulkIsFoil;
            card.PurchasePrice = BulkPurchasePrice;
        }
        _logger.LogInformation("Applied bulk edit to {Count} scanned cards: Condition={Condition}, Foil={Foil}, Price={Price}",
            SelectedScannedCards.Count, BulkCondition, BulkIsFoil, BulkPurchasePrice);
        Message = $"Applied bulk edit to {SelectedScannedCards.Count} card(s).";
    }

    // Storage container selection
    public ObservableCollection<StorageContainer> AvailableContainers { get; } = [];
    public ListCollectionView GroupedContainers { get; private set; } = null!;

    [ObservableProperty]
    public partial StorageContainer? ActiveContainer { get; set; }

    [ObservableProperty]
    public partial int? ActivePage { get; set; }

    [ObservableProperty]
    public partial int? ActiveSlot { get; set; }

    [ObservableProperty]
    public partial string? ActiveSection { get; set; }

    public bool ShowBinderFields => ActiveContainer?.ContainerType == ContainerType.Binder;
    public bool ShowBoxFields => ActiveContainer?.ContainerType == ContainerType.Box;

    partial void OnActiveContainerChanged(StorageContainer? value)
    {
        ActivePage = null;
        ActiveSlot = null;
        ActiveSection = null;
        OnPropertyChanged(nameof(ShowBinderFields));
        OnPropertyChanged(nameof(ShowBoxFields));
    }

    public void LoadContainers()
    {
        var previousActiveId = ActiveContainer?.Id;
        AvailableContainers.Clear();
        foreach (var c in containerService.GetAll().OrderBy(c => c.ContainerType).ThenBy(c => c.Name))
            AvailableContainers.Add(c);

        GroupedContainers = new ListCollectionView(AvailableContainers);
        GroupedContainers.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StorageContainer.ContainerType)));
        OnPropertyChanged(nameof(GroupedContainers));

        // Restore previous selection, or default to Bulk
        ActiveContainer = AvailableContainers.FirstOrDefault(c => c.Id == previousActiveId)
            ?? AvailableContainers.FirstOrDefault(c => c.ContainerType == ContainerType.Bulk);

        // Keep the collection VM's container list in sync
        Collection.LoadContainers();
    }

    [ObservableProperty]
    public partial bool IsDarkTheme { get; set; } = displaySettings.Value.Theme != "Light";

    partial void OnIsDarkThemeChanged(bool value)
    {
        OmniCardTheme.Apply(value);
        PersistDisplaySettings();
    }

    [ObservableProperty]
    public partial double CardDetailFontSize { get; set; } = displaySettings.Value.CardDetailFontSize;

    partial void OnCardDetailFontSizeChanged(double value) => PersistDisplaySettings();

    [ObservableProperty]
    public partial double CardPreviewScale { get; set; } = displaySettings.Value.CardPreviewScale;

    public double CardPreviewWidth => 150.0 * CardPreviewScale / 100.0;

    partial void OnCardPreviewScaleChanged(double value)
    {
        OnPropertyChanged(nameof(CardPreviewWidth));
        PersistDisplaySettings();
    }

    [ObservableProperty]
    public partial double ScannerFontSize { get; set; } = displaySettings.Value.ScannerFontSize;

    public double ScannerFontSizeSmall => ScannerFontSize - 2;

    partial void OnScannerFontSizeChanged(double value)
    {
        OnPropertyChanged(nameof(ScannerFontSizeSmall));
        PersistDisplaySettings();
    }

    [ObservableProperty]
    public partial double ScannerListWidth { get; set; } = displaySettings.Value.ScannerListWidth;

    partial void OnScannerListWidthChanged(double value) => PersistDisplaySettings();

    [ObservableProperty]
    public partial string? DefaultScannerName { get; set; } = displaySettings.Value.DefaultScannerName;

    partial void OnDefaultScannerNameChanged(string? value) => PersistDisplaySettings();

    [ObservableProperty]
    public partial ScanQuality ScanQuality { get; set; } = displaySettings.Value.ScanQuality;

    partial void OnScanQualityChanged(ScanQuality value)
    {
        ScannerService.ScanQuality = value;
        PersistDisplaySettings();
    }

    [ObservableProperty]
    public partial bool ShowScannerUI { get; set; } = displaySettings.Value.ShowScannerUI;

    partial void OnShowScannerUIChanged(bool value) => PersistDisplaySettings();

    private void PersistDisplaySettings()
    {
        try
        {
            var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var json = File.ReadAllText(appSettingsPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            using var stream = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                bool wroteDisplay = false;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "Display")
                    {
                        WriteDisplaySection(writer);
                        wroteDisplay = true;
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }

                if (!wroteDisplay)
                    WriteDisplaySection(writer);

                writer.WriteEndObject();
            }

            File.WriteAllBytes(appSettingsPath, stream.ToArray());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist display settings");
        }
    }

    private void WriteDisplaySection(System.Text.Json.Utf8JsonWriter writer)
    {
        writer.WriteStartObject("Display");
        writer.WriteNumber("CardDetailFontSize", CardDetailFontSize);
        writer.WriteString("Theme", IsDarkTheme ? "Dark" : "Light");
        writer.WriteNumber("CardPreviewScale", CardPreviewScale);

        // Persist column visibility from CollectionViewModel
        writer.WriteStartObject("CollectionColumnVisibility");
        foreach (var kv in Collection.GetColumnVisibilityForPersistence())
            writer.WriteBoolean(kv.Key, kv.Value);
        writer.WriteEndObject();

        writer.WriteBoolean("StackDuplicates", Collection.IsStacked);
        writer.WriteNumber("ScannerFontSize", ScannerFontSize);
        writer.WriteNumber("ScannerListWidth", ScannerListWidth);
        if (DefaultScannerName is not null)
            writer.WriteString("DefaultScannerName", DefaultScannerName);
        else
            writer.WriteNull("DefaultScannerName");

        writer.WriteString("ScanQuality", ScanQuality.ToString());
        writer.WriteBoolean("ShowScannerUI", ShowScannerUI);

        writer.WriteEndObject();
    }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEbayConnected { get; set; }

    [RelayCommand]
    public void ConnectToEbay()
    {
        var result = dialogService.ConnectToEbay();
        if (result == true)
        {
            IsEbayConnected = ebayAuthService.IsConnected;
            Message = "Connected to eBay.";
        }
    }

    [RelayCommand]
    public void DisconnectEbay()
    {
        _ebaySyncTimer?.Stop();
        _ebaySyncTimer = null;
        ebayAuthService.Disconnect();
        IsEbayConnected = false;
        Message = "Disconnected from eBay.";
    }

    [ObservableProperty]
    public partial bool ShowHashPreview { get; set; }

    private HashPreviewView? _hashPreviewWindow;

    partial void OnShowHashPreviewChanged(bool value)
    {
        if (value)
        {
            _hashPreviewWindow = App.Host.Services.GetRequiredService<HashPreviewView>();
            var main = Application.Current.MainWindow;
            if (main is not null && main != _hashPreviewWindow && main.IsLoaded)
                _hashPreviewWindow.Owner = main;
            _hashPreviewWindow.Closed += (_, _) =>
            {
                _hashPreviewWindow = null;
                ShowHashPreview = false;
            };
            _hashPreviewWindow.Show();

            CardService.OnHashStage = stage =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (_hashPreviewWindow is null) return;

                    // On first stage of a new scan, clear previous results
                    if (stage.StageName == "Original")
                        _hashPreviewWindow.ViewModel.Clear();

                    _hashPreviewWindow.ViewModel.AddStage(stage);

                    // On last stage, show the hash text
                    if (stage.StageName == "Hash")
                    {
                        _hashPreviewWindow.ViewModel.HashText = $"0x{CardService.LastComputedHash:X16}";
                    }
                });
            };
        }
        else
        {
            CardService.OnHashStage = null;
            if (_hashPreviewWindow is not null)
            {
                _hashPreviewWindow.Close();
                _hashPreviewWindow = null;
            }
        }
    }

    // Game selection
    public IReadOnlyList<CardGame> AvailableGames => CardService.AvailableGames;

    [ObservableProperty]
    public partial CardGame SelectedGame { get; set; }

    partial void OnSelectedGameChanging(CardGame value)
    {
        _previousGame = SelectedGame;
    }

    partial void OnSelectedGameChanged(CardGame value)
    {
        if (_suppressGameChangeHandler)
            return;

        if (CardService.ScannedCards.Count > 0)
        {
            _logger.LogWarning("Blocked game switch from {Old} to {New}: {Count} pending scan(s)",
                _previousGame, value, CardService.ScannedCards.Count);

            MessageBox.Show(
                $"You have {CardService.ScannedCards.Count} unconfirmed scan(s). " +
                "Please commit or discard them before switching games.",
                "Game Switch Blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _suppressGameChangeHandler = true;
            SelectedGame = _previousGame;
            _suppressGameChangeHandler = false;
            return;
        }

        _logger.LogInformation("Switched active game to {Game}", value);
        CardService.SelectedGame = value;
        SetFilterText = "";
        LoadAvailableSets();
        Collection.SetGame(value);
        InvalidateHomeTab();
    }

    // Set filter — comma-separated set codes
    private List<SetInfo> _allSets = [];

    [ObservableProperty]
    public partial string SetFilterText { get; set; } = "";

    [RelayCommand]
    public void ApplySetFilterText()
    {
        UpdateSetFilter();
    }

    partial void OnSetFilterTextChanged(string value) => UpdateSetFilter();

    private void LoadAvailableSets()
    {
        _allSets = CardService.ActiveGameService.GetAvailableSets().ToList();

        // Register set names for tooltip display on set symbols
        foreach (var set in _allSets)
            setSymbolCache.RegisterSetName(set.SetCode, set.SetName);

        SetFilterText = "";
        UpdateSetFilter();
    }

    private void UpdateSetFilter()
    {
        var text = SetFilterText.Trim();
        if (string.IsNullOrEmpty(text))
        {
            CardService.SelectedSetFilter = null;
            _logger.LogInformation("Set filter cleared");
            return;
        }

        var codes = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var knownCodes = _allSets.Select(s => s.SetCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validCodes = codes
            .Where(c => knownCodes.Contains(c))
            .Select(c => _allSets.First(s => s.SetCode.Equals(c, StringComparison.OrdinalIgnoreCase)).SetCode)
            .ToHashSet();

        if (validCodes.Count == 0)
        {
            // Text is non-empty but has no valid codes (typo or mid-typing).
            // Preserve the current filter so partial input doesn't accidentally
            // clear an active filter. The user can explicitly clear via the X button.
            _logger.LogDebug("Set filter: no valid codes in '{Text}', keeping current filter", text);
        }
        else
        {
            CardService.SelectedSetFilter = validCodes;
            _logger.LogInformation("Set filter changed to: {Codes}", string.Join(", ", validCodes));
        }
    }

    [RelayCommand]
    public void ClearSetFilter()
    {
        SetFilterText = "";
    }

    [RelayCommand]
    public void OpenSetFilterBuilder()
    {
        var currentFilter = CardService.SelectedSetFilter;
        var result = DialogService.OpenSetFilterBuilder(_allSets, currentFilter);
        if (result is not null)
            SetFilterText = string.Join(", ", result);
    }

    // Scanner tab — multi-select support
    [ObservableProperty]
    public partial ScannedCard? SelectedScannedCard { get; set; }

    public List<ScannedCard> SelectedScannedCards { get; private set; } = [];

    public bool HasSelection => SelectedScannedCards.Count > 0;
    public bool HasSingleSelection => SelectedScannedCards.Count == 1;
    public bool HasMultiSelection => SelectedScannedCards.Count > 1;
    public int SelectionCount => SelectedScannedCards.Count;
    public string AssignButtonText => HasMultiSelection ? $"Assign to {SelectionCount} Cards" : "Assign to Selected Card";

    // Shared detail properties — show value only when all selected cards agree
    public string? SharedMatchName => GetSharedValue(s => s.Match?.Name);
    public string? SharedSetName => GetSharedValue(s => s.Match?.SetName);
    public string? SharedSetCode => GetSharedValue(s => s.Match?.SetCode);
    public string? SharedCollectorNumber => GetSharedValue(s => s.Match?.CollectorNumber);
    public string? SharedRarity => GetSharedValue(s => s.Match?.Rarity);
    public string? SharedCondition => GetSharedValue(s => s.Condition);
    public bool? SharedIsFoil => SelectedScannedCards.Count > 0 && SelectedScannedCards.All(s => s.IsFoil == SelectedScannedCards[0].IsFoil) ? SelectedScannedCards[0].IsFoil : null;
    public bool AllMatched => SelectedScannedCards.Count > 0 && SelectedScannedCards.All(s => s.Match is not null);
    public bool AnyUnmatched => SelectedScannedCards.Any(s => s.Match is null);
    public object? SharedMatchSource => HasSingleSelection ? SelectedScannedCards[0].Match?.Source : null;

    private string? GetSharedValue(Func<ScannedCard, string?> selector)
    {
        if (SelectedScannedCards.Count == 0) return null;
        var first = selector(SelectedScannedCards[0]);
        return SelectedScannedCards.All(s => selector(s) == first) ? first : null;
    }

    public bool ShowOverrideBinderFields => HasSingleSelection && SelectedScannedCard?.OverrideContainer?.ContainerType == ContainerType.Binder;
    public bool ShowOverrideBoxFields => HasSingleSelection && SelectedScannedCard?.OverrideContainer?.ContainerType == ContainerType.Box;

    public void UpdateSelection(IList<ScannedCard> selected)
    {
        // Unsubscribe from old cards
        foreach (var card in SelectedScannedCards)
            card.PropertyChanged -= OnScannedCardPropertyChanged;

        SelectedScannedCards = selected.ToList();
        SelectedScannedCard = SelectedScannedCards.Count == 1 ? SelectedScannedCards[0] : null;

        // Subscribe to new cards
        foreach (var card in SelectedScannedCards)
            card.PropertyChanged += OnScannedCardPropertyChanged;

        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(AssignButtonText));
        OnPropertyChanged(nameof(SharedMatchName));
        OnPropertyChanged(nameof(SharedSetName));
        OnPropertyChanged(nameof(SharedSetCode));
        OnPropertyChanged(nameof(SharedCollectorNumber));
        OnPropertyChanged(nameof(SharedRarity));
        OnPropertyChanged(nameof(SharedCondition));
        OnPropertyChanged(nameof(SharedIsFoil));
        OnPropertyChanged(nameof(AllMatched));
        OnPropertyChanged(nameof(AnyUnmatched));
        OnPropertyChanged(nameof(SharedMatchSource));
        OnPropertyChanged(nameof(ShowOverrideBinderFields));
        OnPropertyChanged(nameof(ShowOverrideBoxFields));
        OnPropertyChanged(nameof(ShowPrintingSelector));
        ConfirmMatchCommand.NotifyCanExecuteChanged();
        ClearMatchCommand.NotifyCanExecuteChanged();
        RefreshAvailablePrintings();

        if (DialogService.IsCardPreviewOpen)
            DialogService.UpdateCardPreview(SelectedScannedCard);
    }

    private void RefreshAvailablePrintings()
    {
        AvailablePrintings.Clear();
        _selectedPrinting = null;

        var sharedName = SharedMatchName;
        if (!HasSelection || !AllMatched || sharedName is null)
        {
            OnPropertyChanged(nameof(SelectedPrinting));
            return;
        }

        // Use the first selected card's match as the reference printing
        var refMatch = SelectedScannedCards[0].Match!;
        try
        {
            var printings = CardService.ActiveGameService.GetPrintings(sharedName);
            foreach (var p in printings)
                AvailablePrintings.Add(p);

            // Pre-select only when all cards share the same printing
            var sharedId = GetSharedValue(s => s.Match?.GameSpecificId);
            _selectedPrinting = sharedId is not null
                ? AvailablePrintings.FirstOrDefault(p => p.GameSpecificId == sharedId)
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load printings for {CardName}", sharedName);
        }

        OnPropertyChanged(nameof(SelectedPrinting));
    }

    private void OnScannedCardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScannedCard.OverrideContainer) or nameof(ScannedCard.Match)
            or nameof(ScannedCard.Condition) or nameof(ScannedCard.IsFoil))
        {
            NotifySelectionChanged();
        }

        if (e.PropertyName is nameof(ScannedCard.Match))
        {
            OnPropertyChanged(nameof(HasMatchedScans));
            ConfirmMatchCommand.NotifyCanExecuteChanged();
            ClearMatchCommand.NotifyCanExecuteChanged();
        }
    }

    // Scan filter/sort state
    [ObservableProperty]
    public partial ScanFilterMode ScanFilterMode { get; set; }

    [ObservableProperty]
    public partial ScanSortField ScanSortField { get; set; }

    [ObservableProperty]
    public partial bool ScanSortAscending { get; set; } = true;

    partial void OnScanFilterModeChanged(ScanFilterMode value) => ApplyScanView();
    partial void OnScanSortFieldChanged(ScanSortField value) => ApplyScanView();
    partial void OnScanSortAscendingChanged(bool value) => ApplyScanView();

    [RelayCommand]
    public void SetScanFilter(ScanFilterMode mode)
    {
        ScanFilterMode = ScanFilterMode == mode ? ScanFilterMode.None : mode;
    }

    private ICollectionView? _scanView;
    public ICollectionView ScanView
    {
        get
        {
            if (_scanView is null)
            {
                _scanView = CollectionViewSource.GetDefaultView(CardService.ScannedCards);
                _scanView.Filter = ScanFilter;
            }
            return _scanView;
        }
    }

    private bool ScanFilter(object obj)
    {
        if (obj is not ScannedCard card) return false;

        return ScanFilterMode switch
        {
            ScanFilterMode.HighConfidence => card.Match?.Confidence is >= 80,
            ScanFilterMode.LowConfidence => card.Match?.Confidence is not null and < 80,
            ScanFilterMode.Flagged => card.FlagReason != FlagReason.None,
            _ => true
        };
    }

    public void ApplyScanView()
    {
        ScanView.Filter = ScanFilter;

        ScanView.SortDescriptions.Clear();
        if (ScanSortField != ScanSortField.None)
        {
            var direction = ScanSortAscending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;

            var propertyPath = ScanSortField switch
            {
                ScanSortField.Confidence => "Match.Confidence",
                ScanSortField.Name => "Match.Name",
                ScanSortField.Set => "Match.SetCode",
                _ => ""
            };

            if (propertyPath.Length > 0)
                ScanView.SortDescriptions.Add(new SortDescription(propertyPath, direction));
        }

        ScanView.Refresh();
    }

    private bool _scanSessionResetDone;

    public void ResetScanFilterSort()
    {
        ScanFilterMode = ScanFilterMode.None;
        ScanSortField = ScanSortField.None;
        ScanSortAscending = true;
        _scanSessionResetDone = false;
    }

    public void OnScanCardAdded()
    {
        if (!_scanSessionResetDone && CardService.ScannedCards.Count == 1)
        {
            ResetScanFilterSort();
            _scanSessionResetDone = true;
        }
        RefreshScanStats();
        RefreshDiagnosticCount();
    }

    private static string SerializeMatchData(CardMatch? match)
    {
        if (match is null)
            return JsonSerializer.Serialize(new { unmatched = true });

        return JsonSerializer.Serialize(new
        {
            name = match.Name,
            setCode = match.SetCode,
            setName = match.SetName,
            collectorNumber = match.CollectorNumber,
            rarity = match.Rarity,
            gameSpecificId = match.GameSpecificId,
            confidence = match.Confidence,
        });
    }

    [RelayCommand]
    public void ToggleFlag(ScannedCard card)
    {
        if (card.FlagReason != FlagReason.None)
        {
            // Unflagging — record fix if no other fix was already recorded
            var previousReason = card.FlagReason;
            if (card.FlagFix is null)
            {
                card.FlagFix = new ScanFlagFix
                {
                    FixType = "ManualUnflag",
                    OriginalData = JsonSerializer.Serialize(new { flagReason = card.FlagReason.ToString() }),
                    ResolvedData = JsonSerializer.Serialize(new { flagReason = FlagReason.None.ToString() }),
                    OriginalFlagReason = card.FlagReason,
                };
            }
            card.FlagReason = FlagReason.None;
            try { _diagnosticService.LogUserUnflagged(card.Hash, card, card.FlagFix?.OriginalFlagReason ?? previousReason); } catch { }
        }
        else
        {
            card.FlagReason = FlagReason.Manual;
            try { _diagnosticService.LogUserFlagged(card.Hash, card); } catch { }
        }
        ApplyScanView();
    }

    [RelayCommand(CanExecute = nameof(CanClearMatch))]
    public void ClearMatch(ScannedCard card)
    {
        if (card.Match is null) return;

        var originalFlagReason = card.FlagReason;

        card.FlagFix = new ScanFlagFix
        {
            FixType = "ClearMatch",
            OriginalFlagReason = originalFlagReason,
            OriginalData = SerializeMatchData(card.Match),
            ResolvedData = "",
        };

        card.Match = null;
        card.FlagReason = FlagReason.MissingFromDatabase;

        try { _diagnosticService.LogUserFlagged(card.Hash, card); } catch { }

        ApplyScanView();
        Message = "Match cleared — marked as missing from database.";
    }

    public bool CanClearMatch(ScannedCard card) => card?.Match is not null;

    // Scan statistics
    [ObservableProperty]
    public partial int ScanCount { get; set; }

    [ObservableProperty]
    public partial double ScanAvgConfidence { get; set; }

    [ObservableProperty]
    public partial int ScanHighConfidenceCount { get; set; }

    [ObservableProperty]
    public partial int ScanLowConfidenceCount { get; set; }

    [ObservableProperty]
    public partial int ScanFlaggedCount { get; set; }

    public void RefreshScanStats()
    {
        var cards = CardService.ScannedCards;
        ScanCount = cards.Count;

        var withConfidence = cards.Where(c => c.Match?.Confidence is not null).ToList();
        ScanAvgConfidence = withConfidence.Count > 0
            ? withConfidence.Average(c => c.Match!.Confidence!.Value)
            : 0;

        ScanHighConfidenceCount = cards.Count(c => c.Match?.Confidence is >= 80);
        ScanLowConfidenceCount = cards.Count(c => c.Match?.Confidence is not null and < 80);
        ScanFlaggedCount = cards.Count(c => c.FlagReason != FlagReason.None);
        OnPropertyChanged(nameof(HasMatchedScans));
    }

    /// <summary>Callback for the view to programmatically select and scroll to a card.</summary>
    public Action<ScannedCard>? RequestScrollToCard { get; set; }

    [RelayCommand]
    public void NavigateToNextFlag()
    {
        var cards = CardService.ScannedCards;
        if (cards.Count == 0) return;

        var startIndex = SelectedScannedCard is not null
            ? cards.IndexOf(SelectedScannedCard) + 1
            : 0;

        for (var i = 0; i < cards.Count; i++)
        {
            var idx = (startIndex + i) % cards.Count;
            if (cards[idx].IsFlagged)
            {
                SelectedScannedCard = cards[idx];
                RequestScrollToCard?.Invoke(cards[idx]);
                return;
            }
        }
    }

    [RelayCommand]
    public void NavigateToPreviousFlag()
    {
        var cards = CardService.ScannedCards;
        if (cards.Count == 0) return;

        var startIndex = SelectedScannedCard is not null
            ? cards.IndexOf(SelectedScannedCard) - 1
            : cards.Count - 1;

        for (var i = 0; i < cards.Count; i++)
        {
            var idx = (startIndex - i + cards.Count) % cards.Count;
            if (cards[idx].IsFlagged)
            {
                SelectedScannedCard = cards[idx];
                RequestScrollToCard?.Invoke(cards[idx]);
                return;
            }
        }
    }

    [RelayCommand]
    public async Task RotateLeft()
    {
        if (SelectedScannedCard is null) return;
        await RotateScan(SelectedScannedCard, System.Drawing.RotateFlipType.Rotate270FlipNone);
    }

    [RelayCommand]
    public async Task RotateRight()
    {
        if (SelectedScannedCard is null) return;
        await RotateScan(SelectedScannedCard, System.Drawing.RotateFlipType.Rotate90FlipNone);
    }

    private async Task RotateScan(ScannedCard scan, System.Drawing.RotateFlipType rotation)
    {
        try
        {
            // Read, rotate, save
            var imageBytes = File.ReadAllBytes(scan.TempImagePath);
            using var bmp = new System.Drawing.Bitmap(new MemoryStream(imageBytes));
            bmp.RotateFlip(rotation);

            using var rotatedStream = new MemoryStream();
            bmp.Save(rotatedStream, System.Drawing.Imaging.ImageFormat.Png);
            var rotatedBytes = rotatedStream.ToArray();

            File.WriteAllBytes(scan.TempImagePath, rotatedBytes);

            // Recompute hash
            rotatedStream.Position = 0;
            var newHash = CardService.ComputeHashFromStream(rotatedStream);
            scan.Hash = newHash;

            // Re-run matching
            OcrMatchResult? ocrResult = null;
            if (scan.Game == CardGame.OnePiece)
            {
                var (cn, conf) = await CardService.OcrService.DetectOptcgCollectorNumberAsync(rotatedBytes);
                if (cn is not null && conf >= 0.5)
                    ocrResult = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = conf };
            }

            var (match, matchedGame) = CardService.FindBestMatch(newHash, null, ocrResult, CardService.SelectedSetFilter, null);
            scan.Match = match;
            scan.Game = matchedGame;

            if (match is not null && scan.FlagReason is FlagReason.NoMatch or FlagReason.VeryLowConfidence or FlagReason.MissingFromDatabase)
                scan.FlagReason = FlagReason.None;

            // Force image refresh by toggling path
            var path = scan.TempImagePath;
            scan.TempImagePath = "";
            scan.TempImagePath = path;

            _logger.LogInformation("Manually rotated scan {Path}, new hash {Hash:X16}, match: {Match}",
                path, newHash, match?.Name ?? "(none)");

            RefreshScanStats();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual rotation failed");
        }
    }

    [ObservableProperty]
    public partial string ManualSearchQuery { get; set; } = "";

    public ObservableCollection<CardMatch> ManualSearchResults { get; } = [];

    [ObservableProperty]
    public partial CardMatch? SelectedManualSearchResult { get; set; }

    // Printing selector
    public ObservableCollection<CardMatch> AvailablePrintings { get; } = [];

    private CardMatch? _selectedPrinting;
    public CardMatch? SelectedPrinting
    {
        get => _selectedPrinting;
        set
        {
            if (_selectedPrinting == value) return;
            var oldPrinting = _selectedPrinting;
            _selectedPrinting = value;
            OnPropertyChanged(nameof(SelectedPrinting));

            // Swap match on all selected cards when user picks a different printing
            if (value is not null && oldPrinting is not null
                && value.GameSpecificId != oldPrinting.GameSpecificId
                && SelectedScannedCards.Count > 0)
            {
                foreach (var card in SelectedScannedCards)
                {
                    if (card.Match is null) continue;
                    LogMismatchIfHighConfidence(card, card.Match, value);

                    // Record fix for flagged cards
                    if (card.IsFlagged)
                    {
                        card.FlagFix = new ScanFlagFix
                        {
                            FixType = "PrintingChange",
                            OriginalFlagReason = card.FlagReason,
                            OriginalData = SerializeMatchData(card.Match),
                            ResolvedData = SerializeMatchData(value),
                        };
                        card.FlagReason = FlagReason.None;
                    }

                    try { _diagnosticService.LogUserCorrected(card.Hash, card, value); } catch { }
                    card.Match = value;

                    try
                    {
                        var bestArtHash = card.ArtHashes?.FirstOrDefault(h => h != 0);
                        CardService.ActiveGameService.RecordCorrection(card.Hash, value.GameSpecificId, bestArtHash is > 0 ? bestArtHash : null);
                        _logger.LogInformation("Printing change correction: {Hash:X16} -> {CardId} ({SetCode})", card.Hash, value.GameSpecificId, value.SetCode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to record printing change correction for {Hash:X16}", card.Hash);
                    }
                }
            }
        }
    }

    public bool ShowPrintingSelector => HasSelection && AllMatched && SharedMatchName is not null;

    // Home tab — collection stats + set completion
    public ObservableCollection<SetCompletionSummary> SetCompletionResults { get; } = [];

    // Collection summary stats
    [ObservableProperty]
    public partial int StatTotalCards { get; set; }

    [ObservableProperty]
    public partial int StatTotalSets { get; set; }

    [ObservableProperty]
    public partial int StatFoilCount { get; set; }

    [ObservableProperty]
    public partial int StatCommonCount { get; set; }

    [ObservableProperty]
    public partial int StatUncommonCount { get; set; }

    [ObservableProperty]
    public partial int StatRareCount { get; set; }

    [ObservableProperty]
    public partial int StatMythicCount { get; set; }

    [ObservableProperty]
    public partial int StatOtherRarityCount { get; set; }

    [ObservableProperty]
    public partial decimal StatTotalValue { get; set; }

    [ObservableProperty]
    public partial SetCompletionSummary? SelectedSetCompletion { get; set; }

    partial void OnSelectedSetCompletionChanged(SetCompletionSummary? value)
    {
        if (value is not null)
            _ = ExpandSetCompletionCommand.ExecuteAsync(value);
    }

    [ObservableProperty]
    public partial bool IsCalculatingCompletion { get; set; }

    [ObservableProperty]
    public partial string CompletionStatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>When true the next Home-tab activation will recalculate stats.</summary>
    private bool _homeTabDirty = true;

    /// <summary>Mark the home tab as needing a refresh. If the tab is currently
    /// visible the refresh fires immediately; otherwise it fires on next activation.</summary>
    private void InvalidateHomeTab()
    {
        _homeTabDirty = true;
        if (SelectedTabIndex == 0)
            _ = CalculateSetCompletionCommand.ExecuteAsync(null);
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 0 && _homeTabDirty) // Home tab — only reload when dirty
            _ = CalculateSetCompletionCommand.ExecuteAsync(null);
        else if (value == 1 && !Collection.ShowCardList) // Collection tab - refresh overview
            Collection.LoadOverview();
    }

    public bool HasMatchedScans =>
        CardService.ScannedCards.Count > 0 &&
        CardService.ScannedCards.All(c => c.Match is not null || c.FlagReason == FlagReason.MissingFromDatabase);

    public void Initialize()
    {
        SelectedGame = CardService.SelectedGame;
        ScannerService.ScanQuality = ScanQuality;

        // Wire Collection delegates before Initialize so PersistSettings works
        Collection.PersistSettings = PersistDisplaySettings;
        Collection.ReportMessage = msg => Message = msg;
        Collection.CollectionChanged = InvalidateHomeTab;

        // Wire Sealed delegates
        Sealed.ReportMessage = msg => Message = msg;
        Sealed.LoadInstances();

        // Keep HasMatchedScans in sync with the ScannedCards collection
        if (_scannedCardsHandler is not null)
            CardService.ScannedCards.CollectionChanged -= _scannedCardsHandler;
        _scannedCardsHandler = (_, _) => OnPropertyChanged(nameof(HasMatchedScans));
        CardService.ScannedCards.CollectionChanged += _scannedCardsHandler;

        LoadAvailableSets();
        LoadContainers(); // also calls Collection.LoadContainers()
        Collection.SetGame(SelectedGame);
        Collection.Initialize();

        IsEbayConnected = ebayAuthService.IsConnected;
        InvalidateHomeTab();
        RefreshDiagnosticCount();

        // Start eBay listing sync timer (every 5 minutes)
        if (ebayAuthService.IsConnected)
        {
            _ebaySyncTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5),
            };
            _ebaySyncTimer.Tick += async (_, _) => await SyncEbayListings();
            _ebaySyncTimer.Start();

            // Initial sync
            _ = SyncEbayListings();
        }
    }

    private IEbaySyncService EbaySyncService => App.Host.Services.GetRequiredService<IEbaySyncService>();

    private async Task SyncEbayListings()
    {
        try
        {
            var synced = await EbaySyncService.SyncAllActiveAsync();
            if (synced > 0)
            {
                Message = $"eBay sync: {synced} listing(s) updated.";
                _ = Collection.SearchCollection();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "eBay sync failed");
        }
    }

    [RelayCommand]
    public async Task Scan()
    {
        if (IsAuditComplete) return;
        if (ConnectToScanner(false) ?? false)
        {
            _logger.LogInformation("User initiated scan");
            await ScannerService.ScanAsync(ShowScannerUI);
            if (ScannerService.LastScanError is not null)
            {
                Message = ScannerService.LastScanError;
                _logger.LogWarning("Scan failed: {Error}", ScannerService.LastScanError);
            }
        }
    }

    [RelayCommand]
    public void ImportFromFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Folder with Scanned Card Images"
        };
        if (dlg.ShowDialog() != true) return;

        var folder = dlg.FolderName;
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif"
        };

        var imageFiles = Directory.GetFiles(folder)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f)
            .ToList();

        if (imageFiles.Count == 0)
        {
            Message = "No image files found in the selected folder.";
            return;
        }

        var tempDir = ScanImageCache.Instance?.TempScansDirectory ?? string.Empty;
        if (string.IsNullOrEmpty(tempDir))
        {
            Message = "Scan cache not initialized.";
            return;
        }
        Directory.CreateDirectory(tempDir);

        _logger.LogInformation("Importing {Count} image(s) from {Folder}", imageFiles.Count, folder);
        CardService.StartNewDiagnosticSession();

        for (int i = 0; i < imageFiles.Count; i++)
        {
            var sourceFile = imageFiles[i];
            Message = $"Importing {i + 1}/{imageFiles.Count}...";

            try
            {
                var destFile = Path.Combine(tempDir, $"{Guid.NewGuid()}{Path.GetExtension(sourceFile)}");
                File.Copy(sourceFile, destFile);

                // Verify copy
                var sourceInfo = new FileInfo(sourceFile);
                var destInfo = new FileInfo(destFile);
                if (destInfo.Exists && destInfo.Length == sourceInfo.Length)
                {
                    File.Delete(sourceFile);
                    _logger.LogDebug("Copied and deleted source: {Source} -> {Dest}", sourceFile, destFile);
                }
                else
                {
                    _logger.LogWarning("Copy verification failed for {Source}, keeping original", sourceFile);
                }

                using var stream = File.OpenRead(destFile);
                CardService.AddFromStream(stream);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import {File}", sourceFile);
            }
        }

        Message = $"Imported {imageFiles.Count} image(s) from folder.";
        _logger.LogInformation("Folder import complete: {Count} images", imageFiles.Count);
    }

    [RelayCommand]
    public void ConnectToScanner() => ConnectToScanner(true);

    [RelayCommand]
    public async Task RefreshCardData()
    {
        _logger.LogInformation("User initiated card data refresh for {Game}", SelectedGame);

        if (RefreshCooldownHelper.IsCooldownActive(dataPathService.DataDirectory, SelectedGame, out var nextAvailable))
        {
            var lastRefresh = RefreshCooldownHelper.GetLastRefresh(dataPathService.DataDirectory, SelectedGame);
            var timeAgo = DateTime.UtcNow - lastRefresh.GetValueOrDefault(DateTime.UtcNow);
            var timeAgoText = timeAgo.TotalHours >= 1
                ? $"{(int)timeAgo.TotalHours}h {timeAgo.Minutes}m ago"
                : timeAgo.Minutes < 1
                    ? "less than a minute ago"
                    : $"{timeAgo.Minutes}m ago";

            _logger.LogInformation("Refresh cooldown active for {Game}, last refresh {TimeAgo}", SelectedGame, timeAgoText);

            var result = MessageBox.Show(
                $"Card data for {SelectedGame} was last refreshed {timeAgoText}.\n\n" +
                $"Refresh is available once every 24 hours to minimize API load.\n" +
                $"Next refresh available at {nextAvailable.ToLocalTime():g}.\n\n" +
                "Click Yes to refresh anyway, or No to cancel.",
                "Refresh Cooldown",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
                return;

            _logger.LogInformation("User forced refresh for {Game} despite cooldown", SelectedGame);
        }

        var progress = new Progress<string>((str) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Message = str;
            });
        });

        await CardService.ActiveGameService.DownloadBulkDataAsync(progress);
        RefreshCooldownHelper.RecordRefresh(dataPathService.DataDirectory, SelectedGame);
        LoadAvailableSets();

        if (SelectedGame == CardGame.Mtg)
        {
            var sets = _allSets.Select(s => (s.SetCode, s.SetName)).ToList();
            await setSymbolCache.PreloadSymbolsAsync(sets, progress);
        }

        InvalidateHomeTab();
    }

    [RelayCommand]
    public async Task ComputeImageHashes()
    {
        var result = MessageBox.Show(
            "This will re-download all card art and recompute every image hash from scratch.\n\n" +
            "This can take a long time (potentially hours for large databases) and will use " +
            "significant disk space to store card art.\n\n" +
            "Continue?",
            "Compute Image Hashes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        _logger.LogInformation("User initiated image hash computation for {Game}", SelectedGame);
        var progress = new Progress<string>((str) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Message = str;
            });
        });

        await CardService.ActiveGameService.ComputeImageHashesAsync(forceAll: true, progress);
        LoadAvailableSets();
        InvalidateHomeTab();
    }

    [RelayCommand]
    public async Task ComputeMissingHashes()
    {
        _logger.LogInformation("User initiated missing hash computation for {Game}", SelectedGame);
        var progress = new Progress<string>((str) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Message = str;
            });
        });

        await CardService.ActiveGameService.ComputeImageHashesAsync(forceAll: false, progress);
        LoadAvailableSets();
        InvalidateHomeTab();
    }

    // Matches SET-NUM patterns like TMT-002, OP15-041, BRC-85a.
    // Pattern is single-sourced in PasteClassifier.CodePattern.
    [GeneratedRegex(PasteClassifier.CodePattern)]
    private static partial Regex SetCollectorNumberRegex();

    [RelayCommand]
    public void ManualSearch()
    {
        _logger.LogDebug("Manual search: {Query}", ManualSearchQuery);
        ManualSearchResults.Clear();

        var query = ManualSearchQuery.Trim();
        var setFilter = CardService.SelectedSetFilter;

        // Detect SET-NUM pattern (e.g. TMT-002, OP15-041) and rewrite to filter syntax
        var setNumMatch = SetCollectorNumberRegex().Match(query);
        if (setNumMatch.Success)
        {
            var set = setNumMatch.Groups[1].Value;
            var num = setNumMatch.Groups[2].Value;

            query = SelectedGame == CardGame.OnePiece
                ? $"cn:{set}-{num}"       // OPTCG CardSetId is the full code
                : $"set:{set} cn:{num}";  // MTG uses separate set + collector number
        }

        // Single set: use the set: prefix for efficient DB-level filtering
        if (setFilter is { Count: 1 } && !setNumMatch.Success)
            query = $"set:{setFilter.First()} {query}";

        var results = CardService.ActiveGameService.SearchCards(query, maxResults: int.MaxValue);

        // Multiple sets: post-filter since SearchCards doesn't support multi-set queries
        if (setFilter is { Count: > 1 } && !setNumMatch.Success)
            results = results.Where(m => setFilter.Contains(m.SetCode)).ToList();

        foreach (var match in results)
            ManualSearchResults.Add(match);
    }

    [RelayCommand]
    public void AssignMatch()
    {
        if (SelectedScannedCards.Count == 0 || SelectedManualSearchResult is null)
            return;

        var newMatch = SelectedManualSearchResult;
        _logger.LogInformation("Manually assigned match \"{CardName}\" to {Count} scanned card(s)", newMatch.Name, SelectedScannedCards.Count);

        foreach (var card in SelectedScannedCards)
        {
            var oldMatch = card.Match;

            // Record fix for flagged cards
            if (card.IsFlagged)
            {
                card.FlagFix = new ScanFlagFix
                {
                    FixType = "CardReassign",
                    OriginalFlagReason = card.FlagReason,
                    OriginalData = SerializeMatchData(card.Match),
                    ResolvedData = SerializeMatchData(newMatch),
                };
                card.FlagReason = FlagReason.None;
            }

            try { _diagnosticService.LogUserCorrected(card.Hash, card, newMatch); } catch { }
            card.Match = newMatch;

            // Record correction for each unique hash
            if (oldMatch?.GameSpecificId != newMatch.GameSpecificId)
            {
                if (oldMatch is not null)
                    LogMismatchIfHighConfidence(card, oldMatch, newMatch);

                try
                {
                    var bestArtHash = card.ArtHashes?.FirstOrDefault(h => h != 0);
                    CardService.ActiveGameService.RecordCorrection(card.Hash, newMatch.GameSpecificId, bestArtHash is > 0 ? bestArtHash : null);
                    _logger.LogInformation("Recorded hash correction: {Hash:X16} → {CardId}", card.Hash, newMatch.GameSpecificId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to record hash correction for {Hash:X16}", card.Hash);
                }
            }
        }

        ManualSearchResults.Clear();
        ManualSearchQuery = "";
        SelectedManualSearchResult = null;
    }

    /// <summary>
    /// Ctrl+V in the scanned queue: assign a card to the selected scanned card(s) from
    /// clipboard text. A collector-number code is looked up and assigned directly; any
    /// other text prefills and focuses the manual search box to pick a printing.
    /// </summary>
    public void PasteAssign(string? clipboardText)
    {
        var kind = PasteClassifier.Classify(clipboardText);
        if (kind == PasteClassifier.PasteKind.Empty)
            return;

        if (SelectedScannedCards.Count == 0)
        {
            Message = "Select one or more cards first.";
            return;
        }

        var text = clipboardText!.Trim();
        ManualSearchQuery = text;
        ManualSearch();

        if (PasteClassifier.ShouldAssignDirectly(kind, ManualSearchResults.Count))
        {
            var result = ManualSearchResults[0];

            // A code is a printed number shared across alternate-art printings. If more than
            // one printing shares it, don't guess which — let the user pick from search.
            var printingCount = CardService.ActiveGameService
                .GetPrintings(result.Name)
                .Count(p => p.CollectorNumber == result.CollectorNumber);

            if (printingCount <= 1)
            {
                SelectedManualSearchResult = result;
                var name = result.Name;
                var count = SelectedScannedCards.Count;
                AssignMatch(); // assigns to all selected, records corrections, clears search
                Message = $"Assigned {name} to {count} card(s).";
                return;
            }

            FocusManualSearchBox();
            Message = $"{printingCount} printings of {result.Name} ({result.CollectorNumber}) — pick one.";
            return;
        }

        // Name paste, or a code with no matches → let the user pick / refine.
        FocusManualSearchBox();
        Message = kind == PasteClassifier.PasteKind.Code
            ? $"No match for {text}."
            : $"Search results for \"{text}\" — pick a printing.";
    }

    [RelayCommand]
    public void ConfirmMatch(ScannedCard card)
    {
        if (card.Match is null) return;

        var match = card.Match;
        try
        {
            var bestArtHash = card.ArtHashes?.FirstOrDefault(h => h != 0);
            CardService.ActiveGameService.RecordCorrection(card.Hash, match.GameSpecificId, bestArtHash is > 0 ? bestArtHash : null);
            _logger.LogInformation("Confirmed match: {Hash:X16} → {CardId} ({CardName})", card.Hash, match.GameSpecificId, match.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record match confirmation for {Hash:X16}", card.Hash);
        }

        // Record fix for flagged cards
        if (card.IsFlagged)
        {
            card.FlagFix = new ScanFlagFix
            {
                FixType = "MatchConfirmed",
                OriginalFlagReason = card.FlagReason,
                OriginalData = SerializeMatchData(match),
                ResolvedData = SerializeMatchData(new CardMatch
                {
                    Name = match.Name,
                    SetCode = match.SetCode,
                    SetName = match.SetName,
                    CollectorNumber = match.CollectorNumber,
                    Rarity = match.Rarity,
                    GameSpecificId = match.GameSpecificId,
                    Confidence = 100,
                }),
            };
            card.FlagReason = FlagReason.None;
        }

        try { _diagnosticService.LogUserConfirmed(card.Hash, card); } catch { }

        // Replace Match with 100% confidence so the UI updates
        card.Match = new CardMatch
        {
            Name = match.Name,
            SetCode = match.SetCode,
            SetName = match.SetName,
            CollectorNumber = match.CollectorNumber,
            Rarity = match.Rarity,
            ImageUri = match.ImageUri,
            GameSpecificId = match.GameSpecificId,
            LocalImagePath = match.LocalImagePath,
            Confidence = 100,
            Source = match.Source
        };

        Message = $"Confirmed match for \"{match.Name}\".";
    }

    [RelayCommand]
    public void ToggleAuditComplete()
    {
        if (!IsAuditComplete)
        {
            // --- Perform audit ---
            var cards = CardService.ScannedCards
                .Where(c => c.Match is not null)
                .ToList();

            if (cards.Count == 0) return;

            _preAuditSnapshots = new Dictionary<ScannedCard, AuditSnapshot>(cards.Count);

            foreach (var card in cards)
            {
                _preAuditSnapshots[card] = new AuditSnapshot(card.Match!.Confidence, card.FlagReason, card.FlagFix);
                ConfirmMatch(card);
            }

            IsAuditComplete = true;
            Message = $"Audit complete — confirmed {cards.Count} cards.";
            _logger.LogInformation("Audit complete: confirmed {Count} cards", cards.Count);
        }
        else
        {
            // --- Undo audit ---
            if (_preAuditSnapshots is not null)
            {
                foreach (var (card, snapshot) in _preAuditSnapshots)
                {
                    if (card.Match is null) continue;
                    var match = card.Match;
                    card.Match = new CardMatch
                    {
                        Name = match.Name,
                        SetCode = match.SetCode,
                        SetName = match.SetName,
                        CollectorNumber = match.CollectorNumber,
                        Rarity = match.Rarity,
                        ImageUri = match.ImageUri,
                        GameSpecificId = match.GameSpecificId,
                        LocalImagePath = match.LocalImagePath,
                        Confidence = snapshot.Confidence,
                        Source = match.Source
                    };
                    card.FlagReason = snapshot.FlagReason;
                    card.FlagFix = snapshot.FlagFix;
                }

                var count = _preAuditSnapshots.Count;
                _preAuditSnapshots.Clear();
                _preAuditSnapshots = null;
                Message = $"Audit undone — reverted {count} cards.";
                _logger.LogInformation("Audit undone: reverted {Count} cards", count);
            }

            IsAuditComplete = false;
        }
    }

    private void LogMismatchIfHighConfidence(ScannedCard card, CardMatch oldMatch, CardMatch newMatch)
    {
        _ = LogMismatchIfHighConfidenceAsync(card, oldMatch, newMatch);
    }

    private async Task LogMismatchIfHighConfidenceAsync(ScannedCard card, CardMatch oldMatch, CardMatch newMatch)
    {
        try
        {
            await mismatchLogService.LogMismatchAsync(oldMatch, newMatch, card);
            if (oldMatch.Confidence is >= 80 && oldMatch.GameSpecificId != newMatch.GameSpecificId)
            {
                _logger.LogInformation("Logged high-confidence mismatch: {OldName} ({OldSet}) -> {NewName} ({NewSet}) at {Confidence:F0}%",
                    oldMatch.Name, oldMatch.SetCode, newMatch.Name, newMatch.SetCode, oldMatch.Confidence);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log mismatch for hash {Hash:X16}", card.Hash);
        }
    }

    [ObservableProperty]
    public partial bool IsCommitting { get; set; }

    [ObservableProperty]
    public partial bool IsAuditComplete { get; set; }

    private record struct AuditSnapshot(double? Confidence, FlagReason FlagReason, ScanFlagFix? FlagFix);
    private Dictionary<ScannedCard, AuditSnapshot>? _preAuditSnapshots;

    // --- Audit Mode ---

    [ObservableProperty]
    public partial bool IsAuditMode { get; set; }

    public string AuditLocationName => auditService.AuditLocationName ?? "";

    [RelayCommand]
    public void StartAudit(int containerId)
    {
        if (IsAuditMode) return;

        // Clear any existing scans
        CardService.ScannedCards.Clear();

        auditService.StartAudit(containerId);
        IsAuditMode = true;
        OnPropertyChanged(nameof(AuditLocationName));

        // Switch to scanner tab (index 2)
        SelectedTabIndex = 2;
        _logger.LogInformation("Audit mode started for location {Id}", containerId);
    }

    [RelayCommand]
    public void EndAudit()
    {
        if (!IsAuditMode) return;

        CardService.ScannedCards.Clear();
        auditService.EndAudit();
        IsAuditMode = false;
        OnPropertyChanged(nameof(AuditLocationName));
        _logger.LogInformation("Audit mode ended");
    }

    [RelayCommand]
    public void GenerateAuditReport()
    {
        if (!IsAuditMode) return;

        var report = auditService.GenerateReport(CardService.ScannedCards);
        dialogService.ShowAuditReport(report);
    }

    [RelayCommand]
    public async Task CommitScans()
    {
        if (IsAuditMode) return; // Cannot commit in audit mode

        var count = CardService.ScannedCards.Count;
        if (count == 0) return;

        _logger.LogInformation("Committing {Count} scanned cards to collection", count);
        IsCommitting = true;
        Message = $"Committing {count} cards...";

        // Snapshot the data we need before clearing the UI
        var scans = CardService.ScannedCards.ToList();
        var container = ActiveContainer;
        var page = ActivePage;
        var slot = ActiveSlot;
        var section = ActiveSection;

        try
        {
            ResetScanFilterSort();
            var progress = new Progress<string>(msg =>
                Application.Current.Dispatcher.Invoke(() => Message = msg));

            await Task.Run(() =>
            {
                CardService.CommitScans(scans, container, page, slot, section, progress);
            });

            // Auto-increment slot for binders
            if (container?.ContainerType == ContainerType.Binder && ActiveSlot.HasValue)
                ActiveSlot = ActiveSlot.Value + count;

            // Release memory
            SelectedScannedCards = [];
            SelectedScannedCard = null;
            NotifySelectionChanged();
            CardService.ScannedCards.Clear();

            // Clear audit state
            _preAuditSnapshots?.Clear();
            _preAuditSnapshots = null;
            IsAuditComplete = false;

            _ = Collection.SearchCollection();
            InvalidateHomeTab();
            Message = $"Committed {count} cards to collection.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to commit scans");
            Message = $"Error committing scans: {ex.Message}";
        }
        finally
        {
            IsCommitting = false;
        }
    }

    [RelayCommand]
    public void ManageStorageLocations()
    {
        dialogService.ManageStorageContainers();
        LoadContainers(); // Refresh after potential adds/renames/deletes
        if (!Collection.ShowCardList)
            Collection.LoadOverview();
    }

    [RelayCommand]
    public void ShowDataLocation() => dialogService.ShowDataLocation();

    [RelayCommand]
    public void CheckDecklist() => DialogService.ShowDecklistCheck();

    [RelayCommand]
    public void CardDoubleClick()
    {
        if (SelectedScannedCard is not null)
            DialogService.ShowCard(SelectedScannedCard);
    }

    [RelayCommand]
    public void ClearScans()
    {
        if (IsAuditComplete) return;
        _logger.LogInformation("Clearing {Count} scanned cards from queue", CardService.ScannedCards.Count);
        ResetScanFilterSort();
        SelectedScannedCards = [];
        SelectedScannedCard = null;
        NotifySelectionChanged();
        CardService.ClearTempFiles();
        CardService.ScannedCards.Clear();
        Message = "Scan queue cleared.";
    }

    [RelayCommand]
    public void ClearScanCache()
    {
        if (CardService.ScannedCards.Count > 0)
            ClearScans();

        var tempScansDir = ScanImageCache.Instance?.TempScansDirectory ?? string.Empty;

        var count = 0;
        if (Directory.Exists(tempScansDir))
        {
            var files = Directory.GetFiles(tempScansDir);
            count = files.Length;
            foreach (var file in files)
            {
                try { File.Delete(file); }
                catch { /* best effort */ }
            }
        }

        ScanImageCache.Instance?.Clear();
        Message = $"Cleared {count} cached scan image(s).";
        _logger.LogInformation("Manual scan cache clear: removed {Count} file(s)", count);
    }

    [RelayCommand]
    public void ClearDiagnosticLogs()
    {
        var result = System.Windows.MessageBox.Show(
            "This will permanently delete all Flag Resolution, Mismatch Log, and Diagnostic Event records.\n\nContinue?",
            "Clear Diagnostic Logs",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        var (flagCount, mismatchCount, diagnosticCount) = CardService.ClearDiagnosticLogs();
        Message = $"Cleared {flagCount} flag resolution(s), {mismatchCount} mismatch log(s), and {diagnosticCount} diagnostic event(s).";
        RefreshDiagnosticCount();
    }

    [RelayCommand]
    public async Task DeleteOrphanedScans()
    {
        var result = System.Windows.MessageBox.Show(
            "This will permanently delete scan image files that have no corresponding card in the collection.\n\nContinue?",
            "Delete Orphaned Scans",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        var progress = new Progress<string>(msg =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => Message = msg));

        var (deleted, errors) = await Task.Run(() => CardService.DeleteOrphanedScans(progress));
        Message = $"Deleted {deleted} orphaned scan(s)" + (errors > 0 ? $" ({errors} error(s))" : "") + ".";
    }

    [RelayCommand]
    public void RemoveScannedCard()
    {
        if (SelectedScannedCards.Count == 0) return;
        _logger.LogInformation("Removing {Count} scanned card(s) from queue", SelectedScannedCards.Count);
        var toRemove = SelectedScannedCards.ToList();
        foreach (var card in toRemove)
        {
            CardService.RemoveTempFile(card);
            CardService.ScannedCards.Remove(card);
        }
        SelectedScannedCards = [];
        SelectedScannedCard = null;
        NotifySelectionChanged();
    }

    // Focus commands

    [RelayCommand]
    public void FocusCollectionSearchBox() => Collection.FocusSearch?.Invoke();

    [RelayCommand]
    public void FocusManualSearchBox() => FocusManualSearch?.Invoke();

    /// <summary>Set by the View to select all items in the active tab.</summary>
    public Action? SelectAllInActiveTab { get; set; }

    [RelayCommand]
    public void SelectAll() => SelectAllInActiveTab?.Invoke();

    // Scanner context menu commands

    [RelayCommand]
    public void SetScannedCondition(string condition)
    {
        foreach (var card in SelectedScannedCards)
            card.Condition = condition;
        Message = $"Set condition to {condition} on {SelectedScannedCards.Count} card(s).";
    }

    [RelayCommand]
    public void SetScannedFoil(string isFoilStr)
    {
        var isFoil = isFoilStr == "True";
        foreach (var card in SelectedScannedCards)
            card.IsFoil = isFoil;
        Message = $"Set {(isFoil ? "Foil" : "Non-Foil")} on {SelectedScannedCards.Count} card(s).";
    }

    [RelayCommand]
    public void CopyScannedCardNames()
    {
        var names = string.Join(Environment.NewLine, SelectedScannedCards.Where(s => s.Match is not null).Select(s => s.Match!.Name));
        if (!string.IsNullOrEmpty(names))
            System.Windows.Clipboard.SetText(names);
    }

    // Export / Import commands

    [RelayCommand]
    public void ExportAllAppNative()
    {
        var cards = GetAllCollectionCards();
        if (ExportToFile("collection-" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv", out var path))
        {
            csvService.ExportAppNative(path, cards);
            Message = $"Exported {cards.Count} cards to {Path.GetFileName(path)}";
        }
    }

    [RelayCommand]
    public void ExportViewAppNative()
    {
        var cards = Collection.CollectionSearchResults.ToList();
        if (ExportToFile("collection-view-" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv", out var path))
        {
            csvService.ExportAppNative(path, cards);
            Message = $"Exported {cards.Count} cards to {Path.GetFileName(path)}";
        }
    }

    [RelayCommand]
    public void ExportAllTcgPlayer()
    {
        var cards = GetAllCollectionCards();
        if (ExportToFile("collection-tcgplayer.csv", out var path))
        {
            csvService.ExportTcgPlayer(path, cards);
            Message = $"Exported {cards.Count} cards to {Path.GetFileName(path)}";
        }
    }

    [RelayCommand]
    public void ExportAllMoxfield()
    {
        var cards = GetAllCollectionCards();
        if (ExportToFile("collection-moxfield.csv", out var path))
        {
            csvService.ExportMoxfield(path, cards);
            Message = $"Exported {cards.Count} cards to {Path.GetFileName(path)}";
        }
    }

    [RelayCommand]
    public void ExportAllManabox()
    {
        var cards = GetAllCollectionCards();
        if (ExportToFile("collection-manabox.csv", out var path))
        {
            csvService.ExportManabox(path, cards);
            Message = $"Exported {cards.Count} cards to {Path.GetFileName(path)}";
        }
    }

    public void ExportLocationManabox(int containerId, string containerName)
    {
        var results = new System.Collections.ObjectModel.ObservableCollection<CollectionCard>();
        CardService.SearchCollection("", null, containerId, results);
        var cards = results.ToList();

        if (cards.Count == 0)
        {
            Message = $"No cards in \"{containerName}\" to export.";
            return;
        }

        var safeName = string.Join("_", containerName.Split(System.IO.Path.GetInvalidFileNameChars()));
        if (ExportToFile($"{safeName}-manabox.csv", out var path))
        {
            csvService.ExportManabox(path, cards);
            Message = $"Exported {cards.Count} cards from \"{containerName}\" to {System.IO.Path.GetFileName(path)}";
        }
    }

    [RelayCommand]
    public void ExportScansManaboxCsv()
    {
        if (IsAuditMode) return;

        var scans = CardService.ScannedCards.ToList();
        if (scans.Count == 0) return;

        if (!ExportToFile("scans-manabox.csv", out var path)) return;

        csvService.ExportManaboxScans(path, scans);

        ResetScanFilterSort();
        SelectedScannedCards = [];
        SelectedScannedCard = null;
        NotifySelectionChanged();
        CardService.ClearTempFiles();
        CardService.ScannedCards.Clear();
        _preAuditSnapshots?.Clear();
        _preAuditSnapshots = null;
        IsAuditComplete = false;
        var exported = scans.Count(s => s.Match is not null);
        Message = $"Exported {exported} cards to {Path.GetFileName(path)}. Scan queue cleared.";
    }

    [RelayCommand]
    public void ExportScansManaboxCollectionCsv()
    {
        if (IsAuditMode) return;

        var scans = CardService.ScannedCards.ToList();
        if (scans.Count == 0) return;

        if (!ExportToFile("scans-manabox-collection.csv", out var path)) return;

        csvService.ExportManaboxScansCollection(path, scans);

        ResetScanFilterSort();
        SelectedScannedCards = [];
        SelectedScannedCard = null;
        NotifySelectionChanged();
        CardService.ClearTempFiles();
        CardService.ScannedCards.Clear();
        _preAuditSnapshots?.Clear();
        _preAuditSnapshots = null;
        IsAuditComplete = false;
        var exported = scans.Count(s => s.Match is not null);
        Message = $"Exported {exported} cards to {Path.GetFileName(path)}. Scan queue cleared.";
    }

    [RelayCommand]
    public void ExportScansManaboxText()
    {
        if (IsAuditMode) return;

        var scans = CardService.ScannedCards.ToList();
        if (scans.Count == 0) return;

        if (!ExportToFile("scans-manabox.txt", "Text files (*.txt)|*.txt", out var path)) return;

        csvService.ExportManaboxScansText(path, scans);

        ResetScanFilterSort();
        SelectedScannedCards = [];
        SelectedScannedCard = null;
        NotifySelectionChanged();
        CardService.ClearTempFiles();
        CardService.ScannedCards.Clear();
        _preAuditSnapshots?.Clear();
        _preAuditSnapshots = null;
        IsAuditComplete = false;
        var exported = scans.Count(s => s.Match is not null);
        Message = $"Exported {exported} cards to {Path.GetFileName(path)}. Scan queue cleared.";
    }

    [RelayCommand]
    public void ImportCollection()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            Title = "Import Collection",
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var preview = csvService.PreviewImport(dialog.FileName);
            var imported = dialogService.ShowImportPreview(preview);
            if (imported.HasValue)
            {
                Message = $"Imported {imported.Value} cards";
                _ = Collection.SearchCollection();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import collection");
            System.Windows.MessageBox.Show($"Failed to import: {ex.Message}", "Import Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private List<CollectionCard> GetAllCollectionCards()
    {
        var results = new System.Collections.ObjectModel.ObservableCollection<CollectionCard>();
        CardService.SearchCollection("", null, null, results);
        return results.ToList();
    }

    private static bool ExportToFile(string defaultFileName, out string path)
        => ExportToFile(defaultFileName, "CSV files (*.csv)|*.csv", out path);

    private static bool ExportToFile(string defaultFileName, string filter, out string path)
    {
        path = "";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = defaultFileName,
            Filter = filter,
            Title = "Export Collection",
        };

        if (dialog.ShowDialog() != true)
            return false;

        path = dialog.FileName;
        return true;
    }

    [RelayCommand]
    public void ReprocessScans()
    {
        if (IsAuditComplete) return;
        _logger.LogInformation("User initiated reprocess of unmatched scans");
        CardService.ReprocessScans();
        var matched = CardService.ScannedCards.Count(s => s.Match is not null);
        var total = CardService.ScannedCards.Count;
        Message = $"Reprocess complete: {matched}/{total} cards matched.";
    }

    [RelayCommand]
    public void RefreshHomeTab() => InvalidateHomeTab();

    [RelayCommand]
    public async Task CalculateSetCompletion()
    {
        IsCalculatingCompletion = true;
        CompletionStatusMessage = "Loading collection stats...";

        try
        {
            var progress = new Progress<string>(msg =>
            {
                Application.Current.Dispatcher.Invoke(() => CompletionStatusMessage = msg);
            });

            // Compute collection stats on background thread
            var allCards = new System.Collections.ObjectModel.ObservableCollection<CollectionCard>();
            await Task.Run(() => CardService.SearchCollection("", SelectedGame, null, allCards));

            StatTotalCards = allCards.Count;
            StatFoilCount = allCards.Count(c => c.IsFoil);

            // Calculate total value: use purchase price if set, otherwise look up market price
            decimal totalValue = 0;
            var gameService = CardService.ActiveGameService;
            var cardsNeedingPrice = allCards.Where(c => !c.PurchasePrice.HasValue).ToList();
            var batchPrices = new Dictionary<(string, bool), decimal>();
            foreach (var foilGroup in cardsNeedingPrice.GroupBy(c => c.IsFoil))
            {
                var prices = gameService.GetCurrentPrices(
                    foilGroup.Select(c => c.GameCardId).Distinct(), foilGroup.Key);
                foreach (var kvp in prices)
                    batchPrices.TryAdd((kvp.Key, foilGroup.Key), kvp.Value);
            }
            foreach (var card in allCards)
            {
                if (card.PurchasePrice.HasValue)
                    totalValue += card.PurchasePrice.Value;
                else
                    totalValue += batchPrices.GetValueOrDefault((card.GameCardId, card.IsFoil));
            }
            StatTotalValue = totalValue;

            var rarityGroups = allCards.GroupBy(c => c.Rarity.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());
            StatCommonCount = rarityGroups.GetValueOrDefault("common", 0);
            StatUncommonCount = rarityGroups.GetValueOrDefault("uncommon", 0);
            StatRareCount = rarityGroups.GetValueOrDefault("rare", 0);
            StatMythicCount = rarityGroups.GetValueOrDefault("mythic", 0);
            StatOtherRarityCount = allCards.Count - StatCommonCount - StatUncommonCount - StatRareCount - StatMythicCount;

            // Compute set completion — only for sets in collection
            var results = await Task.Run<Task<List<SetCompletionSummary>>>(() =>
                CardService.CalculateSetCompletionAsync(SelectedGame, progress)).Unwrap();

            foreach (var old in SetCompletionResults)
                old.MissingCards = null;
            SelectedSetCompletion = null;
            SetCompletionResults.Clear();

            // Only show sets the user owns cards in
            var ownedSets = results
                .Where(s => s.OwnedCount > 0)
                .OrderByDescending(s => s.CompletionPercent)
                .ThenBy(s => s.SetName);

            foreach (var summary in ownedSets)
                SetCompletionResults.Add(summary);

            StatTotalSets = SetCompletionResults.Count;
            var complete = SetCompletionResults.Count(s => s.CompletionPercent >= 100);
            CompletionStatusMessage = $"{StatTotalCards} cards across {StatTotalSets} sets — {complete} complete";
            _homeTabDirty = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate collection stats");
            CompletionStatusMessage = "Failed to load collection stats.";
        }
        finally
        {
            IsCalculatingCompletion = false;
        }
    }

    [RelayCommand]
    public async Task ExpandSetCompletion(SetCompletionSummary summary)
    {
        if (summary is null || summary.MissingCards is not null) return; // null row or already loaded
        summary.IsLoadingMissing = true;
        try
        {
            var missing = await Task.Run(() =>
                CardService.GetMissingCardsForSet(SelectedGame, summary.SetCode));
            summary.MissingCards = new(missing);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load missing cards for set {SetCode}", summary.SetCode);
        }
        finally
        {
            summary.IsLoadingMissing = false;
        }
    }

    [RelayCommand]
    public void ClearDefaultScanner()
    {
        DefaultScannerName = null;
        Message = "Default scanner cleared.";
    }

    [ObservableProperty]
    public partial int DiagnosticEventCount { get; set; }

    public void RefreshDiagnosticCount()
    {
        try { DiagnosticEventCount = _diagnosticService.GetEventCount(); } catch { }
    }

    [RelayCommand]
    public void ExportDiagnostics()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = $"omnicard-diagnostics-{DateTime.Now:yyyy-MM-dd}",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _diagnosticService.ExportDiagnostics(dialog.FileName);
                Message = $"Diagnostics exported to {Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex)
            {
                Message = $"Export failed: {ex.Message}";
            }
        }
    }

    private bool? ConnectToScanner(bool force)
    {
        if (ScannerService.DataSource is not null && !force)
            return true;

        // Ensure TWAIN session is open (deferred from startup)
        ScannerService.EnsureSessionOpen();

        // Try auto-connecting the default scanner
        if (!force && DefaultScannerName is not null)
        {
            var defaultSource = ScannerService.Session
                .OfType<DataSource>()
                .FirstOrDefault(s => s.Name == DefaultScannerName);

            if (defaultSource is not null)
            {
                _logger.LogInformation("Auto-connecting to default scanner: {Name}", DefaultScannerName);
                ScannerService.DataSource = defaultSource;
                return true;
            }

            // Default scanner not available — tell user and fall through to dialog
            _logger.LogWarning("Default scanner \"{Name}\" not available", DefaultScannerName);
            System.Windows.MessageBox.Show(
                $"The default scanner \"{DefaultScannerName}\" is not available.\n\nPlease select a scanner.",
                "Scanner Not Available",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }

        _logger.LogInformation("Opening scanner connection dialog");
        var (connected, setAsDefault) = DialogService.ConnectToScanner();
        if (!connected)
        {
            _logger.LogInformation("Scanner connection cancelled");
            return false;
        }

        if (setAsDefault && ScannerService.DataSource is not null)
        {
            DefaultScannerName = ScannerService.DataSource.Name;
            _logger.LogInformation("Set default scanner to: {Name}", DefaultScannerName);
        }

        _logger.LogInformation("Scanner connection result: {Connected}", ScannerService.DataSource?.Name ?? "(unknown)");
        return true;
    }

   
}
