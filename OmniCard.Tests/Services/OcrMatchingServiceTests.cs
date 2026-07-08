using System.Drawing;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class OcrMatchingServiceTests
{
    [Theory]
    [InlineData(500, 700, 0, 35, 21, 375, 49)]   // Modern: 7%, 3%, 75%, 7% of 500x700
    [InlineData(500, 700, 1, 25, 14, 400, 56)]    // Borderless: 5%, 2%, 80%, 8%
    [InlineData(500, 700, 2, 50, 35, 350, 49)]    // Retro: 10%, 5%, 70%, 7%
    public void ToPixelRect_NameRegions_ReturnsCorrectPixels(
        int imgW, int imgH, int regionIndex, int expectedX, int expectedY, int expectedW, int expectedH)
    {
        var region = OcrMatchingService.NameCropRegions[regionIndex];
        var rect = OcrMatchingService.ToPixelRect(region, imgW, imgH);

        Assert.Equal(expectedX, rect.X);
        Assert.Equal(expectedY, rect.Y);
        Assert.Equal(expectedW, rect.Width);
        Assert.Equal(expectedH, rect.Height);
    }

    [Fact]
    public void ToPixelRect_SymbolRegion_ReturnsCorrectPixels()
    {
        var rect = OcrMatchingService.ToPixelRect(OcrMatchingService.SymbolCropRegion, 500, 700);

        Assert.Equal(410, rect.X);  // 82% of 500
        Assert.Equal(301, rect.Y);  // 43% of 700
        Assert.Equal(60, rect.Width);  // 12% of 500
        Assert.Equal(49, rect.Height); // 7% of 700
    }

    [Fact]
    public void ToPixelRect_ClampsToImageBounds()
    {
        // Region that would extend past image edge
        var rect = OcrMatchingService.ToPixelRect((0.95, 0.95, 0.20, 0.20), 100, 100);

        Assert.Equal(95, rect.X);
        Assert.Equal(95, rect.Y);
        Assert.Equal(5, rect.Width);   // Clamped: min(20, 100-95)
        Assert.Equal(5, rect.Height);  // Clamped
    }
}
