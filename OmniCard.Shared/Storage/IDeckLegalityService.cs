namespace OmniCard.Shared.Storage;

/// <summary>Evaluates a deck box's current contents against its assigned deck type's build rules and
/// returns advisory (non-blocking) warnings.</summary>
public interface IDeckLegalityService
{
    /// <summary>Checks the deck box with the given id. Returns an empty-warning result when the box
    /// isn't a deck box, has no deck type, or satisfies every rule.</summary>
    DeckLegality Check(int deckBoxId);
}
