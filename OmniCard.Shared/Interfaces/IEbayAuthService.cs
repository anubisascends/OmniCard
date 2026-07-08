using System.ComponentModel;

namespace OmniCard.Interfaces;

public interface IEbayAuthService : INotifyPropertyChanged
{
    bool IsConnected { get; }
    Task<string?> GetAccessTokenAsync();
    Task<bool> ExchangeCodeForTokensAsync(string authCode);
    void Disconnect();
    string GetAuthorizationUrl();
}
