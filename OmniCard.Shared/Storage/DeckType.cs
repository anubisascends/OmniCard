using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Storage;

/// <summary>A named deck format for a single game (e.g. Magic "Commander", "Standard"). Per-game
/// reference data in the unified store: the app seeds a built-in set on startup and users may add
/// their own. A <see cref="StorageContainer"/> of type <see cref="ContainerType.DeckBox"/> points at
/// one via <see cref="StorageContainer.DeckTypeId"/>. The nullable rule fields drive non-blocking
/// deck-legality warnings — a null rule means "no constraint".</summary>
public class DeckType
{
    public int Id { get; set; }
    public CardGame Game { get; set; }
    public string Name { get; set; } = "";

    /// <summary>True for a seeded default. Built-ins may still be renamed/edited by the user; the
    /// seeder keys off <see cref="BuiltInKey"/>, not <see cref="Name"/>, so a rename survives re-seed.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Stable seed key for built-ins (e.g. <c>"mtg.commander"</c>) so re-seeding is
    /// idempotent even after the user renames the type. Null for user-created custom types.</summary>
    public string? BuiltInKey { get; set; }

    public int SortOrder { get; set; }

    // --- Build rules (all nullable/zero = "no constraint"). Warnings only, never blocking. ---

    /// <summary>Minimum legal main-deck size (e.g. 60 for Standard, 100 for Commander). Null = no minimum.</summary>
    public int? DeckSizeMin { get; set; }

    /// <summary>Maximum legal main-deck size (e.g. 100 for Commander). Null = no maximum.</summary>
    public int? DeckSizeMax { get; set; }

    /// <summary>Maximum copies of any one card (e.g. 4 for Standard, 1 for Commander). Null = no limit.
    /// Ignored for basic lands when <see cref="BasicLandsExempt"/> is set.</summary>
    public int? MaxCopiesPerCard { get; set; }

    /// <summary>Singleton format — at most one of each card. Equivalent to a copy limit of 1 but kept
    /// distinct so the UI can label the format correctly. Basics exempt when <see cref="BasicLandsExempt"/>.</summary>
    public bool Singleton { get; set; }

    /// <summary>Basic lands (and other unlimited-copy staples) are exempt from the singleton/copy limit.</summary>
    public bool BasicLandsExempt { get; set; }

    /// <summary>When true, a card's copy identity for the singleton/copy-limit rule is its printed card
    /// number (<see cref="Product.CollectorNumber"/>) rather than its name — so every art of the same
    /// number counts together toward the limit. One Piece TCG works this way (max 4 per card number,
    /// e.g. OP01-001). Cards with no collector number fall back to name-based counting.</summary>
    public bool CopiesCountByCollectorNumber { get; set; }

    /// <summary>How many commander/leader cards the deck expects (Commander = 1; leader-based games = 1;
    /// most 60-card formats = 0).</summary>
    public int CommanderSlots { get; set; }
}
