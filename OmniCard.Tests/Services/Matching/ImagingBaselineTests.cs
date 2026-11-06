using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching.Games;
using OmniCard.Imaging;
using OmniCard.Tests.Fakes;
using Xunit.Abstractions;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// Cross-platform parity of the perceptual hashes. The reference values in
/// <c>TestData/Baseline/imaging-baseline.json</c> were produced by the original System.Drawing (GDI+)
/// pipeline on Windows (upstream commit e9291b5), before the port to SkiaSharp + <see cref="GdiPlusResampler"/>. Catalog hashes and
/// user hash corrections already stored in databases were computed by that pipeline, so the port must
/// reproduce them — exactly in most cases, otherwise within the tolerance documented below. Inputs cover
/// PNG (lossless), JPEG (Scryfall art plus GDI+-encoded scans) and TIFF.
/// </summary>
public class ImagingBaselineTests(ITestOutputHelper output)
{
    // Tolerance, in Hamming bits out of 64. Matching accepts a catalog hit at distance <= 14
    // (ScryfallService "confident" threshold: 8), so a few bits of drift never changes an otherwise
    // clear match. Residual differences come from GDI+'s internal fixed-point rounding (±1-4 levels on
    // some resized pixels) and JPEG decoder differences, flipping bits whose DCT value sits at the median.
    // Measured on the fixtures (Windows, Linux): 86.8% identical, mean 0.28 bits, max 4 bits; the 32x32
    // grey stage within 4 levels of GDI+'s.
    internal const int MaxBitsPerHash = 6;
    internal const double MaxMeanBits = 0.5;
    internal const double MinIdenticalFraction = 0.8;
    internal const int MaxGrayLevelDiff = 6;

    // Near-uniform inputs (the synthetic white cards with one line of text) are excluded from the bounds:
    // histogram equalization stretches a 1-level difference to full contrast there, so their hash is
    // unstable under any resampler — including GDI+ itself. Real card images sit far above this
    // (standard deviation of the 32x32 grey >= 24 in the fixtures; the synthetic cards <= 10.4).
    internal const double NearUniformStdDev = 16;

    private sealed record Entry(
        string file, int width, int height, string hash, string edgeHash, string[] artHashes,
        string[] cropHashes, Dictionary<string, string> rotatedHashes, string? symbolHash, string gray32);

    internal static IReadOnlyList<T> LoadBaseline<T>(string name) =>
        JsonSerializer.Deserialize<List<T>>(File.ReadAllText(TestImages.Fixture("Baseline", name)))!;

    private static ulong Hex(string s) => Convert.ToUInt64(s, 16);

