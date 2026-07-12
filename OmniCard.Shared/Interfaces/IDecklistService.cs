using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IDecklistService
{
    Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url);
    (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text);
    DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries);
}
