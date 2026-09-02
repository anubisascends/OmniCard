using OmniCard.Collection;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

/// <summary>The in-memory Scryfall-syntax filter used by the binder import-audit tray. Kept in
/// lockstep with CollectionQueryBuilder's SQL semantics.</summary>
public class CollectionCardMatcherTests
{
    private static List<CollectionCard> Sample() =>
    [
        new() { Name = "Lightning Bolt", SetCode = "LEA", Number = "161", Rarity = "common", Color = "R", IsFoil = false, Condition = "NM" },
        new() { Name = "Counterspell", SetCode = "LEB", Number = "54", Rarity = "uncommon", Color = "U", IsFoil = true, Condition = "LP" },
        new() { Name = "Shivan Dragon", SetCode = "LEA", Number = "175", Rarity = "rare", Color = "R", IsFoil = false, Condition = "NM" },
        new() { Name = "Niv-Mizzet", SetCode = "GPT", Number = "159", Rarity = "rare", Color = "UR", IsFoil = false, Condition = "NM" },
    ];

    [Fact]
    public void NameContains_IsCaseInsensitiveSubstring()
    {
        var r = CollectionCardMatcher.Filter(Sample(), "bolt");
        Assert.Single(r);
        Assert.Equal("Lightning Bolt", r[0].Name);
    }

    [Fact]
    public void Set_MatchesExactCode()
    {
        var r = CollectionCardMatcher.Filter(Sample(), "set:lea");
        Assert.Equal(2, r.Count);
        Assert.All(r, c => Assert.Equal("LEA", c.SetCode));
    }

    [Fact]
    public void Rarity_GreaterOrEqual_UsesRankLadder()
    {
        var r = CollectionCardMatcher.Filter(Sample(), "r>=rare");
        Assert.Equal(2, r.Count);
        Assert.All(r, c => Assert.Equal("rare", c.Rarity));
    }

    [Fact]
    public void Foil_Filters()
    {
        var r = CollectionCardMatcher.Filter(Sample(), "is:foil");
        Assert.Single(r);
        Assert.Equal("Counterspell", r[0].Name);
    }

    [Fact]
    public void Color_Superset_MatchesMulticolorContainingColor()
    {
        // c:u → cards whose colors include U: Counterspell (U) and Niv-Mizzet (UR).
        var r = CollectionCardMatcher.Filter(Sample(), "c:u");
        Assert.Equal(2, r.Count);
        Assert.Contains(r, c => c.Name == "Counterspell");
        Assert.Contains(r, c => c.Name == "Niv-Mizzet");
    }

    [Fact]
    public void CollectorNumber_ExactMatch()
    {
        var r = CollectionCardMatcher.Filter(Sample(), "cn:161");
        Assert.Single(r);
        Assert.Equal("Lightning Bolt", r[0].Name);
    }

    [Fact]
    public void Negation_Excludes()
    {
        var r = CollectionCardMatcher.Filter(Sample(), "-set:lea");
        Assert.Equal(2, r.Count);
        Assert.DoesNotContain(r, c => c.SetCode == "LEA");
    }

    [Fact]
    public void EmptyQuery_ReturnsAll()
    {
        Assert.Equal(4, CollectionCardMatcher.Filter(Sample(), "").Count);
        Assert.Equal(4, CollectionCardMatcher.Filter(Sample(), "   ").Count);
    }
}
