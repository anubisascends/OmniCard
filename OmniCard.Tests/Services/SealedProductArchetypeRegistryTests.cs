using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class SealedProductArchetypeRegistryTests
{
    [Fact]
    public void AllSealedProductTypes_HaveArchetypes()
    {
        foreach (var type in Enum.GetValues<SealedProductType>())
        {
            var archetype = SealedProductArchetypeRegistry.GetArchetype(type);
            Assert.NotNull(archetype);
        }
    }

    [Fact]
    public void GetDisplayName_ReturnsHumanReadable()
    {
        Assert.Equal("Play Booster Box", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.PlayBoosterBox));
        Assert.Equal("Collector Booster Pack", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.CollectorBoosterPack));
        Assert.Equal("Commander Deck", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.CommanderDeck));
        Assert.Equal("Fat Pack", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.FatPack));
        Assert.Equal("Card", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.Card));
    }

    [Fact]
    public void GetTier_ReturnsCorrectTiers()
    {
        Assert.Equal(ArchetypeTier.Case, SealedProductArchetypeRegistry.GetTier(SealedProductType.Case));
        Assert.Equal(ArchetypeTier.Box, SealedProductArchetypeRegistry.GetTier(SealedProductType.PlayBoosterBox));
        Assert.Equal(ArchetypeTier.Pack, SealedProductArchetypeRegistry.GetTier(SealedProductType.BoosterPack));
        Assert.Equal(ArchetypeTier.Deck, SealedProductArchetypeRegistry.GetTier(SealedProductType.CommanderDeck));
        Assert.Equal(ArchetypeTier.Kit, SealedProductArchetypeRegistry.GetTier(SealedProductType.Bundle));
        Assert.Equal(ArchetypeTier.Special, SealedProductArchetypeRegistry.GetTier(SealedProductType.SecretLair));
        Assert.Equal(ArchetypeTier.Card, SealedProductArchetypeRegistry.GetTier(SealedProductType.Card));
    }

    [Fact]
    public void GenerateTemplateName_SubstitutesSetName()
    {
        var name = SealedProductArchetypeRegistry.GenerateTemplateName(SealedProductType.PlayBoosterBox, "Modern Horizons 3");
        Assert.Equal("Modern Horizons 3 Play Booster Box", name);
    }

    [Fact]
    public void GenerateTemplateName_NullSetName_UsesGeneric()
    {
        var name = SealedProductArchetypeRegistry.GenerateTemplateName(SealedProductType.PlayBoosterBox, null);
        Assert.Equal("Generic Play Booster Box", name);
    }

    [Fact]
    public void GenerateTemplateName_SecretLair_UsesColonFormat()
    {
        var name = SealedProductArchetypeRegistry.GenerateTemplateName(SealedProductType.SecretLair, "Featuring: Bloodghast");
        Assert.Equal("Secret Lair: Featuring: Bloodghast", name);
    }

    [Fact]
    public void PlayBoosterBox_HasCorrectDefaults()
    {
        var archetype = SealedProductArchetypeRegistry.GetArchetype(SealedProductType.PlayBoosterBox);
        Assert.Equal(ArchetypeTier.Box, archetype.Tier);
        Assert.Single(archetype.DefaultContents);
        Assert.Equal(36, archetype.DefaultContents[0].Quantity);
        Assert.Equal(SealedProductType.PlayBoosterPack, archetype.DefaultContents[0].ChildType);
    }

    [Fact]
    public void PrereleaseKit_HasMultipleContentLines()
    {
        var archetype = SealedProductArchetypeRegistry.GetArchetype(SealedProductType.PrereleaseKit);
        Assert.Equal(ArchetypeTier.Kit, archetype.Tier);
        Assert.Equal(2, archetype.DefaultContents.Count);
        Assert.Contains(archetype.DefaultContents, c => c.ChildType == SealedProductType.PlayBoosterPack && c.Quantity == 6);
        Assert.Contains(archetype.DefaultContents, c => c.ChildType == SealedProductType.PromoPack && c.Quantity == 1);
    }

    [Fact]
    public void LeafTypes_HaveEmptyContents()
    {
        var leafTypes = new[]
        {
            SealedProductType.PlayBoosterPack, SealedProductType.DraftBoosterPack,
            SealedProductType.SetBoosterPack, SealedProductType.CollectorBoosterPack,
            SealedProductType.ThemeBoosterPack, SealedProductType.BoosterPack,
            SealedProductType.PromoPack, SealedProductType.FixedPack, SealedProductType.Card,
        };

        foreach (var type in leafTypes)
        {
            var archetype = SealedProductArchetypeRegistry.GetArchetype(type);
            Assert.Empty(archetype.DefaultContents);
        }
    }

    [Fact]
    public void GetTypesGroupedByTier_ReturnsAllTypes()
    {
        var grouped = SealedProductArchetypeRegistry.GetTypesGroupedByTier();
        var allTypes = grouped.SelectMany(g => g).ToList();
        var enumValues = Enum.GetValues<SealedProductType>().ToList();
        Assert.Equal(enumValues.Count, allTypes.Count);
    }
}
