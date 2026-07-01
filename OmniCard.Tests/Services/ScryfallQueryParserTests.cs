using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class ScryfallQueryParserTests
{
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
    public void Parse_AllAliases_Normalized(string input, string expectedField, string expectedValue)
    {
        var result = ScryfallQueryParser.Parse(input);
        Assert.Single(result);
        Assert.Equal((expectedField, expectedValue), result[0]);
    }
}
