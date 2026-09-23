namespace OmniCard.Web.Mcp;

/// <summary>
/// Configuration for the MCP endpoint's OAuth resource-server mode, bound from the <c>Mcp</c> config
/// section. When <see cref="IsValid"/>, <c>/mcp</c> requires a valid IdP-issued JWT (remote clients
/// welcome); otherwise the server falls back to the phase-1 loopback-only, no-auth behavior.
///
/// <para>OmniCard is only a <em>resource server</em> — it validates tokens minted by an external
/// identity provider (Entra ID / Auth0 / Keycloak). <see cref="Authority"/> drives OIDC discovery
/// (issuer + JWKS); <see cref="Audience"/> must equal the audience the IdP stamps into access tokens.</para>
/// </summary>
public sealed class McpOAuthOptions
{
    /// <summary>Turn on OAuth for <c>/mcp</c>. Also requires Authority/Audience/PublicBaseUrl to be set.</summary>
    public bool Enabled { get; set; }

    /// <summary>The IdP issuer/authority URL (e.g. the Entra tenant v2 endpoint, Auth0 domain, or
    /// Keycloak realm). OIDC discovery under this URL supplies the issuer and signing keys.</summary>
    public string? Authority { get; set; }

    /// <summary>The token audience the IdP mints (Entra: the API's Application ID URI; Auth0: the API
    /// identifier; Keycloak: the client/audience). Tokens with any other audience are rejected.</summary>
    public string? Audience { get; set; }

    /// <summary>Scopes advertised in the Protected Resource Metadata document (e.g. <c>mcp:tools</c>).</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>The public HTTPS origin the server is reached at (e.g.
    /// <c>https://omnicard.example.com</c>). Used to build the PRM <c>resource</c> URI so it is correct
    /// even behind IIS / a reverse proxy.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>True only when OAuth is enabled AND every required field is present. Program.cs uses
    /// this to decide between OAuth-gated (remote) and loopback-only (phase-1) modes.</summary>
    public bool IsValid =>
        Enabled
        && !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(Audience)
        && !string.IsNullOrWhiteSpace(PublicBaseUrl);

    /// <summary>Binds the options from the <c>Mcp</c> config section (<c>Mcp:OAuth:*</c> plus
    /// <c>Mcp:PublicBaseUrl</c>).</summary>
    public static McpOAuthOptions FromConfiguration(IConfiguration config)
    {
        var oauth = config.GetSection("Mcp:OAuth");
        return new McpOAuthOptions
        {
            Enabled = oauth.GetValue("Enabled", false),
            Authority = oauth["Authority"],
            Audience = oauth["Audience"],
            Scopes = oauth.GetSection("Scopes").Get<string[]>() ?? [],
            PublicBaseUrl = config["Mcp:PublicBaseUrl"],
        };
    }
}
