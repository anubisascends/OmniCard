using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class TcgCsvOcrTests
{
    // FFTCG's tightened set-code pattern: 1-2 digit opus, exactly 3 digit card number, one rarity letter.
    private const string FftcgPattern = @"(\d{1,2}-\d{3}[A-Z])";

    [Theory]
    [InlineData(@"(\d+\s*/\s*\d+)", "abc 123 / 198 xy", "123/198")]
    [InlineData(@"(\d+-\d+[A-Z]?)", "PR 1-001H", "1-001H")]
    [InlineData(@"([A-Z0-9]+-[A-Z]{0,2}\d+)", "noise LOB-EN001 noise", "LOB-EN001")]
    // FFTCG: isolates the code from the surrounding bottom credit line.
    [InlineData(FftcgPattern, "FINAL FANTASY X 29-110C", "29-110C")]
    [InlineData(FftcgPattern, "CHARACTER ILLUSTRATION KAEDE YAMAGUCHI FINAL FANTASY 29-080C", "29-080C")]
    [InlineData(FftcgPattern, "PR 1-001H", "1-001H")]
    public void ExtractCollectorNumber_NormalizesAndMatches(string pattern, string ocrText, string expected)
    {
        var ok = OcrMatchingService.TryExtractCollectorNumber(ocrText, pattern, out var result);
        Assert.True(ok);
        Assert.Equal(expected, result);
    }

    [Theory]
    // Art-noise reads inflate the digit runs; the strict \d{3} shape rejects them (they must fall
    // through to pHash rather than resolve to a wrong card).
    [InlineData("29-1100F")]  // 4-digit "card number" — real FFTCG numbers are always 3 digits
    [InlineData("9999-1100F")]
    public void FftcgPattern_RejectsDigitInflatedNoise(string noise)
    {
        Assert.False(OcrMatchingService.TryExtractCollectorNumber(noise, FftcgPattern, out _));
    }

    [Fact]
    public void ExtractCollectorNumber_NoMatch_ReturnsFalse()
    {
        Assert.False(OcrMatchingService.TryExtractCollectorNumber("nothing here", @"(\d+/\d+)", out _));
    }
}
