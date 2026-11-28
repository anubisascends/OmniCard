using System.Drawing;
using System.Text.RegularExpressions;
using OmniCard.Shared.Matching;
using SkiaSharp;

namespace OmniCard.Imaging;

/// <summary>
/// Locates and straightens the two codes in a Cyberpunk TCG card's lower-left corner so Tesseract can read
/// them as ordinary horizontal lines:
/// <list type="bullet">
/// <item>the product code ("MS01 - WNC [A]"), yellow text running down the left edge — rotated 90°;</item>
/// <item>the collector number ("120", "β120" on Beta), dark digits set diagonally in a yellow box —
/// isolated as the dark ink the box encloses, its slant measured from the ink itself (principal axis),
/// then rotated level.</item>
/// </list>
/// Each code is rendered as a clean black-on-white line (ink isolated by colour, so the dark frame, the
/// blue trim and the patterned borders of some cards never reach the OCR). Tuned on real flatbed scans
/// (OmniCard.Tests/TestData/CyberpunkCode).
/// </summary>
internal static class CyberpunkCodeReader
{
    /// <summary>The left-edge band holding the vertical product code (fractions of the card).</summary>
    internal static readonly (double X, double Y, double W, double H) ProductRegion = (0.01, 0.78, 0.08, 0.215);

    /// <summary>The area around the diagonal yellow number box.</summary>
    internal static readonly (double X, double Y, double W, double H) NumberRegion = (0.035, 0.85, 0.15, 0.14);

    internal const string ProductWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-[]";
    internal const string NumberWhitelist = "0123456789B";

    private const int GlyphHeight = 56;   // rendered ink height Tesseract sees
    private const int Margin = 24;

    // --- Product code ---

    /// <summary>The product code straightened to a horizontal black-on-white line — as drawn, with bolder
    /// strokes, and smaller — or empty when the band holds no yellow text.</summary>
    internal static List<SKBitmap> RenderProductLines(SKBitmap card)
    {
        var rect = OcrMatchingService.ToPixelRect(ProductRegion, card.Width, card.Height);
        if (rect.Width < 8 || rect.Height < 20) return [];
        var (px, w, h) = CropBgra(card, rect);

        // Yellow text over a black, dark-blue or patterned border. Over blue the thin strokes blend greenish,
        // so "yellow" is loose: bright in red and green, and not white or grey (or the blue trim).
        var mask = new bool[w * h];
        for (int i = 0; i < w * h; i++)
            mask[i] = Yellowness(px, i) > 25 && MinRg(px, i) > 100;

        // Drop what isn't a glyph: specks, and anything far bigger than a letter — the yellow number box
        // (or its edge) can poke into the band.
        var maxGlyph = Math.Max(6, (int)(0.032 * card.Height));
        KeepComponents(mask, w, h, c => c.Area >= 4 && c.Width <= maxGlyph && c.Height <= maxGlyph);
        if (mask.Count(m => m) < 30) return [];

        // Draw from each pixel's red/green brightness (not the 0/1 mask): the letters are only ~10 px tall,
        // and the soft edges keep stroke shapes an upscaled threshold would lose. The background — black or
        // dark blue — is dark in red and green whatever its pattern. Scaled to the line's own strong strokes.
        var keep = Dilate(mask, w, h);
        var full = Percentile(Enumerable.Range(0, w * h).Where(i => mask[i]).Select(i => (double)MinRg(px, i)), 0.8);
        const double floor = 70;
        var ink = new float[w * h];
        for (int i = 0; i < ink.Length; i++)
            if (keep[i] && Yellowness(px, i) > 0)
                ink[i] = (float)Math.Clamp((MinRg(px, i) - floor) / Math.Max(30, full - floor), 0, 1);

        // The text reads top-to-bottom (letters' tops face right): a quarter turn anticlockwise levels it.
        // Thin letters on patterned frames read better bolder; the variants vote.
        var bold = ink.Select(v => Math.Min(1f, v * 1.8f)).ToArray();
        return [Render(ink, w, h, 90), Render(bold, w, h, 90), Render(ink, w, h, 90, glyphHeight: 40)];
    }

