using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Interfaces;

namespace OmniCard.Tests.Services;

public class SetSymbolCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IDataPathService> _mockPathService;

    private const string MinimalSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><circle cx="16" cy="16" r="16" fill="#000"/></svg>""";

    public SetSymbolCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"setsymbol-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _mockPathService = new Mock<IDataPathService>();
        _mockPathService.Setup(p => p.SymbolsCacheDirectory).Returns(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static IHttpClientFactory CreateMockHttpFactory(int callLimit = int.MaxValue)
    {
        var callCount = 0;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                if (callCount > callLimit)
                    throw new InvalidOperationException("HTTP should not have been called again");
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(MinimalSvg)),
                };
            });

        var client = new HttpClient(mockHandler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory.Object;
    }

    private SetSymbolCache CreateCache(IHttpClientFactory? httpFactory = null)
    {
        return new SetSymbolCache(
            httpFactory ?? CreateMockHttpFactory(),
            _mockPathService.Object,
            NullLogger<SetSymbolCache>.Instance);
    }

    // --- Name registration ---

    [Fact]
    public void RegisterSetName_GetSetName_RoundTrip()
    {
        var cache = CreateCache();
        cache.RegisterSetName("m10", "Magic 2010");
        Assert.Equal("Magic 2010", cache.GetSetName("M10")); // case-insensitive
    }

    [Fact]
    public void GetSetName_UnknownCode_ReturnsNull()
    {
        var cache = CreateCache();
        Assert.Null(cache.GetSetName("UNKNOWN"));
    }

    // --- FormatRarityDisplay ---

    [Theory]
    [InlineData("common", "Common")]
    [InlineData("uncommon", "Uncommon")]
    [InlineData("rare", "Rare")]
    [InlineData("mythic", "Mythic Rare")]
    [InlineData("special", "special")]
    [InlineData(null, "")]
    public void FormatRarityDisplay_ReturnsExpected(string? input, string expected)
    {
        Assert.Equal(expected, SetSymbolCache.FormatRarityDisplay(input!));
    }

    // --- GetSymbolSvgPathAsync ---

    [Fact]
    public async Task GetSymbolSvgPathAsync_UnsupportedRarity_ReturnsNull()
    {
        var cache = CreateCache();
        var result = await cache.GetSymbolSvgPathAsync("M10", "special");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSymbolSvgPathAsync_Downloads_AndCachesToDisk()
    {
        var cache = CreateCache();
        var result = await cache.GetSymbolSvgPathAsync("M10", "common");

        // File should be saved to disk, and the returned path should point at it.
        var filePath = Path.Combine(_tempDir, "M10", "C.svg");
        Assert.True(File.Exists(filePath));
        Assert.Equal(filePath, result);
    }

    [Fact]
    public async Task GetSymbolSvgPathAsync_SecondCall_UsesCache_NoExtraHttp()
    {
        var httpFactory = CreateMockHttpFactory(callLimit: 1);
        var cache = CreateCache(httpFactory);

        // First call downloads
        await cache.GetSymbolSvgPathAsync("M10", "common");
        // Second call should use in-memory cache (no HTTP)
        var result = await cache.GetSymbolSvgPathAsync("M10", "common");

        // If this doesn't throw, the HTTP was only called once (callLimit: 1)
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetSymbolSvgPathAsync_404_WritesMissingMarker()
    {
        var cache = CreateCache(CreateNotFoundHttpFactory());
        var result = await cache.GetSymbolSvgPathAsync("PWAR", "common");

        Assert.Null(result);
        var markerPath = Path.Combine(_tempDir, "PWAR", "C.svg.missing");
        Assert.True(File.Exists(markerPath));
    }

    [Fact]
    public async Task GetSymbolSvgPathAsync_ExistingMissingMarker_SkipsHttp()
    {
        // Pre-seed a .missing marker (e.g. written by a prior launch or by RasterizeSymbolAsync).
        // The lazy load must honour it and never touch the network — this is the regression that
        // caused dozens of promo sets to be re-fetched (and 404'd) on every startup.
        Directory.CreateDirectory(Path.Combine(_tempDir, "TSB"));
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "TSB", "C.svg.missing"), []);

        // callLimit: 0 → any HTTP call throws.
        var cache = CreateCache(CreateNotFoundHttpFactory(callLimit: 0));
        var result = await cache.GetSymbolSvgPathAsync("TSB", "common");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSymbolSvgPathAsync_ConcurrentCalls_SingleHttp()
    {
        // Many callers requesting the same symbol at once must coalesce into one network request.
        var httpFactory = CreateMockHttpFactory(callLimit: 1);
        var cache = CreateCache(httpFactory);

        var tasks = Enumerable.Range(0, 16).Select(_ => cache.GetSymbolSvgPathAsync("M10", "common"));
        var results = await Task.WhenAll(tasks);

        // If more than one request had fired, the mock (callLimit: 1) would have thrown.
        Assert.All(results, Assert.NotNull);
    }

    // --- RasterizeSymbolAsync negative caching ---

    private static IHttpClientFactory CreateNotFoundHttpFactory(int callLimit = int.MaxValue)
    {
        var callCount = 0;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                if (callCount > callLimit)
                    throw new InvalidOperationException("HTTP should not have been called again");
                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            });

        var client = new HttpClient(mockHandler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory.Object;
    }

    [Fact]
    public async Task RasterizeSymbolAsync_404_WritesMissingMarker()
    {
        var cache = CreateCache(CreateNotFoundHttpFactory());
        var result = await cache.RasterizeSymbolAsync("PMEI");

        Assert.Null(result);
        var markerPath = Path.Combine(_tempDir, "PMEI", "C.svg.missing");
        Assert.True(File.Exists(markerPath));
    }

    [Fact]
    public async Task RasterizeSymbolAsync_SecondCallAfter404_SkipsHttp()
    {
        var httpFactory = CreateNotFoundHttpFactory(callLimit: 1);
        var cache = CreateCache(httpFactory);

        // First call 404s and writes the marker
        await cache.RasterizeSymbolAsync("PMEI");
        // Second call should short-circuit on the marker (no HTTP)
        var result = await cache.RasterizeSymbolAsync("PMEI");

        // If this doesn't throw, the HTTP was only called once (callLimit: 1)
        Assert.Null(result);
    }

    [Fact]
    public async Task RasterizeSymbolAsync_ValidSvg_ReturnsPngBytes()
    {
        // Exercises the SkiaSharp (Svg.Skia) rasterization path end-to-end: download the SVG,
        // render it to a 32×32 raster, and encode as PNG.
        var cache = CreateCache();
        var result = await cache.RasterizeSymbolAsync("M10");

        Assert.NotNull(result);
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        Assert.True(result!.Length > 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, result[..8]);
    }
}
