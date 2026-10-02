using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Lists;

public interface IDecklistService
{
    Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url);
    (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text);
    List<DecklistEntry> ParseDecklistPrintings(string text);
    /// <summary>Owned-vs-missing check of a decklist. <paramref name="siteIds"/> limits the owned
    /// copies considered to locations in those sites (null = every site) so a user is never told to
    /// pull a card from a site they can't see.</summary>
    DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game,
        IReadOnlyCollection<int>? siteIds = null);
}
