using OmniCard.Shared.Lists;

namespace OmniCard.Shared.Audit;

public interface IDecklistPdfExporter
{
    void Export(DecklistCheckResult result, string filePath);
    void ExportDetailed(DecklistCheckResult result, string filePath);
}
