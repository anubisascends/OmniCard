namespace OmniCard.Shared.Settings;

/// <summary>Reads and persists the watched scan folder configuration (<see cref="ScanFolderSettings"/>).</summary>
public interface IScanFolderSettingsService
{
    /// <summary>The current settings (never null), with values clamped to their valid ranges.</summary>
    ScanFolderSettings Get();

    /// <summary>Validates and persists new settings. Returns the validation errors; nothing is saved
    /// unless the list is empty.</summary>
    IReadOnlyList<string> Save(ScanFolderSettings settings);

    /// <summary>Raised after settings are saved, so the folder watcher can reconfigure itself.</summary>
    event Action? Changed;
}
