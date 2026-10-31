using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;
using OmniCard.CardMatching.Games;
using OmniCard.Tests.Fakes;

namespace OmniCard.Tests.Services.Games;

/// <summary>Tests for Yu-Gi-Oh! edition detection (1st Edition / Limited / Unlimited) and the loose
/// token relaxation that lets holofoil letter-only reads through to the fuzzy catalog matcher.</summary>
public class YugiohEditionTests
{
    [Theory]
    [InlineData("61027400 1st Edition", "1st Edition")]
    [InlineData("1ST EDITION", "1st Edition")]
    [InlineData("IST EDITON", "1st Edition")]         // 1→I, missing I (OCR noise) still classifies
    [InlineData("LIMITED EDITION", "Limited Edition")]
    [InlineData("56838842 1ST EDITI0N", "1st Edition")]
    public void ClassifyEdition_DetectsEdition(string ocr, string expected)
    {
        Assert.Equal(expected, OcrMatchingService.ClassifyEdition(ocr));
    }

    [Theory]
    [InlineData("76407432")]                          // OTS/Unlimited: password only, no edition text
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("©2020 Studio Dice")]                 // copyright line, no edition
    public void ClassifyEdition_ReturnsNull_WhenNoEditionText(string ocr)
    {
        Assert.Null(OcrMatchingService.ClassifyEdition(ocr));
    }

    [Fact]
    public void ExtractLooseToken_DropsLetterOnlyToken_ByDefault()
    {
        // A holofoil read where the collector digits came out as letters: rejected unless allowed.
        Assert.Null(OcrMatchingService.ExtractLooseToken("DAMA-ENULZ"));
    }

    [Fact]
    public void ExtractLooseToken_KeepsLetterOnlyToken_WhenAllowed()
    {
        var token = OcrMatchingService.ExtractLooseToken("DAMA-ENULZ", allowLetterOnly: true);
        Assert.Equal("DAMA-ENULZ", token);
    }

    [Fact]
    public void ExtractLooseToken_PrefersDigitBearingToken_EvenWhenLetterOnlyAllowed()
    {
        // When both a digit-bearing and a letter-only token are present, the digit-bearing one wins.
        var token = OcrMatchingService.ExtractLooseToken("NOISE CYAC-EN083", allowLetterOnly: true);
        Assert.Equal("CYAC-EN083", token);
    }

    // White cards with Arial Bold text inside the given region, pre-rendered by the original System.Drawing
    // helper (see TestData/Baseline/OcrRendered) so every OS OCRs the same pixels.

    private static OcrMatchingService CreateService() =>
        new(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            NullLogger<OcrMatchingService>.Instance);

    [Fact]
    public async Task DetectYugiohEditionAsync_ReadsFirstEdition()
    {
        var image = TestImages.OcrRendered("yugioh-1st-edition");
        using var service = CreateService();
        var (edition, confidence) = await service.DetectYugiohEditionAsync(image);
        Assert.Equal("1st Edition", edition);
        Assert.True(confidence >= 0.5, $"confidence {confidence} should clear the gate");
    }

    [Fact]
    public async Task DetectYugiohEditionAsync_ReturnsNull_ForUnlimited()
    {
        // Password only — an Unlimited print has no edition text.
        var image = TestImages.OcrRendered("yugioh-unlimited-password");
        using var service = CreateService();
        var (edition, _) = await service.DetectYugiohEditionAsync(image);
        Assert.Null(edition);
    }

    [Fact]
    public async Task DetectCollectorNumberAsync_ReadsYugiohSetCode_WithSparseTextSpec()
    {
        // End-to-end through the shipping Yu-Gi-Oh! spec (SparseText PSM + tall band): a rendered set
        // code reads back with its prefix intact.
        var image = TestImages.OcrRendered("yugioh-DAMA-EN012");
        using var service = CreateService();
        var (cn, conf) = await service.DetectCollectorNumberAsync(image, YugiohService.OcrSpec);
        Assert.NotNull(cn);
        Assert.Contains("DAMA", cn!);
        Assert.True(conf >= 0.5, $"confidence {conf} should clear the gate");
    }
}
