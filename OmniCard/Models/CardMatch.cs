namespace OmniCard.Models;

public class CardMatch
{
    public string Name { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string CollectorNumber { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string? ImageUri { get; init; }
    public string GameSpecificId { get; init; } = "";
    public string? LocalImagePath { get; init; }

    /// <summary>Confidence percentage (0-100) based on match distance.</summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// The full game-specific card object (Card for MTG, OptcgCard for One Piece).
    /// Used by the detail panel DataTemplates to display game-specific fields.
    /// </summary>
    public object Source { get; init; } = null!;
}