    [Fact]
    public void PerceptualHashes_MatchGdiPlusBaseline_WithinTolerance()
    {
        var svc = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        var distances = new List<(string Kind, string File, int Bits)>();
        var nearUniform = new HashSet<string>();
        int grayMaxDiff = 0;

        foreach (var e in LoadBaseline<Entry>("imaging-baseline.json"))
        {
            var bytes = File.ReadAllBytes(TestImages.Fixture(e.file.Split('/')));
            void Add(string kind, ulong expected, ulong actual) =>
                distances.Add((kind, e.file, BitOperations.PopCount(expected ^ actual)));

            var expectedGray = Convert.FromBase64String(e.gray32);
            double meanGray = expectedGray.Average(v => (double)v);
            if (Math.Sqrt(expectedGray.Average(v => (v - meanGray) * (v - meanGray))) < NearUniformStdDev)
                nearUniform.Add(e.file);

            byte[]? gray = null;
            var hash = svc.ComputeHash(new MemoryStream(bytes), s =>
            {
                if (s.StageName == "Grayscale 32x32")
                {
                    using var stage = ImageOps.Decode(s.ImageData);
                    gray = ImageOps.GetBgra(stage).Where((_, i) => i % 4 == 0).ToArray();
                }
            });
            Add("pHash", Hex(e.hash), hash);
            Add("edge", Hex(e.edgeHash), svc.ComputeEdgeHash(new MemoryStream(bytes)));
            var art = svc.ComputeArtHash(new MemoryStream(bytes), ScryfallService.ArtCropRegions);
            for (int i = 0; i < art.Length; i++) Add($"art{i}", Hex(e.artHashes[i]), art[i]);

            for (int i = 0; i < expectedGray.Length; i++)
                grayMaxDiff = Math.Max(grayMaxDiff, Math.Abs(expectedGray[i] - gray![i]));

            using var src = ImageOps.Decode(bytes);
            Assert.Equal(e.width, src.Width);
            Assert.Equal(e.height, src.Height);
            var fracs = new[] { 0.02, 0.04 };
            for (int i = 0; i < fracs.Length; i++)
            {
                int x = (int)(src.Width * fracs[i]), y = (int)(src.Height * fracs[i]);
                using var crop = ImageOps.Crop(src, new System.Drawing.Rectangle(x, y, src.Width - 2 * x, src.Height - 2 * y));
                Add("crop", Hex(e.cropHashes[i]), svc.ComputeHash(crop));
            }

            // Rotation is lossless, so the rotated hashes test Rotate + the resampler, not new rounding.
            // Skipped for the 1200 dpi scans to keep the suite fast.
            if ((long)src.Width * src.Height <= 4_000_000)
                foreach (var (degrees, expected) in e.rotatedHashes)
                {
                    using var rotated = ImageOps.Rotate(src, int.Parse(degrees));
                    Add("rot" + degrees, Hex(expected), svc.ComputeHash(new MemoryStream(ImageOps.EncodePng(rotated))));
                }

            if (e.symbolHash is not null)
            {
                var rect = OcrMatchingService.ToPixelRect(OcrMatchingService.SymbolCropRegion, src.Width, src.Height);
                using var cropped = ImageOps.Crop(src, rect);
                using var resized = ImageOps.Resize(cropped, 32, 32);
                Add("symbol", Hex(e.symbolHash), svc.ComputeHash(new MemoryStream(ImageOps.EncodePng(resized))));
            }
        }

        foreach (var g in distances.Where(d => nearUniform.Contains(d.File)).GroupBy(d => d.File))
            output.WriteLine($"near-uniform (not asserted) {g.Key}: identical {g.Count(d => d.Bits == 0)}/{g.Count()}, max {g.Max(d => d.Bits)} bits");
        distances.RemoveAll(d => nearUniform.Contains(d.File));

        foreach (var g in distances.GroupBy(d => d.Kind))
            output.WriteLine($"{g.Key,-7} n={g.Count(),3} identical={g.Count(d => d.Bits == 0),3} mean={g.Average(d => d.Bits):F2} max={g.Max(d => d.Bits)}");
        foreach (var d in distances.Where(d => d.Bits > 2))
            output.WriteLine($"  {d.Kind} {d.File}: {d.Bits} bits");
        double identical = (double)distances.Count(d => d.Bits == 0) / distances.Count;
        double mean = distances.Average(d => d.Bits);
        output.WriteLine($"overall n={distances.Count} identical={identical:P1} mean={mean:F2} max={distances.Max(d => d.Bits)}; 32x32 grey max pixel diff={grayMaxDiff}");

        Assert.All(distances, d => Assert.True(d.Bits <= MaxBitsPerHash, $"{d.Kind} of {d.File} differs by {d.Bits} bits"));
        Assert.True(mean <= MaxMeanBits, $"mean Hamming distance {mean:F2} exceeds {MaxMeanBits}");
        Assert.True(identical >= MinIdenticalFraction, $"only {identical:P1} of hashes are identical to the GDI+ baseline");
        Assert.True(grayMaxDiff <= MaxGrayLevelDiff, $"32x32 grey stage differs from GDI+ by up to {grayMaxDiff} levels");
    }
}
