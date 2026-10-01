using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching.Games;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// Engine-backed: locate the One Piece collector line on real scans, OCR it, and resolve it. Fixtures
/// are the bottom-right of real flatbed scans (named "{number}_{scan width}x{scan height}.png") and are
/// pasted back into a blank canvas of the original size, since the locator works in fractions of the scan.
/// They cover the failure modes that sank the old fixed crop: a number low in the frame (OP09-110, dark on
/// yellow), white-on-colour glyphs that bleed into multi-glyph blobs (OP09-055 blue, OP13-085 black) and a
/// number that fuses into one blob (EB03-007 red).
/// </summary>
public class OptcgCollectorLineTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "TestData", "OptcgCollector");

    // Real numbers plus near-misses a garbled read could snap to instead.
    private static readonly OptcgCollectorNumberResolver Resolver = new(
    [
        "OP09-110", "OP11-088", "OP05-110", "OP09-055", "OP09-050", "OP05-055", "EB03-007", "EB03-001",
        "EB01-007", "OP13-085", "OP13-086", "OP03-085", "P-110", "P-085", "P-055",
    ]);

    private static OcrMatchingService CreateService() =>
        new(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            NullLogger<OcrMatchingService>.Instance);

    private static Bitmap ScanFromFixture(string number)
    {
        var path = Directory.GetFiles(DataDir, $"{number}_*.png").Single();
        var size = Path.GetFileNameWithoutExtension(path).Split('_')[1].Split('x').Select(int.Parse).ToArray();
        var scan = new Bitmap(size[0], size[1], PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(scan);
        g.Clear(Color.White);
        using var strip = new Bitmap(path);
        g.DrawImageUnscaled(strip, (int)(0.45 * size[0]), (int)(0.84 * size[1]));
        return scan;
    }

    private static byte[] Png(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Theory]
    [InlineData("OP09-110")]
    [InlineData("OP09-055")]
    [InlineData("OP13-085")]
    [InlineData("EB03-007")]
    public async Task ReadsResolveToPrintedNumber(string number)
    {
        using var scan = ScanFromFixture(number);
        using var service = CreateService();

        var reads = await service.ReadOptcgCollectorTextsAsync(Png(scan));

        var top = Resolver.Resolve(reads).FirstOrDefault();
        Assert.True(top?.CardNumber == number, $"expected {number}, resolved {top?.CardNumber ?? "nothing"} from [{string.Join(" | ", reads)}]");
    }

    [Theory]
    [InlineData("OP09-110")]
    [InlineData("OP09-055")]
    public void Locate_FindsTheNumberAsTheRightmostLine(string number)
    {
        using var scan = ScanFromFixture(number);

        var lines = OptcgCollectorLineLocator.Locate(scan);
        try
        {
            Assert.NotEmpty(lines);
            // Rendered dark-on-white with a margin, upscaled to a fixed glyph height.
            var first = lines[0];
            Assert.True(first.Height is > 60 and < 160, $"line height {first.Height}");
            Assert.True(first.Width > first.Height * 2, $"line {first.Width}x{first.Height} should be a wide strip");
            Assert.Equal(Color.White.ToArgb(), first.GetPixel(2, 2).ToArgb());
        }
        finally
        {
            foreach (var line in lines) line.Dispose();
        }
    }

    [Fact]
    public void Locate_BlankScan_FindsNothing()
    {
        using var scan = new Bitmap(800, 1060);
        using (var g = Graphics.FromImage(scan)) g.Clear(Color.White);

        Assert.Empty(OptcgCollectorLineLocator.Locate(scan));
    }
}
