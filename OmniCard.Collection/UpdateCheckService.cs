using System.Net.Http;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Checks the OmniCard GitHub repository's latest published release for a newer version than the
/// one running. Uses the public <c>releases/latest</c> endpoint (no auth, excludes drafts and
/// pre-releases). Every failure is swallowed into a <c>null</c> result so a failed or offline
/// check silently does nothing — the update notice is meant to be helpful, never disruptive.
/// </summary>
public class UpdateCheckService(IHttpClientFactory httpClientFactory) : IUpdateCheckService
{
    // Source of truth for releases. The desktop app and web companion both live in this repo.
    private const string Owner = "anubisascends";
    private const string Repo = "OmniCard";
    private const string LatestReleaseEndpoint = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            // GitHub's API rejects requests without a User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await client.GetAsync(LatestReleaseEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            var tag = GetString(root, "tag_name");
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var releaseUrl = GetString(root, "html_url")
                ?? $"https://github.com/{Owner}/{Repo}/releases/latest";

            var latestDisplay = Normalize(tag);
            var currentDisplay = Normalize(currentVersion);

            return new UpdateCheckResult(
                UpdateAvailable: IsNewer(currentVersion, tag),
                CurrentVersion: currentDisplay,
                LatestVersion: latestDisplay,
                ReleaseUrl: releaseUrl);
        }
        catch
        {
            // Best-effort: any network/parse/cancellation failure just means "no update info".
            return null;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="latest"/> represents a strictly newer release than
    /// <paramref name="current"/>. A leading <c>v</c> and any pre-release/build suffix
    /// (<c>-alpha.1</c>, <c>+sha</c>) are ignored, so a dev build (e.g. <c>1.2.1-alpha.3</c>)
    /// running ahead of the last release (<c>1.2.0</c>) is not flagged. Unparseable input on
    /// either side yields false (fail closed — never nag on garbage).
    /// </summary>
    public static bool IsNewer(string? current, string? latest)
    {
        if (!TryParseVersion(current, out var currentVersion)) return false;
        if (!TryParseVersion(latest, out var latestVersion)) return false;
        return latestVersion > currentVersion;
    }

    /// <summary>
    /// Parses a tag/version string like <c>v1.2.0</c>, <c>1.2</c>, or <c>1.2.1-alpha.3+abc</c>
    /// into a <see cref="Version"/> using only its numeric <c>major.minor[.patch[.rev]]</c> core.
    /// </summary>
    private static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var core = raw.Trim();
        if (core is ['v' or 'V', ..]) core = core[1..];

        // Drop pre-release / build-metadata suffixes: keep only the leading numeric portion.
        var cut = core.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) core = core[..cut];
        if (core.Length == 0) return false;

        // System.Version needs at least major.minor.
        if (!core.Contains('.')) core += ".0";

        return Version.TryParse(core, out version!);
    }

    /// <summary>Strips a leading <c>v</c> for display (e.g. <c>v1.2.0</c> -&gt; <c>1.2.0</c>).</summary>
    private static string Normalize(string raw)
    {
        var v = raw?.Trim() ?? "";
        return v is ['v' or 'V', ..] ? v[1..] : v;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
