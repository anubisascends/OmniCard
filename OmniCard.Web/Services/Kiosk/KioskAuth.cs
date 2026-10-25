using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OmniCard.Shared.Kiosk;

namespace OmniCard.Web.Services.Kiosk;

/// <summary>
/// The kiosk device cookie: a second cookie scheme, separate from the staff sign-in (<see cref="AppAuthGate"/>),
/// scoped to <c>/api/kiosk</c> and naming only the <see cref="KioskDevice"/>. It grants nothing outside the
/// kiosk API — a kiosk device that opens <c>/app</c> just sees the staff sign-in screen.
/// </summary>
public static class KioskAuth
{
    public const string Scheme = "Kiosk";
    public const string CookiePath = "/api/kiosk";

    /// <summary>Kiosks stay enrolled until revoked; the cookie slides, so an in-use kiosk never lapses.</summary>
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    private const string DeviceClaim = "omnicard:kiosk_device";

    public static void Configure(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "OmniCard.Kiosk";
        options.Cookie.Path = CookiePath;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = CookieLifetime;
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    }

    /// <summary>The device id in the request's kiosk cookie, or null when there is none.</summary>
    public static async Task<int?> DeviceIdAsync(HttpContext ctx)
    {
        var result = await ctx.AuthenticateAsync(Scheme);
        var raw = result.Succeeded ? result.Principal?.FindFirstValue(DeviceClaim) : null;
        return int.TryParse(raw, out var id) ? id : null;
    }

    public static Task SignInAsync(HttpContext ctx, KioskDevice device)
    {
        var identity = new ClaimsIdentity([new Claim(DeviceClaim, device.Id.ToString()), new Claim(ClaimTypes.Name, device.Name)], Scheme);
        var props = new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.Add(CookieLifetime) };
        return ctx.SignInAsync(Scheme, new ClaimsPrincipal(identity), props);
    }

    public static Task SignOutAsync(HttpContext ctx) => ctx.SignOutAsync(Scheme);
}
