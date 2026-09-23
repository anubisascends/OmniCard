using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Authentication;

namespace OmniCard.Web.Mcp;

/// <summary>
/// Wires the MCP endpoint as an OAuth 2.0 resource server: JWT bearer validation of the external IdP's
/// access tokens, plus the MCP handler that serves the Protected Resource Metadata document and issues
/// the <c>401 + WWW-Authenticate: resource_metadata=…</c> challenge that lets clients discover the IdP.
///
/// <para><b>Scoped to /mcp only.</b> These schemes are added <em>alongside</em> the app's cookie scheme
/// without changing the global default, so the SPA's cookie auth is untouched — the JWT is consulted
/// only for the <c>/mcp</c> endpoint (via its authorization policy, and because the MCP scheme forwards
/// authentication to JWT bearer).</para>
/// </summary>
public static class McpAuthExtensions
{
    /// <summary>Adds the <c>Bearer</c> (JWT) and MCP authentication schemes for the resource-server flow.
    /// Call only when <see cref="McpOAuthOptions.IsValid"/>.</summary>
    public static AuthenticationBuilder AddOmniCardMcpAuth(this AuthenticationBuilder builder, McpOAuthOptions options)
    {
        builder.AddJwtBearer(jwt =>
        {
            // Authority drives OIDC discovery (issuer + JWKS signing keys); Audience sets ValidAudience.
            jwt.Authority = options.Authority;
            jwt.Audience = options.Audience;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Common across IdPs: display name in "name", roles in "roles".
                NameClaimType = "name",
                RoleClaimType = "roles",
            };
        });

        builder.AddMcp(mcp =>
        {
            // Validate the presented token via JWT bearer; the MCP handler only owns the challenge +
            // metadata. (The ctor already defaults this to "Bearer"; set explicitly for clarity.)
            mcp.ForwardAuthenticate = JwtBearerDefaults.AuthenticationScheme;
            mcp.ResourceMetadata = BuildResourceMetadata(options);
        });

        return builder;
    }

    /// <summary>Builds the RFC 9728 Protected Resource Metadata document advertised at
    /// <c>/.well-known/oauth-protected-resource</c>. Extracted so it can be unit-tested without a host.</summary>
    public static ProtectedResourceMetadata BuildResourceMetadata(McpOAuthOptions options) => new()
    {
        Resource = $"{options.PublicBaseUrl!.TrimEnd('/')}/mcp",
        AuthorizationServers = { options.Authority! },
        ScopesSupported = [.. options.Scopes],
        // BearerMethodsSupported already defaults to ["header"].
    };
}
