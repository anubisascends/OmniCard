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
    void ShowDataLocation();
    int? PickCoverArt(int containerId, string containerName);
    MoveToLocationResult? PickMoveToLocation();
    SealedProductTemplate? EditSealedProductTemplate(SealedProductTemplate? existing);
    List<SealedProductInstance>? OpenSealedProductEntry();
    List<SealedProductInstance>? CrackSealedProduct(SealedProductInstance instance);
    void ShowAuditReport(AuditReport report);
    bool? OpenEbayListingDialog(CollectionCard card);
    bool? OpenManualAdd(StorageContainer? defaultContainer = null);
    void ShowDecklistCheck();
}
