using OmniCard.Helpers;

namespace OmniCard.Tests.Helpers;

public class MtgSymbolParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        var result = MtgSymbolParser.Parse(null);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var result = MtgSymbolParser.Parse("");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_PlainText_ReturnsSingleTextSegment()
    {
        var result = MtgSymbolParser.Parse("Draw a card.");
        Assert.Single(result);
        var seg = Assert.IsType<TextSegment>(result[0]);
        Assert.Equal("Draw a card.", seg.Text);
    }

    [Fact]
    public void Parse_SingleSymbol_ReturnsSingleSymbolSegment()
    {
        var result = MtgSymbolParser.Parse("{W}");
        Assert.Single(result);
        var seg = Assert.IsType<SymbolSegment>(result[0]);
        Assert.Equal("W", seg.Code);
        Assert.Equal("W", seg.FileName);
    }

    [Fact]
    public void Parse_ManaCost_ReturnsMultipleSymbols()
    {
        var result = MtgSymbolParser.Parse("{5}{R}");
        Assert.Equal(2, result.Count);
        Assert.Equal("5", Assert.IsType<SymbolSegment>(result[0]).FileName);
        Assert.Equal("R", Assert.IsType<SymbolSegment>(result[1]).FileName);
    }

    [Fact]
    public void Parse_MixedTextAndSymbols_ReturnsCorrectSequence()
    {
        var result = MtgSymbolParser.Parse("{T}: Add {G}.");
        Assert.Equal(4, result.Count);
        Assert.Equal("T", Assert.IsType<SymbolSegment>(result[0]).Code);
        Assert.Equal(": Add ", Assert.IsType<TextSegment>(result[1]).Text);
        Assert.Equal("G", Assert.IsType<SymbolSegment>(result[2]).Code);
        Assert.Equal(".", Assert.IsType<TextSegment>(result[3]).Text);
    }

    [Fact]
    public void Parse_HybridMana_ResolvesFileName()
    {
        var result = MtgSymbolParser.Parse("{W/U}");
        var seg = Assert.IsType<SymbolSegment>(result[0]);
        Assert.Equal("W/U", seg.Code);
        Assert.Equal("WU", seg.FileName);
    }

    [Fact]
    public void Parse_PhyrexianMana_ResolvesFileName()
    {
        var result = MtgSymbolParser.Parse("{W/P}");
        var seg = Assert.IsType<SymbolSegment>(result[0]);
        Assert.Equal("W/P", seg.Code);
        Assert.Equal("WP", seg.FileName);
    }

    [Fact]
    public void Parse_HybridPhyrexian_ResolvesFileName()
    {
        var result = MtgSymbolParser.Parse("{W/U/P}");
        var seg = Assert.IsType<SymbolSegment>(result[0]);
        Assert.Equal("W/U/P", seg.Code);
        Assert.Equal("WUP", seg.FileName);
    }

    [Fact]
    public void Parse_GenericHybrid_ResolvesFileName()
    {
        var result = MtgSymbolParser.Parse("{2/W}");
        var seg = Assert.IsType<SymbolSegment>(result[0]);
        Assert.Equal("2/W", seg.Code);
        Assert.Equal("2W", seg.FileName);
    }

    [Fact]
    public void Parse_HalfMana_ResolvesSpecialFileName()
    {
        var result = MtgSymbolParser.Parse("{½}");
        var seg = Assert.IsType<SymbolSegment>(result[0]);
        Assert.Equal("½", seg.Code);
        Assert.Equal("HALF", seg.FileName);
    }

    [Fact]
    public void Parse_InfinityMana_ResolvesSpecialFileName()
    {
        var result = MtgSymbolParser.Parse("{∞}");
        var seg = Assert.IsType<SymbolSegment>(result[0]);
        Assert.Equal("∞", seg.Code);
        Assert.Equal("INFINITY", seg.FileName);
    }

    [Fact]
    public void Parse_ComplexOracleText_ParsesCorrectly()
    {
        var result = MtgSymbolParser.Parse("Pay {2}{W/U}, {T}: Target creature gains flying until end of turn.");
        Assert.Equal(6, result.Count);
        Assert.Equal("Pay ", Assert.IsType<TextSegment>(result[0]).Text);
        Assert.Equal("2", Assert.IsType<SymbolSegment>(result[1]).Code);
        Assert.Equal("WU", Assert.IsType<SymbolSegment>(result[2]).FileName);
        Assert.Equal(", ", Assert.IsType<TextSegment>(result[3]).Text);
        Assert.Equal("T", Assert.IsType<SymbolSegment>(result[4]).Code);
        Assert.Equal(": Target creature gains flying until end of turn.", Assert.IsType<TextSegment>(result[5]).Text);
    }

    [Fact]
    public void Parse_TextWithNewlines_PreservesNewlines()
    {
        var result = MtgSymbolParser.Parse("First strike\n{T}: Deal 1 damage.");
        Assert.Equal(3, result.Count);
        Assert.Equal("First strike\n", Assert.IsType<TextSegment>(result[0]).Text);
        Assert.Equal("T", Assert.IsType<SymbolSegment>(result[1]).Code);
        Assert.Equal(": Deal 1 damage.", Assert.IsType<TextSegment>(result[2]).Text);
    }
}
