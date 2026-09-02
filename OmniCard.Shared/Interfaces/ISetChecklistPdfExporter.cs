using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ISetChecklistPdfExporter
{
    void Export(SetChecklistReport report, string filePath);
}
