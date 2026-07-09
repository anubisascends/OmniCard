// OmniCard.Web/Hubs/ScanHub.cs
using Microsoft.AspNetCore.SignalR;

namespace OmniCard.Web.Hubs;

public class ScanHub : Hub
{
    private readonly ILogger<ScanHub> _logger;

    public ScanHub(ILogger<ScanHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Desktop client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Desktop client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
