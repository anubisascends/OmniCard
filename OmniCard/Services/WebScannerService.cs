// OmniCard/Services/WebScannerService.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;
using System.IO;

namespace OmniCard.Services;

public sealed class WebScannerService : IAsyncDisposable
{
    private readonly ICardService _cardService;
    private readonly ILogger<WebScannerService> _logger;
    private readonly IOptionsMonitor<WebCompanionSettings> _settings;
    private HubConnection? _hubConnection;
    private IDisposable? _settingsChangeToken;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public WebScannerService(
        ICardService cardService,
        ILogger<WebScannerService> logger,
        IOptionsMonitor<WebCompanionSettings> settings)
    {
        _cardService = cardService;
        _logger = logger;
        _settings = settings;
    }

    public async Task StartAsync()
    {
        var baseUrl = _settings.CurrentValue.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogInformation("WebCompanion BaseUrl not configured — phone scanner disabled");
            return;
        }

        await ConnectAsync(baseUrl);

        // Reconnect if settings change
        _settingsChangeToken = _settings.OnChange(async newSettings =>
        {
            var newUrl = newSettings.BaseUrl;
            if (string.IsNullOrWhiteSpace(newUrl))
            {
                await DisconnectAsync();
                return;
            }

            // Reconnect if URL changed
            if (_hubConnection is null || _hubConnection.State == HubConnectionState.Disconnected)
                await ConnectAsync(newUrl);
        });
    }

    private async Task ConnectAsync(string baseUrl)
    {
        await DisconnectAsync();

        var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/scan";
        _logger.LogInformation("Connecting to phone scanner hub at {Url}", hubUrl);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<byte[]>("ImageReceived", OnImageReceived);

        _hubConnection.Reconnecting += _ =>
        {
            _logger.LogWarning("Phone scanner connection lost, reconnecting...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += _ =>
        {
            _logger.LogInformation("Phone scanner reconnected");
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync();
            _logger.LogInformation("Connected to phone scanner hub");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to phone scanner hub at {Url} — phone scanning unavailable", hubUrl);
        }
    }

    private void OnImageReceived(byte[] imageData)
    {
        _logger.LogInformation("Received image from phone: {Size} bytes", imageData.Length);
        try
        {
            using var stream = new MemoryStream(imageData);
            _cardService.AddFromStream(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process phone scan image");
        }
    }

    private async Task DisconnectAsync()
    {
        if (_hubConnection is not null)
        {
            try
            {
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting from phone scanner hub");
            }
            _hubConnection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _settingsChangeToken?.Dispose();
        await DisconnectAsync();
    }
}
