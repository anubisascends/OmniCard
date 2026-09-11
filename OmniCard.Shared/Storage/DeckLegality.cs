namespace OmniCard.Shared.Storage;

/// <summary>Result of checking a deck box's contents against its deck type's construction rules.
/// Advisory only — warnings never block any action.</summary>
public sealed class DeckLegality
{
    /// <summary>True when there are no warnings (the deck satisfies every rule of its type). Also true
    /// when the box has no deck type assigned (nothing to check against).</summary>
    public bool Ok => Warnings.Count == 0;

    /// <summary>The deck type's display name, or null if the box has none assigned.</summary>
    public string? DeckTypeName { get; init; }

    /// <summary>Cards in the main deck (excludes cards tagged sideboard and the commander(s)).</summary>
    public int MainDeckCount { get; init; }

    /// <summary>Number of cards tagged as the deck's commander/leader.</summary>
    public int CommanderCount { get; init; }

    /// <summary>Whole-deck card count used for deck-size rules: main deck plus the command-zone
    /// card(s). For a legal Commander deck this is 99 + 1 = 100.</summary>
    public int TotalDeckCount { get; init; }

    public IReadOnlyList<DeckLegalityWarning> Warnings { get; init; } = [];
}

/// <summary>A single rule violation (or advisory) surfaced to the user.</summary>
public sealed record DeckLegalityWarning(string Code, string Message);
