using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class ClearMatchCommandTests
{
    [Fact]
    public void ClearMatch_SetsMatchToNull()
    {
        var card = CreateCardWithMatch();
        ClearMatch(card);
        Assert.Null(card.Match);
    }

    [Fact]
    public void ClearMatch_SetsFlagReasonToMissingFromDatabase()
    {
        var card = CreateCardWithMatch();
        ClearMatch(card);
        Assert.Equal(FlagReason.MissingFromDatabase, card.FlagReason);
    }

    [Fact]
    public void ClearMatch_CreatesFlagFix_WithOriginalMatchData()
    {
        var card = CreateCardWithMatch();
        ClearMatch(card);

        Assert.NotNull(card.FlagFix);
        Assert.Equal("ClearMatch", card.FlagFix.FixType);
        Assert.Equal(FlagReason.None, card.FlagFix.OriginalFlagReason);
        Assert.Contains("Lightning Bolt", card.FlagFix.OriginalData);
        Assert.Equal("", card.FlagFix.ResolvedData);
    }

    [Fact]
    public void ClearMatch_PreservesOriginalFlagReason_WhenAlreadyFlagged()
    {
        var card = CreateCardWithMatch();
        card.FlagReason = FlagReason.VeryLowConfidence;
        ClearMatch(card);

        Assert.NotNull(card.FlagFix);
        Assert.Equal(FlagReason.VeryLowConfidence, card.FlagFix.OriginalFlagReason);
        Assert.Equal(FlagReason.MissingFromDatabase, card.FlagReason);
    }

    [Fact]
    public void ClearMatch_NoOp_WhenMatchIsNull()
    {
        var card = new ScannedCard { Hash = 0x1234, Game = CardGame.OnePiece };
        ClearMatch(card);

        Assert.Null(card.FlagFix);
        Assert.Equal(FlagReason.None, card.FlagReason);
    }

    /// <summary>
    /// Mirrors the logic that will be in RootViewModel.ClearMatch().
    /// Extracted here so we can test the logic without the full ViewModel.
    /// </summary>
    private static void ClearMatch(ScannedCard card)
    {
        if (card.Match is null) return;

        var originalFlagReason = card.FlagReason;
        var originalMatchData = System.Text.Json.JsonSerializer.Serialize(new
        {
            name = card.Match.Name,
            setCode = card.Match.SetCode,
            setName = card.Match.SetName,
            collectorNumber = card.Match.CollectorNumber,
            rarity = card.Match.Rarity,
            gameSpecificId = card.Match.GameSpecificId,
            confidence = card.Match.Confidence,
        });

        card.FlagFix = new ScanFlagFix
        {
            FixType = "ClearMatch",
            OriginalFlagReason = originalFlagReason,
            OriginalData = originalMatchData,
            ResolvedData = "",
        };

        card.Match = null;
        card.FlagReason = FlagReason.MissingFromDatabase;
    }

    private static ScannedCard CreateCardWithMatch()
    {
        return new ScannedCard
        {
            Hash = 0xDEADBEEF,
            Game = CardGame.Mtg,
            Match = new CardMatch
            {
                Name = "Lightning Bolt",
                SetCode = "lea",
                SetName = "Alpha",
                CollectorNumber = "1",
                Rarity = "common",
                GameSpecificId = "abc-123",
                Confidence = 85,
            },
        };
    }
}
