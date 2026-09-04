using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Site-wide passphrase login for the SPA. Deliberately NOT gated (it must be reachable while
/// locked). All other API controllers derive from <see cref="ApiControllerBase"/> and require a
/// successful login here when a passphrase is configured.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(IConfiguration config) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<AuthStatusDto> Status() =>
        new AuthStatusDto(AppAuthGate.IsEnabled(config), AppAuthGate.IsUnlocked(HttpContext));

    [HttpPost("login")]
    public ActionResult<AuthStatusDto> Login([FromBody] LoginRequest request)
    {
        if (!AppAuthGate.IsEnabled(config))
            // Nothing to unlock; treat as already authorized.
            return new AuthStatusDto(false, true);

        if (!AppAuthGate.Verify(config, request.Passphrase))
            return Unauthorized(new { error = "Incorrect passphrase." });

        AppAuthGate.Unlock(HttpContext);
        return new AuthStatusDto(true, true);
    }

    [HttpPost("logout")]
    public ActionResult<AuthStatusDto> Logout()
    {
        AppAuthGate.Lock(HttpContext);
        return new AuthStatusDto(AppAuthGate.IsEnabled(config), false);
    }
}
