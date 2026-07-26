using OmniCard.Web;

namespace OmniCard.Tests.Web;

public class CardImageUrlTests
{
    [Fact]
    public void Resolve_ScanImagePath_TakesPrecedenceAndExtractsFilename()
    {
        var url = CardImageUrl.Resolve("scans/12345.jpg", "https://api.example.com/card.jpg");
        Assert.Equal("/scans/12345.jpg", url);
    }

    [Fact]
    public void Resolve_NoScanPath_FallsBackToImageUri()
    {
        var url = CardImageUrl.Resolve(null, "https://api.example.com/card.jpg");
        Assert.Equal("https://api.example.com/card.jpg", url);
    }

    [Fact]
    public void Resolve_EmptyScanPath_FallsBackToImageUri()
    {
        var url = CardImageUrl.Resolve("", "https://api.example.com/card.jpg");
        Assert.Equal("https://api.example.com/card.jpg", url);
    }

    [Fact]
    public void Resolve_NoScanPathOrUri_ReturnsNull()
    {
        Assert.Null(CardImageUrl.Resolve(null, null));
        Assert.Null(CardImageUrl.Resolve(null, ""));
    }
}
