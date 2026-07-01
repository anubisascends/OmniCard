using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IEbayAuthService : INotifyPropertyChanged
{
    bool IsConnected { get; }
    Task<string?> GetAccessTokenAsync();
    Task<bool> ExchangeCodeForTokensAsync(string authCode);
    void Disconnect();
    string GetAuthorizationUrl();
}

public partial class EbayAuthService : ObservableObject, IEbayAuthService
{
    private const string CredentialPrefix = "OmniCard:eBay:";
    private const string AccessTokenKey = CredentialPrefix + "AccessToken";
    private const string RefreshTokenKey = CredentialPrefix + "RefreshToken";
    private const string TokenExpiryKey = CredentialPrefix + "TokenExpiry";
    private const string RefreshTokenExpiryKey = CredentialPrefix + "RefreshTokenExpiry";

    private static readonly string[] OAuthScopes =
    [
        "https://api.ebay.com/oauth/api_scope",
        "https://api.ebay.com/oauth/api_scope/sell.inventory",
        "https://api.ebay.com/oauth/api_scope/sell.account",
        "https://api.ebay.com/oauth/api_scope/sell.fulfillment",
    ];

    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<EbayAuthService> _logger;

    public EbayAuthService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        ICredentialStore credentialStore,
        ILogger<EbayAuthService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    public bool IsConnected
    {
        get
        {
            var refreshToken = _credentialStore.Get(RefreshTokenKey);
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            var expiryStr = _credentialStore.Get(RefreshTokenExpiryKey);
            if (expiryStr is null || !DateTime.TryParse(expiryStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiry))
                return false;

            return expiry > DateTime.UtcNow;
        }
    }

    public string GetAuthorizationUrl()
    {
        var scopes = Uri.EscapeDataString(string.Join(" ", OAuthScopes));
        return $"{_settings.AuthBaseUrl}/oauth2/authorize" +
            $"?client_id={Uri.EscapeDataString(_settings.AppId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(_settings.RuName)}" +
            $"&scope={scopes}";
    }

    public async Task<bool> ExchangeCodeForTokensAsync(string authCode)
    {
        try
        {
            _logger.LogInformation("Exchanging authorization code for tokens");
            var client = _httpClientFactory.CreateClient();

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AppId}:{_settings.CertId}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = authCode,
                ["redirect_uri"] = _settings.RuName,
            });

            var response = await client.PostAsync($"{_settings.ApiBaseUrl}/identity/v1/oauth2/token", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Token exchange failed: {Status} — {Error}", response.StatusCode, error);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(json);

            var accessToken = tokenResponse.GetProperty("access_token").GetString()!;
            var refreshToken = tokenResponse.GetProperty("refresh_token").GetString()!;
            var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
            var refreshExpiresIn = tokenResponse.GetProperty("refresh_token_expires_in").GetInt32();

            _credentialStore.Set(AccessTokenKey, accessToken);
            _credentialStore.Set(RefreshTokenKey, refreshToken);
            _credentialStore.Set(TokenExpiryKey, DateTime.UtcNow.AddSeconds(expiresIn).ToString("o"));
            _credentialStore.Set(RefreshTokenExpiryKey, DateTime.UtcNow.AddSeconds(refreshExpiresIn).ToString("o"));

            OnPropertyChanged(nameof(IsConnected));
            _logger.LogInformation("Successfully connected to eBay");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange authorization code for tokens");
            return false;
        }
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        if (!IsConnected)
            return null;

        // Check if access token is still valid (with 5-minute safety margin)
        var accessToken = _credentialStore.Get(AccessTokenKey);
        var expiryStr = _credentialStore.Get(TokenExpiryKey);

        if (accessToken is not null && expiryStr is not null
            && DateTime.TryParse(expiryStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiry)
            && expiry > DateTime.UtcNow.AddMinutes(5))
        {
            return accessToken;
        }

        // Try to refresh
        return await RefreshAccessTokenAsync();
    }

    private async Task<string?> RefreshAccessTokenAsync()
    {
        var refreshToken = _credentialStore.Get(RefreshTokenKey);
        if (string.IsNullOrEmpty(refreshToken))
            return null;

        try
        {
            _logger.LogInformation("Refreshing eBay access token");
            var client = _httpClientFactory.CreateClient();

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AppId}:{_settings.CertId}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = string.Join(" ", OAuthScopes),
            });

            var response = await client.PostAsync($"{_settings.ApiBaseUrl}/identity/v1/oauth2/token", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token refresh failed: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(json);

            var newAccessToken = tokenResponse.GetProperty("access_token").GetString()!;
            var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();

            _credentialStore.Set(AccessTokenKey, newAccessToken);
            _credentialStore.Set(TokenExpiryKey, DateTime.UtcNow.AddSeconds(expiresIn).ToString("o"));

            _logger.LogInformation("eBay access token refreshed successfully");
            return newAccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh eBay access token");
            return null;
        }
    }

    public void Disconnect()
    {
        _logger.LogInformation("Disconnecting from eBay");
        _credentialStore.Delete(AccessTokenKey);
        _credentialStore.Delete(RefreshTokenKey);
        _credentialStore.Delete(TokenExpiryKey);
        _credentialStore.Delete(RefreshTokenExpiryKey);
        OnPropertyChanged(nameof(IsConnected));
    }
}
