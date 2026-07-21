using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IReceiptPdfExporter
{
    void Export(ReceiptDocument document, string filePath);
}
