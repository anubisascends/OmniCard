using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OmniCard.Imaging;

/// <summary>
/// Finds the One Piece collector number ("OP14-109") in a scan and renders it as a clean, tight,
/// OCR-ready line. A fixed crop can't work: flatbed scans leave a varying mat around the card, so the
/// number's height drifts by ~4% of the image (measured 0.924–0.965 on one batch) — more than the line
/// is tall — and a box loose enough to always catch it also catches the subtype banner, the rarity box
/// and the cost circle, which Tesseract then misreads together with the number.
///
/// Instead: binarize the bottom-right band (both polarities — the number is dark on yellow cards and
/// white on every other colour), take glyph-sized connected components, chain them into text rows, and
/// pick the right-most row (the number sits right of the centered subtype). Only that row's own
/// components are rendered — anti-aliased, upscaled, dark-on-white with a margin — so the neighbouring
/// rarity box and cost circle never reach the OCR.
/// </summary>
internal static class OptcgCollectorLineLocator
{
    // The search band: bottom-right of the scan, generous enough for any mat size.
    internal static readonly (double X, double Y, double W, double H) SearchRegion = (0.50, 0.86, 0.47, 0.14);

    // Glyph cap height as a fraction of the scan height (the number is ~0.011–0.013).
    private const double MinGlyphHeight = 0.007, MaxGlyphHeight = 0.022;
    // White-on-colour glyphs bleed into one another, so a component may be several glyphs wide; a solid
    // one (the rarity box) is not text.
    private const double MaxBlobAspect = 7.0, MaxBlobFill = 0.7, SingleGlyphAspect = 1.3, GlyphPitch = 0.62;
    // "OP14-109" is 7 glyph-sized marks (the dash is too short to count).
    private const int MinRowGlyphs = 5;
    private const int RenderGlyphHeight = 48, RenderMargin = 24;

    private readonly record struct Component(int Id, int X, int Y, int W, int H, int Pixels)
    {
        public int Right => X + W;
        public int Bottom => Y + H;
        public double CenterY => Y + H / 2.0;
        public int GlyphCount => W <= H * SingleGlyphAspect ? 1 : Math.Max(1, (int)Math.Round(W / (GlyphPitch * H)));
    }

    /// <summary>Candidate text lines, right-most first (at most <paramref name="max"/>). The caller owns
    /// and disposes the bitmaps.</summary>
    public static List<Bitmap> Locate(Bitmap scan, int max = 2)
    {
        var region = new Rectangle(
            (int)(SearchRegion.X * scan.Width), (int)(SearchRegion.Y * scan.Height),
            (int)(SearchRegion.W * scan.Width), scan.Height - (int)(SearchRegion.Y * scan.Height));
        region.Intersect(new Rectangle(0, 0, scan.Width, scan.Height));
        if (region.Width < 20 || region.Height < 10) return [];

        var (lum, w, h) = Luminance(scan, region);
        var threshold = Otsu(lum);
        double minH = MinGlyphHeight * scan.Height, maxH = MaxGlyphHeight * scan.Height;

        var rows = new List<(int Right, Bitmap Line)>();
        foreach (var darkText in new[] { true, false })
        {
            var ink = new bool[lum.Length];
            for (int i = 0; i < ink.Length; i++)
                ink[i] = darkText ? lum[i] <= threshold : lum[i] > threshold;
            var labels = new int[lum.Length];
            var components = Label(ink, w, h, labels);

            var glyphs = components
                .Where(c => c.H >= minH && c.H <= maxH
                    && (c.W <= c.H * SingleGlyphAspect || (c.W <= c.H * MaxBlobAspect && c.Pixels < MaxBlobFill * c.W * c.H)))
                .OrderBy(c => c.X);
            foreach (var row in ChainRows(glyphs).Where(r => r.Sum(c => c.GlyphCount) >= MinRowGlyphs))
                rows.Add((row.Max(c => c.Right), Render(lum, labels, components, row, threshold, w, h)));
        }

        var ordered = rows.OrderByDescending(r => r.Right).ToList();
        foreach (var (_, line) in ordered.Skip(max)) line.Dispose();
        return ordered.Take(max).Select(r => r.Line).ToList();
    }

    // Greedy left-to-right chaining: a glyph joins the first row whose height and baseline it matches
    // and whose last glyph ends within ~1.6 glyph heights (wide enough to bridge the dash).
    private static List<List<Component>> ChainRows(IEnumerable<Component> glyphs)
    {
        var rows = new List<List<Component>>();
        foreach (var g in glyphs)
        {
            List<Component>? joined = null;
            foreach (var row in rows)
            {
                var last = row[^1];
                var rowHeight = row.Average(c => c.H);
                var rowCenter = row.Average(c => c.CenterY);
                if (Math.Abs(g.CenterY - rowCenter) < rowHeight * 0.35
                    && Math.Abs(g.H - rowHeight) < rowHeight * 0.35
                    && g.X >= last.X && g.X - last.Right < rowHeight * 1.6)
                {
                    joined = row;
                    break;
                }
            }
            if (joined is null) rows.Add([g]);
            else joined.Add(g);
        }
        return rows;
    }

