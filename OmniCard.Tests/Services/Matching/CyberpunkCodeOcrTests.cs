using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;
using OmniCard.Shared.Matching;
using SkiaSharp;
using Xunit.Abstractions;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// Engine-backed: read the product code ("MS01 - WNC [A]", vertical up the left edge) and the diagonal
/// collector number (yellow box) from real Cyberpunk TCG flatbed scans. Fixtures are the bottom-left
/// corner of each scan (named "{number}-{card}_{scan width}x{scan height}.png"), pasted back into a blank
/// canvas of the original size since the reader works in fractions of the card. They cover a plain black
/// frame (Modded Kusanagi, Westbrook Netrunner ×2, Hacked Corpo) and a patterned blue Program frame
/// (Trust No One).
/// </summary>
public class CyberpunkCodeOcrTests(ITestOutputHelper output)
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "TestData", "CyberpunkCode");

    private static OcrMatchingService CreateService() =>
        new(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            NullLogger<OcrMatchingService>.Instance);

    internal static SKBitmap ScanFromFixture(string name)
    {
        var path = Directory.GetFiles(DataDir, $"{name}_*.png").Single();
        var size = Path.GetFileNameWithoutExtension(path).Split('_')[1].Split('x').Select(int.Parse).ToArray();
        var scan = ImageOps.Create(size[0], size[1], SKColors.White);
        using var corner = ImageOps.Decode(File.ReadAllBytes(path));
        ImageOps.DrawOver(scan, corner, 0, (int)(0.75 * size[1]));
        return scan;
    }

    public static TheoryData<string, string> Samples => new()
    {
        { "120-modded-kusanagi", "120" },
        { "127-westbrook-netrunner-a", "127" },
        { "127-westbrook-netrunner-b", "127" },
        { "139-trust-no-one", "139" },
        { "114-hacked-corpo", "114" },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void ReadsProductCodeAndNumber(string fixture, string number)
    {
        using var scan = ScanFromFixture(fixture);
        using var service = CreateService();

        var (code, productReads, numberReads) = service.ReadCyberpunkCodeWithReads(ImageOps.EncodePng(scan));
        output.WriteLine($"product reads: {string.Join(" | ", productReads)}");
        output.WriteLine($"number reads:  {string.Join(" | ", numberReads)}");

        Assert.Equal("MS01", code.Product);
        Assert.Equal("WNC", code.SetCode);
        Assert.Equal("A", code.Variant);
        Assert.Equal(number, code.Number);
        Assert.False(code.Beta);
        Assert.Equal(number, code.CatalogNumber);
    }

    [Fact]
    public void BlankCard_ReadsNothing()
    {
        using var blank = ImageOps.Create(770, 1060, SKColors.White);
        using var service = CreateService();

        var (code, _, _) = service.ReadCyberpunkCodeWithReads(ImageOps.EncodePng(blank));

        Assert.Null(code.SetCode);
        Assert.Null(code.Number);
    }
}
