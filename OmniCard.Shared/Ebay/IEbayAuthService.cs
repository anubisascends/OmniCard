using System.ComponentModel;

namespace OmniCard.Shared.Ebay;

public interface IEbayAuthService : INotifyPropertyChanged
{
    bool IsConnected { get; }
    Task<string?> GetAccessTokenAsync();
    Task<bool> ExchangeCodeForTokensAsync(string authCode);
    void Disconnect();
    /// <summary>eBay's consent-page URL. <paramref name="state"/> is echoed back on the redirect (the
    /// broker reads the return address from it; the callback checks its nonce).</summary>
    string GetAuthorizationUrl(string? state = null);

    /// <summary>
    /// Returns the names of required eBay settings that are missing or blank.
    /// An empty list means the app is configured well enough to attempt an OAuth connection.
    /// </summary>
    IReadOnlyList<string> GetMissingConfiguration();
}