    // Renders only the row's own components (plus the small marks between them — the dash) from the
    // grey levels, normalised to dark-on-white and upscaled bicubically, so glyph edges stay smooth.
    private static Bitmap Render(byte[] lum, int[] labels, List<Component> all, List<Component> row, int threshold, int w, int h)
    {
        int x0 = row.Min(c => c.X), x1 = row.Max(c => c.Right), y0 = row.Min(c => c.Y), y1 = row.Max(c => c.Bottom);
        var glyphHeight = row.Average(c => c.H);
        double bandTop = y0 - glyphHeight * 0.25, bandBottom = y1 + glyphHeight * 0.25, slack = glyphHeight * 0.2;
        var keep = all
            .Where(c => c.Y >= bandTop && c.Bottom <= bandBottom && c.X >= x0 - slack && c.Right <= x1 + slack && c.H <= glyphHeight * 1.35)
            .Select(c => c.Id)
            .ToHashSet();

        var pad = (int)Math.Ceiling(glyphHeight * 0.3);
        x0 = Math.Max(0, x0 - pad); y0 = Math.Max(0, y0 - pad);
        x1 = Math.Min(w, x1 + pad); y1 = Math.Min(h, y1 + pad);
        int cw = x1 - x0, ch = y1 - y0;

        double inkSum = 0; int inkCount = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (keep.Contains(labels[y * w + x])) { inkSum += lum[y * w + x]; inkCount++; }
        var inkLevel = inkCount > 0 ? inkSum / inkCount : 0;
        var backgroundLevel = 2.0 * threshold - inkLevel; // mirror the ink level about the threshold
        if (Math.Abs(backgroundLevel - inkLevel) < 1) backgroundLevel = inkLevel + 1;

        var pixels = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int sx = x0 + x, sy = y0 + y;
                // A one-pixel halo keeps each glyph's anti-aliased edge.
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++)
                    for (int dx = -1; dx <= 1 && !near; dx++)
                    {
                        int nx = sx + dx, ny = sy + dy;
                        near = nx >= 0 && ny >= 0 && nx < w && ny < h && keep.Contains(labels[ny * w + nx]);
                    }
                var t = near ? (lum[sy * w + sx] - inkLevel) / (backgroundLevel - inkLevel) : 1.0;
                var v = (byte)Math.Clamp(t * 255, 0, 255);
                int o = (y * cw + x) * 4;
                pixels[o] = pixels[o + 1] = pixels[o + 2] = v;
                pixels[o + 3] = 255;
            }

        using var small = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        var data = small.LockBits(new Rectangle(0, 0, cw, ch), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < ch; y++)
                Marshal.Copy(pixels, y * cw * 4, data.Scan0 + y * data.Stride, cw * 4);
        }
        finally { small.UnlockBits(data); }

        var scale = RenderGlyphHeight / glyphHeight;
        int sw = Math.Max(1, (int)(cw * scale)), sh = Math.Max(1, (int)(ch * scale));
        var line = new Bitmap(sw + 2 * RenderMargin, sh + 2 * RenderMargin, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(line);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(small, new Rectangle(RenderMargin, RenderMargin, sw, sh), 0, 0, cw, ch, GraphicsUnit.Pixel);
        return line;
    }

    private static (byte[] Lum, int W, int H) Luminance(Bitmap scan, Rectangle region)
    {
        using var crop = scan.Clone(region, PixelFormat.Format32bppArgb);
        int w = crop.Width, h = crop.Height;
        var data = crop.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var raw = new byte[w * 4];
        var lum = new byte[w * h];
        try
        {
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, raw, 0, raw.Length);
                for (int x = 0; x < w; x++)
                    lum[y * w + x] = (byte)(0.114 * raw[x * 4] + 0.587 * raw[x * 4 + 1] + 0.299 * raw[x * 4 + 2]);
            }
        }
        finally { crop.UnlockBits(data); }
        return (lum, w, h);
    }

    internal static int Otsu(byte[] lum)
    {
        var hist = new int[256];
        foreach (var l in lum) hist[l]++;
        double sum = 0;
        for (int i = 0; i < 256; i++) sum += i * (double)hist[i];
        double sumB = 0, maxVariance = 0;
        int weightB = 0, threshold = 127;
        for (int i = 0; i < 256; i++)
        {
            weightB += hist[i];
            if (weightB == 0) continue;
            int weightF = lum.Length - weightB;
            if (weightF == 0) break;
            sumB += i * (double)hist[i];
            double meanB = sumB / weightB, meanF = (sum - sumB) / weightF;
            double between = (double)weightB * weightF * (meanB - meanF) * (meanB - meanF);
            if (between > maxVariance) { maxVariance = between; threshold = i; }
        }
        return threshold;
    }

    // 8-connected components; labels[] gets each pixel's component id (0 = background).
    private static List<Component> Label(bool[] ink, int w, int h, int[] labels)
    {
        var components = new List<Component>();
        var stack = new Stack<int>();
        for (int start = 0; start < ink.Length; start++)
        {
            if (!ink[start] || labels[start] != 0) continue;
            int id = components.Count + 1;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
            labels[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop(), px = p % w, py = p / w;
                count++;
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx, ny = py + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int q = ny * w + nx;
                        if (ink[q] && labels[q] == 0) { labels[q] = id; stack.Push(q); }
                    }
            }
            components.Add(new Component(id, minX, minY, maxX - minX + 1, maxY - minY + 1, count));
        }
        return components;
    }
}
