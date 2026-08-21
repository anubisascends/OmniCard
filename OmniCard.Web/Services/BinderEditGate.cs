using System.Security.Cryptography;
using System.Text;

namespace OmniCard.Web.Services;

/// <summary>
/// The passphrase gate for the binder editor. The rest of the web companion is anonymous/read-only;
/// editing is the one write surface, so it is locked behind a shared passphrase configured in
/// <c>appsettings.json</c> (key <c>Binder:EditPassphrase</c>). Unlock state is kept per-browser in
/// the session. Fails closed: if no passphrase is configured, editing is never unlocked.
/// </summary>
public static class BinderEditGate
{
    public const string ConfigKey = "Binder:EditPassphrase";
    private const string SessionKey = "binder-edit-unlocked";

    /// <summary>The configured passphrase, or null/empty when editing is disabled.</summary>
    public static string? ConfiguredPassphrase(IConfiguration config) => config[ConfigKey];

    /// <summary>True when a passphrase is configured at all (i.e. editing is enabled on this server).</summary>
    public static bool IsEnabled(IConfiguration config) => !string.IsNullOrWhiteSpace(ConfiguredPassphrase(config));

    public static bool IsUnlocked(HttpContext ctx) => ctx.Session.GetString(SessionKey) == "1";

    public static void Unlock(HttpContext ctx) => ctx.Session.SetString(SessionKey, "1");

    /// <summary>Constant-time check of an entered passphrase against the configured one. Returns
    /// false when editing is disabled (no passphrase configured) or the entry doesn't match.</summary>
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
