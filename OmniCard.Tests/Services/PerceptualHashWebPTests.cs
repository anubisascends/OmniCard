using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

// The poneglyph CDN serves scan images as WebP. System.Drawing/GDI+ cannot decode WebP
// (throws "Parameter is not valid"), so those cards never hashed. ComputeHash must fall
// back to a WebP-capable decoder.
public class PerceptualHashWebPTests
{
    // A 16x16 image, encoded two ways from identical pixels.
    private const string WebPBase64 =
        "UklGRjAAAABXRUJQVlA4TCMAAAAvD8ADALkyRPQ/dvWvf/Q/QKRtUwn3b3jwdCAmICYArpP1HwA=";
    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAV0lEQVR4nJXLWwqAMAxE0VTHt1b3v1oRUapt0gkc7t8NInJ44IoEHp6hISEZWga+A6qQDZ0NpaE3QBkGDfRhLII5TDnUhvmHGZYUOawvfthuriGKRO+wn9UfBvbMpguPAAAAAElFTkSuQmCC";

    [Fact]
    public void ComputeHash_WebPImage_DoesNotThrow()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        using var stream = new MemoryStream(Convert.FromBase64String(WebPBase64));

        // Before the fix this throws ArgumentException ("Parameter is not valid") from GDI+.
        var ex = Record.Exception(() => service.ComputeHash(stream));

        Assert.Null(ex);
    }

    [Fact]
    public void ComputeHash_WebPAndPng_SamePixels_ProduceNearIdenticalHash()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

        using var webpStream = new MemoryStream(Convert.FromBase64String(WebPBase64));
        using var pngStream = new MemoryStream(Convert.FromBase64String(PngBase64));

        var webpHash = service.ComputeHash(webpStream);
        var pngHash = service.ComputeHash(pngStream);

        // Same pixels via either codec must produce a near-identical perceptual hash
        // (the WIC decode→re-encode path may differ by a couple of LSBs vs a direct GDI+
        // decode; a large distance would signal a decode bug like channel swapping).
        int hamming = System.Numerics.BitOperations.PopCount(webpHash ^ pngHash);
        Assert.True(hamming <= 6, $"WebP vs PNG pHash Hamming distance was {hamming} (expected small)");
    }

    [Fact]
    public void ComputeHash_WebP_IsDeterministic()
    {
        var service = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        var bytes = Convert.FromBase64String(WebPBase64);

        var h1 = service.ComputeHash(new MemoryStream(bytes));
        var h2 = service.ComputeHash(new MemoryStream(bytes));

        Assert.Equal(h1, h2);
    }
}
