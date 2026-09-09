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
public sealed class AuthController(UserService users) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<AuthStatusDto> Status()
    {
        var authed = AppAuthGate.IsAuthenticated(HttpContext);
        return new AuthStatusDto(
            AuthRequired: true,
            Authenticated: authed,
            Username: AppAuthGate.CurrentUsername(HttpContext),
            IsAdmin: AppAuthGate.IsAdmin(HttpContext));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthStatusDto>> Login([FromBody] LoginRequest request)
    {
        var user = await users.AuthenticateAsync(request.Username, request.Password);
        if (user is null)
            return Unauthorized(new { error = "Incorrect username or password." });

        await AppAuthGate.SignInAsync(HttpContext, user, request.RememberMe);
        return new AuthStatusDto(true, true, user.Username, user.IsAdmin || user.IsSystem);
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
