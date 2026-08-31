namespace OmniCard.Models;

/// <summary>
/// Root of the <c>scanner-profiles.json</c> file: all per-scanner profiles keyed by their
/// sanitized scanner key. (Not linked into the host — the host receives a single resolved
/// <see cref="ScannerProfile"/> on disk via the <c>--settings</c> argument.)
/// </summary>
public sealed class ScannerProfilesDocument
{
    public Dictionary<string, ScannerProfile> Profiles { get; set; } = [];
}
