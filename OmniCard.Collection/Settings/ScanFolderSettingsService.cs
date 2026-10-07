using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Shared.Games;
using OmniCard.Shared.Settings;

namespace OmniCard.Collection.Settings;

/// <summary>
/// File-backed <see cref="IScanFolderSettingsService"/>, persisting to <c>scan-folder-settings.json</c>
/// in the data directory (mirrors <see cref="ScanBadgeSettingsService"/>). Save validates the folder
/// set: the watcher moves files out of these folders, so they must not overlap each other or the
/// data directory.
/// </summary>
public sealed class ScanFolderSettingsService : IScanFolderSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly string _dataDirectory;
    private readonly object _lock = new();

    public event Action? Changed;

    public ScanFolderSettingsService(IDataPathService dataPathService)
    {
        _dataDirectory = dataPathService.DataDirectory;
        _filePath = Path.Combine(_dataDirectory, "scan-folder-settings.json");
    }

    public ScanFolderSettings Get()
    {
        ScanFolderSettings settings;
        lock (_lock)
        {
            if (!File.Exists(_filePath))
                settings = new ScanFolderSettings();
            else
            {
                try
                {
                    settings = JsonSerializer.Deserialize<ScanFolderSettings>(File.ReadAllText(_filePath), JsonOptions)
                               ?? new ScanFolderSettings();
                }
                catch (JsonException)
                {
                    settings = new ScanFolderSettings();
                }
            }
        }
        Normalize(settings);
        return settings;
    }

    public IReadOnlyList<string> Save(ScanFolderSettings settings)
    {
        Normalize(settings);
        var errors = Validate(settings, _dataDirectory);
        if (errors.Count > 0)
            return errors;

        lock (_lock)
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        Changed?.Invoke();
        return [];
    }

    /// <summary>Clamps numbers into range, trims paths and drops folders with no path.</summary>
    internal static void Normalize(ScanFolderSettings s)
    {
        s.QuietPeriodSeconds = Math.Clamp(s.QuietPeriodSeconds,
            ScanFolderSettings.MinQuietPeriodSeconds, ScanFolderSettings.MaxQuietPeriodSeconds);
        s.RetentionDays = Math.Clamp(s.RetentionDays,
            ScanFolderSettings.MinRetentionDays, ScanFolderSettings.MaxRetentionDays);
        s.Folders = (s.Folders ?? [])
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Path))
            .ToList();
        foreach (var f in s.Folders)
        {
            f.Path = f.Path.Trim();
            f.Condition = string.IsNullOrWhiteSpace(f.Condition) ? "NM" : f.Condition.Trim();
            f.Language = CardLanguages.Normalize(f.Language);
            f.SetCodes = (f.SetCodes ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (f.DefaultContainerId is <= 0)
                f.DefaultContainerId = null;
        }
    }

    internal static List<string> Validate(ScanFolderSettings s, string dataDirectory)
    {
        var errors = new List<string>();

        foreach (var dup in s.Folders.GroupBy(f => f.Game).Where(g => g.Count() > 1))
            errors.Add($"{dup.Key} has more than one scan folder.");

        var full = new List<(ScanFolderConfig Folder, string Path)>();
        foreach (var f in s.Folders)
        {
            if (!Path.IsPathFullyQualified(f.Path))
            {
                errors.Add($"{f.Game}: the folder must be a full path (e.g. D:\\Scans\\{f.Game}).");
                continue;
            }
            string path;
            try
            {
                path = WithSeparator(Path.GetFullPath(f.Path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"{f.Game}: '{f.Path}' is not a valid folder path.");
                continue;
            }
            full.Add((f, path));
        }

        var dataDir = WithSeparator(Path.GetFullPath(dataDirectory));
        foreach (var (folder, path) in full)
        {
            if (IsUnder(path, dataDir) || IsUnder(dataDir, path))
                errors.Add($"{folder.Game}: the scan folder can't be inside (or contain) the OmniCard data directory.");
        }

        for (var i = 0; i < full.Count; i++)
            for (var j = i + 1; j < full.Count; j++)
            {
                if (IsUnder(full[i].Path, full[j].Path) || IsUnder(full[j].Path, full[i].Path))
                    errors.Add($"The {full[i].Folder.Game} and {full[j].Folder.Game} scan folders overlap; each game needs its own folder.");
            }

        return errors;
    }

    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or below it (both end in a separator).</summary>
    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
}
