using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Gates an API action behind the binder-editor passphrase: returns <c>401</c> unless the current
/// session has been unlocked via the passphrase form. Applied to <see cref="BinderEditController"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class BinderEditAuthAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!BinderEditGate.IsUnlocked(context.HttpContext))
            context.Result = new UnauthorizedObjectResult(new { error = "Binder editing is locked. Enter the passphrase first." });
    }
}
