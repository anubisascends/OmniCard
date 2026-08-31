using System.Drawing;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class OcrMatchingServiceTests
{
    [Theory]
    [InlineData(500, 700, 0, 35, 21, 375, 49)]   // Modern: 7%, 3%, 75%, 7% of 500x700
    [InlineData(500, 700, 1, 25, 14, 400, 56)]    // Borderless: 5%, 2%, 80%, 8%
    [InlineData(500, 700, 2, 50, 35, 350, 49)]    // Retro: 10%, 5%, 70%, 7%
    public void ToPixelRect_NameRegions_ReturnsCorrectPixels(
        int imgW, int imgH, int regionIndex, int expectedX, int expectedY, int expectedW, int expectedH)
    {
        var region = OcrMatchingService.NameCropRegions[regionIndex];
        var rect = OcrMatchingService.ToPixelRect(region, imgW, imgH);

        Assert.Equal(expectedX, rect.X);
        Assert.Equal(expectedY, rect.Y);
        Assert.Equal(expectedW, rect.Width);
        Assert.Equal(expectedH, rect.Height);
    }

    [Fact]
    public void ToPixelRect_SymbolRegion_ReturnsCorrectPixels()
    {
        var rect = OcrMatchingService.ToPixelRect(OcrMatchingService.SymbolCropRegion, 500, 700);

        Assert.Equal(410, rect.X);  // 82% of 500
        Assert.Equal(301, rect.Y);  // 43% of 700
        Assert.Equal(60, rect.Width);  // 12% of 500
        Assert.Equal(49, rect.Height); // 7% of 700
    }

    [Fact]
    public void ToPixelRect_ClampsToImageBounds()
    {
        // Region that would extend past image edge
        var rect = OcrMatchingService.ToPixelRect((0.95, 0.95, 0.20, 0.20), 100, 100);

        Assert.Equal(95, rect.X);
        Assert.Equal(95, rect.Y);
        Assert.Equal(5, rect.Width);   // Clamped: min(20, 100-95)
        Assert.Equal(5, rect.Height);  // Clamped
    }

    // --- MTG bottom-left (set code + collector number) parser ---
    // Inputs are real (or realistic) OCR reads of the modern MTG corner block; leading zeros are
    // stripped to match how Scryfall stores the collector number.

    [Theory]
    [InlineData("R 0066\nMKC • EN SVETLIN VELINOV", "MKC", "66")]   // standard modern two-line block
    [InlineData("C 0062\nEOC • EN ALLEN PANAKAL", "EOC", "62")]     // observed sample
    [InlineData("025\nSCD • EN JOHANN BODIN", "SCD", "25")]         // rarity on its own, collector first
    [InlineData("066/281 M\nDMU • EN", "DMU", "66")]                // older "{collector}/{total}" format
    [InlineData("M 0004\nBLC EN", "BLC", "4")]                      // star separator dropped by whitelist
    [InlineData("U 0173\nM3C • EN JESPER EJSING", "M3C", "173")]    // digit-bearing set code
    public void TryExtractMtgSetAndNumber_ParsesRealReads(string ocr, string expectedSet, string expectedNumber)
    {
        var ok = OcrMatchingService.TryExtractMtgSetAndNumber(ocr, out var set, out var number);

        Assert.True(ok);
        Assert.Equal(expectedSet, set);
        Assert.Equal(expectedNumber, number);
    }

    [Fact]
    public void TryExtractMtgSetAndNumber_PrefersCollectorOverCopyrightYear()
    {
        // The copyright year can share the corner block; a shorter non-year number must win.
        var ok = OcrMatchingService.TryExtractMtgSetAndNumber("EOC • EN\nTM & © 2024 WIZARDS 100", out var set, out var number);

        Assert.True(ok);
        Assert.Equal("EOC", set);
        Assert.Equal("100", number);
    }

    [Theory]
    [InlineData("SOME RULES TEXT 123")]     // no language marker → no anchored set code
    [InlineData("MKC • EN")]                 // set code but no collector number
    [InlineData("0066")]                     // collector number but no set code
    [InlineData("")]                         // empty
    public void TryExtractMtgSetAndNumber_RejectsIncompleteReads(string ocr)
    {
        Assert.False(OcrMatchingService.TryExtractMtgSetAndNumber(ocr, out _, out _));
    }

    [Fact]
    public void ToPixelRect_MtgCollectorRegion_IsBottomLeftCorner()
    {
        var rect = OcrMatchingService.ToPixelRect(OcrMatchingService.MtgCollectorRegion, 717, 1001);

        Assert.Equal(14, rect.X);    // 2% of 717
        Assert.Equal(945, rect.Y);   // 94.5% of 1001
        Assert.True(rect.Width > 200 && rect.Width < 260);   // ~34% of 717
        Assert.True(rect.Y + rect.Height <= 1001);           // stays on card
    }
}
