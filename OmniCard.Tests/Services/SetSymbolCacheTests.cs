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

    // --- GetSetSymbolAsync ---

    [Fact]
    public async Task GetSetSymbolAsync_UnsupportedRarity_ReturnsNull()
    {
        var cache = CreateCache();
        var result = await cache.GetSetSymbolAsync("M10", "special");
        Assert.Null(result);
    }

    [StaFact]
    public async Task GetSetSymbolAsync_Downloads_AndCachesToDisk()
    {
        var cache = CreateCache();
        var result = await cache.GetSetSymbolAsync("M10", "common");

        // File should be saved to disk
        var filePath = Path.Combine(_tempDir, "M10", "C.svg");
        Assert.True(File.Exists(filePath));
    }

    [StaFact]
    public async Task GetSetSymbolAsync_SecondCall_UsesCache_NoExtraHttp()
    {
        var httpFactory = CreateMockHttpFactory(callLimit: 1);
        var cache = CreateCache(httpFactory);

        // First call downloads
        await cache.GetSetSymbolAsync("M10", "common");
        // Second call should use in-memory cache (no HTTP)
        var result = await cache.GetSetSymbolAsync("M10", "common");

        // If this doesn't throw, the HTTP was only called once (callLimit: 1)
        Assert.NotNull(result);
    }

}
