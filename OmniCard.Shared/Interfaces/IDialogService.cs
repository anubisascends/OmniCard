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
    void ShowAuditReport(AuditReport report);
    bool? OpenEbayListingDialog(CollectionCard card);
    bool? OpenManualAdd(StorageContainer? defaultContainer = null);
    void ShowDecklistCheck();
    Product? EditProduct(Product? existing);
    (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? AddLotDialog(int productId);
    bool OpenUnitsDialog(Product product);
    void OpenMovementHistory();
    ListForSaleResult? PickListForSale(decimal suggestedPrice);
    int ShowTcgOrderImportPreview(TcgOrderImportPreview preview);
}
