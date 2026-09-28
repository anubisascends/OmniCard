using OmniCard.Shared.Lists;

namespace OmniCard.Shared.Audit;

/// <summary>Printable checklists for a decklist check: the cards to pull from the collection (with
/// where to find each copy) and the cards still missing (a shopping list). Both have a tick-box per
/// row so they can be checked off by hand.</summary>
public interface IDecklistPrintExporter
{
    void ExportPullList(DecklistCheckResult result, string filePath);
    void ExportMissingList(DecklistCheckResult result, string filePath);
}