    // --- Collector number ---

    /// <summary>The collector number straightened to a horizontal black-on-white line, plus alternates a
    /// few degrees either side of the measured slant; empty when no yellow box is found.</summary>
    internal static List<SKBitmap> RenderNumberLines(SKBitmap card)
    {
        var rect = OcrMatchingService.ToPixelRect(NumberRegion, card.Width, card.Height);
        if (rect.Width < 10 || rect.Height < 10) return [];
        var (px, w, h) = CropBgra(card, rect);

        var yellow = new bool[w * h];
        for (int i = 0; i < w * h; i++)
            yellow[i] = Yellowness(px, i) > 60 && MinRg(px, i) > 120;

        // The box is the largest yellow component (the product code's yellow letters are small).
        var box = Components(yellow, w, h).OrderByDescending(c => c.Area).FirstOrDefault();
        if (box is null || box.Area < (int)(0.0006 * card.Width * card.Height)) return [];

        // Ink = dark pixels inside the box's bounds that yellow fully encloses (not connected to the bounds'
        // edge — that's the card frame around the box). A loose yellow test makes the barrier, so the
        // box's anti-aliased rim can't leak the frame into the digits; the yellow inside a 0 or a 4 is
        // never dark, so counters stay open.
        int bx0 = box.X0, by0 = box.Y0, bw = box.Width, bh = box.Height;
        var inBox = new bool[bw * bh];
        var dark = new bool[bw * bh];
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                var src = (y + by0) * w + x + bx0;
                inBox[y * bw + x] = Yellowness(px, src) > 35 && MinRg(px, src) > 90;
                dark[y * bw + x] = Luminance(px, src) < 125;
            }
        var outside = new bool[bw * bh];
        var stack = new Stack<int>();
        for (int x = 0; x < bw; x++) { Seed(x, 0); Seed(x, bh - 1); }
        for (int y = 0; y < bh; y++) { Seed(0, y); Seed(bw - 1, y); }
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            int x = i % bw, y = i / bw;
            if (x > 0) Seed(x - 1, y);
            if (x < bw - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < bh - 1) Seed(x, y + 1);
        }
        void Seed(int x, int y)
        {
            var i = y * bw + x;
            if (inBox[i] || outside[i]) return;
            outside[i] = true;
            stack.Push(i);
        }

        var digits = new bool[bw * bh];
        for (int i = 0; i < digits.Length; i++) digits[i] = dark[i] && !inBox[i] && !outside[i];
        KeepComponents(digits, bw, bh, c => c.Area >= 6);
        if (digits.Count(m => m) < 40) return [];

        // Draw from how dark each pixel is, around the digits only (see RenderProductLine).
        var keep = Dilate(digits, bw, bh);
        double Lum(int i) => Luminance(px, (i / bw + by0) * w + i % bw + bx0);
        var full = Percentile(Enumerable.Range(0, bw * bh).Where(i => digits[i]).Select(Lum), 0.2);
        var ink = new float[bw * bh];
        for (int i = 0; i < ink.Length; i++)
            if (keep[i] && !outside[i])
                ink[i] = (float)Math.Clamp((190 - Lum(i)) / Math.Max(30, 190 - full), 0, 1);

        var slant = PrincipalAngle(digits, bw, bh);
        return [Render(ink, bw, bh, slant), Render(ink, bw, bh, slant - 6), Render(ink, bw, bh, slant + 6)];
    }

    // --- Parsing ---

    // "MS01 - WNC [A]": product line (two letters + two digits), the set code, the bracketed variant.
    // Digit slots accept the letters Tesseract confuses with 0/1 (and a doubled "OQ1"); a bracket may read
    // as I, L, J, T, ( or | — or as two of them ("WNCI[A]"). The opening one is required, so a line whose
    // variant didn't read ("MS01-WNC") isn't split into set "WN", variant "C".
    private static readonly Regex ProductPattern = new(
        @"([A-Z]{2}[0-9OQDILT]{2,3}?)-*([A-Z]{2,5}?)[\[(|IJLT]{1,2}([A-Z])[\])|IJLT]{0,2}$", RegexOptions.Compiled);

    /// <summary>Parses an OCR'd product line into (product, set code, variant). The set code is required.</summary>
    internal static (string Product, string SetCode, string? Variant)? ParseProduct(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = Regex.Replace(text.ToUpperInvariant(), @"\s+", "");
        var m = ProductPattern.Match(s);
        if (!m.Success)
        {
            // The variant didn't read: settle for "MS01-WNC".
            var bare = Regex.Match(s, @"([A-Z]{2}[0-9OQDILT]{2,3}?)-*([A-Z]{2,5})$");
            if (!bare.Success) return null;
            return (FixDigits(bare.Groups[1].Value), bare.Groups[2].Value, null);
        }
        return (FixDigits(m.Groups[1].Value), m.Groups[2].Value, m.Groups[3].Value);
    }

    // The product is two letters and two digits; a doubled read ("OQ1") keeps its last two.
    private static string FixDigits(string product) =>
        product[..2] + new string(product[2..].Select(c => c switch
        {
            'O' or 'Q' or 'D' => '0',
            'I' or 'L' or 'T' => '1',
            _ => c,
        }).ToArray())[^2..];

    /// <summary>Parses an OCR'd number: three digits, optionally preceded by the Beta β (which Tesseract reads
    /// as B or 8 — no printed number runs past three digits, so a fourth leading character is the β).</summary>
    internal static (string Number, bool Beta)? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = Regex.Replace(text.ToUpperInvariant(), @"\s+", "");
        var m = Regex.Match(s, @"^([B8])?(\d{3})$");
        if (!m.Success) return null;
        return (m.Groups[2].Value, m.Groups[1].Success);
    }

    // --- Pixel helpers ---

    private static (byte[] Px, int W, int H) CropBgra(SKBitmap card, Rectangle rect)
    {
        using var crop = ImageOps.Crop(card, rect);
        return (ImageOps.GetBgra(crop), crop.Width, crop.Height);
    }

    private static int MinRg(byte[] px, int i) => Math.Min(px[i * 4 + 2], px[i * 4 + 1]);

    private static double Luminance(byte[] px, int i) => 0.299 * px[i * 4 + 2] + 0.587 * px[i * 4 + 1] + 0.114 * px[i * 4];

    // How much more red/green than blue a pixel is — high for the card's yellow, ~0 for black/white/grey,
    // negative for the blue trim.
    private static int Yellowness(byte[] px, int i) => MinRg(px, i) - px[i * 4];

    internal sealed class Component
    {
        public List<int> Pixels { get; } = [];
        public int X0 = int.MaxValue, Y0 = int.MaxValue, X1 = -1, Y1 = -1;
        public int Area => Pixels.Count;
        public int Width => X1 - X0 + 1;
        public int Height => Y1 - Y0 + 1;
    }

    /// <summary>8-connected components of the set pixels.</summary>
    internal static List<Component> Components(bool[] mask, int w, int h)
    {
        var seen = new bool[mask.Length];
        var result = new List<Component>();
        var stack = new Stack<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start]) continue;
            var c = new Component();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                c.Pixels.Add(i);
                int x = i % w, y = i / w;
                if (x < c.X0) c.X0 = x;
                if (x > c.X1) c.X1 = x;
                if (y < c.Y0) c.Y0 = y;
                if (y > c.Y1) c.Y1 = y;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var n = ny * w + nx;
                        if (mask[n] && !seen[n]) { seen[n] = true; stack.Push(n); }
                    }
            }
            result.Add(c);
        }
        return result;
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0 : sorted[(int)Math.Clamp(p * (sorted.Count - 1), 0, sorted.Count - 1)];
    }

    private static bool[] Dilate(bool[] mask, int w, int h)
    {
        var result = new bool[mask.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!mask[y * w + x]) continue;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h) result[ny * w + nx] = true;
                    }
            }
        return result;
    }

    private static void KeepComponents(bool[] mask, int w, int h, Func<Component, bool> keep)
    {
        foreach (var c in Components(mask, w, h))
            if (!keep(c))
                foreach (var i in c.Pixels) mask[i] = false;
    }

    /// <summary>The ink's principal axis in degrees (image coordinates, y down; 0 = horizontal, positive =
    /// sloping down to the right), in (-90, 90].</summary>
    internal static double PrincipalAngle(bool[] mask, int w, int h)
    {
        double n = 0, sx = 0, sy = 0;
        for (int i = 0; i < mask.Length; i++)
            if (mask[i]) { n++; sx += i % w; sy += i / w; }
        if (n == 0) return 0;
        double mx = sx / n, my = sy / n, cxx = 0, cyy = 0, cxy = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) continue;
            double dx = i % w - mx, dy = i / w - my;
            cxx += dx * dx; cyy += dy * dy; cxy += dx * dy;
        }
        return 0.5 * Math.Atan2(2 * cxy, cxx - cyy) * 180 / Math.PI;
    }

    /// <summary>Draws the ink (0 = none … 1 = full) black on white, turned <paramref name="degrees"/>
    /// anticlockwise (so ink sloping down-right at that angle comes out level), scaled to
    /// <see cref="GlyphHeight"/> with a white margin.</summary>
    internal static SKBitmap Render(float[] mask, int w, int h, double degrees, int glyphHeight = GlyphHeight)
    {
        var t = degrees * Math.PI / 180;
        double cos = Math.Cos(t), sin = Math.Sin(t);
        double cx = w / 2.0, cy = h / 2.0;

        // Bounds of the ink once turned.
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i] < 0.5f) continue;
            double x = i % w + 0.5 - cx, y = i / w + 0.5 - cy;
            double rx = x * cos + y * sin, ry = -x * sin + y * cos;
            minX = Math.Min(minX, rx); maxX = Math.Max(maxX, rx);
            minY = Math.Min(minY, ry); maxY = Math.Max(maxY, ry);
        }
        if (minX > maxX) return ImageOps.Create(2 * Margin, 2 * Margin, SKColors.White);

        var scale = glyphHeight / Math.Max(1.0, maxY - minY);
        int ow = (int)Math.Ceiling((maxX - minX) * scale) + 2 * Margin;
        int oh = (int)Math.Ceiling((maxY - minY) * scale) + 2 * Margin;
        var outPx = new byte[ow * oh * 4];
        for (int v = 0; v < oh; v++)
            for (int u = 0; u < ow; u++)
            {
                // Back from the output pixel to the source, bilinear over the 0/1 mask: smooth grey edges
                // read better than a hard threshold of the upscaled pixel steps.
                double rx = (u - Margin + 0.5) / scale + minX, ry = (v - Margin + 0.5) / scale + minY;
                double sx = rx * cos - ry * sin + cx - 0.5, sy = rx * sin + ry * cos + cy - 0.5;
                var o = (v * ow + u) * 4;
                byte c = (byte)Math.Round(255 * (1 - Math.Clamp(Sample(mask, w, h, sx, sy), 0, 1)));
                outPx[o] = outPx[o + 1] = outPx[o + 2] = c;
                outPx[o + 3] = 255;
            }
        return ImageOps.FromBgra(outPx, ow, oh);
    }

    private static double Sample(float[] mask, int w, int h, double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        double At(int xx, int yy) => xx >= 0 && yy >= 0 && xx < w && yy < h ? mask[yy * w + xx] : 0;
        return At(x0, y0) * (1 - fx) * (1 - fy) + At(x0 + 1, y0) * fx * (1 - fy)
             + At(x0, y0 + 1) * (1 - fx) * fy + At(x0 + 1, y0 + 1) * fx * fy;
    }
}
