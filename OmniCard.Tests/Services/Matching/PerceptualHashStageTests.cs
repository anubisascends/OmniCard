using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;
using OmniCard.Shared.Matching;
using OmniCard.Tests.Fakes;
using SkiaSharp;

namespace OmniCard.Tests.Services.Matching;

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
            using var bmp = ImageOps.Decode(stage.ImageData);
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
        return new MemoryStream(TestImages.Png(64, 64, SKColors.White, g =>
        {
            using var blue = TestImages.Fill(SKColors.Blue);
            g.DrawRect(SKRect.Create(10, 10, 30, 30), blue);
            using var red = TestImages.Fill(SKColors.Red);
            g.DrawOval(SKRect.Create(25, 25, 30, 30), red);
        }));
    }
}
