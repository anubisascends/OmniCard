using System.Windows.Media.Imaging;
using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class CardArtCacheTests : IDisposable
{
    private readonly string _tempDir;

    public CardArtCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"artcache-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>Creates a minimal valid PNG file at the given path.</summary>
    private static string CreateTestImage(string directory, string filename = "test.png")
    {
        var path = Path.Combine(directory, filename);
        var bmp = new RenderTargetBitmap(100, 100, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create);
        encoder.Save(fs);
        return path;
    }

    /// <summary>Creates a mock IHttpClientFactory returning the given image bytes.</summary>
    private static IHttpClientFactory CreateMockHttpFactory(byte[] responseBytes)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBytes),
            });

        var client = new HttpClient(mockHandler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory.Object;
    }

    private CardArtCache CreateCache(IHttpClientFactory? httpFactory = null, int capacity = 20)
    {
        httpFactory ??= new Mock<IHttpClientFactory>().Object;
        return new CardArtCache(
            NullLogger<CardArtCache>.Instance,
            httpFactory,
            capacity);
    }

    // --- Null / empty path handling ---

    [Fact]
    public void GetImage_NullPaths_ReturnsNull()
    {
        var cache = CreateCache();
        var result = cache.GetImage(null, null);
        Assert.Null(result);
        Assert.Equal(0, cache.Count);
    }

    // --- Local file loading ---

    [StaFact]
    public void GetImage_LocalFile_ReturnsBitmapImage()
    {
        var cache = CreateCache();
        var path = CreateTestImage(_tempDir);

        var result = cache.GetImage(path, null);

        Assert.NotNull(result);
        Assert.True(result!.IsFrozen);
        Assert.Equal(1, cache.Count);
    }

    [StaFact]
    public void GetImage_SameKey_ReturnsCachedInstance()
    {
        var cache = CreateCache();
        var path = CreateTestImage(_tempDir);

        var first = cache.GetImage(path, null);
        var second = cache.GetImage(path, null);

        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
    }

    // --- HTTP fallback ---

    [StaFact]
    public void GetImage_HttpFallback_WhenLocalFileMissing()
    {
        // Create a PNG in memory for the HTTP mock response
        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            var bmp = new RenderTargetBitmap(50, 50, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(ms);
            pngBytes = ms.ToArray();
        }

        var httpFactory = CreateMockHttpFactory(pngBytes);
        var cache = CreateCache(httpFactory);

        // No local file, provide imageUri
        var result = cache.GetImage(null, "https://example.com/card.png");

        Assert.NotNull(result);
        Assert.True(result!.IsFrozen);
        Assert.Equal(1, cache.Count);
    }

    // --- LRU eviction ---

    [StaFact]
    public void LruEviction_AtCapacity_RemovesOldest()
    {
        var cache = CreateCache(capacity: 2);

        var p1 = CreateTestImage(_tempDir, "1.png");
        var p2 = CreateTestImage(_tempDir, "2.png");
        var p3 = CreateTestImage(_tempDir, "3.png");

        cache.GetImage(p1, null);
        cache.GetImage(p2, null);
        Assert.Equal(2, cache.Count);

        cache.GetImage(p3, null);
        Assert.Equal(2, cache.Count); // oldest (p1) evicted
    }

    // --- Evict / Clear ---

    [StaFact]
    public void Evict_RemovesEntry()
    {
        var cache = CreateCache();
        var path = CreateTestImage(_tempDir);
        cache.GetImage(path, null);
        Assert.Equal(1, cache.Count);

        cache.Evict(path);
        Assert.Equal(0, cache.Count);
    }

    [StaFact]
    public void Clear_EmptiesCache()
    {
        var cache = CreateCache();
        var p1 = CreateTestImage(_tempDir, "a.png");
        var p2 = CreateTestImage(_tempDir, "b.png");
        cache.GetImage(p1, null);
        cache.GetImage(p2, null);
        Assert.Equal(2, cache.Count);

        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    // --- Async loading ---

    [Fact]
    public async Task GetImageAsync_NullPaths_ReturnsNull()
    {
        var cache = CreateCache();
        var result = await cache.GetImageAsync(null, null);
        Assert.Null(result);
        Assert.Equal(0, cache.Count);
    }

    [StaFact]
    public async Task GetImageAsync_LocalFile_ReturnsBitmapImage()
    {
        var cache = CreateCache();
        var path = CreateTestImage(_tempDir);

        var result = await cache.GetImageAsync(path, null);

        Assert.NotNull(result);
        Assert.True(result!.IsFrozen);
        Assert.Equal(1, cache.Count);
    }

    [StaFact]
    public async Task GetImageAsync_HttpFallback_WhenLocalFileMissing()
    {
        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            var bmp = new RenderTargetBitmap(50, 50, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(ms);
            pngBytes = ms.ToArray();
        }

        var httpFactory = CreateMockHttpFactory(pngBytes);
        var cache = CreateCache(httpFactory);

        var result = await cache.GetImageAsync(null, "https://example.com/card.png");

        Assert.NotNull(result);
        Assert.True(result!.IsFrozen);
        Assert.Equal(1, cache.Count);
    }

    [StaFact]
    public async Task GetImageAsync_SameKey_ReturnsCachedInstance()
    {
        var cache = CreateCache();
        var path = CreateTestImage(_tempDir);

        var first = await cache.GetImageAsync(path, null);
        var second = await cache.GetImageAsync(path, null);

        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
    }

    // --- Default capacity ---

    [StaFact]
    public void DefaultCapacity_IsTwoHundred()
    {
        // Construct with the default capacity (no capacity argument).
        var cache = new CardArtCache(
            NullLogger<CardArtCache>.Instance,
            new Mock<IHttpClientFactory>().Object);

        // Insert 201 distinct local images; the oldest must be evicted at 200.
        for (int i = 0; i < 201; i++)
        {
            var path = CreateTestImage(_tempDir, $"cap-{i}.png");
            Assert.NotNull(cache.GetImage(path, null));
        }

        Assert.Equal(200, cache.Count);
    }
}
