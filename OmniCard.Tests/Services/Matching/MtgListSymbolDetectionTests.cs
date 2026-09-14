using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// Validates the MTG Planeswalker-glyph detector that flags The List (plst) reprints. The glyph is
/// printed just left of the collector number on List cards and absent on everything else. Fixtures are
/// downscaled grayscale crops of real scans chosen at the decision boundary: the positives include the
/// lowest-scoring real glyph (0033), a card whose glyph is drift-clipped (0026) and one with an
/// oversized glyph (0017); the negatives include the two highest-scoring non-List cards (0007, 0015)
/// and a bright metallic MB2 border (0001) that swamped brightness- and pHash-based detectors.
/// </summary>
public class MtgListSymbolDetectionTests
{
    private static readonly string DataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData", "ListSymbol");

    private static OcrMatchingService CreateService() =>
        new(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            NullLogger<OcrMatchingService>.Instance);

    [Theory]
    [InlineData("list-pos-0017.png")]
    [InlineData("list-pos-0026.png")]
    [InlineData("list-pos-0033.png")]
    public void DetectsGlyph_OnListCards(string file)
    {
        using var service = CreateService();
        var (present, confidence) = service.DetectMtgListSymbol(File.ReadAllBytes(Path.Combine(DataDir, file)));

        Assert.True(present, $"expected the List glyph to be detected on {file} (NCC {confidence:F3})");
    }

    [Theory]
    [InlineData("list-neg-0001.png")]
    [InlineData("list-neg-0007.png")]
    [InlineData("list-neg-0015.png")]
    public void DoesNotDetectGlyph_OnNonListCards(string file)
    {
        using var service = CreateService();
        var (present, confidence) = service.DetectMtgListSymbol(File.ReadAllBytes(Path.Combine(DataDir, file)));

        Assert.False(present, $"did not expect the List glyph on {file} (NCC {confidence:F3})");
    }

    [Fact]
    public void SeparationMargin_HoldsBetweenClusters()
    {
        using var service = CreateService();
        double LowestPositive() => new[] { "list-pos-0017.png", "list-pos-0026.png", "list-pos-0033.png" }
            .Min(f => service.PeakListSymbolCorrelation(File.ReadAllBytes(Path.Combine(DataDir, f))));
        double HighestNegative() => new[] { "list-neg-0001.png", "list-neg-0007.png", "list-neg-0015.png" }
            .Max(f => service.PeakListSymbolCorrelation(File.ReadAllBytes(Path.Combine(DataDir, f))));

        // The two clusters must stay clearly separated — a shrinking gap is the early warning that a
        // template or region change is eroding the detector before either class actually flips.
        Assert.True(LowestPositive() - HighestNegative() > 0.08,
            $"cluster gap too small: lowest positive {LowestPositive():F3}, highest negative {HighestNegative():F3}");
    }

    [Fact]
    public void LandscapeImage_IsRejected()
    {
        using var service = CreateService();
        using var ms = new MemoryStream();
        using (var bmp = new System.Drawing.Bitmap(600, 400))
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

        var (present, _) = service.DetectMtgListSymbol(ms.ToArray());
        Assert.False(present); // glyph position is only defined for upright portrait scans
    }
}
