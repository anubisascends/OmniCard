using System.Security.Cryptography;
using System.Text;

namespace OmniCard.Web.Services;

/// <summary>
/// Site-wide passphrase gate for the full web app (the read/write SPA), distinct from the older
/// binder-only <see cref="BinderEditGate"/>. A single shared passphrase is configured under
/// <c>Auth:Passphrase</c>; unlock state is kept per-browser in the session.
///
/// Policy: auth is only <em>enforced</em> when a passphrase is configured. With no passphrase set
/// the site is open (suitable for a trusted, isolated LAN or local development) — set the passphrase
/// in production to lock it down. <see cref="Verify"/> uses a constant-time comparison.
/// </summary>
public static class AppAuthGate
{
    public const string ConfigKey = "Auth:Passphrase";
    private const string SessionKey = "app-unlocked";

    public static string? ConfiguredPassphrase(IConfiguration config) => config[ConfigKey];

    /// <summary>True when a passphrase is configured — i.e. auth is required on this server.</summary>
    public static bool IsEnabled(IConfiguration config) => !string.IsNullOrWhiteSpace(ConfiguredPassphrase(config));

    public static bool IsUnlocked(HttpContext ctx) => ctx.Session.GetString(SessionKey) == "1";

    public static void Unlock(HttpContext ctx) => ctx.Session.SetString(SessionKey, "1");

    public static void Lock(HttpContext ctx) => ctx.Session.Remove(SessionKey);

    /// <summary>True if the request should be allowed through: either auth is disabled (no
    /// passphrase configured) or this session has been unlocked.</summary>
    public static bool IsAuthorized(HttpContext ctx, IConfiguration config) =>
        !IsEnabled(config) || IsUnlocked(ctx);

    /// <summary>Constant-time check of an entered passphrase against the configured one. Returns
    /// false when auth is disabled (nothing to unlock) or the entry doesn't match.</summary>
    public static bool Verify(IConfiguration config, string? entered)
    {
        var expected = ConfiguredPassphrase(config);
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrEmpty(entered))
            return false;

        var a = Encoding.UTF8.GetBytes(entered);
        var b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
