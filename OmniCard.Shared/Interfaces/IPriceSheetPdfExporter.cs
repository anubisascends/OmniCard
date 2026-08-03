using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IPriceSheetPdfExporter
{
    void Export(PriceSheetReport report, string filePath);
}
