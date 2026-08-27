using OmniCard.Collection;
using Xunit;

namespace OmniCard.Tests.Services;

public class DeckCardClassifierTests
{
    [Theory]
    [InlineData("Creature — Elf Druid", "Creature")]
    [InlineData("Legendary Creature — Human Wizard", "Creature")]
    [InlineData("Instant", "Instant")]
    [InlineData("Sorcery", "Sorcery")]
    [InlineData("Artifact", "Artifact")]
    [InlineData("Enchantment — Aura", "Enchantment")]
    [InlineData("Land", "Land")]
    [InlineData("Basic Land — Forest", "Land")]
    [InlineData("Legendary Planeswalker — Jace", "Planeswalker")]
    [InlineData("Battle — Siege", "Battle")]
    public void ClassifyByType_MapsSingleTypeLines(string typeLine, string expected)
    {
        var group = DeckCardClassifier.Classify(DeckGroupAxis.Type, typeLine, cmc: null, tags: null);
        Assert.Equal(expected, group.Key);
    }

    [Theory]
    // Multi-type cards resolve by precedence — Creature beats Artifact/Enchantment/Land.
    [InlineData("Artifact Creature — Golem", "Creature")]
    [InlineData("Enchantment Creature — God", "Creature")]
    [InlineData("Land Creature — Dryad Arbor", "Creature")]
    [InlineData("Artifact Land", "Artifact")]
    public void ClassifyByType_ResolvesMultiTypeByPrecedence(string typeLine, string expected)
    {
        var group = DeckCardClassifier.Classify(DeckGroupAxis.Type, typeLine, cmc: null, tags: null);
        Assert.Equal(expected, group.Key);
    }

    [Fact]
    public void ClassifyByType_UnknownOrMissingTypeLine_GoesToOther()
    {
        Assert.Equal("Other", DeckCardClassifier.Classify(DeckGroupAxis.Type, "Scheme", null, null).Key);
        Assert.Equal("Other", DeckCardClassifier.Classify(DeckGroupAxis.Type, null, null, null).Key);
        Assert.Equal("Other", DeckCardClassifier.Classify(DeckGroupAxis.Type, "", null, null).Key);
    }

    [Fact]
    public void ClassifyByType_CommanderTag_OverridesTypeLine()
    {
        var group = DeckCardClassifier.Classify(
            DeckGroupAxis.Type, "Legendary Creature — Human", cmc: 4, tags: ["Commander"]);
        Assert.Equal("Commander", group.Key);
        Assert.Equal(0, group.SortOrder); // pinned first
    }

    [Fact]
    public void ClassifyByType_SideboardTag_OverridesTypeLine()
    {
        var group = DeckCardClassifier.Classify(
            DeckGroupAxis.Type, "Instant", cmc: 2, tags: ["sideboard"]);
        Assert.Equal("Sideboard", group.Key);
        Assert.True(group.SortOrder > 100); // pinned last, after Other
    }

    [Theory]
    [InlineData(0, "0", 0)]
    [InlineData(1, "1", 1)]
    [InlineData(3, "3", 3)]
    [InlineData(6, "6", 6)]
    [InlineData(7, "7+", 7)]
    [InlineData(12, "7+", 7)]
    public void ClassifyByManaValue_BucketsByConvertedCost(double cmc, string expectedKey, int expectedOrder)
    {
        var group = DeckCardClassifier.Classify(DeckGroupAxis.ManaValue, "Creature", cmc, tags: null);
        Assert.Equal(expectedKey, group.Key);
        Assert.Equal(expectedOrder, group.SortOrder);
    }

    [Fact]
    public void ClassifyByManaValue_NullCmc_BucketsAsZero()
    {
        var group = DeckCardClassifier.Classify(DeckGroupAxis.ManaValue, "Land", cmc: null, tags: null);
        Assert.Equal("0", group.Key);
    }

    [Fact]
    public void ClassifyByManaValue_SideboardSplitsOut_ButCommanderStaysInBucket()
    {
        var sideboard = DeckCardClassifier.Classify(DeckGroupAxis.ManaValue, "Instant", cmc: 2, tags: ["sideboard"]);
        Assert.Equal("Sideboard", sideboard.Key);

        // A commander is not split out of the mana curve — it sits in its CMC bucket.
        var commander = DeckCardClassifier.Classify(DeckGroupAxis.ManaValue, "Legendary Creature", cmc: 5, tags: ["commander"]);
        Assert.Equal("5", commander.Key);
    }

    [Fact]
    public void Classify_NoneAxis_SingleAllGroup()
    {
        var group = DeckCardClassifier.Classify(DeckGroupAxis.None, "Creature", cmc: 3, tags: null);
        Assert.Equal("All", group.Key);
    }
}
