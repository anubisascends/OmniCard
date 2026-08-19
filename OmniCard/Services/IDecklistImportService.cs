using OmniCard.Models;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Services;

public interface IDecklistImportService
{
    /// <summary>Parse + resolve a decklist file's text against the active game into preview rows.</summary>
    IReadOnlyList<DecklistImportRow> ResolveFile(string fileText);

    /// <summary>Resolve already-parsed decklist entries (e.g. from a URL fetch) into preview rows.</summary>
    IReadOnlyList<DecklistImportRow> ResolveEntries(IEnumerable<DecklistEntry> entries);

    /// <summary>Add resolved rows to a list; returns total cards added (sum of quantities). When
    /// <paramref name="defaultFoil"/> is set, rows are added as foil with the given finish (or the
    /// game's basic finish when <paramref name="defaultFoilType"/> is null).</summary>
    int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows, bool defaultFoil = false, string? defaultFoilType = null);

    /// <summary>Add resolved rows to a location; returns total cards added (sum of quantities). See
    /// <see cref="CommitToList"/> for the foil-treatment parameters.</summary>
    int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows, bool defaultFoil = false, string? defaultFoilType = null);
}
