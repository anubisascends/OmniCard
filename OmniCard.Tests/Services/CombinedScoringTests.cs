using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class CombinedScoringTests
{
    [Fact]
    public void StringSimilarity_ExactMatch_Returns1()
    {
        Assert.Equal(1.0, ScryfallService.StringSimilarity("Lightning Bolt", "Lightning Bolt"));
    }

    [Fact]
    public void StringSimilarity_CaseInsensitive_Returns1()
    {
        Assert.Equal(1.0, ScryfallService.StringSimilarity("Lightning Bolt", "lightning bolt"));
    }

    [Fact]
    public void StringSimilarity_CloseMatch_ReturnsHigh()
    {
        var score = ScryfallService.StringSimilarity("Lightning Bolt", "Lightning Bot");
        Assert.True(score > 0.8);
    }

    [Fact]
    public void StringSimilarity_CompletelyDifferent_ReturnsLow()
    {
        var score = ScryfallService.StringSimilarity("Lightning Bolt", "Counterspell");
        Assert.True(score < 0.4);
    }

    [Fact]
    public void StringSimilarity_NullInput_Returns0()
    {
        Assert.Equal(0.0, ScryfallService.StringSimilarity(null, "test"));
        Assert.Equal(0.0, ScryfallService.StringSimilarity("test", null));
    }

    [Fact]
    public void StringSimilarity_EmptyInput_Returns0()
    {
        Assert.Equal(0.0, ScryfallService.StringSimilarity("", "test"));
    }
}
