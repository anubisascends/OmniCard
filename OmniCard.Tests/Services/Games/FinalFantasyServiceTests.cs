using OmniCard.Shared.Games;
using OmniCard.CardMatching.Games;

namespace OmniCard.Tests.Services.Games;

public class FinalFantasyServiceTests
{
    [Fact]
    public void SubtypePrices_MapsNormalAndFoil()
    {
        var rows = new List<TcgCsvPrice>
        {
            new() { ProductId = 1, SubTypeName = "Normal", MarketPrice = 1.25m },
            new() { ProductId = 1, SubTypeName = "Foil", MarketPrice = 4.75m },
        };
        var (normal, foil) = FinalFantasyService.MapSubtypePricesForTest(rows);
        Assert.Equal(1.25m, normal);
        Assert.Equal(4.75m, foil);
    }
}
