using OmniCard.Web;

namespace OmniCard.Tests.Web;

public class CardImageUrlTests
{
    [Fact]
    public void Resolve_ImageUri_TakesPrecedenceOverScan()
    {
        // Matches the desktop art pipeline (CardArtCandidateResolver): downloaded/catalog art wins,
        // the scan is only a fallback. A stored scan path may point at a file that no longer exists,
        // so preferring it would hide good catalog art behind a 404.
        var url = CardImageUrl.Resolve("scans/12345.jpg", "https://api.example.com/card.jpg");
        Assert.Equal("https://api.example.com/card.jpg", url);
    }

    [Fact]
    public void Resolve_NoImageUri_FallsBackToScanFilename()
    {
        var url = CardImageUrl.Resolve("scans/12345.jpg", null);
        Assert.Equal("/scans/12345.jpg", url);
    }

    [Fact]
    public void Resolve_EmptyImageUri_FallsBackToScanFilename()
    {
        var url = CardImageUrl.Resolve("scans/12345.jpg", "");
        Assert.Equal("/scans/12345.jpg", url);
    }

    [Fact]
    public void Resolve_NoScanPathOrUri_ReturnsNull()
    {
        Assert.Null(CardImageUrl.Resolve(null, null));
        Assert.Null(CardImageUrl.Resolve("", ""));
    }

    [Fact]
    public void Resolve_ScanFallback_WithScansDirectory_ReturnsNullWhenFileMissing()
    {
        // When the scans directory is known, a scan whose file is gone must not be served (it would
        // 404) — the caller renders a placeholder instead, mirroring the desktop.
        var missingDir = Path.Combine(Path.GetTempPath(), "omnicard-tests-no-such-dir");
        var url = CardImageUrl.Resolve("scans/does-not-exist.jpg", null, missingDir);
        Assert.Null(url);
    }

    [Fact]
    public void Resolve_ScanFallback_WithScansDirectory_ReturnsUrlWhenFileExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "real.jpg");
            File.WriteAllBytes(file, [0xFF, 0xD8, 0xFF]);
            var url = CardImageUrl.Resolve("scans/real.jpg", null, dir);
            Assert.Equal("/scans/real.jpg", url);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ImageUri_IgnoresScansDirectoryCheck()
    {
        // Catalog art wins regardless of whether any scan file exists.
        var url = CardImageUrl.Resolve("scans/whatever.jpg", "https://api.example.com/card.jpg", "/no/such/dir");
        Assert.Equal("https://api.example.com/card.jpg", url);
    }
}
