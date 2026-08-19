namespace OmniCard.Models;

/// <summary>
/// Single source of truth for foil-finish vocabulary per game. A card's <c>FoilType</c> is a
/// sub-classification of the boolean foil flag (a Rainbow Foil and a Cold Foil of the same print
/// are different finishes with different values). The lists here are curated starters shown in the
/// finish dropdowns — the field itself is free-text, so users can add finishes not listed.
/// </summary>
public static class FoilTypes
{
    // "Other" is appended so users have an escape hatch in the dropdown; the field also accepts
    // any typed value. The first "real" entry of each list is the game's basic/most-common finish.
    private static readonly IReadOnlyList<string> Mtg =
        ["Foil", "Etched", "Rainbow", "Textured", "Galaxy", "Surge", "Halo", "Confetti",
         "Neon Ink", "Gilded", "Serialized", "Oil Slick", "Other"];

    private static readonly IReadOnlyList<string> Pokemon =
        ["Holofoil", "Reverse Holofoil", "Cosmos Holo", "Poké Ball Holo", "Master Ball Holo",
         "Confetti", "Other"];

    // Yu-Gi-Oh! foiling is expressed through rarities; "Foil" is the generic basic finish used for
    // backfill, followed by the specific holo rarities.
    private static readonly IReadOnlyList<string> YuGiOh =
        ["Foil", "Ultra Rare", "Secret Rare", "Ultimate Rare", "Ghost Rare", "Starlight Rare",
         "Gold Rare", "Prismatic Secret Rare", "Other"];

    private static readonly IReadOnlyList<string> FinalFantasy = ["Premium"];

    private static readonly IReadOnlyList<string> Riftbound = ["Foil", "Other"];

    private static readonly IReadOnlyList<string> OnePiece =
        ["Foil", "Alternate Art", "Manga", "Parallel", "Other"];

    /// <summary>Curated finish presets for the given game (for dropdowns). Never empty.</summary>
    public static IReadOnlyList<string> ForGame(CardGame game) => game switch
    {
        CardGame.Mtg => Mtg,
        CardGame.Pokemon => Pokemon,
        CardGame.YuGiOh => YuGiOh,
        CardGame.FinalFantasy => FinalFantasy,
        CardGame.Riftbound => Riftbound,
        CardGame.OnePiece => OnePiece,
        _ => ["Foil"],
    };

    /// <summary>
    /// The finish assigned to existing/imported foil cards that have no explicit finish yet — the
    /// most common foil for the game. Used by the one-time backfill and as a sensible fallback.
    /// </summary>
    public static string BasicFoilType(CardGame game) => game switch
    {
        CardGame.Pokemon => "Holofoil",
        CardGame.FinalFantasy => "Premium",
        _ => "Foil",
    };
}
