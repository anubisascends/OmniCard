using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class PerceptualHashStageTests
{
    [Fact]
    public void ComputeHash_WithCallback_EmitsFiveStages()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        var stages = new List<HashStageResult>();

        using var stream = CreateTestImage();
        var hash = service.ComputeHash(stream, stage => stages.Add(stage));

        Assert.Equal(5, stages.Count);
        Assert.Equal("Original", stages[0].StageName);
        Assert.Equal("Grayscale 32x32", stages[1].StageName);
        Assert.Equal("Histogram Equalized", stages[2].StageName);
        Assert.Equal("DCT Coefficients", stages[3].StageName);
        Assert.Equal("Hash", stages[4].StageName);

        // All stages produce valid PNG data
        foreach (var stage in stages)
        {
            Assert.NotNull(stage.ImageData);
            Assert.True(stage.ImageData.Length > 0);
            // Verify it's a valid image by loading it
            using var ms = new MemoryStream(stage.ImageData);
            using var bmp = new Bitmap(ms);
            Assert.True(bmp.Width > 0);
        }
    }

    [Fact]
    public void ComputeHash_WithoutCallback_ReturnsIdenticalHash()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

        using var stream1 = CreateTestImage();
        var hashWithout = service.ComputeHash(stream1);

        using var stream2 = CreateTestImage();
        var hashWith = service.ComputeHash(stream2, _ => { });

        Assert.Equal(hashWithout, hashWith);
    }

    [Fact]
    public void ComputeHash_NullCallback_NoException()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        using var stream = CreateTestImage();

        var hash = service.ComputeHash(stream, null);

        Assert.NotEqual(0UL, hash);
    }

    private static MemoryStream CreateTestImage()
    {
        // Create a simple 64x64 test image with some variation
        using var bmp = new Bitmap(64, 64, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Blue, 10, 10, 30, 30);
            g.FillEllipse(Brushes.Red, 25, 25, 30, 30);
        }
        var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return ms;
    }
}
