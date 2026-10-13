using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using OmniCard.Shared.Matching;
namespace OmniCard.Imaging;

/// <summary>
/// Computes perceptual hashes (pHash) for card images using a pipeline inspired by
/// tmikonen/magic_card_detector: contrast normalization followed by DCT-based hashing.
/// </summary>
public sealed class PerceptualHashService : IPerceptualHashService
{
    private readonly ILogger<PerceptualHashService> _logger;

    internal const int HashSize = 8;
    private const int ImageSize = HashSize * 4; // 32x32

    public PerceptualHashService(ILogger<PerceptualHashService> logger)
    {
        _logger = logger;
    }

    public ulong ComputeHash(Stream imageStream, Action<HashStageResult>? onStage = null)
    {
        using var original = ImageOps.Decode(imageStream);
        return ComputeHash(original, onStage);
    }

    /// <summary>pHash of an already-decoded image. Lets a caller that hashes several crops of one scan
    /// decode it once instead of round-tripping each crop through an encoded stream.</summary>
    public ulong ComputeHash(SKBitmap original, Action<HashStageResult>? onStage = null)
    {
        var sw = Stopwatch.StartNew();
        onStage?.Invoke(new HashStageResult("Original", ImageOps.EncodePng(original)));

        // Grayscale + resize to 32x32 for DCT input
        var grayscale = ToGrayscaleResized(original, ImageSize, ImageSize);
        onStage?.Invoke(new HashStageResult("Grayscale 32x32", GrayToPng(grayscale)));

        // Histogram equalization on the grayscale image to normalize lighting.
        // magic-card-detector applies CLAHE to the L channel in LAB;
        // global histogram equalization is sufficient for scanner-captured cards
        // where lighting is uniform across the image.
        HistogramEqualize(grayscale);
        onStage?.Invoke(new HashStageResult("Histogram Equalized", GrayToPng(grayscale)));

        // Extract pixel luminance as doubles
        var pixels = ExtractPixels(grayscale);

        // DCT + median-threshold hash (shared with the edge hash).
        var hash = ComputeHashFromPixels(pixels, out var dct);

        if (onStage is not null)
        {
            onStage(new HashStageResult("DCT Coefficients", RenderDctHeatmap(dct)));
            onStage(new HashStageResult("Hash", RenderHashGrid(hash)));
        }

        sw.Stop();
        _logger.LogDebug("Computed pHash {Hash:X16} from {Width}x{Height} image in {ElapsedMs}ms", hash, original.Width, original.Height, sw.ElapsedMilliseconds);
        return hash;
    }

    // DCT-II of the 32x32 input, then a median-threshold hash over the low-frequency
    // 8x8 block (DC excluded from the median). Shared by ComputeHash and ComputeEdgeHash.
    private static ulong ComputeHashFromPixels(double[,] pixels, out double[,] dct)
    {
        dct = ComputeDct2D(pixels);

        var values = new double[HashSize * HashSize - 1];
        int idx = 0;
        for (int y = 0; y < HashSize; y++)
        {
            for (int x = 0; x < HashSize; x++)
            {
                if (y == 0 && x == 0) continue;
                values[idx++] = dct[y, x];
            }
        }

        var median = Median(values);
        ulong hash = 0;
        for (int y = 0; y < HashSize; y++)
        {
            for (int x = 0; x < HashSize; x++)
            {
                if (dct[y, x] > median)
                    hash |= 1UL << (y * HashSize + x);
            }
        }
        return hash;
    }

    public ulong ComputeEdgeHash(Stream imageStream, Action<HashStageResult>? onStage = null)
    {
        using var original = ImageOps.Decode(imageStream);
        return ComputeEdgeHash(original, onStage);
    }

