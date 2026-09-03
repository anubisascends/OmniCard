namespace OmniCard.Models;

/// <summary>
/// Outcome of checking GitHub for a newer OmniCard release. Best-effort: the service returns
/// <c>null</c> (never throws) when the check can't complete (offline, rate-limited, etc.).
/// </summary>
/// <param name="UpdateAvailable">True when <paramref name="LatestVersion"/> is newer than the running build.</param>
/// <param name="CurrentVersion">The running build's version (e.g. <c>1.2.0</c>).</param>
/// <param name="LatestVersion">The latest published release's version (e.g. <c>1.3.0</c>).</param>
/// <param name="ReleaseUrl">The GitHub release page URL to open when the user clicks the notice.</param>
public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl);
