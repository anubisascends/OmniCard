using OmniCard.Shared.Collection;
using OmniCard.Shared.Scanning;

namespace OmniCard.Shared.ImportExport;

public interface ICsvExportImportService
{
    void ExportAppNative(string filePath, IEnumerable<CollectionCard> cards);
    void ExportTcgPlayer(string filePath, IEnumerable<CollectionCard> cards);
    void ExportMoxfield(string filePath, IEnumerable<CollectionCard> cards);
    void ExportManabox(string filePath, IEnumerable<CollectionCard> cards);
    void ExportPriceTicker(string filePath, IEnumerable<CollectionCard> cards);
    void ExportManaboxScans(string filePath, IEnumerable<ScannedCard> scans);
    void ExportManaboxScansCollection(string filePath, IEnumerable<ScannedCard> scans);
    void ExportManaboxScansText(string filePath, IEnumerable<ScannedCard> scans);
    CsvImportPreview PreviewImport(string filePath);
    int ImportCards(CsvImportPreview preview, bool skipDuplicates, int? targetContainerId = null, string? defaultFoilType = null);
}
