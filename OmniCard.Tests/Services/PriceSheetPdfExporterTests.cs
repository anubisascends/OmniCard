using System.IO;
using OmniCard.Audit;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class PriceSheetPdfExporterTests : IDisposable
{
    private readonly string _tempDir;

    public PriceSheetPdfExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardPriceSheet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    private static PriceSheetReport Sample() => new()
    {
        LocationName = "Show Box",
        Lines =
        [
            new PriceSheetLine { Name = "Ambush Viper", GameDisplayName = "Magic: The Gathering", SetCode = "AAA", CollectorNumber = "1", Price = 0.25m },
            new PriceSheetLine { Name = "Blue-Eyes White Dragon", GameDisplayName = "Yu-Gi-Oh!", SetCode = "SDK", CollectorNumber = "1", Price = 5m },
            new PriceSheetLine { Name = "Booster Box", GameDisplayName = "Pokémon", SetCode = "Base", CollectorNumber = null, Price = 199.99m },
            new PriceSheetLine { Name = "Counterspell", GameDisplayName = "Magic: The Gathering", SetCode = "AAA", CollectorNumber = "2", Price = 1m },
        ],
    };

    [Fact]
    public void Export_WritesNonEmptyPdf()
    {
        var path = Path.Combine(_tempDir, "sheet.pdf");
        new PriceSheetPdfExporter().Export(Sample(), path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public void Export_EmptyReport_StillWritesPdf()
    {
        var path = Path.Combine(_tempDir, "empty.pdf");
        new PriceSheetPdfExporter().Export(new PriceSheetReport { LocationName = "Empty" }, path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
    }
}
