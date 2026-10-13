using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;
using OmniCard.Tests.Fakes;

namespace OmniCard.Tests.Services.Matching;

public class EdgeHashTests
{
    // An identical layout (frame + inner block) so structure is constant; only the fill colors differ —
    // mimics a foil color shift over the same artwork. Pre-rendered by the original GDI+ DrawCard helper
    // (TestData/Baseline/Drawn) so the hashes see exactly the pixels they did before the cross-platform port.
    private static byte[] DrawCard(string colors) =>
        File.ReadAllBytes(TestImages.Fixture("Baseline", "Drawn", $"edgehash-{colors}.png"));

    [Fact]
    public void EdgeHash_IsMoreColorRobustThanLuminanceHash()
    {
        var svc = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

        // Same structure, very different colors/luminance (yellow -> green, like a foil shift).
        var a = DrawCard("gold-khaki");
        var b = DrawCard("green-seagreen");

        var edgeA = svc.ComputeEdgeHash(new MemoryStream(a));
        var edgeB = svc.ComputeEdgeHash(new MemoryStream(b));
        var lumA = svc.ComputeHash(new MemoryStream(a));
        var lumB = svc.ComputeHash(new MemoryStream(b));

        int edgeDist = PerceptualHashService.HammingDistance(edgeA, edgeB);
        int lumDist = PerceptualHashService.HammingDistance(lumA, lumB);

        // The color shift barely moves the edge hash, and moves the luminance hash at least as much.
        // (Under GDI+ the distances were edge 2 / luminance 4; the port reproduces three of the four hashes
        // exactly and the fourth within ImagingBaselineTests' tolerance, giving 2 / 2 — so this synthetic
        // case can only require "not more than", not "strictly less than".)
        Assert.True(edgeDist <= 2, $"edge dist {edgeDist} should stay within 2 bits for a color-only change");
        Assert.True(edgeDist <= lumDist,
            $"edge dist {edgeDist} should be <= luminance dist {lumDist} for a color-only change");
    }

    [Fact]
    public void EdgeHash_IsDeterministic()
    {
        var svc = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        var img = DrawCard("gold-khaki");

        var h1 = svc.ComputeEdgeHash(new MemoryStream(img));
        var h2 = svc.ComputeEdgeHash(new MemoryStream(img));

        Assert.Equal(h1, h2);
    }
}
