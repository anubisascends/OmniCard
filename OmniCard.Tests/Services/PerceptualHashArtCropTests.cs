using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class PerceptualHashArtCropTests
{
    private static readonly (double X, double Y, double W, double H)[] TestCropRegions =
    [
        (0.07, 0.11, 0.86, 0.44), // Modern
        (0.00, 0.00, 1.00, 0.55), // Borderless
        (0.10, 0.10, 0.80, 0.42), // Retro
    ];

    [Fact]
    public void ComputeArtHash_ReturnsOneHashPerRegion()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        using var stream = CreateCardImage();

        var hashes = service.ComputeArtHash(stream, TestCropRegions);

        Assert.Equal(3, hashes.Length);
        Assert.All(hashes, h => Assert.NotEqual(0UL, h));
    }

    [Fact]
    public void ComputeArtHash_DifferentRegions_ProduceDifferentHashes()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        using var stream = CreateCardImage();

        var hashes = service.ComputeArtHash(stream, TestCropRegions);

        // At least two of the three hashes should differ (different crops of the same image)
        Assert.True(hashes[0] != hashes[1] || hashes[1] != hashes[2],
            "Expected at least some crop regions to produce different hashes");
    }

    [Fact]
    public void ComputeArtHash_SameInput_Deterministic()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

        using var stream1 = CreateCardImage();
        var hashes1 = service.ComputeArtHash(stream1, TestCropRegions);

        using var stream2 = CreateCardImage();
        var hashes2 = service.ComputeArtHash(stream2, TestCropRegions);

        for (int i = 0; i < hashes1.Length; i++)
            Assert.Equal(hashes1[i], hashes2[i]);
    }

    [Fact]
    public void ComputeArtHash_SingleRegion_ReturnsOneHash()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        using var stream = CreateCardImage();

        var hashes = service.ComputeArtHash(stream, [(0.07, 0.11, 0.86, 0.44)]);

        Assert.Single(hashes);
        Assert.NotEqual(0UL, hashes[0]);
    }

    [Fact]
    public void ComputeArtHash_DiffersFromFullCardHash()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

        using var stream1 = CreateCardImage();
        var fullHash = service.ComputeHash(stream1);

        using var stream2 = CreateCardImage();
        var artHashes = service.ComputeArtHash(stream2, TestCropRegions);

        // Art hash (any region) should differ from full-card hash
        Assert.All(artHashes, h => Assert.NotEqual(fullHash, h));
    }

    /// <summary>
    /// Creates a 500x700 test image simulating a card with distinct art and frame regions.
    /// </summary>
    private static MemoryStream CreateCardImage()
    {
        using var bmp = new Bitmap(500, 700, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            // Frame (border) — solid gray
            g.Clear(Color.Gray);
            // Art region — colorful shapes (roughly 7%-93% X, 11%-55% Y)
            g.FillRectangle(Brushes.Blue, 35, 77, 200, 150);
            g.FillEllipse(Brushes.Red, 150, 120, 180, 130);
            g.FillRectangle(Brushes.Green, 100, 200, 250, 80);
            // Text box — white area below art
            g.FillRectangle(Brushes.White, 35, 400, 430, 250);
        }
        var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return ms;
    }
}
