using OmniCard.Shared.Sales;

namespace OmniCard.Shared.Audit;

public interface IReceiptPdfExporter
{
    void Export(ReceiptDocument document, string filePath);
}
