using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

/// <summary>
/// Engine-backed integration tests for the Tesseract OCR path. These exercise the real
/// native engine + bundled tessdata (unlike the geometry-only <see cref="OcrMatchingServiceTests"/>),
/// so they validate that the native binaries and language data are present in the test output
/// and that the crop-region → OCR → parse pipeline actually reads text.
/// </summary>
public class OcrMatchingServiceEngineTests
{
    private static OcrMatchingService CreateService() =>
        new(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            NullLogger<OcrMatchingService>.Instance);

    /// <summary>Renders a white card with a single black text string placed inside the given
    /// percentage region, matching how the service crops before OCR.</summary>
    private static byte[] RenderCard(int width, int height,
        (double X, double Y, double W, double H) region, string text, int fontSize)
    {
        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = OcrMatchingService.ToPixelRect(region, width, height);
            using var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(text, font, Brushes.Black, new PointF(rect.X, rect.Y));
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public async Task DetectOptcgCollectorNumberAsync_ReadsCollectorNumber()
    {
        var image = RenderCard(600, 840, OcrMatchingService.OptcgCollectorNumberRegion, "OP15-043", fontSize: 30);

        using var service = CreateService();
        var (collectorNumber, confidence) = await service.DetectOptcgCollectorNumberAsync(image);

        Assert.Equal("OP15-043", collectorNumber);
        Assert.True(confidence >= 0.5, $"confidence {confidence} should clear the downstream lookup gate");
    }

    [Fact]
    public async Task AnalyzeCardAsync_ReadsCardName()
    {
        var image = RenderCard(600, 840, OcrMatchingService.NameCropRegions[0], "Lightning Bolt", fontSize: 32);

        using var service = CreateService();
        var result = await service.AnalyzeCardAsync(image);

        Assert.False(string.IsNullOrWhiteSpace(result.RecognizedName));
        Assert.Contains("Lightning", result.RecognizedName!, StringComparison.OrdinalIgnoreCase);
        // Must clear the downstream OCR-assisted scoring gate (ScryfallService requires
        // NameConfidence > 0.3); a cleanly-read name should report well above that.
        Assert.True(result.NameConfidence > 0.3, $"NameConfidence {result.NameConfidence} must clear the 0.3 scoring gate");
    }

    [Fact]
    public async Task DetectOptcgCollectorNumberAsync_ReturnsNull_WhenNoNumberPresent()
    {
        // Blank card — nothing to read in the collector-number region.
        var image = RenderCard(600, 840, OcrMatchingService.OptcgCollectorNumberRegion, "", fontSize: 30);

        using var service = CreateService();
        var (collectorNumber, confidence) = await service.DetectOptcgCollectorNumberAsync(image);

        Assert.Null(collectorNumber);
        Assert.Equal(0, confidence);
    }
}
