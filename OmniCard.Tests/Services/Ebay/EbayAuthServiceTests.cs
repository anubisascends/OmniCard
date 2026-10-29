using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.eBay;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Settings;

namespace OmniCard.Tests.Services.Ebay;

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
    public void GetMissingConfiguration_ReturnsEmpty_WhenAllRequiredFieldsSet()
    {
        var settings = new EbaySettings
        {
            AppId = "app",
            CertId = "cert",
            RuName = "ru",
            Environment = "sandbox",
        };
        var svc = new EbayAuthService(
            Options.Create(settings),
            new StubHttpClientFactory(),
            new InMemoryCredentialStore(),
            NullLogger<EbayAuthService>.Instance);

        Assert.Empty(svc.GetMissingConfiguration());
    }

    [Fact]
    public void GetMissingConfiguration_DoesNotRequireAcceptUrl()
    {
        // Connecting no longer needs a callback the browser can reach: the user can paste the address
        // eBay sends them to, so only the keyset + RuName are required.
        var settings = new EbaySettings
        {
            AppId = "app",
            CertId = "cert",
            RuName = "ru",
            Environment = "sandbox",
        };
        var svc = new EbayAuthService(
            Options.Create(settings),
            new StubHttpClientFactory(),
            new InMemoryCredentialStore(),
            NullLogger<EbayAuthService>.Instance);

        Assert.Empty(svc.GetMissingConfiguration());
    }

    [Fact]
    public void GetMissingConfiguration_ReportsAllRequiredFields_WhenEmpty()
    {
        var svc = new EbayAuthService(
            Options.Create(new EbaySettings()),
            new StubHttpClientFactory(),
            new InMemoryCredentialStore(),
            NullLogger<EbayAuthService>.Instance);

        var missing = svc.GetMissingConfiguration();

        Assert.Contains("AppId", missing);
        Assert.Contains("CertId", missing);
        Assert.Contains("RuName", missing);
        Assert.Equal(3, missing.Count);
    }

    [Fact]
    public void GetMissingConfiguration_DoesNotRequireDevId()
    {
        // DevId is not used by the OAuth authorization-code flow, so it must
        // not block connecting even when blank.
        var settings = new EbaySettings
        {
            AppId = "app",
            CertId = "cert",
            RuName = "ru",
            DevId = "",
            Environment = "sandbox",
        };
        var svc = new EbayAuthService(
            Options.Create(settings),
            new StubHttpClientFactory(),
            new InMemoryCredentialStore(),
            NullLogger<EbayAuthService>.Instance);

        Assert.DoesNotContain("DevId", svc.GetMissingConfiguration());
    }

    [Fact]
    public void GetMissingConfiguration_TreatsWhitespaceAsMissing()
    {
        var settings = new EbaySettings
        {
            AppId = "   ",
            CertId = "cert",
            RuName = "ru",
            Environment = "sandbox",
        };
        var svc = new EbayAuthService(
            Options.Create(settings),
            new StubHttpClientFactory(),
            new InMemoryCredentialStore(),
            NullLogger<EbayAuthService>.Instance);

        Assert.Contains("AppId", svc.GetMissingConfiguration());
    }

    [Fact]
    public void GetMissingConfiguration_DoesNotRequireCertId_WhenBrokerConfigured()
    {
        var settings = new EbaySettings { AppId = "app", RuName = "ru", BrokerUrl = "https://broker.example" };
        var svc = new EbayAuthService(
            Options.Create(settings),
            new StubHttpClientFactory(),
            new InMemoryCredentialStore(),
            NullLogger<EbayAuthService>.Instance);

        Assert.True(settings.UsesBroker);
        Assert.Empty(svc.GetMissingConfiguration());
    }

    [Fact]
    public void UsesBroker_IsFalse_WhenServerHasItsOwnCertId()
    {
        var settings = new EbaySettings { AppId = "app", CertId = "cert", RuName = "ru", BrokerUrl = "https://broker.example" };
        Assert.False(settings.UsesBroker);
    }

    [Fact]
    public void GetAuthorizationUrl_AppendsState()
    {
        var url = CreateService().GetAuthorizationUrl("abc-_123");
        Assert.EndsWith("&state=abc-_123", url);
        Assert.DoesNotContain("state=", CreateService().GetAuthorizationUrl());
    }

    [Fact]
    public async Task ExchangeCode_GoesThroughBroker_WithoutCredentialsOrRedirectUri()
    {
        var handler = new CapturingTokenHandler();
        var settings = new EbaySettings { AppId = "app", RuName = "ru", BrokerUrl = "https://broker.example/" };
        var store = new InMemoryCredentialStore();
        var svc = new EbayAuthService(
            Options.Create(settings),
            new FakeHttpClientFactory(handler),
            store,
            NullLogger<EbayAuthService>.Instance);

        Assert.True(await svc.ExchangeCodeForTokensAsync("v^1#code"));

        Assert.Equal("https://broker.example/token", handler.Url);
        Assert.Null(handler.Authorization);
        Assert.Equal("grant_type=authorization_code&code=v%5E1%23code", handler.Body);
        Assert.True(svc.IsConnected);
    }

    [Fact]
    public async Task ExchangeCode_GoesStraightToEbay_WithOwnKeyset()
    {
        var handler = new CapturingTokenHandler();
        var settings = new EbaySettings
        {
            AppId = "app", CertId = "cert", RuName = "ru", BrokerUrl = "https://broker.example", Environment = "production",
        };
        var svc = new EbayAuthService(
            Options.Create(settings),
            new FakeHttpClientFactory(handler),
            new InMemoryCredentialStore(),
            NullLogger<EbayAuthService>.Instance);

        Assert.True(await svc.ExchangeCodeForTokensAsync("code"));

        Assert.Equal("https://api.ebay.com/identity/v1/oauth2/token", handler.Url);
        Assert.Equal("Basic", handler.Authorization?.Scheme);
        Assert.Contains("redirect_uri=ru", handler.Body);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RefreshesThroughBroker()
    {
        var handler = new CapturingTokenHandler();
        var store = new InMemoryCredentialStore();
        store.Set("OmniCard:eBay:AccessToken", "old");
        store.Set("OmniCard:eBay:TokenExpiry", DateTime.UtcNow.AddMinutes(-1).ToString("o"));
        store.Set("OmniCard:eBay:RefreshToken", "refresh");
        store.Set("OmniCard:eBay:RefreshTokenExpiry", DateTime.UtcNow.AddMonths(18).ToString("o"));
        var svc = new EbayAuthService(
            Options.Create(new EbaySettings { AppId = "app", RuName = "ru", BrokerUrl = "https://broker.example" }),
            new FakeHttpClientFactory(handler),
            store,
            NullLogger<EbayAuthService>.Instance);

        Assert.Equal("new-access", await svc.GetAccessTokenAsync());
        Assert.Equal("https://broker.example/token", handler.Url);
        Assert.StartsWith("grant_type=refresh_token&refresh_token=refresh&scope=", handler.Body);
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

/// <summary>Records the token request and answers like eBay's token endpoint.</summary>
public class CapturingTokenHandler : HttpMessageHandler
{
    public string? Url { get; private set; }
    public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Url = request.RequestUri?.ToString();
        Authorization = request.Headers.Authorization;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":7200,"refresh_token_expires_in":47304000}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
    }
}
