using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Services;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Per-user login for the SPA. Deliberately NOT gated (it must be reachable while logged out). All
/// other API controllers derive from <see cref="ApiControllerBase"/> and require a signed-in user.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(UserService users, PermissionService permissions) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<AuthStatusDto>> Status()
    {
        var authed = AppAuthGate.IsAuthenticated(HttpContext);
        // Effective permissions are resolved fresh (from PermissionService) so the SPA's nav/buttons
        // reflect admin changes without a re-login. Admins get the full catalog.
        var userId = AppAuthGate.CurrentUserId(HttpContext);
        var perms = authed && userId is int id
            ? (await permissions.GetEffectiveAsync(id)).OrderBy(p => p).ToList()
            : [];
        return new AuthStatusDto(
            AuthRequired: true,
            Authenticated: authed,
            Username: AppAuthGate.CurrentUsername(HttpContext),
            IsAdmin: AppAuthGate.IsAdmin(HttpContext),
            Permissions: perms);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthStatusDto>> Login([FromBody] LoginRequest request)
    {
        var user = await users.AuthenticateAsync(request.Username, request.Password);
        if (user is null)
            return Unauthorized(new { error = "Incorrect username or password." });

        await AppAuthGate.SignInAsync(HttpContext, user, request.RememberMe);
        var perms = (await permissions.GetEffectiveAsync(user.Id)).OrderBy(p => p).ToList();
        return new AuthStatusDto(true, true, user.Username, user.IsAdmin || user.IsSystem, perms);
    }

    [HttpPost("logout")]
    public async Task<ActionResult<AuthStatusDto>> Logout()
    {
        await AppAuthGate.SignOutAsync(HttpContext);
        return new AuthStatusDto(true, false);
    }

    /// <summary>Self-service password change for the signed-in user (requires the current password).</summary>
    [HttpPost("change-password")]
    [ApiAuth]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = AppAuthGate.CurrentUserId(HttpContext);
        if (userId is null)
            return Unauthorized(new { error = "Not authenticated." });

        try
        {
            var ok = await users.ChangePasswordAsync(userId.Value, request.CurrentPassword, request.NewPassword);
            if (!ok)
                return BadRequest(new { error = "Current password is incorrect." });
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
