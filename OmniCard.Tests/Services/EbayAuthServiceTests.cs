using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class EbayAuthServiceTests
{
    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        DevId = "test-dev-id",
        RuName = "test-ru-name",
        Environment = "sandbox",
    };

    private EbayAuthService CreateService(InMemoryCredentialStore? store = null)
    {
        store ??= new InMemoryCredentialStore();
        return new EbayAuthService(
            Options.Create(_settings),
            new StubHttpClientFactory(),
            store,
            NullLogger<EbayAuthService>.Instance);
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenNoTokens()
    {
        var svc = CreateService();
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public void IsConnected_ReturnsTrue_WhenRefreshTokenExists()
    {
        var store = new InMemoryCredentialStore();
        store.Set("OmniCard:eBay:RefreshToken", "some-refresh-token");
        store.Set("OmniCard:eBay:RefreshTokenExpiry", DateTime.UtcNow.AddMonths(18).ToString("o"));
        var svc = CreateService(store);
        Assert.True(svc.IsConnected);
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenRefreshTokenExpired()
    {
        var store = new InMemoryCredentialStore();
        store.Set("OmniCard:eBay:RefreshToken", "some-refresh-token");
        store.Set("OmniCard:eBay:RefreshTokenExpiry", DateTime.UtcNow.AddDays(-1).ToString("o"));
        var svc = CreateService(store);
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public void GetAuthorizationUrl_ContainsAppIdAndScopes()
    {
        var svc = CreateService();
        var url = svc.GetAuthorizationUrl();
        Assert.Contains("test-app-id", url);
        Assert.Contains("test-ru-name", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("sell.inventory", url);
        Assert.StartsWith("https://auth.sandbox.ebay.com", url);
    }

    [Fact]
    public void GetAuthorizationUrl_UsesProductionUrl_WhenConfigured()
    {
        var settings = new EbaySettings
        {
            AppId = "prod-id",
            CertId = "prod-cert",
            RuName = "prod-ru",
            Environment = "production",
        };
        var svc = new EbayAuthService(
            Options.Create(settings),
            new StubHttpClientFactory(),
            new InMemoryCredentialStore(),
            NullLogger<EbayAuthService>.Instance);
        var url = svc.GetAuthorizationUrl();
        Assert.StartsWith("https://auth.ebay.com", url);
    }

    [Fact]
    public void Disconnect_ClearsAllCredentials()
    {
        var store = new InMemoryCredentialStore();
        store.Set("OmniCard:eBay:AccessToken", "token");
        store.Set("OmniCard:eBay:RefreshToken", "refresh");
        store.Set("OmniCard:eBay:TokenExpiry", "2026-01-01");
        store.Set("OmniCard:eBay:RefreshTokenExpiry", "2027-01-01");

        var svc = CreateService(store);
        svc.Disconnect();

        Assert.False(store.Exists("OmniCard:eBay:AccessToken"));
        Assert.False(store.Exists("OmniCard:eBay:RefreshToken"));
        Assert.False(store.Exists("OmniCard:eBay:TokenExpiry"));
        Assert.False(store.Exists("OmniCard:eBay:RefreshTokenExpiry"));
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNull_WhenNotConnected()
    {
        var svc = CreateService();
        var token = await svc.GetAccessTokenAsync();
        Assert.Null(token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsToken_WhenNotExpired()
    {
        var store = new InMemoryCredentialStore();
        store.Set("OmniCard:eBay:AccessToken", "valid-access-token");
        store.Set("OmniCard:eBay:TokenExpiry", DateTime.UtcNow.AddHours(1).ToString("o"));
        store.Set("OmniCard:eBay:RefreshToken", "refresh");
        store.Set("OmniCard:eBay:RefreshTokenExpiry", DateTime.UtcNow.AddMonths(18).ToString("o"));

        var svc = CreateService(store);
        var token = await svc.GetAccessTokenAsync();
        Assert.Equal("valid-access-token", token);
    }
}

public class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _store = [];

    public string? Get(string target) => _store.GetValueOrDefault(target);
    public void Set(string target, string value) => _store[target] = value;
    public void Delete(string target) => _store.Remove(target);
    public bool Exists(string target) => _store.ContainsKey(target);
}

public class StubHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
