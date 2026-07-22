using OmniCard.CardMatching;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundAttributeExtractorTests
{
    [Fact]
    public void ExtractColorAndType_ReadFromRiftboundSource()
    {
        var card = new RiftboundCard { Domain = "Body/Order", CardType = "Legend" };
        var match = new CardMatch { Name = "Vex", Source = card };

        Assert.Equal("Body/Order", CardAttributeExtractor.ExtractColor(match, CardGame.Riftbound));
        Assert.Equal("Legend", CardAttributeExtractor.ExtractCardType(match, CardGame.Riftbound));
    }
}
