using OmniCard.CardMatching;

namespace OmniCard.Tests.Services;

public class ScryfallQueryParserTests
{
    // --- Backward-compatible flat Parse tests ---

    [Fact]
    public void Parse_PlainText_ReturnsNameFilter()
    {
        var result = ScryfallQueryParser.Parse("lightning bolt");
        Assert.Equal([("name", "lightning"), ("name", "bolt")], result);
    }

    [Fact]
    public void Parse_FieldPrefix_ReturnsFieldValuePair()
    {
        var result = ScryfallQueryParser.Parse("set:som");
        Assert.Equal([("set", "som")], result);
    }

    [Fact]
    public void Parse_QuotedValue_PreservesSpaces()
    {
        var result = ScryfallQueryParser.Parse("name:\"lightning bolt\"");
        Assert.Equal([("name", "lightning bolt")], result);
    }

    [Fact]
    public void Parse_ShortAliases_Normalized()
    {
        var result = ScryfallQueryParser.Parse("t:instant c:w r:rare");
        Assert.Equal([("type", "instant"), ("color", "w"), ("rarity", "rare")], result);
    }

    [Fact]
    public void Parse_MixedPrefixAndPlain_Parsed()
    {
        var result = ScryfallQueryParser.Parse("bolt set:alpha");
        Assert.Equal([("name", "bolt"), ("set", "alpha")], result);
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(ScryfallQueryParser.Parse(""));
        Assert.Empty(ScryfallQueryParser.Parse("   "));
    }

    [Theory]
    [InlineData("n:bolt", "name", "bolt")]
    [InlineData("s:som", "set", "som")]
    [InlineData("e:som", "set", "som")]
    [InlineData("cn:42", "cn", "42")]
    [InlineData("number:42", "cn", "42")]
    [InlineData("o:draw", "oracle", "draw")]
    [InlineData("edition:m21", "set", "m21")]
    public void Parse_AllAliases_Normalized(string input, string expectedField, string expectedValue)
    {
        var result = ScryfallQueryParser.Parse(input);
        Assert.Single(result);
        Assert.Equal((expectedField, expectedValue), result[0]);
    }

    [Fact]
    public void Parse_OrFlattened()
    {
        var result = ScryfallQueryParser.Parse("set:tla or set:tle");
        Assert.Equal([("set", "tla"), ("set", "tle")], result);
    }

    // --- ParseFilter tree structure tests ---

    [Fact]
    public void ParseFilter_SimpleOr_ReturnsOrFilter()
    {
        var result = ScryfallQueryParser.ParseFilter("set:tla or set:tle");
        var or = Assert.IsType<OrFilter>(result);
        Assert.Equal(2, or.Children.Count);
        var left = Assert.IsType<FieldFilter>(or.Children[0]);
        Assert.Equal("set", left.Field);
        Assert.Equal("tla", left.Value);
        var right = Assert.IsType<FieldFilter>(or.Children[1]);
        Assert.Equal("set", right.Field);
        Assert.Equal("tle", right.Value);
    }

    [Fact]
    public void ParseFilter_MultipleOrs()
    {
        var result = ScryfallQueryParser.ParseFilter("set:a or set:b or set:c");
        var or = Assert.IsType<OrFilter>(result);
        Assert.Equal(3, or.Children.Count);
    }

    [Fact]
    public void ParseFilter_CaseInsensitiveOr()
    {
        var result = ScryfallQueryParser.ParseFilter("set:tla OR set:tle");
        Assert.IsType<OrFilter>(result);
    }

    [Fact]
    public void ParseFilter_OrAtStart_TreatedAsLiteral()
    {
        var result = ScryfallQueryParser.ParseFilter("or set:tla");
        var and = Assert.IsType<AndFilter>(result);
        Assert.Equal(2, and.Children.Count);
        var first = Assert.IsType<FieldFilter>(and.Children[0]);
        Assert.Equal("name", first.Field);
        Assert.Equal("or", first.Value);
    }

    // --- Comparison operators ---

    [Theory]
    [InlineData("set:tla", ComparisonOp.Contains)]
    [InlineData("set=tla", ComparisonOp.Exact)]
    [InlineData("set!=tla", ComparisonOp.NotEqual)]
    [InlineData("r<rare", ComparisonOp.LessThan)]
    [InlineData("r>rare", ComparisonOp.GreaterThan)]
    [InlineData("r<=rare", ComparisonOp.LessOrEqual)]
    [InlineData("r>=rare", ComparisonOp.GreaterOrEqual)]
    public void ParseFilter_ComparisonOperators(string input, ComparisonOp expectedOp)
    {
        var result = ScryfallQueryParser.ParseFilter(input);
        var field = Assert.IsType<FieldFilter>(result);
        Assert.Equal(expectedOp, field.Op);
    }

    // --- Negation ---

    [Fact]
    public void ParseFilter_Negation_SetsFlag()
    {
        var result = ScryfallQueryParser.ParseFilter("-c:r");
        var field = Assert.IsType<FieldFilter>(result);
        Assert.Equal("color", field.Field);
        Assert.Equal("r", field.Value);
        Assert.True(field.Negated);
    }

