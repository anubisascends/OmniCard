using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>
/// Checks the project's GitHub releases for a newer version than the one currently running.
/// Implementations are best-effort and non-intrusive: they return <c>null</c> (rather than
/// throwing) on any network/parse failure, so a failed check silently does nothing.
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>
    /// Queries the latest published GitHub release and compares it to <paramref name="currentVersion"/>.
    /// </summary>
    /// <param name="currentVersion">
    /// The running build's version (e.g. <c>1.2.0</c> or <c>v1.2.0</c>); a leading <c>v</c> and any
    /// pre-release/build suffix are ignored for comparison.
    /// </param>
    /// <returns>
    /// A result describing the latest release and whether it is newer, or <c>null</c> if the check
    /// could not be completed.
    /// </returns>
    Task<UpdateCheckResult?> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken = default);
}
