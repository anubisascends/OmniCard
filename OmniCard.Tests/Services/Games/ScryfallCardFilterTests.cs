using OmniCard.CardMatching.Search;
using OmniCard.Shared.Cards;

namespace OmniCard.Tests.Services.Games;

/// <summary>
/// Exercises the in-memory Scryfall matcher (<see cref="ScryfallCardFilter"/>) across the full syntax
/// surface, parsing each query with the MTG catalog schema exactly as <c>SearchCards</c> does.
/// </summary>
public class ScryfallCardFilterTests
{
    private static bool Match(Card c, string query)
    {
        var node = ScryfallQueryParser.ParseFilter(query, MtgSearchSchema.Catalog);
        return node is not null && ScryfallCardFilter.Matches(c, node);
    }

    private static Card Bolt() => new()
    {
        Name = "Lightning Bolt", SetCode = "lea", SetName = "Limited Edition Alpha", SetType = "core",
        CollectorNumber = "161", TypeLine = "Instant", OracleText = "Lightning Bolt deals 3 damage to any target.",
        ManaCost = "{R}", Cmc = 1, Colors = ["R"], ColorIdentity = ["R"], Rarity = "common",
        Keywords = [], Games = ["paper", "mtgo"], Artist = "Christopher Rush", Layout = "normal",
        BorderColor = "black", Frame = "1993", Lang = "en", ReleasedAt = "1993-08-05",
        Legalities = new() { ["modern"] = "not_legal", ["legacy"] = "legal", ["vintage"] = "restricted" },
        Prices = new Prices { Usd = "3.50" },
    };

    private static Card Dragon() => new()
    {
        Name = "Shivan Dragon", SetCode = "m19", SetName = "Core Set 2019", SetType = "core",
        CollectorNumber = "217", TypeLine = "Creature — Dragon", OracleText = "Flying\n{R}: Shivan Dragon gets +1/+0 until end of turn.",
        ManaCost = "{4}{R}{R}", Cmc = 6, Power = "5", Toughness = "5", Colors = ["R"], ColorIdentity = ["R"],
        Rarity = "rare", Keywords = ["Flying"], Games = ["paper", "arena"], Artist = "Donato Giancola",
        Layout = "normal", BorderColor = "black", Frame = "2015", Lang = "en", ReleasedAt = "2018-07-13",
        FlavorText = "Fire is its ally.", Watermark = "izzet",
        Legalities = new() { ["modern"] = "legal", ["standard"] = "not_legal" },
        Prices = new Prices { Usd = "0.35" }, ProducedMana = ["R"],
    };

    private static Card Teferi() => new()
    {
        Name = "Teferi, Hero of Dominaria", SetCode = "dom", SetName = "Dominaria", SetType = "expansion",
        CollectorNumber = "207", TypeLine = "Legendary Planeswalker — Teferi", OracleText = "+1: Draw a card.",
        ManaCost = "{3}{W}{U}", Cmc = 5, Loyalty = "4", Colors = ["W", "U"], ColorIdentity = ["W", "U"],
        Rarity = "mythic", Keywords = [], Games = ["paper", "mtgo", "arena"], Artist = "Chris Rallis",
        Layout = "normal", BorderColor = "black", Frame = "2015", FrameEffects = ["legendary"],
        Lang = "en", ReleasedAt = "2018-04-27",
        Legalities = new() { ["modern"] = "legal", ["commander"] = "legal" },
        Prices = new Prices { Usd = "12.00", Eur = "10.50", Tix = "1.20" },
    };

    // ---- Comparison operators & mana value ----

    [Theory]
    [InlineData("cmc>=6", true)]
    [InlineData("cmc>6", false)]
    [InlineData("cmc=6", true)]
    [InlineData("cmc<=6", true)]
    [InlineData("cmc<6", false)]
    [InlineData("cmc!=1", true)]
    [InlineData("mv:6", true)]
    [InlineData("manavalue>5", true)]
    public void Cmc_Comparisons(string query, bool expected) => Assert.Equal(expected, Match(Dragon(), query));

    // ---- Power / toughness / loyalty (numeric text columns) ----

    [Theory]
    [InlineData("pow>=5", true)]
    [InlineData("pow>5", false)]
    [InlineData("pow=5", true)]
    [InlineData("tou<3", false)]
    [InlineData("pow>=tou", true)]  // 5 >= 5
    [InlineData("pow>tou", false)]  // 5 > 5 is false
    public void PowerToughness_Comparisons(string query, bool expected) => Assert.Equal(expected, Match(Dragon(), query));

