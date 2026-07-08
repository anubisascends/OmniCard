using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICsvExportImportService
{
    void ExportAppNative(string filePath, IEnumerable<CollectionCard> cards);
    void ExportTcgPlayer(string filePath, IEnumerable<CollectionCard> cards);
    void ExportMoxfield(string filePath, IEnumerable<CollectionCard> cards);
    void ExportManabox(string filePath, IEnumerable<CollectionCard> cards);
    CsvImportPreview PreviewImport(string filePath);
    int ImportCards(CsvImportPreview preview, bool skipDuplicates, int? targetContainerId = null);
}
