using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OmniCard.Web.Services;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Web.Api.Infrastructure;

/// <summary>
/// Gates an API action behind a signed-in user (<see cref="AppAuthGate"/>): returns <c>401</c> unless
/// the request carries a valid auth cookie. Apply to every API controller except
/// <see cref="AuthController"/> (which must stay reachable while logged out). Optionally require an
/// admin via <see cref="RequireAdmin"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiAuthAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>When true, the signed-in user must also be an admin (else <c>403</c>).</summary>
    public bool RequireAdmin { get; init; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!AppAuthGate.IsAuthenticated(context.HttpContext))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Not authenticated. Please sign in." });
            return;
        }
        if (RequireAdmin && !AppAuthGate.IsAdmin(context.HttpContext))
            context.Result = new ObjectResult(new { error = "Administrator access required." }) { StatusCode = 403 };
    }
}
