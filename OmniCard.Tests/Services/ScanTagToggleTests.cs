using OmniCard.Collection;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ScanTagToggleTests
{
    [Fact]
    public void Apply_True_AddsTagToEveryCard()
    {
        var cards = new[] { new ScannedCard { Hash = 1 }, new ScannedCard { Hash = 2 } };

        ScanTagToggle.Apply(cards, "Foil", apply: true);

        Assert.All(cards, c => Assert.Contains("Foil", c.Tags));
    }

    [Fact]
    public void Apply_True_IsCaseInsensitiveAndSkipsDuplicates()
    {
        var card = new ScannedCard { Hash = 1 };
        card.Tags.Add("foil");

        ScanTagToggle.Apply([card], "Foil", apply: true);

        Assert.Equal(["foil"], card.Tags); // no duplicate added under different casing
    }

    [Fact]
    public void Apply_False_RemovesTagFromEveryCard()
    {
        var cardA = new ScannedCard { Hash = 1 };
        cardA.Tags.Add("Foil");
        var cardB = new ScannedCard { Hash = 2 };
        cardB.Tags.Add("Foil");
        cardB.Tags.Add("PSA");

        ScanTagToggle.Apply([cardA, cardB], "Foil", apply: false);

        Assert.Empty(cardA.Tags);
        Assert.Equal(["PSA"], cardB.Tags);
    }

    [Fact]
    public void Apply_False_IsCaseInsensitiveAndNoOpWhenAbsent()
    {
        var card = new ScannedCard { Hash = 1 };
        card.Tags.Add("Foil");

        ScanTagToggle.Apply([card], "foil", apply: false);
        Assert.Empty(card.Tags);

        ScanTagToggle.Apply([card], "NeverThere", apply: false); // no throw, no change
        Assert.Empty(card.Tags);
    }

    [Fact]
    public void CreateAndApply_TrimsAppliesAndReturnsTrimmedName()
    {
        var card = new ScannedCard { Hash = 1 };

        var result = ScanTagToggle.CreateAndApply([card], "  Brand New  ");

        Assert.Equal("Brand New", result);
        Assert.Contains("Brand New", card.Tags);
    }

    [Fact]
    public void CreateAndApply_BlankName_ReturnsNullAndDoesNotTouchCards()
    {
        var card = new ScannedCard { Hash = 1 };

        var result = ScanTagToggle.CreateAndApply([card], "   ");

        Assert.Null(result);
        Assert.Empty(card.Tags);
    }
}
