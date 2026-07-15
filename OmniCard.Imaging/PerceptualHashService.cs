using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;
using SkiaSharp;
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
        var sw = Stopwatch.StartNew();
        using var original = LoadBitmap(imageStream);
        onStage?.Invoke(new HashStageResult("Original", BitmapToPng(original)));

        // Grayscale + resize to 32x32 for DCT input
        using var grayscale = ToGrayscaleResized(original, ImageSize, ImageSize);
        onStage?.Invoke(new HashStageResult("Grayscale 32x32", BitmapToPng(grayscale)));

        // Histogram equalization on the grayscale image to normalize lighting.
        // magic-card-detector applies CLAHE to the L channel in LAB;
        // global histogram equalization is sufficient for scanner-captured cards
        // where lighting is uniform across the image.
        HistogramEqualize(grayscale);
        onStage?.Invoke(new HashStageResult("Histogram Equalized", BitmapToPng(grayscale)));

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
        var sw = Stopwatch.StartNew();
        using var original = LoadBitmap(imageStream);
        onStage?.Invoke(new HashStageResult("Original", BitmapToPng(original)));

        // Grayscale + resize to 32x32, then gradient magnitude — captures structure
        // (shape boundaries) and discards color/brightness, so a foil color shift barely
        // moves the hash.
        using var grayscale = ToGrayscaleResized(original, ImageSize, ImageSize);
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
        using var original = new Bitmap(imageStream);
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

            using var cropped = original.Clone(new Rectangle(x, y, w, h), PixelFormat.Format32bppArgb);
            using var croppedStream = new MemoryStream();
            cropped.Save(croppedStream, ImageFormat.Png);
            croppedStream.Position = 0;

            hashes[i] = ComputeHash(croppedStream, onStage is not null ? stage => onStage(new HashStageResult($"Art[{i}] {stage.StageName}", stage.ImageData)) : null);
        }

        return hashes;
    }

    /// <summary>
    /// Computes the Hamming distance between two perceptual hashes.
    /// Lower values indicate more similar images.
    /// </summary>
    public static int HammingDistance(ulong a, ulong b)
        => BitOperations.PopCount(a ^ b);

    private static Bitmap ToGrayscaleResized(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format8bppIndexed);

        // Set grayscale palette
        var palette = result.Palette;
        for (int i = 0; i < 256; i++)
            palette.Entries[i] = Color.FromArgb(i, i, i);
        result.Palette = palette;

        // Draw source into a 32bpp intermediate, then extract luminance
        using var temp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(temp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, width, height);
        }

        var srcData = temp.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

        unsafe
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = (byte*)srcData.Scan0 + y * srcData.Stride;
                byte* dstRow = (byte*)dstData.Scan0 + y * dstData.Stride;
                for (int x = 0; x < width; x++)
                {
                    int b = srcRow[x * 4];
                    int g = srcRow[x * 4 + 1];
                    int r = srcRow[x * 4 + 2];
                    // ITU-R BT.601 luminance
                    dstRow[x] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                }
            }
        }

        temp.UnlockBits(srcData);
        result.UnlockBits(dstData);
        return result;
    }

    private static void HistogramEqualize(Bitmap grayscale)
    {
        int width = grayscale.Width;
        int height = grayscale.Height;
        int totalPixels = width * height;

        var data = grayscale.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadWrite, PixelFormat.Format8bppIndexed);

        // Build histogram
        var histogram = new int[256];
        unsafe
        {
            for (int y = 0; y < height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < width; x++)
                    histogram[row[x]]++;
            }
        }

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
        unsafe
        {
            for (int y = 0; y < height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < width; x++)
                    row[x] = map[row[x]];
            }
        }

        grayscale.UnlockBits(data);
    }

    private static double[,] ExtractPixels(Bitmap grayscale)
    {
        int width = grayscale.Width;
        int height = grayscale.Height;
        var pixels = new double[height, width];

        var data = grayscale.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);

        unsafe
        {
            for (int y = 0; y < height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < width; x++)
                    pixels[y, x] = row[x] / 255.0;
            }
        }

        grayscale.UnlockBits(data);
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

    private static byte[] BitmapToPng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    // Loads an image into a System.Drawing.Bitmap. GDI+ handles PNG/JPEG/BMP directly but
    // throws ArgumentException ("Parameter is not valid") for formats it lacks a codec for
    // (notably WebP, which the poneglyph CDN serves for scan images). For those we fall back
    // to SkiaSharp, which bundles its own codecs (no OS/WIC dependency, so it works on CI and
    // Windows editions without the WebP codec), transcoding to PNG so the pipeline is unchanged.
    private static Bitmap LoadBitmap(Stream imageStream)
    {
        byte[] bytes;
        if (imageStream is MemoryStream ms)
        {
            bytes = ms.ToArray();
        }
        else
        {
            using var buffer = new MemoryStream();
            imageStream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        try
        {
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (ArgumentException)
        {
            // GDI+ cannot decode this format — transcode via SkiaSharp (handles WebP, etc.).
            var png = TranscodeToPng(bytes);
            return new Bitmap(new MemoryStream(png));
        }
    }

    private static byte[] TranscodeToPng(byte[] input)
    {
        using var skBitmap = SKBitmap.Decode(input)
            ?? throw new ArgumentException("Unsupported or corrupt image data — SkiaSharp could not decode it.");
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
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

        using var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int intensity = (int)((dct[y, x] - min) / range * 255);
                bmp.SetPixel(x, y, Color.FromArgb(intensity, intensity, intensity));
            }
        }

        return BitmapToPng(bmp);
    }

    private static byte[] RenderHashGrid(ulong hash)
    {
        int size = HashSize;
        using var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool bit = (hash & (1UL << (y * size + x))) != 0;
                bmp.SetPixel(x, y, bit ? Color.White : Color.Black);
            }
        }

        return BitmapToPng(bmp);
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
