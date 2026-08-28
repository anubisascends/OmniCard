using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Card;
using OmniCard.Views.CollectionCardEditor;
using OmniCard.Views.Connection;
using OmniCard.Views.CoverArtPicker;
using OmniCard.Views.BatchDecklistImport;
using OmniCard.Views.CsvImport;
using OmniCard.Views.EbayAuth;
using OmniCard.Views.SetFilterBuilder;
using OmniCard.Views.SortFilterBuilder;
using OmniCard.Views.MoveToLocation;
using OmniCard.Views.PickTrade;
using OmniCard.Views.AuditReport;
using OmniCard.Views.StorageManager;
using OmniCard.Views.EbayListing;
using OmniCard.Views.ManualAdd;
using OmniCard.Views.DecklistCheck;
using OmniCard.Views.Inventory;
using OmniCard.Views.LogViewer;
using OmniCard.Views.MovementHistory;
using OmniCard.Views.Sales;
using OmniCard.Views.SalesListing;
using OmniCard.Views.Settings;
using OmniCard.Views.TcgOrderImport;
using OmniCard.Views.ManageTags;
using OmniCard.Views.TopValueCards;
using OmniCard.Views.About;
using OmniCard.Views.Documentation;

namespace OmniCard.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public IServiceProvider Services { get; } = services;

    private CardView? _cardWindow;

    private static void SetOwner(Window wnd)
    {
        var main = Application.Current.MainWindow;
        if (main is not null && main != wnd && main.IsLoaded)
            wnd.Owner = main;
    }

    public (bool Connected, bool SetAsDefault) ConnectToScanner()
    {
        var wnd = Services.GetRequiredService<ConnectionView>();
        SetOwner(wnd);
        var result = wnd.ShowDialog() == true;
        return (result, result && wnd.ViewModel.SetAsDefault);
    }

    public bool? ConnectToEbay()
    {
        var wnd = Services.GetRequiredService<EbayAuthView>();
        SetOwner(wnd);
        return wnd.ShowDialog();
    }

    public void ShowCard(ScannedCard card)
    {
        if (_cardWindow is null)
        {
            _cardWindow = Services.GetRequiredService<CardView>();
            SetOwner(_cardWindow);
            _cardWindow.Topmost = true;
            _cardWindow.Closed += (_, _) => _cardWindow = null;
        }

        _cardWindow.ViewModel.Card = card;
        _cardWindow.Show();
        _cardWindow.Activate();
    }

    public bool IsCardPreviewOpen => _cardWindow is not null;

    public void UpdateCardPreview(ScannedCard? card)
    {
        if (_cardWindow is null) return;

        if (card is null)
        {
            _cardWindow.ViewModel.Card = null;
            return;
        }

        _cardWindow.ViewModel.Card = card;
    }

    public bool? EditCollectionCard(CollectionCard card)
    {
        var wnd = Services.GetRequiredService<CollectionCardEditorView>();
        SetOwner(wnd);
        wnd.ViewModel.LoadCard(card);
        return wnd.ShowDialog();
    }

    public void ManageStorageContainers()
    {
        var wnd = Services.GetRequiredService<StorageManagerView>();
        SetOwner(wnd);
        wnd.ShowDialog();
    }

    public int? ShowImportPreview(CsvImportPreview preview)
    {
        var wnd = Services.GetRequiredService<CsvImportView>();
        SetOwner(wnd);
        wnd.ViewModel.LoadPreview(preview);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.ImportedCount : null;
    }

    public BatchDecklistImportSummary? ShowBatchDecklistImport()
    {
        var wnd = Services.GetRequiredService<BatchDecklistImportView>();
        SetOwner(wnd);
        var csv = Services.GetRequiredService<ICsvExportImportService>();
        wnd.ViewModel.ImportCsvFile = path => ShowImportPreview(csv.PreviewImport(path));
        wnd.ViewModel.PickFiles = () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Import files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
                Title = "Add files",
                Multiselect = true,
            };
            return d.ShowDialog() == true ? d.FileNames : null;
        };
        wnd.ViewModel.Load();
        wnd.ShowDialog();
        return wnd.ViewModel.Result;   // set on Import, or on Cancel when CSVs were imported
    }

    public bool OpenSortFilterBuilder(CardGame game)
    {
        var wnd = Services.GetRequiredService<SortFilterBuilderView>();
        wnd.ViewModel.Initialize(game);
        SetOwner(wnd);
        wnd.ShowDialog();
        return wnd.ViewModel.PresetsChanged;
    }

    public IReadOnlyList<string>? OpenSetFilterBuilder(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter)
    {
        var wnd = Services.GetRequiredService<SetFilterBuilderView>();
        wnd.ViewModel.Initialize(allSets, currentFilter);
        SetOwner(wnd);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.GetSelectedCodes() : null;
    }

    public void ShowSettings()
    {
        var wnd = Services.GetRequiredService<SettingsView>();
        SetOwner(wnd);
        wnd.ShowDialog();
    }

    public void ShowAbout()
    {
        var wnd = Services.GetRequiredService<AboutView>();
        SetOwner(wnd);
        wnd.ShowDialog();
    }

    public void ShowDocumentation()
    {
        var wnd = Services.GetRequiredService<DocumentationView>();
        SetOwner(wnd);
        wnd.ShowDialog();
    }

    public int? PickCoverArt(int containerId, string containerName)
    {
        var wnd = Services.GetRequiredService<CoverArtPickerView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(containerId, containerName);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.SelectedCardId : null;
    }

    public MoveToLocationResult? PickMoveToLocation()
    {
        var wnd = Services.GetRequiredService<MoveToLocationView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public void ShowBinderView(int containerId)
    {
        var wnd = Services.GetRequiredService<Views.Binder.BinderView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(containerId);
        wnd.ShowDialog();
    }

    public void ShowBinderAudit(int containerId)
    {
        var wnd = Services.GetRequiredService<Views.BinderAudit.BinderAuditView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(containerId);
        wnd.ShowDialog();
    }

    public (int InsertIndex, bool DoubleSided)? InsertBinderPage(int containerId, int? nearPage)
    {
        var sheets = Services.GetRequiredService<IStorageContainerService>().GetSheets(containerId);
        var vm = new Views.Binder.InsertBinderPageViewModel(sheets, nearPage);
        var wnd = new Views.Binder.InsertBinderPageDialog(vm);
        SetOwner(wnd);
        var result = wnd.ShowDialog();
        return result == true ? vm.ToResult() : null;
    }

    public int? MoveBinderPage(int containerId, int movingSheetIndex)
    {
        var sheets = Services.GetRequiredService<IStorageContainerService>().GetSheets(containerId);
        var vm = new Views.Binder.MoveBinderPageViewModel(sheets, movingSheetIndex);
        var wnd = new Views.Binder.MoveBinderPageDialog(vm);
        SetOwner(wnd);
        var result = wnd.ShowDialog();
        return result == true ? vm.ToResult() : null;
    }

    public (int DeltaPages, Models.BinderShiftScope Scope)? ShiftBinderPage(int page)
    {
        var vm = new Views.Binder.ShiftBinderPageViewModel(page);
        var wnd = new Views.Binder.ShiftBinderPageDialog(vm);
        SetOwner(wnd);
        var result = wnd.ShowDialog();
        return result == true ? vm.ToResult() : null;
    }

    public MoveListToLocationResult? PickMoveListToLocation()
    {
        var wnd = Services.GetRequiredService<Views.MoveListToLocation.MoveListToLocationView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }


    public IReadOnlyList<ScanListTargetResult>? PickListTargetsForScans(IReadOnlyList<(CardGame Game, int Count)> groups, string defaultName)
    {
        var wnd = Services.GetRequiredService<Views.CreateListFromScans.CreateListFromScansView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(groups, defaultName);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public TradeSummary? PickTrade()
    {
        var wnd = Services.GetRequiredService<PickTradeView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public void OpenTrades()
    {
        var wnd = Services.GetRequiredService<Views.Trades.TradesView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        wnd.ShowDialog();
    }

    public void ShowAuditReport(AuditReport report)
    {
        var wnd = Services.GetRequiredService<AuditReportView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(report);
        wnd.ShowDialog();
    }

    public bool? OpenEbayListingDialog(CollectionCard card)
    {
        var wnd = Services.GetRequiredService<EbayListingView>();
        SetOwner(wnd);
        wnd.ViewModel.LoadCard(card);
        return wnd.ShowDialog();
    }

    public bool? OpenEbayListingDialog(Product product, int lotId, decimal? suggestedPrice)
    {
        var wnd = Services.GetRequiredService<EbayListingView>();
        SetOwner(wnd);
        wnd.ViewModel.LoadSealedProduct(product, lotId, suggestedPrice);
        return wnd.ShowDialog();
    }

    public bool? OpenManualAdd(StorageContainer? defaultContainer = null)
    {
        var wnd = Services.GetRequiredService<ManualAddView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(defaultContainer);
        return wnd.ShowDialog();
    }

    public bool? OpenManualAddToSlot(int containerId, int page, int slot)
    {
        var wnd = Services.GetRequiredService<ManualAddView>();
        SetOwner(wnd);
        wnd.ViewModel.LoadForSlot(containerId, page, slot);
        return wnd.ShowDialog();
    }

    public void ShowDecklistCheck()
    {
        var wnd = Services.GetRequiredService<DecklistCheckView>();
        SetOwner(wnd);
        wnd.ShowDialog();
    }

    public bool ShowDeckBoxSync(StorageContainer deckBox)
    {
        var wnd = Services.GetRequiredService<Views.DeckBoxSync.DeckBoxSyncView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(deckBox);
        wnd.ShowDialog();
        return wnd.ViewModel.DidCommit;
    }

    public Product? EditProduct(Product? existing)
    {
        var wnd = Services.GetRequiredService<ProductEditorView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(existing);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? AddLotDialog(int productId)
    {
        var wnd = Services.GetRequiredService<AddLotView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(productId);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? EditLotDialog(InventoryLot lot)
    {
        var wnd = Services.GetRequiredService<AddLotView>();
        SetOwner(wnd);
        wnd.ViewModel.LoadForEdit(lot);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public bool OpenUnitsDialog(Product product) => OpenUnitsDialog(product, null);

    public bool OpenUnitsDialog(Product product, int? preselectLotId)
    {
        var wnd = Services.GetRequiredService<OpenUnitsView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(product, preselectLotId);
        var result = wnd.ShowDialog();
        return result == true && wnd.ViewModel.WasOpened;
    }

    public void OpenMovementHistory()
    {
        var wnd = Services.GetRequiredService<MovementHistoryView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        wnd.ShowDialog();
    }

    public void OpenLogViewer()
    {
        var wnd = Services.GetRequiredService<LogViewerView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        wnd.ShowDialog();
    }

    public (CardGame Game, int? ContainerId)? ShowTopValueCards()
    {
        var wnd = Services.GetRequiredService<TopValueCardsView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        wnd.ShowDialog();
        return wnd.ViewModel.NavigationResult;
    }

    public ListForSaleResult? PickListForSale(decimal suggestedPrice)
    {
        var vm = new ListForSaleViewModel(suggestedPrice);
        var wnd = new ListForSaleDialog(vm);
        SetOwner(wnd);
        var result = wnd.ShowDialog();
        return result == true ? vm.ToResult() : null;
    }

    public int ShowTcgOrderImportPreview(TcgOrderImportPreview preview)
    {
        var wnd = Services.GetRequiredService<TcgOrderImportView>();
        wnd.ViewModel.LoadPreview(preview);
        SetOwner(wnd);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.ImportedCount : 0;
    }

    public bool Confirm(string message, string title)
        => System.Windows.MessageBox.Show(message, title,
               System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
           == System.Windows.MessageBoxResult.Yes;

    public string? RequireReason(string title, string message)
    {
        var wnd = Services.GetRequiredService<RequireReasonView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(title, message);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public void ManageTags()
    {
        var wnd = Services.GetRequiredService<ManageTagsView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        wnd.ShowDialog();
    }
}
