using OmniCard.Shared.Sets;

namespace OmniCard.Shared.Audit;

public interface ISetChecklistPdfExporter
{
    void Export(SetChecklistReport report, string filePath);
}
