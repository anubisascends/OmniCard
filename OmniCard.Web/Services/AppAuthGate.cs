using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>
/// Site-wide authentication for the SPA, now backed by per-user accounts (see <see cref="UserService"/>)
/// instead of the old shared passphrase. Identity is carried in an encrypted, HttpOnly cookie issued
/// by ASP.NET Core cookie authentication; "remember me" makes that cookie persistent
/// (<see cref="SignInAsync"/>), otherwise it's a session cookie cleared when the browser closes.
///
/// Auth is always enforced — there is always at least the seeded Admin account — so every API
/// controller except <see cref="Api.AuthController"/> requires a signed-in user.
/// </summary>
public static class AppAuthGate
{
    /// <summary>Cookie auth scheme used for the app login.</summary>
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Persistent "remember me" cookies last this long; session cookies expire on browser close.</summary>
    public static readonly TimeSpan RememberDuration = TimeSpan.FromDays(30);

    private const string IsAdminClaim = "omnicard:is_admin";

    public static bool IsAuthenticated(HttpContext ctx) => ctx.User?.Identity?.IsAuthenticated == true;

    public static bool IsAdmin(HttpContext ctx) =>
        IsAuthenticated(ctx) && ctx.User.HasClaim(IsAdminClaim, "true");

    public static string? CurrentUsername(HttpContext ctx) =>
        IsAuthenticated(ctx) ? ctx.User.Identity?.Name : null;

    /// <summary>The signed-in user's id, or null when not authenticated.</summary>
    public static int? CurrentUserId(HttpContext ctx)
    {
        var raw = ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Issue the auth cookie for <paramref name="user"/>. Persistent when <paramref name="rememberMe"/>.</summary>
    public static Task SignInAsync(HttpContext ctx, User user, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(IsAdminClaim, (user.IsAdmin || user.IsSystem) ? "true" : "false"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        var props = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.Add(RememberDuration) : null,
        };
        return ctx.SignInAsync(Scheme, principal, props);
    }

    public static Task SignOutAsync(HttpContext ctx) => ctx.SignOutAsync(Scheme);
}
