using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class RiftboundOcrParseTests
{
    [Theory]
    [InlineData("UNL • 150/219", "UNL-150")]
    [InlineData("UNL 150/219", "UNL-150")]     // bullet dropped by OCR
    [InlineData("OGN · 209/298", "OGN-209")]   // middle-dot separator
    [InlineData("SFD•96/221", "SFD-96")]        // no spaces
    public void ExtractsSetAndCollector_IgnoringTotal(string ocr, string expected)
    {
        Assert.True(OcrMatchingService.TryExtractRiftboundNumber(ocr, out var formatted));
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("League Splash Team")]  // flavour/credit line, no number pattern
    [InlineData("")]
    public void RejectsNonCollectorText(string ocr)
    {
        Assert.False(OcrMatchingService.TryExtractRiftboundNumber(ocr, out _));
    }
}
