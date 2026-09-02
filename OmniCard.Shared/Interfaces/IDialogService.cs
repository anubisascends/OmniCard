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

    /// <summary>Opens the drag-and-drop binder placement editor for a Binder location as a modal dialog.</summary>
    void ShowBinderView(int containerId);

    /// <summary>Opens the read-only visual binder audit for a Binder location as a modal dialog:
    /// mark each pocket (correct/missing/wrong/extra) then review and apply the corrections.</summary>
    void ShowBinderAudit(int containerId);

    /// <summary>Asks how the user wants to audit a binder <paramref name="locationName"/>: the
    /// pocket-marking audit or an import-file + drag-drop reconcile. Returns the choice, or
    /// <see cref="BinderAuditChoice.Cancel"/> if dismissed.</summary>
    BinderAuditChoice PromptBinderAuditMode(string locationName);

    /// <summary>Opens the import-driven binder audit: <paramref name="importedCards"/> populate a
    /// sideboard tray the user drags into the binder's pockets, then applies to reconcile the binder.
    /// Returns true if changes were applied (so the caller can refresh).</summary>
    bool ShowBinderImportAudit(int containerId, IReadOnlyList<CollectionCard> importedCards);

    /// <summary>Prompts for where to insert a new binder sheet and whether it's double- or
    /// single-sided. <paramref name="nearPage"/> pre-selects the sheet the user is currently viewing.
    /// Returns the target sheet index and side choice, or null if cancelled.</summary>
    (int InsertIndex, bool DoubleSided)? InsertBinderPage(int containerId, int? nearPage);

    /// <summary>Prompts for where to move the sheet identified by <paramref name="movingSheetIndex"/>.
    /// Returns the destination as an insertion index into the list of the other sheets, or null if
    /// cancelled.</summary>
    int? MoveBinderPage(int containerId, int movingSheetIndex);

    /// <summary>Prompts for how to shift the cards on <paramref name="page"/> — a direction
    /// (front/back), a number of pages, and a scope (only this page, this page and all before, or all
    /// after). Returns the signed page delta (negative = toward the front, positive = toward the back)
    /// and the chosen scope, or null if cancelled or no shift was chosen.</summary>
    (int DeltaPages, BinderShiftScope Scope)? ShiftBinderPage(int page);

    /// <summary>Prompts for a list destination (existing or new) per game group, for "Create List from Scans".
    /// Returns null if cancelled.</summary>
    IReadOnlyList<ScanListTargetResult>? PickListTargetsForScans(IReadOnlyList<(CardGame Game, int Count)> groups, string defaultName);
    void ShowAuditReport(AuditReport report);

    /// <summary>Asks how the user wants to audit <paramref name="locationName"/>: by scanning each
    /// card or by importing a known-good collection file (Manabox / Mythic Tools). Returns the chosen
    /// source, or <see cref="AuditSourceChoice.Cancel"/> if dismissed.</summary>
    AuditSourceChoice PromptAuditSource(string locationName);
    bool? OpenEbayListingDialog(CollectionCard card);

    /// <summary>Opens the eBay listing dialog for a sealed inventory product, keyed by the owning
    /// lot id. <paramref name="suggestedPrice"/> pre-fills the price field.</summary>
    bool? OpenEbayListingDialog(Product product, int lotId, decimal? suggestedPrice);

    bool? OpenManualAdd(StorageContainer? defaultContainer = null);

    /// <summary>Opens the Add-Card dialog locked to a specific binder page/slot (container/page/slot
    /// read-only). Adding places the card into that slot, displacing any occupant to the Unplaced pool.</summary>
    bool? OpenManualAddToSlot(int containerId, int page, int slot);
    void ShowDecklistCheck();

    /// <summary>Opens the "Upgrade Deck" dialog for a Deck Box location: fetch/paste a target decklist,
    /// review cuts/adds, and apply the moves. Returns true if the user committed changes (so the caller
    /// can refresh the location view).</summary>
    bool ShowDeckBoxSync(StorageContainer deckBox);
    Product? EditProduct(Product? existing);
    (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? AddLotDialog(int productId);

    /// <summary>Opens the lot editor prefilled from <paramref name="lot"/>. Returns the edited
    /// quantity/cost/location/source/date, or null if cancelled. The caller applies these to the
    /// existing lot, preserving every other field.</summary>
    (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? EditLotDialog(InventoryLot lot);

    bool OpenUnitsDialog(Product product);

    /// <summary>Opens the Open-Units dialog with <paramref name="preselectLotId"/> preselected.</summary>
    bool OpenUnitsDialog(Product product, int? preselectLotId);
    void OpenMovementHistory();
    void OpenLogViewer();
    ListForSaleResult? PickListForSale(decimal suggestedPrice);
    TradeSummary? PickTrade();

    /// <summary>Opens the read-only Trades history window (all trade sessions).</summary>
    void OpenTrades();
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

    /// <summary>Opens the About dialog (app version, description, third-party attributions).</summary>
    void ShowAbout();

    /// <summary>Opens the browsable/searchable Help &amp; Documentation dialog.</summary>
    void ShowDocumentation();
}
