using OmniCard.Shared.Sets;

namespace OmniCard.Tests.Models;

public class SetOrderingTests
{
    [Fact]
    public void InNaturalOrder_SortsNumberedSetsByValueNotLexically()
    {
        var sets = new[]
        {
            new SetInfo("OP10", "One Piece 10"),
            new SetInfo("OP02", "One Piece 2"),
            new SetInfo("OP17", "The World's Strongest Warriors"),
            new SetInfo("OP01", "Romance Dawn"),
        };

        var ordered = sets.InNaturalOrder().Select(s => s.SetCode).ToList();

        Assert.Equal(new[] { "OP01", "OP02", "OP10", "OP17" }, ordered);
    }

    [Fact]
    public void InNaturalOrder_GroupsByAlphaPrefixThenNumber()
    {
        var sets = new[]
        {
            new SetInfo("OP01", "a"),
            new SetInfo("ST10", "b"),
            new SetInfo("EB01", "c"),
            new SetInfo("ST02", "d"),
            new SetInfo("OP17", "e"),
        };

        var ordered = sets.InNaturalOrder().Select(s => s.SetCode).ToList();

        Assert.Equal(new[] { "EB01", "OP01", "OP17", "ST02", "ST10" }, ordered);
    }

    [Fact]
    public void InNaturalOrder_IsCaseInsensitiveOnAlphaRuns()
    {
        var sets = new[]
        {
            new SetInfo("mom", "March of the Machine"),
            new SetInfo("MKM", "Murders at Karlov Manor"),
        };

        var ordered = sets.InNaturalOrder().Select(s => s.SetCode).ToList();

        Assert.Equal(new[] { "MKM", "mom" }, ordered);
    }

    [Fact]
    public void InNaturalOrder_TieBreaksOnSetName()
    {
        var sets = new[]
        {
            new SetInfo("OP01", "Zebra"),
            new SetInfo("OP01", "Apple"),
        };

        var ordered = sets.InNaturalOrder().Select(s => s.SetName).ToList();

        Assert.Equal(new[] { "Apple", "Zebra" }, ordered);
    }
}
