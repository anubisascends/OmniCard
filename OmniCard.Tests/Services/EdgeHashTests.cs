using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class EdgeHashTests
{
    // Draws an identical layout (frame + inner block) so structure is constant; only the
    // fill colors differ between calls — mimics a foil color shift over the same artwork.
    private static byte[] DrawCard(Color frame, Color inner)
    {
        using var bmp = new Bitmap(200, 280);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
            using var framePen = new Pen(frame, 12);
            g.DrawRectangle(framePen, 10, 10, 180, 260);
            using var innerBrush = new SolidBrush(inner);
            g.FillRectangle(innerBrush, 40, 60, 120, 90);
            g.FillEllipse(innerBrush, 60, 180, 80, 60);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void EdgeHash_IsMoreColorRobustThanLuminanceHash()
    {
        var svc = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

        // Same structure, very different colors/luminance (yellow -> green, like a foil shift).
        var a = DrawCard(Color.Gold, Color.Khaki);
        var b = DrawCard(Color.Green, Color.SeaGreen);

        var edgeA = svc.ComputeEdgeHash(new MemoryStream(a));
        var edgeB = svc.ComputeEdgeHash(new MemoryStream(b));
        var lumA = svc.ComputeHash(new MemoryStream(a));
        var lumB = svc.ComputeHash(new MemoryStream(b));

        int edgeDist = PerceptualHashService.HammingDistance(edgeA, edgeB);
        int lumDist = PerceptualHashService.HammingDistance(lumA, lumB);

        // The color shift moves the luminance hash more than the edge hash.
        Assert.True(edgeDist < lumDist,
            $"edge dist {edgeDist} should be < luminance dist {lumDist} for a color-only change");
    }

    [Fact]
    public void EdgeHash_IsDeterministic()
    {
        var svc = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        var img = DrawCard(Color.Gold, Color.Khaki);

        var h1 = svc.ComputeEdgeHash(new MemoryStream(img));
        var h2 = svc.ComputeEdgeHash(new MemoryStream(img));

        Assert.Equal(h1, h2);
    }
}
