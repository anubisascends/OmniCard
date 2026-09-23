using Microsoft.Extensions.Configuration;
using OmniCard.Web.Mcp;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers the MCP OAuth resource-server config binding and the Protected Resource Metadata document it
/// produces. These are the pieces that decide whether /mcp runs OAuth-gated (remote) or loopback-only,
/// and what clients discover at /.well-known/oauth-protected-resource.
/// </summary>
public class McpOAuthOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void FromConfiguration_BindsAllFields()
    {
        var opts = McpOAuthOptions.FromConfiguration(Config(new()
        {
            ["Mcp:PublicBaseUrl"] = "https://omnicard.example.com",
            ["Mcp:OAuth:Enabled"] = "true",
            ["Mcp:OAuth:Authority"] = "https://login.example.com/tenant",
            ["Mcp:OAuth:Audience"] = "api://omnicard-mcp",
            ["Mcp:OAuth:Scopes:0"] = "mcp:tools",
        }));

        Assert.True(opts.Enabled);
        Assert.Equal("https://login.example.com/tenant", opts.Authority);
        Assert.Equal("api://omnicard-mcp", opts.Audience);
        Assert.Equal(["mcp:tools"], opts.Scopes);
        Assert.Equal("https://omnicard.example.com", opts.PublicBaseUrl);
        Assert.True(opts.IsValid);
    }

    [Fact]
    public void IsValid_False_WhenDisabled_Or_MissingRequiredFields()
    {
        // Disabled → invalid even with everything else present.
        Assert.False(new McpOAuthOptions
        {
            Enabled = false,
            Authority = "https://login.example.com",
            Audience = "api://omnicard-mcp",
            PublicBaseUrl = "https://omnicard.example.com",
        }.IsValid);

        // Enabled but no audience → invalid.
        Assert.False(new McpOAuthOptions
        {
            Enabled = true,
            Authority = "https://login.example.com",
            PublicBaseUrl = "https://omnicard.example.com",
        }.IsValid);

        // Empty config → disabled → loopback fallback.
        Assert.False(McpOAuthOptions.FromConfiguration(Config(new())).IsValid);
    }

    [Fact]
    public void BuildResourceMetadata_ProducesPrmDocument()
    {
        var opts = new McpOAuthOptions
        {
            Enabled = true,
            Authority = "https://login.example.com/tenant",
            Audience = "api://omnicard-mcp",
            Scopes = ["mcp:tools", "mcp:read"],
            PublicBaseUrl = "https://omnicard.example.com/", // trailing slash should be trimmed
        };

        var prm = McpAuthExtensions.BuildResourceMetadata(opts);

        Assert.Equal("https://omnicard.example.com/mcp", prm.Resource);
        Assert.Contains("https://login.example.com/tenant", prm.AuthorizationServers);
        Assert.Equal(["mcp:tools", "mcp:read"], prm.ScopesSupported);
        Assert.Contains("header", prm.BearerMethodsSupported); // SDK default
    }
}
