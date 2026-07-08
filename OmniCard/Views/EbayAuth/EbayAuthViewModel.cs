using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.EbayAuth;

public sealed partial class EbayAuthViewModel(
    IEbayAuthService ebayAuthService,
    IOptions<EbaySettings> settings,
    ILogger<EbayAuthViewModel> logger) : ViewModel
{
    private readonly ILogger<EbayAuthViewModel> _logger = logger;

    public string AuthUrl => ebayAuthService.GetAuthorizationUrl();
    public string AcceptUrl => settings.Value.AcceptUrl;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public Action<bool>? CloseDialog { get; set; }

    public async Task HandleRedirectAsync(Uri uri)
    {
        // Manually parse query string to avoid System.Web dependency
        var query = ParseQueryString(uri.Query);

        // Check for user cancellation
        if (query.TryGetValue("error", out var error) && !string.IsNullOrEmpty(error))
        {
            _logger.LogInformation("User cancelled eBay OAuth consent: {Error}", error);
            CloseDialog?.Invoke(false);
            return;
        }

        // Extract auth code
        if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("No auth code in redirect URL: {Uri}", uri);
            ErrorMessage = "Failed to get authorization code from eBay.";
            return;
        }

        _logger.LogInformation("Received eBay authorization code, exchanging for tokens");
        IsLoading = true;

        var success = await ebayAuthService.ExchangeCodeForTokensAsync(code);

        if (success)
        {
            _logger.LogInformation("eBay connection established successfully");
            CloseDialog?.Invoke(true);
        }
        else
        {
            ErrorMessage = "Failed to connect to eBay. Check your internet connection and try again.";
            IsLoading = false;
        }
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return result;

        // Strip leading '?'
        var span = query.TrimStart('?');
        foreach (var pair in span.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(pair)] = "";
            }
            else
            {
                var key = Uri.UnescapeDataString(pair[..idx]);
                var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
                result[key] = value;
            }
        }
        return result;
    }
}
