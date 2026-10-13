using SkiaSharp;

namespace OmniCard.Tests.Fakes;

/// <summary>
/// Draws small synthetic PNGs for imaging tests (SkiaSharp, so the suite runs on any OS). Shapes are drawn
/// without anti-aliasing, like GDI+'s defaults the original System.Drawing helpers relied on. Images that
/// need rendered text are pre-rendered fixtures under <c>TestData/Baseline/OcrRendered</c> instead, since
/// font availability differs per OS.
/// </summary>
internal static class TestImages
{
    public static byte[] Png(int width, int height, SKColor background, Action<SKCanvas>? draw = null)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background);
            draw?.Invoke(canvas);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static SKPaint Fill(SKColor color) => new() { Color = color, Style = SKPaintStyle.Fill, IsAntialias = false };

    public static string Fixture(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "TestData", .. parts]);

    /// <summary>A text-bearing card image pre-rendered by the original System.Drawing helpers (Arial Bold).</summary>
    public static byte[] OcrRendered(string name) => File.ReadAllBytes(Fixture("Baseline", "OcrRendered", name + ".png"));
}
