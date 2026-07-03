using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using NTwain;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Models;
using OmniCard.Services;
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
    IDbContextFactory<CollectionDbContext> collectionDbContextFactory,
    SetSymbolCache setSymbolCache,
    ILogger<RootViewModel> logger) : ViewModel
{
    private readonly ILogger<RootViewModel> _logger = logger;

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
        foreach (var c in containerService.GetAll())
            AvailableContainers.Add(c);
        // Restore previous selection, or default to Bulk
        ActiveContainer = AvailableContainers.FirstOrDefault(c => c.Id == previousActiveId)
            ?? AvailableContainers.FirstOrDefault(c => c.IsSystem);

        // Keep the collection VM's container list in sync
        Collection.LoadContainers();
    }

    [ObservableProperty]
    public partial bool IsDarkTheme { get; set; } = displaySettings.Value.Theme != "Light";

    partial void OnIsDarkThemeChanged(bool value)
    {
        // Apply theme
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(value ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(theme);

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
            _hashPreviewWindow.Owner = Application.Current.MainWindow;
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

    partial void OnSelectedGameChanged(CardGame value)
    {
        _logger.LogInformation("Switched active game to {Game}", value);
        CardService.SelectedGame = value;
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
            CardService.SelectedSetFilter = null;
            _logger.LogInformation("Set filter: no valid codes in '{Text}', filter cleared", text);
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
        UpdateSetFilter();
    }

    [RelayCommand]
    public void OpenSetFilterBuilder()
    {
        var currentFilter = CardService.SelectedSetFilter;
        var result = DialogService.OpenSetFilterBuilder(_allSets, currentFilter);
        if (result is not null)
        {
            SetFilterText = string.Join(", ", result);
            UpdateSetFilter();
        }
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
        RefreshAvailablePrintings();
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
        }
        else
        {
            card.FlagReason = FlagReason.Manual;
        }
        ApplyScanView();
    }

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

        LoadAvailableSets();
        LoadContainers(); // also calls Collection.LoadContainers()
        Collection.SetGame(SelectedGame);
        Collection.Initialize();

        IsEbayConnected = ebayAuthService.IsConnected;
        InvalidateHomeTab();
    }

    [RelayCommand]
    public void Scan()
    {
        if (ConnectToScanner(false) ?? false)
        {
            _logger.LogInformation("User initiated scan");
            ScannerService.Scan();
        }
    }

    [RelayCommand]
    public void ConnectToScanner() => ConnectToScanner(true);

    [RelayCommand]
    public async Task RefreshCardData()
    {
        _logger.LogInformation("User initiated card data refresh for {Game}", SelectedGame);
        var progress = new Progress<string>((str) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Message = str;
            });
        });

        await CardService.ActiveGameService.DownloadBulkDataAsync(progress);
        LoadAvailableSets();

        // Pre-download set symbol SVGs for all known sets
        var sets = _allSets.Select(s => (s.SetCode, s.SetName)).ToList();
        await setSymbolCache.PreloadSymbolsAsync(sets, progress);

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

    [RelayCommand]
    public void ManualSearch()
    {
        _logger.LogDebug("Manual search: {Query}", ManualSearchQuery);
        ManualSearchResults.Clear();

        var query = ManualSearchQuery;
        var setFilter = CardService.SelectedSetFilter;

        // Single set: use the set: prefix for efficient DB-level filtering
        if (setFilter is { Count: 1 })
            query = $"set:{setFilter.First()} {query}";

        var results = CardService.ActiveGameService.SearchCards(query, maxResults: int.MaxValue);

        // Multiple sets: post-filter since SearchCards doesn't support multi-set queries
        if (setFilter is { Count: > 1 })
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

    private void LogMismatchIfHighConfidence(ScannedCard card, CardMatch oldMatch, CardMatch newMatch)
    {
        if (oldMatch.Confidence is not >= 80) return;
        if (oldMatch.GameSpecificId == newMatch.GameSpecificId) return;

        try
        {
            using var ctx = collectionDbContextFactory.CreateDbContext();
            ctx.MismatchLogs.Add(new MismatchLog
            {
                ScanHash = card.Hash,
                ScanImagePath = card.TempImagePath,
                OriginalCardId = oldMatch.GameSpecificId,
                OriginalName = oldMatch.Name,
                OriginalSetCode = oldMatch.SetCode,
                OriginalNumber = oldMatch.CollectorNumber,
                OriginalConfidence = oldMatch.Confidence ?? 0,
                CorrectedCardId = newMatch.GameSpecificId,
                CorrectedName = newMatch.Name,
                CorrectedSetCode = newMatch.SetCode,
                CorrectedNumber = newMatch.CollectorNumber,
            });
            ctx.SaveChanges();
            _logger.LogInformation("Logged high-confidence mismatch: {OldName} ({OldSet}) -> {NewName} ({NewSet}) at {Confidence:F0}%",
                oldMatch.Name, oldMatch.SetCode, newMatch.Name, newMatch.SetCode, oldMatch.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log mismatch for hash {Hash:X16}", card.Hash);
        }
    }

    [ObservableProperty]
    public partial bool IsCommitting { get; set; }

    [RelayCommand]
    public async Task CommitScans()
    {
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

            Collection.SearchCollection();
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
    public void CardDoubleClick()
    {
        if (SelectedScannedCard is not null)
            DialogService.ShowCard(SelectedScannedCard);
    }

    [RelayCommand]
    public void ClearScans()
    {
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
            "This will permanently delete all Flag Resolution and Mismatch Log records.\n\nContinue?",
            "Clear Diagnostic Logs",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        var (flagCount, mismatchCount) = CardService.ClearDiagnosticLogs();
        Message = $"Cleared {flagCount} flag resolution(s) and {mismatchCount} mismatch log(s).";
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
                Collection.SearchCollection();
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
    {
        path = "";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = defaultFileName,
            Filter = "CSV files (*.csv)|*.csv",
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
            foreach (var card in allCards)
            {
                if (card.PurchasePrice.HasValue)
                    totalValue += card.PurchasePrice.Value;
                else
                    totalValue += gameService.GetCurrentPrice(card.GameCardId, card.IsFoil) ?? 0;
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
