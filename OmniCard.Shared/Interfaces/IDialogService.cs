using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IDialogService
{
    (bool Connected, bool SetAsDefault) ConnectToScanner();
    bool? ConnectToEbay();
    void ShowCard(ScannedCard card);
    bool IsCardPreviewOpen { get; }
    void UpdateCardPreview(ScannedCard? card);
    bool? EditCollectionCard(CollectionCard card);
    void ManageStorageContainers();
    int? ShowImportPreview(CsvImportPreview preview);
    bool OpenSortFilterBuilder(CardGame game);
    IReadOnlyList<string>? OpenSetFilterBuilder(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter);
    void ShowSettings();
    int? PickCoverArt(int containerId, string containerName);
    MoveToLocationResult? PickMoveToLocation();
    MoveListToLocationResult? PickMoveListToLocation();

    /// <summary>Prompts for a list destination (existing or new) per game group, for "Create List from Scans".
    /// Returns null if cancelled.</summary>
    IReadOnlyList<ScanListTargetResult>? PickListTargetsForScans(IReadOnlyList<(CardGame Game, int Count)> groups, string defaultName);
    void ShowAuditReport(AuditReport report);
    bool? OpenEbayListingDialog(CollectionCard card);
    bool? OpenManualAdd(StorageContainer? defaultContainer = null);
    void ShowDecklistCheck();
    Product? EditProduct(Product? existing);
    (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? AddLotDialog(int productId);
    bool OpenUnitsDialog(Product product);
    void OpenMovementHistory();
    void OpenLogViewer();
    ListForSaleResult? PickListForSale(decimal suggestedPrice);
    TradeSummary? PickTrade();
    int ShowTcgOrderImportPreview(TcgOrderImportPreview preview);
    bool Confirm(string message, string title);
    BatchDecklistImportSummary? ShowBatchDecklistImport();

    /// <summary>Required-reason modal: Confirm is disabled while the reason field is blank.
    /// Returns the trimmed reason on confirm, or null if cancelled.</summary>
    string? RequireReason(string title, string message);

    /// <summary>Opens the tag library management dialog (rename/delete/merge, usage counts).</summary>
    void ManageTags();

    /// <summary>Opens the Top 100 Cards dialog. Returns the game/location to navigate to if the
    /// user picked "Go to Location" on a row, or null if the dialog was simply closed.</summary>
    (CardGame Game, int? ContainerId)? ShowTopValueCards();
}
