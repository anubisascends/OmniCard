using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Infrastructure;

/// <summary>
/// Gates an API action behind a specific permission. Returns <c>401</c> when not signed in and
/// <c>403</c> when the signed-in user lacks the permission. Admins hold every permission, so this
/// always passes for them. Effective permissions are resolved per request from live DB state
/// (via <see cref="PermissionService"/>), so an admin's grant/deny change takes effect on the
/// affected user's next request — no re-login.
///
/// <para>The base <see cref="ApiAuthAttribute"/> (applied via <see cref="ApiControllerBase"/>) still
/// runs first to require authentication; this adds the per-permission check on top.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermissionAttribute(string permission) : Attribute, IAsyncAuthorizationFilter
{
    public string Permission { get; } = permission;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Honor [AllowAnonymous] so external-redirect endpoints (e.g. the eBay OAuth callback) can opt
        // out of the permission gate — matches ApiAuthAttribute's behavior.
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            return;

        var userId = AppAuthGate.CurrentUserId(context.HttpContext);
        if (userId is null)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Not authenticated. Please sign in." });
            return;
        }

        var permissions = context.HttpContext.RequestServices.GetRequiredService<PermissionService>();
        if (!await permissions.HasPermissionAsync(userId.Value, Permission))
            context.Result = new ObjectResult(new { error = "You don't have permission to do that." }) { StatusCode = 403 };
    }
}
