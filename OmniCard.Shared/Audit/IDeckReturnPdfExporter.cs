using OmniCard.Shared.Storage;

namespace OmniCard.Shared.Audit;

/// <summary>Renders a deck deconstruction's put-back report (where each returned card goes) to PDF.</summary>
public interface IDeckReturnPdfExporter
{
    void Export(DeckReturnReport report, IReadOnlyList<DeckReturnLine> lines, string filePath);
}
