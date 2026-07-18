using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class CardArtCandidateResolverTests
{
    [Fact]
    public void WithBoth_ReturnsDownloadedThenScan()
    {
        var card = new CollectionCard { ScanImagePath = "scan.png", ImageUri = "http://x/art.png" };
        var result = CardArtCandidateResolver.Resolve(card);
        Assert.Equal(2, result.Count);
        Assert.Equal(CardArtKind.Downloaded, result[0].Kind);
        Assert.Equal("http://x/art.png", result[0].Value);
        Assert.Equal(CardArtKind.Scan, result[1].Kind);
        Assert.Equal("scan.png", result[1].Value);
    }

    [Fact]
    public void WithOnlyDownloaded_ReturnsDownloaded()
    {
        var card = new CollectionCard { ScanImagePath = null, ImageUri = "http://x/art.png" };
        var result = CardArtCandidateResolver.Resolve(card);
        Assert.Single(result);
        Assert.Equal(CardArtKind.Downloaded, result[0].Kind);
        Assert.Equal("http://x/art.png", result[0].Value);
    }

    [Fact]
    public void WithOnlyScan_ReturnsScan()
    {
        var card = new CollectionCard { ScanImagePath = "scan.png", ImageUri = null };
        var result = CardArtCandidateResolver.Resolve(card);
        Assert.Single(result);
        Assert.Equal(CardArtKind.Scan, result[0].Kind);
        Assert.Equal("scan.png", result[0].Value);
    }

    [Fact]
    public void WithNeither_ReturnsEmpty()
    {
        var card = new CollectionCard { ScanImagePath = null, ImageUri = null };
        var result = CardArtCandidateResolver.Resolve(card);
        Assert.Empty(result);
    }
}