    [Fact]
    public void Loyalty_Comparison() => Assert.True(Match(Teferi(), "loy>=4"));

    [Fact]
    public void Power_NonCreature_DoesNotMatch() => Assert.False(Match(Bolt(), "pow>=1"));

    // ---- Colours & identity ----

    [Theory]
    [InlineData("c:r", false)]   // Teferi is W/U
    [InlineData("c:u", true)]
    [InlineData("c=wu", true)]
    [InlineData("c:wu", true)]
    [InlineData("c<=wubrg", true)]
    [InlineData("c>=1", true)]
    [InlineData("c>=3", false)]
    [InlineData("c:multicolor", true)]
    [InlineData("c:colorless", false)]
    public void Color_Semantics(string query, bool expected) => Assert.Equal(expected, Match(Teferi(), query));

    [Fact]
    public void Identity_Subset() => Assert.True(Match(Teferi(), "id<=wubrg"));

    [Fact]
    public void Colorless_MatchesArtifactLikeCard()
    {
        var c = Bolt(); c.Colors = []; c.ColorIdentity = [];
        Assert.True(Match(c, "c:colorless"));
        Assert.True(Match(c, "is:colorless"));
    }

    // ---- Types, oracle, keywords, mana cost ----

    [Fact]
    public void Type_Contains() => Assert.True(Match(Dragon(), "t:dragon"));

    [Fact]
    public void Oracle_Contains() => Assert.True(Match(Bolt(), "o:\"deals 3 damage\""));

    [Fact]
    public void Oracle_TildeSubstitutesName() => Assert.True(Match(Bolt(), "o:\"~ deals 3\""));

    [Fact]
    public void Keyword_Match() => Assert.True(Match(Dragon(), "kw:flying"));

    [Theory]
    [InlineData("m:{R}", true)]
    [InlineData("m:R", true)]
    [InlineData("m:{4}{R}{R}", false)] // Bolt is just {R}
    public void Mana_Cost(string query, bool expected) => Assert.Equal(expected, Match(Bolt(), query));

    [Fact]
    public void Mana_Cost_Exact() => Assert.True(Match(Dragon(), "m={4}{R}{R}"));

    // ---- Rarity ordering ----

    [Theory]
    [InlineData("r:rare", true)]
    [InlineData("r>=rare", true)]
    [InlineData("r>rare", false)]        // rare is not > rare
    [InlineData("r<mythic", true)]       // rare < mythic
    [InlineData("r>=uncommon", true)]
    public void Rarity_Ordinal(string query, bool expected) => Assert.Equal(expected, Match(Dragon(), query));

    // ---- Sets, artist, flavor, watermark, border, frame, game, produces ----

    [Fact] public void Set_Code() => Assert.True(Match(Dragon(), "s:m19"));
    [Fact] public void SetType() => Assert.True(Match(Teferi(), "st:expansion"));
    [Fact] public void Artist() => Assert.True(Match(Bolt(), "a:rush"));
    [Fact] public void Flavor() => Assert.True(Match(Dragon(), "ft:fire"));
    [Fact] public void Watermark() => Assert.True(Match(Dragon(), "wm:izzet"));
    [Fact] public void Border() => Assert.True(Match(Bolt(), "border:black"));
    [Fact] public void Frame_Year() => Assert.True(Match(Dragon(), "frame:2015"));
    [Fact] public void Frame_Effect() => Assert.True(Match(Teferi(), "frame:legendary"));
    [Fact] public void Game() => Assert.True(Match(Dragon(), "game:arena"));
    [Fact] public void Game_NegativeForBolt() => Assert.False(Match(Bolt(), "game:arena"));
    [Fact] public void Produces() => Assert.True(Match(Dragon(), "produces:r"));

    // ---- Prices, year, legality ----

    [Theory]
    [InlineData("usd<1", true)]     // Dragon 0.35
    [InlineData("usd>1", false)]
    public void Price_Usd(string query, bool expected) => Assert.Equal(expected, Match(Dragon(), query));

    [Fact] public void Price_Eur_Tix() => Assert.True(Match(Teferi(), "eur<20") && Match(Teferi(), "tix<5"));

    [Fact] public void Year_Bolt() => Assert.True(Match(Bolt(), "year:1993"));
    [Fact] public void Year_Dragon() => Assert.True(Match(Dragon(), "year>=2018"));

