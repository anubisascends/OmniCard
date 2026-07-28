using OmniCard.Models;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Services;

public interface IDecklistImportService
{
    /// <summary>Parse + resolve a decklist file's text against the active game into preview rows.</summary>
    IReadOnlyList<DecklistImportRow> ResolveFile(string fileText);

    /// <summary>Add resolved rows to a list; returns total cards added (sum of quantities).</summary>
    int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows);

    /// <summary>Add resolved rows to a location; returns total cards added (sum of quantities).</summary>
    int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows);
}
