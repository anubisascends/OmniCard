using OmniCard.Shared.Lists;

namespace OmniCard.Shared.Audit;

/// <summary>Printable checklists for a decklist check: the cards to pull from the collection (with
/// where to find each copy) and the cards still missing (a shopping list). Both have a tick-box per
/// row so they can be checked off by hand. Saved lists reuse them as their pick / buy lists (with their
/// own titles) and add a whole-list print.</summary>
public interface IDecklistPrintExporter
{
    void ExportPullList(DecklistCheckResult result, string filePath, string title = "Pull List");
    void ExportMissingList(DecklistCheckResult result, string filePath, string title = "Missing Cards");

    /// <summary>The whole saved list: every line with its quantity, how many the collection already
    /// covers, and its price, plus value totals.</summary>
    void ExportCardList(string listName, IReadOnlyList<ListPrintLine> lines, string filePath);
}

/// <summary>One row of a printed saved list.</summary>
public sealed record ListPrintLine(
    string CardName, string? SetCode, string? CollectorNumber, bool IsFoil, int Quantity, int OwnedQuantity, decimal? Price);
