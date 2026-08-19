using OmniCard.Models;

namespace OmniCard.Tests.Models;

public class FoilTypesTests
{
    [Theory]
    [InlineData(CardGame.Mtg, "Foil")]
    [InlineData(CardGame.Pokemon, "Holofoil")]
    [InlineData(CardGame.YuGiOh, "Foil")]
    [InlineData(CardGame.FinalFantasy, "Premium")]
    [InlineData(CardGame.Riftbound, "Foil")]
    [InlineData(CardGame.OnePiece, "Foil")]
    public void BasicFoilType_ReturnsExpectedPerGame(CardGame game, string expected)
        => Assert.Equal(expected, FoilTypes.BasicFoilType(game));

    [Theory]
    [InlineData(CardGame.Mtg)]
    [InlineData(CardGame.Pokemon)]
    [InlineData(CardGame.YuGiOh)]
    [InlineData(CardGame.FinalFantasy)]
    [InlineData(CardGame.Riftbound)]
    [InlineData(CardGame.OnePiece)]
    public void ForGame_IsNonEmpty_AndContainsBasicFinish(CardGame game)
    {
        var finishes = FoilTypes.ForGame(game);
        Assert.NotEmpty(finishes);
        Assert.Contains(FoilTypes.BasicFoilType(game), finishes);
    }
}
