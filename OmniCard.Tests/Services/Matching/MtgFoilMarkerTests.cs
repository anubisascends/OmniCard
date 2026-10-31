using SkiaSharp;
using OmniCard.Imaging;
using OmniCard.Tests.Fakes;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// The MTG finish marker (M15+): the mark between set code and language on the bottom-left line 2 is a
/// star on foils ("MID ★ EN") and a bullet on non-foils ("MID • EN"). Drawn rasters stand in for the
/// enlarged corner crop: block "letters" in the real proportions, so the layout and shape rules of
/// <see cref="OcrMatchingService.ClassifyMtgFoilMarker"/> are exercised without scan fixtures.
/// </summary>
public class MtgFoilMarkerTests
{
    private const int W = 700, H = 190;
    private const int LetterH = 40, LetterW = 28, Pitch = 38; // 10px tracking between letters
    private const int Line1Top = 40, Line2Top = 110;

    private enum Mark { None, Bullet, Star }

    // Line 1: "365 R". Line 2: {set code} {mark} {language}, then the artist credit after a word gap.
    private static float[] Render(Mark mark, int setChars = 3, bool darkOnLight = false, bool lastSetCharIsT = false)
    {
        using var bmp = ImageOps.Create(W, H, darkOnLight ? SKColors.White : SKColors.Black);
        using (var canvas = new SKCanvas(bmp))
        {
            using var ink = TestImages.Fill(darkOnLight ? SKColors.Black : SKColors.White);
            for (int i = 0; i < 3; i++) canvas.DrawRect(20 + i * Pitch, Line1Top, LetterW, LetterH, ink);
            canvas.DrawRect(200, Line1Top, LetterW, LetterH, ink);

            int x = 20;
            for (int i = 0; i < setChars; i++, x += Pitch)
            {
                if (lastSetCharIsT && i == setChars - 1)
                {
                    // A "T" a touch shorter than its neighbours: star-sized, with a star's sparse fill.
                    canvas.DrawRect(x, Line2Top + 2, LetterW + 6, 7, ink);
                    canvas.DrawRect(x + LetterW / 2 - 1, Line2Top + 2, 8, LetterH - 4, ink);
                }
                else
                    canvas.DrawRect(x, Line2Top, LetterW, LetterH, ink);
            }
            x += 12; // word-ish space either side of the mark
            int markW = 0;
            float cy = Line2Top + LetterH / 2f;
            if (mark == Mark.Bullet)
            {
                markW = 16;
                canvas.DrawOval(x + 8, cy, 8, 8, ink);
            }
            else if (mark == Mark.Star)
            {
                markW = 36;
                using var star = new SKPath();
                star.AddPoly(StarPoints(x + 18, cy, 18, 7), true);
                canvas.DrawPath(star, ink);
            }
            x += markW + 12;
            for (int i = 0; i < 2; i++, x += Pitch) canvas.DrawRect(x, Line2Top, LetterW, LetterH, ink);
            canvas.DrawRect(x + 40, Line2Top, 60, LetterH, ink); // artist credit
        }
        var lum = new float[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = bmp.GetPixel(x, y);
                lum[y * W + x] = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;
            }
        return lum;
    }

    private static SKPoint[] StarPoints(float cx, float cy, float outer, float inner) =>
        Enumerable.Range(0, 10).Select(i =>
        {
            var r = i % 2 == 0 ? outer : inner;
            var a = -Math.PI / 2 + i * Math.PI / 5;
            return new SKPoint(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a)));
        }).ToArray();

    [Fact]
    public void Bullet_ReadsAsNonFoil() =>
        Assert.False(OcrMatchingService.ClassifyMtgFoilMarker(Render(Mark.Bullet), W, H));

    [Fact]
    public void Star_ReadsAsFoil() =>
        Assert.True(OcrMatchingService.ClassifyMtgFoilMarker(Render(Mark.Star), W, H));

    [Fact]
    public void Star_DarkTextOnWhiteBorder_ReadsAsFoil() =>
        Assert.True(OcrMatchingService.ClassifyMtgFoilMarker(Render(Mark.Star, darkOnLight: true), W, H));

    [Fact]
    public void FourCharacterSetCode_StillFound() =>
        Assert.True(OcrMatchingService.ClassifyMtgFoilMarker(Render(Mark.Star, setChars: 4), W, H));

    [Fact]
    public void NoMark_IsUnknown() =>
        Assert.Null(OcrMatchingService.ClassifyMtgFoilMarker(Render(Mark.None), W, H));

    [Fact]
    public void BlankCorner_IsUnknown() =>
        Assert.Null(OcrMatchingService.ClassifyMtgFoilMarker(new float[W * H], W, H));

    [Fact]
    public void StarShapedLetterBesideTheBullet_IsNotTakenForTheMark()
    {
        // "WOT • EN": the "T" looks like a star between two marks, but the real bullet sits right of it —
        // the bullet is the mark, so the card reads as non-foil.
        var lum = Render(Mark.Bullet, lastSetCharIsT: true);
        Assert.False(OcrMatchingService.ClassifyMtgFoilMarker(lum, W, H));
    }
}
