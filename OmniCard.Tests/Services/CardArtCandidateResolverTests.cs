using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class CardArtCandidateResolverTests
{
    [Fact]
    public void Unstacked_WithScan_ReturnsScanOnly()
    {
        var card = new CollectionCard { ScanImagePath = "scan.png", ImageUri = "http://x/art.png" };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: false);
        Assert.Single(result);
        Assert.Equal(CardArtKind.Scan, result[0].Kind);
        Assert.Equal("scan.png", result[0].Value);
    }

    [Fact]
    public void Unstacked_WithoutScan_ReturnsEmpty_EvenWhenDownloadedExists()
    {
        var card = new CollectionCard { ScanImagePath = null, ImageUri = "http://x/art.png" };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: false);
        Assert.Empty(result);
    }

    [Fact]
    public void Stacked_WithBoth_ReturnsDownloadedThenScan()
    {
        var card = new CollectionCard { ScanImagePath = "scan.png", ImageUri = "http://x/art.png" };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: true);
        Assert.Equal(2, result.Count);
        Assert.Equal(CardArtKind.Downloaded, result[0].Kind);
        Assert.Equal("http://x/art.png", result[0].Value);
        Assert.Equal(CardArtKind.Scan, result[1].Kind);
        Assert.Equal("scan.png", result[1].Value);
    }

    [Fact]
    public void Stacked_WithOnlyScan_ReturnsScan()
    {
        var card = new CollectionCard { ScanImagePath = "scan.png", ImageUri = null };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: true);
        Assert.Single(result);
        Assert.Equal(CardArtKind.Scan, result[0].Kind);
    }

    [Fact]
    public void Stacked_WithNeither_ReturnsEmpty()
    {
        var card = new CollectionCard { ScanImagePath = null, ImageUri = null };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: true);
        Assert.Empty(result);
    }
}