    [Fact]
    public void ParseFilter_NegationWithAnd()
    {
        var result = ScryfallQueryParser.ParseFilter("t:creature -c:r");
        var and = Assert.IsType<AndFilter>(result);
        Assert.Equal(2, and.Children.Count);
        var second = Assert.IsType<FieldFilter>(and.Children[1]);
        Assert.True(second.Negated);
        Assert.Equal("color", second.Field);
    }

    // --- Parentheses ---

    [Fact]
    public void ParseFilter_Parentheses_GroupsOrWithAnd()
    {
        var result = ScryfallQueryParser.ParseFilter("(set:tla or set:tle) t:creature");
        var and = Assert.IsType<AndFilter>(result);
        Assert.Equal(2, and.Children.Count);
        Assert.IsType<OrFilter>(and.Children[0]);
        var type = Assert.IsType<FieldFilter>(and.Children[1]);
        Assert.Equal("type", type.Field);
    }

    [Fact]
    public void ParseFilter_NegatedParentheses()
    {
        var result = ScryfallQueryParser.ParseFilter("-(set:tla or set:tle)");
        var not = Assert.IsType<NotFilter>(result);
        Assert.IsType<OrFilter>(not.Inner);
    }

    // --- Exact name ---

    [Fact]
    public void ParseFilter_ExactName()
    {
        var result = ScryfallQueryParser.ParseFilter("!\"Lightning Bolt\"");
        var field = Assert.IsType<FieldFilter>(result);
        Assert.Equal("name", field.Field);
        Assert.Equal(ComparisonOp.Exact, field.Op);
        Assert.Equal("Lightning Bolt", field.Value);
        Assert.False(field.Negated);
    }

    [Fact]
    public void ParseFilter_NegatedExactName()
    {
        var result = ScryfallQueryParser.ParseFilter("-!\"Lightning Bolt\"");
        var field = Assert.IsType<FieldFilter>(result);
        Assert.Equal("name", field.Field);
        Assert.Equal(ComparisonOp.Exact, field.Op);
        Assert.True(field.Negated);
    }

    // --- is: and not: ---

    [Fact]
    public void ParseFilter_IsFoil()
    {
        var result = ScryfallQueryParser.ParseFilter("is:foil");
        var field = Assert.IsType<FieldFilter>(result);
        Assert.Equal("is", field.Field);
        Assert.Equal("foil", field.Value);
        Assert.False(field.Negated);
    }

    [Fact]
    public void ParseFilter_NotFoil_BecomesNegatedIs()
    {
        var result = ScryfallQueryParser.ParseFilter("not:foil");
        var field = Assert.IsType<FieldFilter>(result);
        Assert.Equal("is", field.Field);
        Assert.Equal("foil", field.Value);
        Assert.True(field.Negated);
    }

    [Fact]
    public void ParseFilter_NegatedNotFoil_DoubleNegation()
    {
        var result = ScryfallQueryParser.ParseFilter("-not:foil");
        var field = Assert.IsType<FieldFilter>(result);
        Assert.Equal("is", field.Field);
        Assert.Equal("foil", field.Value);
        Assert.False(field.Negated); // double negation cancels
    }

    // --- Color normalization ---

    [Theory]
    [InlineData("w", "W")]
    [InlineData("white", "W")]
    [InlineData("urg", "URG")]
    [InlineData("rw", "WR")]
    [InlineData("WUBRG", "WUBRG")]
    [InlineData("colorless", "")]
    public void NormalizeColorValue(string input, string expected)
    {
        Assert.Equal(expected, ScryfallQueryParser.NormalizeColorValue(input));
    }

    // --- Rarity comparison ---

    [Theory]
    [InlineData(ComparisonOp.GreaterOrEqual, "rare", new[] { "rare", "mythic" })]
    [InlineData(ComparisonOp.GreaterThan, "rare", new[] { "mythic" })]
    [InlineData(ComparisonOp.LessThan, "rare", new[] { "common", "uncommon" })]
    [InlineData(ComparisonOp.LessOrEqual, "uncommon", new[] { "common", "uncommon" })]
    [InlineData(ComparisonOp.NotEqual, "common", new[] { "uncommon", "rare", "mythic" })]
    public void RaritiesMatching(ComparisonOp op, string value, string[] expected)
    {
        var result = ScryfallQueryParser.RaritiesMatching(op, value);
        Assert.Equal(expected, result);
    }

    // --- Complex queries ---

    [Fact]
    public void ParseFilter_ComplexQuery()
    {
        // (set:tla or set:tle) t:creature -c:r r>=rare
        var result = ScryfallQueryParser.ParseFilter("(set:tla or set:tle) t:creature -c:r r>=rare");
        var and = Assert.IsType<AndFilter>(result);
        Assert.Equal(4, and.Children.Count);

        Assert.IsType<OrFilter>(and.Children[0]); // (set:tla or set:tle)

        var type = Assert.IsType<FieldFilter>(and.Children[1]);
        Assert.Equal("type", type.Field);

        var color = Assert.IsType<FieldFilter>(and.Children[2]);
        Assert.Equal("color", color.Field);
        Assert.True(color.Negated);

        var rarity = Assert.IsType<FieldFilter>(and.Children[3]);
        Assert.Equal("rarity", rarity.Field);
        Assert.Equal(ComparisonOp.GreaterOrEqual, rarity.Op);
    }
}
