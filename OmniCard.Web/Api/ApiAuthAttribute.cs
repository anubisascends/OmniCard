using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Gates an API action behind the site-wide passphrase (<see cref="AppAuthGate"/>): returns
/// <c>401</c> unless the current session is authorized. When no passphrase is configured the gate is
/// open, so this is a no-op. Apply to every API controller except <see cref="AuthController"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiAuthAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!AppAuthGate.IsAuthorized(context.HttpContext, config))
            context.Result = new UnauthorizedObjectResult(new { error = "Not authenticated. Enter the passphrase first." });
    }
}
