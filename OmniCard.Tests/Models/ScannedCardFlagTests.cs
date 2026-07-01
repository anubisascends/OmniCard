using OmniCard.Models;

namespace OmniCard.Tests.Models;

public class ScannedCardFlagTests
{
    [Fact]
    public void FlagReason_DefaultsToNone()
    {
        var card = new ScannedCard
        {
            TempImagePath = "/tmp/test.png",
            Hash = 0x1234
        };

        Assert.Equal(FlagReason.None, card.FlagReason);
        Assert.False(card.IsFlagged);
    }

    [Fact]
    public void IsFlagged_TrueWhenFlagReasonIsManual()
    {
        var card = new ScannedCard
        {
            TempImagePath = "/tmp/test.png",
            Hash = 0x1234
        };

        card.FlagReason = FlagReason.Manual;
        Assert.True(card.IsFlagged);
    }

    [Fact]
    public void IsFlagged_TrueWhenFlagReasonIsNoMatch()
    {
        var card = new ScannedCard
        {
            TempImagePath = "/tmp/test.png",
            Hash = 0x1234
        };

        card.FlagReason = FlagReason.NoMatch;
        Assert.True(card.IsFlagged);
    }

    [Fact]
    public void IsFlagged_TrueWhenFlagReasonIsVeryLowConfidence()
    {
        var card = new ScannedCard
        {
            TempImagePath = "/tmp/test.png",
            Hash = 0x1234
        };

        card.FlagReason = FlagReason.VeryLowConfidence;
        Assert.True(card.IsFlagged);
    }

    [Fact]
    public void IsFlagged_RaisesPropertyChangedWhenFlagReasonChanges()
    {
        var card = new ScannedCard
        {
            TempImagePath = "/tmp/test.png",
            Hash = 0x1234
        };

        var raised = false;
        card.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScannedCard.IsFlagged))
                raised = true;
        };

        card.FlagReason = FlagReason.Manual;

        Assert.True(raised);
    }

    [Fact]
    public void FlagReason_RaisesPropertyChanged()
    {
        var card = new ScannedCard
        {
            TempImagePath = "/tmp/test.png",
            Hash = 0x1234
        };

        var raised = false;
        card.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScannedCard.FlagReason))
                raised = true;
        };

        card.FlagReason = FlagReason.NoMatch;

        Assert.True(raised);
        Assert.Equal(FlagReason.NoMatch, card.FlagReason);
    }
}