    /// <summary>Edge hash of an already-decoded image (see <see cref="ComputeHash(SKBitmap, Action{HashStageResult}?)"/>).</summary>
    public ulong ComputeEdgeHash(SKBitmap original, Action<HashStageResult>? onStage = null)
    {
        var sw = Stopwatch.StartNew();
        onStage?.Invoke(new HashStageResult("Original", ImageOps.EncodePng(original)));

        // Grayscale + resize to 32x32, then gradient magnitude — captures structure
        // (shape boundaries) and discards color/brightness, so a foil color shift barely
        // moves the hash.
        var grayscale = ToGrayscaleResized(original, ImageSize, ImageSize);
        var pixels = ExtractPixels(grayscale);
        var gradient = GradientMagnitude(pixels);

        var hash = ComputeHashFromPixels(gradient, out var dct);
        if (onStage is not null)
        {
            onStage(new HashStageResult("DCT Coefficients", RenderDctHeatmap(dct)));
            onStage(new HashStageResult("Hash", RenderHashGrid(hash)));
        }

        sw.Stop();
        _logger.LogDebug("Computed edge hash {Hash:X16} from {Width}x{Height} image in {ElapsedMs}ms", hash, original.Width, original.Height, sw.ElapsedMilliseconds);
        return hash;
    }

