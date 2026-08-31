using System.IO;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Stores per-scanner settings profiles in <c>scanner-profiles.json</c> under the data directory,
/// keyed by a sanitized scanner name (raw TWAIN names contain spaces/colons that are invalid in
/// filenames/keys). Mirrors <see cref="ScannerSettingsService"/>'s load/save pattern.
/// </summary>
public class ScannerProfileService : IScannerProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public ScannerProfileService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "scanner-profiles.json");
    }

    public ScannerProfile GetProfile(string scannerName)
    {
        var key = KeyFor(scannerName);
        var doc = Load();
        if (doc.Profiles.TryGetValue(key, out var profile))
        {
            profile.ScannerKey = key; // keep key/name in sync with the caller's identity
            if (string.IsNullOrEmpty(profile.ScannerName)) profile.ScannerName = scannerName;
            return profile;
        }
        return new ScannerProfile { ScannerKey = key, ScannerName = scannerName };
    }

    public void SaveProfile(ScannerProfile profile)
    {
        if (string.IsNullOrEmpty(profile.ScannerKey))
            profile.ScannerKey = KeyFor(profile.ScannerName);

        var doc = Load();
        doc.Profiles[profile.ScannerKey] = profile;
        Save(doc);
    }

    /// <summary>Sanitize a raw scanner name into a filesystem/dictionary-safe key
    /// (same idiom used elsewhere for scan filenames).</summary>
    private static string KeyFor(string scannerName)
        => string.Join("_", (scannerName ?? "").Split(Path.GetInvalidFileNameChars()));

    private ScannerProfilesDocument Load()
    {
        if (!File.Exists(_filePath))
            return new ScannerProfilesDocument();

        try
        {
            return JsonSerializer.Deserialize<ScannerProfilesDocument>(File.ReadAllText(_filePath), JsonOptions)
                   ?? new ScannerProfilesDocument();
        }
        catch (JsonException)
        {
            return new ScannerProfilesDocument();
        }
    }

    private void Save(ScannerProfilesDocument doc)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(doc, JsonOptions));
}
