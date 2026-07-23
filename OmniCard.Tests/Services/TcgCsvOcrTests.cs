using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class TcgCsvOcrTests
{
    [Theory]
    [InlineData(@"(\d+\s*/\s*\d+)", "abc 123 / 198 xy", "123/198")]
    [InlineData(@"(\d+-\d+[A-Z]?)", "PR 1-001H", "1-001H")]
    [InlineData(@"([A-Z0-9]+-[A-Z]{0,2}\d+)", "noise LOB-EN001 noise", "LOB-EN001")]
    public void ExtractCollectorNumber_NormalizesAndMatches(string pattern, string ocrText, string expected)
    {
        var ok = OcrMatchingService.TryExtractCollectorNumber(ocrText, pattern, out var result);
        Assert.True(ok);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractCollectorNumber_NoMatch_ReturnsFalse()
    {
        Assert.False(OcrMatchingService.TryExtractCollectorNumber("nothing here", @"(\d+/\d+)", out _));
    }
}
