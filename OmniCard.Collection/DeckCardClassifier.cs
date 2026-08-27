namespace OmniCard.Collection;

/// <summary>Axis a deck box's cards can be grouped/stacked by. <see cref="None"/> is the flat
/// ungrouped view (today's behaviour).</summary>
public enum DeckGroupAxis
{
    None,
    Type,
    ManaValue,
}

/// <summary>The group a card lands in for a given <see cref="DeckGroupAxis"/>: a display
/// <see cref="Key"/> (also the heading text) and a <see cref="SortOrder"/> that orders the groups.</summary>
public readonly record struct DeckGroup(string Key, int SortOrder);

/// <summary>Classifies a single card into its deck-view group for a chosen axis, from catalog data
/// (Magic type line + converted mana value) and the card's reserved tags. Pure and game-agnostic so
/// the WPF app and the read-only web companion group decks identically. Categories (Ramp/Removal/…)
/// are intentionally out of scope for v1 — they can't be derived and need a category source.</summary>
public static class DeckCardClassifier
{
    /// <summary>Reserved tag: a lot tagged this is the deck's commander (floats to its own group and
    /// marks the deck as a Commander deck). Mirrors <see cref="SideboardTag"/>.</summary>
    public const string CommanderTag = "commander";

    /// <summary>Reserved tag: a lot tagged this is in the sideboard (split out of the main groups).</summary>
    public const string SideboardTag = "sideboard";

    // Type bucket display order (alphabetical, matching the user's spec), with Commander pinned first
    // and Sideboard/Other pinned last. The heading string is the dictionary key.
    private const int CommanderOrder = 0;
    private const int OtherOrder = 100;
    private const int SideboardOrder = 101;

    // Ordered (heading, keyword) precedence for resolving a multi-type card (e.g. "Artifact Creature")
    // into a single bucket — first keyword found in the type line wins, so Creature beats Artifact.
    private static readonly (string Heading, string Keyword, int SortOrder)[] TypeBuckets =
    [
        ("Creature",     "creature",     3),
        ("Planeswalker", "planeswalker", 7),
        ("Battle",       "battle",       2),
        ("Instant",      "instant",      5),
        ("Sorcery",      "sorcery",      8),
        ("Artifact",     "artifact",     1),
        ("Enchantment",  "enchantment",  4),
        ("Land",         "land",         6),
    ];

    public static DeckGroup Classify(DeckGroupAxis axis, string? typeLine, double? cmc, IEnumerable<string>? tags)
    {
        var (isCommander, isSideboard) = ReadReservedTags(tags);

        return axis switch
        {
            DeckGroupAxis.Type => ClassifyByType(typeLine, isCommander, isSideboard),
            DeckGroupAxis.ManaValue => ClassifyByManaValue(cmc, isSideboard),
            _ => new DeckGroup("All", 0),
        };
    }

    private static (bool IsCommander, bool IsSideboard) ReadReservedTags(IEnumerable<string>? tags)
    {
        if (tags is null) return (false, false);
        bool commander = false, sideboard = false;
        foreach (var t in tags)
        {
            if (string.Equals(t, CommanderTag, StringComparison.OrdinalIgnoreCase)) commander = true;
            else if (string.Equals(t, SideboardTag, StringComparison.OrdinalIgnoreCase)) sideboard = true;
        }
        return (commander, sideboard);
    }

    private static DeckGroup ClassifyByType(string? typeLine, bool isCommander, bool isSideboard)
    {
        // Reserved tags win over the printed type line: a commander (a legendary creature) would
        // otherwise fall in the Creature bucket, and a sideboarded card in its normal type bucket.
        if (isCommander) return new DeckGroup("Commander", CommanderOrder);
        if (isSideboard) return new DeckGroup("Sideboard", SideboardOrder);

        var line = typeLine?.ToLowerInvariant() ?? "";
        foreach (var (heading, keyword, order) in TypeBuckets)
            if (line.Contains(keyword, StringComparison.Ordinal))
                return new DeckGroup(heading, order);

        return new DeckGroup("Other", OtherOrder);
    }

    private static DeckGroup ClassifyByManaValue(double? cmc, bool isSideboard)
    {
        // Sideboard is split out of the curve; the commander stays in its normal mana-value bucket.
        if (isSideboard) return new DeckGroup("Sideboard", SideboardOrder);

        var mv = (int)Math.Round(cmc ?? 0, MidpointRounding.AwayFromZero);
        if (mv < 0) mv = 0;
        return mv >= 7
            ? new DeckGroup("7+", 7)
            : new DeckGroup(mv.ToString(), mv);
    }
}