    [Fact] public void Format_Legal() => Assert.True(Match(Dragon(), "f:modern"));
    [Fact] public void Format_NotLegal() => Assert.False(Match(Dragon(), "f:standard"));
    [Fact] public void Banned_Is_ForBoltInModern() => Assert.False(Match(Bolt(), "f:modern"));
    [Fact] public void Restricted() => Assert.True(Match(Bolt(), "restricted:vintage"));

    // ---- is: / has: flags ----

    [Fact] public void Is_Permanent() => Assert.True(Match(Dragon(), "is:permanent"));
    [Fact] public void Is_Permanent_NotInstant() => Assert.False(Match(Bolt(), "is:permanent"));
    [Fact] public void Is_Spell() => Assert.True(Match(Bolt(), "is:spell"));
    [Fact] public void Is_Multicolor() => Assert.True(Match(Teferi(), "is:multicolor"));
    [Fact] public void Is_Commander() => Assert.False(Match(Dragon(), "is:commander"));
    [Fact] public void Has_Watermark() => Assert.True(Match(Dragon(), "has:watermark"));
    [Fact] public void Has_Watermark_None() => Assert.False(Match(Bolt(), "has:watermark"));

    [Fact]
    public void Is_Transform_ByLayout()
    {
        var c = Bolt(); c.Layout = "transform";
        Assert.True(Match(c, "is:transform"));
        Assert.True(Match(c, "is:dfc"));
    }

    [Fact]
    public void Is_Foil()
    {
        var c = Bolt(); c.Foil = true;
        Assert.True(Match(c, "is:foil"));
        Assert.False(Match(Bolt(), "is:foil"));
    }

    // ---- Boolean logic: negation, OR, parentheses, exact name ----

    [Fact] public void Negation() => Assert.True(Match(Dragon(), "-t:instant"));
    [Fact] public void Negation_Excludes() => Assert.False(Match(Bolt(), "-t:instant"));

    [Fact]
    public void Or_Expression()
    {
        Assert.True(Match(Bolt(), "t:instant or t:creature"));
        Assert.True(Match(Dragon(), "t:instant or t:creature"));
    }

    [Fact]
    public void Parentheses_Grouping()
    {
        // red AND (dragon OR instant)
        Assert.True(Match(Dragon(), "c:r (t:dragon or t:instant)"));
        Assert.False(Match(Teferi(), "c:r (t:dragon or t:instant)"));
    }

    [Fact]
    public void ExactName_Bang()
    {
        Assert.True(Match(Bolt(), "!\"Lightning Bolt\""));
        Assert.False(Match(Bolt(), "!\"Lightning\""));
    }

    [Fact]
    public void Combined_Query()
    {
        // A red creature with power 5+ that's legal in modern and costs under $1.
        Assert.True(Match(Dragon(), "c:r t:creature pow>=5 f:modern usd<1"));
        Assert.False(Match(Bolt(), "c:r t:creature pow>=5 f:modern usd<1"));
    }

    // ---- Directives ----

    [Fact]
    public void ExtractDirectives_PullsOrderAndUnique()
    {
        var node = ScryfallQueryParser.ParseFilter("t:creature order:cmc direction:desc unique:cards", MtgSearchSchema.Catalog);
        var (filter, directives) = ScryfallCardFilter.ExtractDirectives(node);
        Assert.Equal("cmc", directives.Order);
        Assert.True(directives.Descending);
        Assert.Equal("cards", directives.Unique);
        // The remaining filter still matches a creature and never a directive.
        Assert.NotNull(filter);
        Assert.True(ScryfallCardFilter.Matches(Dragon(), filter!));
    }

    [Fact]
    public void ApplyOrder_ByCmcDescending()
    {
        var cards = new[] { Bolt(), Teferi(), Dragon() }; // cmc 1, 5, 6
        var ordered = ScryfallCardFilter.ApplyOrder(cards, new SearchDirectives("cmc", Descending: true)).ToList();
        Assert.Equal(["Shivan Dragon", "Teferi, Hero of Dominaria", "Lightning Bolt"], ordered.Select(c => c.Name));
    }

    [Fact]
    public void ApplyUnique_Cards_DedupesByName()
    {
        var a = Bolt(); var b = Bolt(); b.SetCode = "leb";
        var unique = ScryfallCardFilter.ApplyUnique([a, b], "cards").ToList();
        Assert.Single(unique);
    }
}
