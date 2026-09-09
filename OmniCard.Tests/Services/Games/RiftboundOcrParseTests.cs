using OmniCard.Imaging;

namespace OmniCard.Tests.Services.Games;

public class RiftboundOcrParseTests
{
    [Theory]
    [InlineData("UNL • 150/219", "UNL-150")]
    [InlineData("UNL 150/219", "UNL-150")]     // bullet dropped by OCR
    [InlineData("OGN · 209/298", "OGN-209")]   // middle-dot separator
    [InlineData("SFD•96/221", "SFD-96")]        // no spaces
    [InlineData("UNL • 208/219", "UNL-208")]    // Battlefield card (landscape), 3-digit collector
    [InlineData("UNL·166/219", "UNL-166")]      // portrait card, middle-dot, no spaces
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
