using OmniCard.Models;

namespace OmniCard.Services;

public static class SealedProductArchetypeRegistry
{
    private static readonly Dictionary<SealedProductType, SealedProductArchetype> Archetypes = new()
    {
        // Cases
        [SealedProductType.Case] = new("{Set} Case", [], ArchetypeTier.Case),

        // Boxes
        [SealedProductType.PlayBoosterBox] = new("{Set} Play Booster Box",
            [new(36, SealedProductType.PlayBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.DraftBoosterBox] = new("{Set} Draft Booster Box",
            [new(36, SealedProductType.DraftBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.SetBoosterBox] = new("{Set} Set Booster Box",
            [new(30, SealedProductType.SetBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.CollectorBoosterBox] = new("{Set} Collector Booster Box",
            [new(12, SealedProductType.CollectorBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.ThemeBoosterBox] = new("{Set} Theme Booster Box",
            [new(12, SealedProductType.ThemeBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.BoosterBox] = new("{Set} Booster Box",
            [new(36, SealedProductType.BoosterPack)], ArchetypeTier.Box),

        // Packs (leaf nodes — no contents)
        [SealedProductType.PlayBoosterPack] = new("{Set} Play Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.DraftBoosterPack] = new("{Set} Draft Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.SetBoosterPack] = new("{Set} Set Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.CollectorBoosterPack] = new("{Set} Collector Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.ThemeBoosterPack] = new("{Set} Theme Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.BoosterPack] = new("{Set} Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.PromoPack] = new("{Set} Promo Pack", [], ArchetypeTier.Pack),

        // Bundles & Kits
        [SealedProductType.Bundle] = new("{Set} Bundle",
            [new(8, SealedProductType.PlayBoosterPack)], ArchetypeTier.Kit),
        [SealedProductType.GiftBundle] = new("{Set} Gift Bundle",
            [new(10, SealedProductType.PlayBoosterPack)], ArchetypeTier.Kit),
        [SealedProductType.FatPack] = new("{Set} Fat Pack",
            [new(9, SealedProductType.BoosterPack)], ArchetypeTier.Kit),
        [SealedProductType.PrereleaseKit] = new("{Set} Prerelease Kit",
            [new(6, SealedProductType.PlayBoosterPack), new(1, SealedProductType.PromoPack)], ArchetypeTier.Kit),
        [SealedProductType.StarterKit] = new("{Set} Starter Kit",
            [new(2, SealedProductType.FixedPack)], ArchetypeTier.Kit),

        // Decks
        [SealedProductType.CommanderDeck] = new("{Set} Commander Deck",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Deck),
        [SealedProductType.PlaneswalkerDeck] = new("{Set} Planeswalker Deck",
            [new(1, SealedProductType.FixedPack), new(1, SealedProductType.BoosterPack)], ArchetypeTier.Deck),
        [SealedProductType.IntroPack] = new("{Set} Intro Pack",
            [new(1, SealedProductType.FixedPack), new(2, SealedProductType.BoosterPack)], ArchetypeTier.Deck),
        [SealedProductType.ThemeDeck] = new("{Set} Theme Deck",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Deck),
        [SealedProductType.IntroDeck] = new("{Set} Intro Deck",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Deck),
        [SealedProductType.WelcomeDeck] = new("{Set} Welcome Deck",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Deck),
        [SealedProductType.FixedPack] = new("{Set} Fixed Pack", [], ArchetypeTier.Pack),

        // Special
        [SealedProductType.SecretLair] = new("Secret Lair: {Set}",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Special),
        [SealedProductType.FromTheVault] = new("From the Vault: {Set}",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Special),
        [SealedProductType.BlisterPack] = new("{Set} Blister Pack",
            [new(3, SealedProductType.BoosterPack)], ArchetypeTier.Pack),

        // Terminal
        [SealedProductType.Card] = new("{Set} Card", [], ArchetypeTier.Card),
    };

    public static SealedProductArchetype GetArchetype(SealedProductType type) =>
        Archetypes[type];

    public static string GetDisplayName(SealedProductType type) =>
        Archetypes[type].NamePattern
            .Replace("{Set} ", "")
            .Replace("{Set}", "")
            .Replace("Secret Lair: ", "Secret Lair")
            .Replace("From the Vault: ", "From the Vault")
            .Trim() switch
        {
            "" => type.ToString(),
            var name => name
        };

    public static ArchetypeTier GetTier(SealedProductType type) =>
        Archetypes[type].Tier;

    public static string GenerateTemplateName(SealedProductType type, string? setName)
    {
        var name = setName ?? "Generic";
        return Archetypes[type].NamePattern.Replace("{Set}", name);
    }

    public static IReadOnlyList<IGrouping<ArchetypeTier, SealedProductType>> GetTypesGroupedByTier() =>
        Archetypes
            .GroupBy(kv => kv.Value.Tier, kv => kv.Key)
            .OrderBy(g => g.Key)
            .ToList();
}