    // Per-pixel gradient magnitude (|dx| + |dy|) on a [height,width] luminance array in [0,1].
    // Forward differences; the far-edge row/column has no next pixel, so its gradient is zero.
    private static double[,] GradientMagnitude(double[,] pixels)
    {
        int h = pixels.GetLength(0);
        int w = pixels.GetLength(1);
        var result = new double[h, w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x + 1 < w ? Math.Abs(pixels[y, x + 1] - pixels[y, x]) : 0;
                double dy = y + 1 < h ? Math.Abs(pixels[y + 1, x] - pixels[y, x]) : 0;
                result[y, x] = dx + dy;
            }
        }
        return result;
    }

    public ulong[] ComputeArtHash(Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<HashStageResult>? onStage = null)
    {
        using var original = ImageOps.Decode(imageStream);
        return ComputeArtHash(original, cropRegions, onStage);
    }

    /// <summary>Art-region hashes of an already-decoded image. Each crop is hashed in memory — PNG is
    /// lossless, so this yields the same hashes the old encode-then-decode round trip did, far faster.</summary>
    public ulong[] ComputeArtHash(SKBitmap original, (double X, double Y, double W, double H)[] cropRegions, Action<HashStageResult>? onStage = null)
    {
        var hashes = new ulong[cropRegions.Length];

        for (int i = 0; i < cropRegions.Length; i++)
        {
            var (xPct, yPct, wPct, hPct) = cropRegions[i];
            var x = (int)(xPct * original.Width);
            var y = (int)(yPct * original.Height);
            var w = Math.Min((int)(wPct * original.Width), original.Width - x);
            var h = Math.Min((int)(hPct * original.Height), original.Height - y);

            if (w < 8 || h < 8)
            {
                _logger.LogWarning("Art crop region {Index} too small ({W}x{H}), skipping", i, w, h);
                continue;
            }

            using var cropped = ImageOps.Crop(original, new Rectangle(x, y, w, h));
            hashes[i] = ComputeHash(cropped, onStage is not null ? stage => onStage(new HashStageResult($"Art[{i}] {stage.StageName}", stage.ImageData)) : null);
        }

        return hashes;
    }

    /// <summary>
    /// Computes the Hamming distance between two perceptual hashes.
    /// Lower values indicate more similar images.
    /// </summary>
    public static int HammingDistance(ulong a, ulong b)
        => BitOperations.PopCount(a ^ b);

    // 8-bit luminance raster (row-major, stride = Width) standing in for the old 8bpp indexed Bitmap.
    private sealed record Gray(byte[] Pixels, int Width, int Height);

    private static Gray ToGrayscaleResized(SKBitmap source, int width, int height)
    {
        // Scale exactly as GDI+ HighQualityBicubic DrawImage onto a transparent 32bpp bitmap did,
        // then extract luminance (alpha ignored, as before).
        var src = ImageOps.GetBgra(source);
        var temp = GdiPlusResampler.Resize(src, source.Width, source.Height, source.Width * 4, width, height);
        var result = new byte[width * height];
        for (int i = 0; i < result.Length; i++)
        {
            int b = temp[i * 4];
            int g = temp[i * 4 + 1];
            int r = temp[i * 4 + 2];
            // ITU-R BT.601 luminance
            result[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
        }
        return new Gray(result, width, height);
    }

    private static void HistogramEqualize(Gray grayscale)
    {
        var pixels = grayscale.Pixels;
        int totalPixels = pixels.Length;

        // Build histogram
        var histogram = new int[256];
        foreach (var v in pixels)
            histogram[v]++;

        // Build cumulative distribution function and map
        var map = new byte[256];
        int cumulative = 0;
        int cdfMin = 0;
        for (int i = 0; i < 256; i++)
        {
            if (cdfMin == 0 && histogram[i] > 0)
                cdfMin = histogram[i];
            cumulative += histogram[i];
            map[i] = (byte)Math.Clamp(
                (int)Math.Round((double)(cumulative - cdfMin) / (totalPixels - cdfMin) * 255), 0, 255);
        }

        // Apply mapping
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = map[pixels[i]];
    }

    private static double[,] ExtractPixels(Gray grayscale)
    {
        var pixels = new double[grayscale.Height, grayscale.Width];
        for (int y = 0; y < grayscale.Height; y++)
            for (int x = 0; x < grayscale.Width; x++)
                pixels[y, x] = grayscale.Pixels[y * grayscale.Width + x] / 255.0;
        return pixels;
    }

    private static double[,] ComputeDct2D(double[,] input)
    {
        int n = input.GetLength(0);
        var temp = new double[n, n];
        var result = new double[n, n];

        // DCT along rows
        for (int y = 0; y < n; y++)
        {
            for (int k = 0; k < n; k++)
            {
                double sum = 0;
                for (int x = 0; x < n; x++)
                    sum += input[y, x] * Math.Cos(Math.PI * (2 * x + 1) * k / (2.0 * n));

                temp[y, k] = sum * Math.Sqrt(2.0 / n) * (k == 0 ? 1.0 / Math.Sqrt(2) : 1.0);
            }
        }

        // DCT along columns
        for (int x = 0; x < n; x++)
        {
            for (int k = 0; k < n; k++)
            {
                double sum = 0;
                for (int y = 0; y < n; y++)
                    sum += temp[y, x] * Math.Cos(Math.PI * (2 * y + 1) * k / (2.0 * n));

                result[k, x] = sum * Math.Sqrt(2.0 / n) * (k == 0 ? 1.0 / Math.Sqrt(2) : 1.0);
            }
        }

        return result;
    }

    private static byte[] GrayToPng(Gray gray)
    {
        using var bmp = new SKBitmap(new SKImageInfo(gray.Width, gray.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        System.Runtime.InteropServices.Marshal.Copy(gray.Pixels, 0, bmp.GetPixels(), gray.Pixels.Length);
        return ImageOps.EncodePng(bmp);
    }

    private static byte[] RenderDctHeatmap(double[,] dct)
    {
        int size = HashSize;
        // Find min/max of the low-frequency block for normalization
        double min = double.MaxValue, max = double.MinValue;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var v = dct[y, x];
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        double range = max - min;
        if (range == 0) range = 1;

        var pixels = new byte[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = (byte)(int)((dct[y, x] - min) / range * 255);
        }

        return GrayToPng(new Gray(pixels, size, size));
    }

    private static byte[] RenderHashGrid(ulong hash)
    {
        int size = HashSize;
        var pixels = new byte[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool bit = (hash & (1UL << (y * size + x))) != 0;
                pixels[y * size + x] = bit ? (byte)255 : (byte)0;
            }
        }

        return GrayToPng(new Gray(pixels, size, size));
    }

    private static double Median(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
