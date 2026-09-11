using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Storage;

/// <summary>A deck box that predates the game-system feature and has no game assigned. Carries the
/// games of the cards currently inside it so the UI can pre-select a suggestion: exactly one distinct
/// game means <see cref="InferredGame"/> is set; empty or mixed means the user must pick.</summary>
public sealed class DeckBoxNeedsGame
{
    public int Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>The single game of every card in the box, or null when the box is empty or holds
    /// more than one game (ambiguous — user must choose).</summary>
    public CardGame? InferredGame { get; init; }

    /// <summary>Distinct games present among the box's cards (for display when ambiguous).</summary>
    public IReadOnlyList<CardGame> CardGames { get; init; } = [];
}
