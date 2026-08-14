using OmniCard.Imaging;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class CollectorNumberFuzzyMatcherTests
{
    [Theory]
    // Real OCR mis-reads from holofoil Yu-Gi-Oh! scans → the true catalog code, all within distance 1.
    [InlineData("AGOV-FNU63", "AGOV-EN063", 1)]  // F→E, U→0
    [InlineData("ANGO-ENQ58", "ANGU-EN058", 0)]  // O→0 and U→0 both canonicalize
    [InlineData("CYAT-ENO60", "CYAC-EN060", 1)]  // T→C mis-read (1 edit)
    [InlineData("GRER-ENQ31", "GRCR-EN031", 1)]  // E→C (1 edit), Q→0
    [InlineData("PHHY-FN006", "PHHY-EN006", 1)]  // F→E
    [InlineData("GRCR-EN033", "GRCR-EN033", 0)]  // clean read
    public void Distance_TolerantOfOcrConfusions(string ocr, string catalog, int expected)
    {
        Assert.Equal(expected, CollectorNumberFuzzyMatcher.Distance(ocr, catalog));
    }

    [Fact]
    public void Distance_UnrelatedCodes_IsLarge()
    {
        Assert.True(CollectorNumberFuzzyMatcher.Distance("GRCR-EN060", "PHHY-EN012") >= 4);
    }

    [Fact]
    public void Canonicalize_CollapsesConfusablesAndDropsSeparators()
    {
        // O/Q/U→0, I/L/T→1, S→5, B→8, G→6, dashes dropped.
        Assert.Equal("6RCREN060", CollectorNumberFuzzyMatcher.Canonicalize("GRCR-EN0G0"));
        Assert.Equal(CollectorNumberFuzzyMatcher.Canonicalize("ANGU-EN058"),
                     CollectorNumberFuzzyMatcher.Canonicalize("ANGO-ENQ58"));
    }

    [Theory]
    [InlineData("GRCR-EN060", "6RCR")]
    [InlineData("PHHY-EN012", "PHHY")]
    [InlineData("GRER-ENQ31", "6RER")]  // dash present → prefix is the part before it
    public void CanonPrefix_ExtractsSetPrefix(string code, string expected)
    {
        Assert.Equal(expected, CollectorNumberFuzzyMatcher.CanonPrefix(code));
    }

    [Theory]
    [InlineData("noise GRCR-EN060 x", "GRCR-EN060")]
    [InlineData("3RCR-ENO31", "3RCR-ENO31")]
    [InlineData("BORT", null)]              // no digits → not a code
    [InlineData("©2020 Studio Dice", null)] // copyright line → no code
    public void ExtractLooseToken_PicksDigitRichCode(string text, string? expected)
    {
        Assert.Equal(expected, OcrMatchingService.ExtractLooseToken(text));
    }
}
