using OmniCard.CardMatching;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class YugiohServiceTests
{
    [Fact]
    public void SubtypePrices_PrefersUnlimited_ForNormal_NoFoilSlot()
    {
        var rows = new List<TcgCsvPrice>
        {
            new() { ProductId = 1, SubTypeName = "1st Edition", MarketPrice = 5.00m },
            new() { ProductId = 1, SubTypeName = "Unlimited", MarketPrice = 2.00m },
            new() { ProductId = 1, SubTypeName = "Limited", MarketPrice = 8.00m },
        };
        var (normal, foil) = YugiohService.MapSubtypePricesForTest(rows);
        Assert.Equal(2.00m, normal);   // Unlimited preferred
        Assert.Null(foil);             // Yu-Gi-Oh has no foil-vs-nonfoil split
    }
}
