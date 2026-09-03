using System.Reflection;

namespace OmniCard.Helpers;

/// <summary>
/// Single source of truth for the running app's version, read at runtime from the entry
/// assembly's <see cref="AssemblyInformationalVersionAttribute"/> (stamped by MinVer from the
/// current git tag — see <c>Directory.Build.props</c>). Shared by the About dialog, the
/// status-bar version label, and the GitHub update check.
/// </summary>
public static class AppVersionInfo
{
    /// <summary>
    /// The informational version with any <c>+&lt;git-sha&gt;</c> build-metadata suffix trimmed
    /// (e.g. <c>1.2.0</c> or <c>1.2.1-alpha.0.3</c>). Falls back to the numeric assembly version.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The version prefixed with <c>v</c> for display (e.g. <c>v1.2.0</c>).</summary>
    public static string Display { get; } = "v" + Version;

    private static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // InformationalVersion often carries a "+<git-sha>" build suffix — trim it for display.
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            if (plus >= 0) info = info[..plus];
        }

        if (!string.IsNullOrWhiteSpace(info)) return info;
        return asm.GetName().Version?.ToString() ?? "1.0.0";
    }
}
