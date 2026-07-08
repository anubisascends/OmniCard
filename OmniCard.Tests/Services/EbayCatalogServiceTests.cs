using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Services;
using System.Net;
using System.Text.Json;

namespace OmniCard.Tests.Services;

public class EbayCatalogServiceTests
{
    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        DevId = "test-dev-id",
        RuName = "test-ru-name",
        Environment = "sandbox",
    };

    [Fact]
    public async Task SearchCatalogAsync_ReturnsMatches_WhenApiReturnsResults()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            itemSummaries = new[]
            {
                new
                {
                    itemId = "v1|123|0",
                    title = "MTG Black Lotus Alpha NM",
                    price = new { value = "5000.00", currency = "USD" },
                    condition = "Near Mint",
                    image = new { imageUrl = "https://img.ebay.com/123.jpg" },
                    categories = new[] { new { categoryId = "38292" } },
                }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayCatalogService(
            Options.Create(_settings),
            factory,
            authService,
            NullLogger<EbayCatalogService>.Instance);

        var results = await svc.SearchCatalogAsync("Black Lotus", "Alpha", null);

        Assert.Single(results);
        Assert.Equal("v1|123|0", results[0].ItemId);
        Assert.Equal("MTG Black Lotus Alpha NM", results[0].Title);
        Assert.Equal(5000.00m, results[0].Price);
    }

    [Fact]
    public async Task SearchCatalogAsync_ReturnsEmpty_WhenNotConnected()
    {
        var authService = new FakeEbayAuthService(null);
        var factory = new FakeHttpClientFactory(new FakeHttpHandler(HttpStatusCode.OK, "{}"));

        var svc = new EbayCatalogService(
            Options.Create(_settings),
            factory,
            authService,
            NullLogger<EbayCatalogService>.Instance);

        var results = await svc.SearchCatalogAsync("Black Lotus", "Alpha", null);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetMarketPriceAsync_CalculatesMedian()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            itemSummaries = new[]
            {
                new { itemId = "1", title = "Card", price = new { value = "10.00", currency = "USD" }, condition = "Near Mint", image = (object?)null, categories = (object?)null },
                new { itemId = "2", title = "Card", price = new { value = "20.00", currency = "USD" }, condition = "Near Mint", image = (object?)null, categories = (object?)null },
                new { itemId = "3", title = "Card", price = new { value = "30.00", currency = "USD" }, condition = "Near Mint", image = (object?)null, categories = (object?)null },
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayCatalogService(
            Options.Create(_settings),
            factory,
            authService,
            NullLogger<EbayCatalogService>.Instance);

        var price = await svc.GetMarketPriceAsync("Black Lotus Alpha NM", "NM", false);

        Assert.NotNull(price);
        Assert.Equal(20.00m, price.MedianPrice);
        Assert.Equal(10.00m, price.LowPrice);
        Assert.Equal(30.00m, price.HighPrice);
        Assert.Equal(3, price.SampleCount);
    }
}

// --- Test doubles ---

public class FakeEbayAuthService : IEbayAuthService
{
    private readonly string? _token;
    public FakeEbayAuthService(string? token) => _token = token;
    public bool IsConnected => _token is not null;
    public Task<string?> GetAccessTokenAsync() => Task.FromResult(_token);
    public Task<bool> ExchangeCodeForTokensAsync(string authCode) => Task.FromResult(true);
    public void Disconnect() { }
    public string GetAuthorizationUrl() => "";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public class FakeHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public FakeHttpHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}

public class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler);
}
