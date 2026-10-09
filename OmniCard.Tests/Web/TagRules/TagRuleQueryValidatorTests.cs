using OmniCard.CardMatching.Search;
using OmniCard.Shared.Cards;
using OmniCard.Web.Services.TagRules;

namespace OmniCard.Tests.Web.TagRules;

public class TagRuleQueryValidatorTests
{
    private static IReadOnlyList<string> Mtg(string q) => TagRuleQueryValidator.Validate(CardGame.Mtg, q, MtgSearchSchema.Public);
    private static IReadOnlyList<string> Other(string q) => TagRuleQueryValidator.Validate(CardGame.Pokemon, q, SharedSearchSchema.Default);

    [Theory]
    [InlineData("kw:flying")]
    [InlineData("t:creature pow>=4 f:modern")]
    [InlineData("is:commander -is:reprint")]
    [InlineData("(c:r or c:g) r>=rare is:foil")]
    [InlineData("lang:ja cond:nm")]
    [InlineData("bolt")]
    public void Mtg_ValidQueries(string q) => Assert.Empty(Mtg(q));

    [Theory]
    [InlineData("loc:binder", "location")]
    [InlineData("tag:trade", "tag:")]
    [InlineData("usd>5", "prices")]
    [InlineData("price>=1", "prices")]
    [InlineData("date>=2024-01-01", "date:")]
    [InlineData("is:missing", "is:missing")]
    [InlineData("t:creature order:cmc", "sorts")]
    [InlineData("powr>3", "Unknown field 'powr'")]
    [InlineData("is:bogus", "Unknown flag")]
    [InlineData("set:", "needs a value")]
    [InlineData("   ", "Enter a search query")]
    public void Mtg_InvalidQueries(string q, string expected) =>
        Assert.Contains(Mtg(q), e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void OtherGames_OnlyAllowTheirOwnFields()
    {
        Assert.Empty(Other("r:rare is:foil set:sv1"));
        Assert.NotEmpty(Other("is:commander")); // an MTG catalog flag
        Assert.NotEmpty(Other("kw:flying"));    // an MTG field
    }

    [Fact]
    public void Errors_AreReportedOnce() => Assert.Single(Mtg("loc:a loc:b"));
}
