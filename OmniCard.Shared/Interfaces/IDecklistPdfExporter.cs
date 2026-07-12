using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IDecklistPdfExporter
{
    void Export(DecklistCheckResult result, string filePath);
}
