using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Storage;

/// <summary>Thrown when cards would be added to or moved into a deck box whose assigned game differs
/// from the cards' game. Enforces the "a deck never crosses game systems" invariant. Controllers map
/// this to HTTP 409.</summary>
public sealed class DeckBoxGameMismatchException(string deckBoxName, CardGame deckBoxGame, CardGame offendingGame)
    : InvalidOperationException(
        $"\"{deckBoxName}\" is a {deckBoxGame} deck box and can't hold {offendingGame} cards.")
{
    public string DeckBoxName { get; } = deckBoxName;
    public CardGame DeckBoxGame { get; } = deckBoxGame;
    public CardGame OffendingGame { get; } = offendingGame;
}
