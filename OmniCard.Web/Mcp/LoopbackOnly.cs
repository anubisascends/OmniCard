using System.Net;

namespace OmniCard.Web.Mcp;

/// <summary>
/// The entire trust model for the MCP endpoint in this phase: it is reachable only from the machine
/// the server runs on. This middleware is branched onto the <c>/mcp</c> path (see
/// <c>Program.cs</c>) and returns <c>403</c> for any request whose remote address isn't loopback,
/// unless the operator opts into remote access with <c>Mcp:AllowRemote=true</c>.
///
/// <para>The MCP tools run with no per-user identity (there is no auth handshake), so binding to
/// loopback is what keeps the collection/sales data from being readable across the LAN. Do NOT set
/// <c>Mcp:AllowRemote</c> without first adding authentication.</para>
///
/// <para>Implemented as branch middleware rather than an endpoint filter because ASP.NET Core
/// endpoint filters only run for minimal-API route handlers, not for the custom endpoints
/// <c>MapMcp</c> registers.</para>
/// </summary>
public sealed class LoopbackOnly(RequestDelegate next, IConfiguration config, ILogger<LoopbackOnly> logger)
{
    private readonly bool _allowRemote = config.GetValue("Mcp:AllowRemote", false);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_allowRemote && !IsLoopback(context.Connection.RemoteIpAddress))
        {
            logger.LogWarning("Rejected non-loopback MCP request from {Remote}.", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "The MCP endpoint is restricted to the local machine. Set Mcp:AllowRemote=true to allow remote clients (only after adding authentication)."
            });
            return;
        }

        await next(context);
    }

    /// <summary>True for IPv4/IPv6 loopback, including IPv4-mapped-IPv6 loopback (<c>::ffff:127.0.0.1</c>).</summary>
    private static bool IsLoopback(IPAddress? ip)
    {
        if (ip is null) return false;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(ip.MapToIPv4())) return true;
        return false;
    }
}
