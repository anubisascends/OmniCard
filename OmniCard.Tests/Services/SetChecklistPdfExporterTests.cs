using System.IO;
using OmniCard.Audit;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class SetChecklistPdfExporterTests
{
    [Fact]
    public void Export_WritesNonEmptyPdf_WithMixedFoilPrices()
    {
        var report = new SetChecklistReport
        {
            Game = CardGame.Mtg,
            SetCode = "set1",
            SetName = "Set One",
            OwnedCount = 1,
            TotalCount = 4,
            AnyFoil = true,
            Rows =
            [
                new WantListRow("2", "Two", "common", 1m, 3m),
                new WantListRow("2a", "Two-A", "uncommon", 2m, null), // no foil price → renders "—"
                new WantListRow("10", "Ten", "rare", null, 12m),      // no normal price
            ],
        };

        var path = Path.Combine(Path.GetTempPath(), $"wantlist-{Guid.NewGuid():N}.pdf");
        try
        {
            new SetChecklistPdfExporter().Export(report, path);

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Export_EmptyWantList_StillWritesPdf()
    {
        var report = new SetChecklistReport
        {
            Game = CardGame.Pokemon,
            SetCode = "s",
            SetName = "Complete Set",
            OwnedCount = 2,
            TotalCount = 2,
            Rows = [],
        };

        var path = Path.Combine(Path.GetTempPath(), $"wantlist-{Guid.NewGuid():N}.pdf");
        try
        {
            new SetChecklistPdfExporter().Export(report, path);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class CollectorNumberComparerTests
{
    [Theory]
    [InlineData("2", "10", -1)]     // numeric, not lexicographic
    [InlineData("10", "2", 1)]
    [InlineData("2", "2a", -1)]     // "2" sorts before "2a"
    [InlineData("2a", "2b", -1)]
    [InlineData("001", "1", 0)]     // leading zeros ignored
    [InlineData("TG04", "TG10", -1)] // alpha prefix then numeric
    public void Compare_OrdersNaturally(string a, string b, int expectedSign)
        => Assert.Equal(expectedSign, Math.Sign(CollectorNumberComparer.Instance.Compare(a, b)));

    [Fact]
    public void Sort_ProducesBinderOrder()
    {
        var input = new[] { "10", "2", "1", "2a", "100", "3" };
        var sorted = input.OrderBy(x => x, CollectorNumberComparer.Instance).ToArray();
        Assert.Equal(["1", "2", "2a", "3", "10", "100"], sorted);
    }
}
