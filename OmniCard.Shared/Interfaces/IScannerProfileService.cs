using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>Loads and saves per-scanner settings profiles (<c>scanner-profiles.json</c>).</summary>
public interface IScannerProfileService
{
    /// <summary>Load the saved profile for a scanner by its raw source name. Returns a fresh profile
    /// (with the sanitized key set) if none is saved — never null.</summary>
    ScannerProfile GetProfile(string scannerName);

    /// <summary>Persist a profile, keyed by its sanitized <see cref="ScannerProfile.ScannerKey"/>.</summary>
    void SaveProfile(ScannerProfile profile);
}
