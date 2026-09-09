using OmniCard.Shared.Sales;

namespace OmniCard.Shared.Audit;

public interface IPriceSheetPdfExporter
{
    void Export(PriceSheetReport report, string filePath);
}
