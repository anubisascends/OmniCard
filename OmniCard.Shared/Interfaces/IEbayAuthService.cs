using System.ComponentModel;

namespace OmniCard.Interfaces;

public interface IEbayAuthService : INotifyPropertyChanged
{
    bool IsConnected { get; }
    Task<string?> GetAccessTokenAsync();
    Task<bool> ExchangeCodeForTokensAsync(string authCode);
    void Disconnect();
    string GetAuthorizationUrl();

    /// <summary>
    /// Returns the names of required eBay settings that are missing or blank.
    /// An empty list means the app is configured well enough to attempt an OAuth connection.
    /// </summary>
    IReadOnlyList<string> GetMissingConfiguration();
}
