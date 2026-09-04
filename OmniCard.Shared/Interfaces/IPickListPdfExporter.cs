using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>Renders a pick list (cards to pull from inventory for active listings) to a PDF the
/// user can print and tick off while pulling stock.</summary>
public interface IPickListPdfExporter
{
    void Export(IReadOnlyList<PickListEntry> entries, string filePath);
}
