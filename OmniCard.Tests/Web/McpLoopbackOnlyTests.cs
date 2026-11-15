using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Web.Mcp;

namespace OmniCard.Tests.Web;

/// <summary>
/// The loopback-only gate on /mcp (no OAuth configured). A tunnel or reverse proxy on the same machine
/// (Cloudflare Tunnel's cloudflared) connects from 127.0.0.1 for remote clients, so proxied requests
/// must be refused even though their socket address is loopback.
/// </summary>
public class McpLoopbackOnlyTests
{
    private static async Task<(bool PassedThrough, int Status)> Run(
        IPAddress remote, Dictionary<string, string>? headers = null, bool allowRemote = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:AllowRemote"] = allowRemote ? "true" : "false" })
            .Build();
        var passed = false;
        var middleware = new LoopbackOnly(_ => { passed = true; return Task.CompletedTask; },
            config, NullLogger<LoopbackOnly>.Instance);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote;
        context.Response.Body = new MemoryStream();
        foreach (var (name, value) in headers ?? []) context.Request.Headers[name] = value;

        await middleware.InvokeAsync(context);
        return (passed, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task DirectLoopback_IsAllowed(string ip)
    {
        var (passed, _) = await Run(IPAddress.Parse(ip));
        Assert.True(passed);
    }

    [Fact]
    public async Task LanAddress_IsRejected()
    {
        var (passed, status) = await Run(IPAddress.Parse("192.168.1.80"));
        Assert.False(passed);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Theory]
    [InlineData("Cf-Connecting-IP", "203.0.113.7")]
    [InlineData("Cf-Ray", "8f1c2d3e4f5a6b7c-DFW")]
    [InlineData("X-Forwarded-For", "203.0.113.7")]
    [InlineData("X-Forwarded-Host", "omnicard.example.com")]
    [InlineData("X-Real-IP", "203.0.113.7")]
    [InlineData("Forwarded", "for=203.0.113.7;proto=https")]
    public async Task LoopbackThroughProxy_IsRejected(string header, string value)
    {
        var (passed, status) = await Run(IPAddress.Loopback, new() { [header] = value });
        Assert.False(passed);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task AllowRemote_LetsProxiedRequestsThrough()
    {
        var (passed, _) = await Run(IPAddress.Loopback, new() { ["Cf-Connecting-IP"] = "203.0.113.7" }, allowRemote: true);
        Assert.True(passed);
    }
}
