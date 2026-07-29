using OmniCard.Models;
using OmniCard.Views.EbayListing;
using Xunit;

namespace OmniCard.Tests.Views;

public class EbayListingViewModelTitleTests
{
    [Theory]
    [InlineData(CardGame.Mtg, "MTG")]
    [InlineData(CardGame.Pokemon, "Pokémon")]
    [InlineData(CardGame.YuGiOh, "Yu-Gi-Oh!")]
    [InlineData(CardGame.OnePiece, "One Piece")]
    [InlineData(CardGame.FinalFantasy, "Final Fantasy")]
    [InlineData(CardGame.Riftbound, "Riftbound")]
    public void GameTitlePrefix_MatchesGame(CardGame game, string expected)
        => Assert.Equal(expected, EbayListingViewModel.GameTitlePrefix(game));

    [Fact]
    public void GameTitlePrefix_IsMtg_OnlyForMagic()
    {
        Assert.Equal("MTG", EbayListingViewModel.GameTitlePrefix(CardGame.Mtg));
        foreach (var game in Enum.GetValues<CardGame>())
            if (game != CardGame.Mtg)
                Assert.NotEqual("MTG", EbayListingViewModel.GameTitlePrefix(game));
    }
}
