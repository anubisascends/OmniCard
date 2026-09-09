using OmniCard.CardMatching;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;

namespace OmniCard.Tests.Services.Games;

public class TcgCsvAttributeTests
{
    [Fact]
    public void ExtractCardType_FromTcgCsvCard()
    {
        var card = new TcgCsvCard { ProductId = 1, Game = CardGame.Pokemon, CardType = "Fire" };
        var match = new CardMatch { Name = "X", Source = card };
        Assert.Equal("Fire", CardAttributeExtractor.ExtractCardType(match, CardGame.Pokemon));
    }
}
