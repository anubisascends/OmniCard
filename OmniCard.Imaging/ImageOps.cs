using System.Drawing;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;

namespace OmniCard.Imaging;

/// <summary>
/// Cross-platform replacements for the System.Drawing (GDI+) operations the imaging pipeline used. Every
/// bitmap is 32-bit BGRA, non-premultiplied — the byte layout of a GDI+ <c>Format32bppArgb</c> LockBits
/// buffer — so pixel loops ported from System.Drawing read the same channels at the same offsets.
/// Decoding uses SkiaSharp (PNG, JPEG, WebP, GIF, BMP) and LibTiff.NET (TIFF), without colour management or
/// EXIF auto-rotation, matching <c>new Bitmap(stream)</c>.
/// </summary>
public static class ImageOps
{
    static ImageOps() => Tiff.SetErrorHandler(new SilentTiffErrorHandler());

    public static SKImageInfo Info(int width, int height) =>
        new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

    /// <summary>Decodes an encoded image. Throws <see cref="ArgumentException"/> for unsupported or corrupt data.</summary>
    public static SKBitmap Decode(byte[] data)
    {
        if (IsTiff(data))
            return DecodeTiff(data);

        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData)
            ?? throw new ArgumentException("Unsupported or corrupt image data.");
        var info = Info(codec.Info.Width, codec.Info.Height);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new ArgumentException($"Image could not be decoded ({result}).");
        }
        return bitmap;
    }

    public static SKBitmap Decode(Stream stream)
    {
        if (stream is MemoryStream ms)
            return Decode(ms.ToArray());
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Decode(buffer.ToArray());
    }

    public static SKBitmap Create(int width, int height, SKColor fill)
    {
        var bitmap = new SKBitmap(Info(width, height));
        bitmap.Erase(fill);
        return bitmap;
    }

    /// <summary>Tightly packed BGRA copy of the bitmap's pixels (stride = width * 4).</summary>
    public static byte[] GetBgra(SKBitmap bitmap)
    {
        if (bitmap.ColorType == SKColorType.Bgra8888 && bitmap.AlphaType != SKAlphaType.Premul
            && bitmap.RowBytes == bitmap.Width * 4)
            return bitmap.GetPixelSpan().ToArray();

        var info = Info(bitmap.Width, bitmap.Height);
        var buffer = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            using var pixmap = bitmap.PeekPixels();
            if (!pixmap.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes))
                throw new ArgumentException("Bitmap pixels could not be converted to BGRA.");
        }
        finally { handle.Free(); }
        return buffer;
    }

    public static SKBitmap FromBgra(byte[] bgra, int width, int height)
    {
        var bitmap = new SKBitmap(Info(width, height));
        Marshal.Copy(bgra, 0, bitmap.GetPixels(), width * height * 4);
        return bitmap;
    }

    public static SKBitmap Crop(SKBitmap source, Rectangle rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0
            || rect.Right > source.Width || rect.Bottom > source.Height)
            throw new ArgumentOutOfRangeException(nameof(rect), $"Crop {rect} is outside the {source.Width}x{source.Height} image.");
        var src = GetBgra(source);
        var dst = new byte[rect.Width * rect.Height * 4];
        for (int y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(src, ((rect.Y + y) * source.Width + rect.X) * 4, dst, y * rect.Width * 4, rect.Width * 4);
        return FromBgra(dst, rect.Width, rect.Height);
    }

    /// <summary>Scales like GDI+ <c>HighQualityBicubic</c> <c>DrawImage</c> onto a new transparent bitmap
    /// (see <see cref="GdiPlusResampler"/>): edge pixels carry partial alpha.</summary>
    public static SKBitmap Resize(SKBitmap source, int width, int height) =>
        FromBgra(GdiPlusResampler.Resize(GetBgra(source), source.Width, source.Height, source.Width * 4, width, height), width, height);

    /// <summary>Lossless rotation by a multiple of 90 degrees, clockwise (GDI+ <c>RotateFlip</c>).</summary>
    public static SKBitmap Rotate(SKBitmap source, int degreesClockwise)
    {
        int w = source.Width, h = source.Height;
        var src = GetBgra(source);
        switch (((degreesClockwise % 360) + 360) % 360)
        {
            case 0:
                return FromBgra(src, w, h);
            case 180:
            {
                var dst = new byte[src.Length];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        Buffer.BlockCopy(src, (y * w + x) * 4, dst, ((h - 1 - y) * w + (w - 1 - x)) * 4, 4);
                return FromBgra(dst, w, h);
            }
            case 90:
            {
                var dst = new byte[src.Length];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        Buffer.BlockCopy(src, (y * w + x) * 4, dst, (x * h + (h - 1 - y)) * 4, 4);
                return FromBgra(dst, h, w);
            }
            case 270:
            {
                var dst = new byte[src.Length];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        Buffer.BlockCopy(src, (y * w + x) * 4, dst, ((w - 1 - x) * h + y) * 4, 4);
                return FromBgra(dst, h, w);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(degreesClockwise), "Only multiples of 90 degrees are supported.");
        }
    }

    /// <summary>Source-over blend of <paramref name="source"/> onto an opaque <paramref name="target"/> at (x, y).</summary>
    public static void DrawOver(SKBitmap target, SKBitmap source, int x, int y)
    {
        var dst = GetBgra(target);
        var src = GetBgra(source);
        for (int sy = 0; sy < source.Height; sy++)
        {
            int ty = y + sy;
            if (ty < 0 || ty >= target.Height) continue;
            for (int sx = 0; sx < source.Width; sx++)
            {
                int tx = x + sx;
                if (tx < 0 || tx >= target.Width) continue;
                int s = (sy * source.Width + sx) * 4, d = (ty * target.Width + tx) * 4;
                int a = src[s + 3];
                for (int c = 0; c < 3; c++)
                    dst[d + c] = (byte)((src[s + c] * a + dst[d + c] * (255 - a) + 127) / 255);
                dst[d + 3] = 255;
            }
        }
        Marshal.Copy(dst, 0, target.GetPixels(), dst.Length);
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        return data.ToArray();
    }

    /// <summary>JPEG-encodes the colour channels; alpha is discarded (not composited), as GDI+ does.</summary>
    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        var bgra = GetBgra(bitmap);
        using var opaque = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
        Marshal.Copy(bgra, 0, opaque.GetPixels(), bgra.Length);
        using var data = opaque.Encode(SKEncodedImageFormat.Jpeg, quality)
            ?? throw new InvalidOperationException("JPEG encoding failed.");
        return data.ToArray();
    }

    private static bool IsTiff(byte[] data) =>
        data.Length >= 4
        && ((data[0] == 'I' && data[1] == 'I' && (data[2] == 42 || data[2] == 43) && data[3] == 0)
            || (data[0] == 'M' && data[1] == 'M' && data[2] == 0 && (data[3] == 42 || data[3] == 43)));

    private static SKBitmap DecodeTiff(byte[] data)
    {
        using var tiff = Tiff.ClientOpen("image", "r", new MemoryStream(data), new TiffStream())
            ?? throw new ArgumentException("Corrupt TIFF data.");
        int w = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
        int h = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        var raster = new int[w * h];
        if (!tiff.ReadRGBAImageOriented(w, h, raster, Orientation.TOPLEFT))
            throw new ArgumentException("Unsupported TIFF data.");

        var bgra = new byte[w * h * 4];
        for (int i = 0; i < raster.Length; i++)
        {
            int p = raster[i], a = Tiff.GetA(p);
            int r = Tiff.GetR(p), g = Tiff.GetG(p), b = Tiff.GetB(p);
            if (a is > 0 and < 255)
            {
                // The RGBA interface premultiplies; GDI+ hands back straight alpha.
                r = Math.Min(255, r * 255 / a); g = Math.Min(255, g * 255 / a); b = Math.Min(255, b * 255 / a);
            }
            bgra[i * 4] = (byte)b; bgra[i * 4 + 1] = (byte)g; bgra[i * 4 + 2] = (byte)r; bgra[i * 4 + 3] = (byte)a;
        }
        return FromBgra(bgra, w, h);
    }

    private sealed class SilentTiffErrorHandler : TiffErrorHandler
    {
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args) { }
        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
    }
}
