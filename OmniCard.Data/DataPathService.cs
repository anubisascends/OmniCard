using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Interfaces;

namespace OmniCard.Data;

public sealed class DataPathService : IDataPathService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // The OmniCard-branded default data directory, and the legacy TCGCardScanner directory a user
    // upgrading from the old app would already have. Computed from the local-app-data root so they
    // can be pointed at a temp folder in tests.
    private readonly string _defaultDataDirectory;
    private readonly string _legacyDataDirectory;

    private readonly string _configPath;
    private string _dataDirectory;
    private string? _pendingDataDirectory;

    public DataPathService(string baseDirectory)
        : this(baseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    /// <summary>
    /// Testable overload. <paramref name="localAppDataRoot"/> is the directory under which the
    /// <c>OmniCard</c> (default) and <c>TCGCardScanner</c> (legacy) data folders live.
    /// </summary>
    public DataPathService(string baseDirectory, string localAppDataRoot)
    {
        _defaultDataDirectory = Path.Combine(localAppDataRoot, "OmniCard");
        _legacyDataDirectory = Path.Combine(localAppDataRoot, "TCGCardScanner");
        _configPath = Path.Combine(baseDirectory, "datapath.json");
        (_dataDirectory, _pendingDataDirectory) = LoadConfig();
    }

    public string DataDirectory => _dataDirectory;
    public string ScansDirectory => Path.Combine(_dataDirectory, "scans");
    public string TempScansDirectory => Path.Combine(_dataDirectory, "temp_scans");
    public string SymbolsCacheDirectory => Path.Combine(_dataDirectory, "symbols", "sets");
    public string ArtCacheDirectory => Path.Combine(_dataDirectory, "art_cache");
    public string LogsDirectory => Path.Combine(_dataDirectory, "logs");
    public string TradesDirectory => Path.Combine(_dataDirectory, "trades");
    public string SessionsDirectory => Path.Combine(_dataDirectory, "sessions");
    public string ScanSessionRecoveryPath => Path.Combine(_dataDirectory, "scan-session.recovery.ocss");

    public string? PendingDataDirectory => _pendingDataDirectory;
    public bool IsMigrationPending => _pendingDataDirectory is not null;

    public void SetPendingDataDirectory(string path)
    {
        _pendingDataDirectory = path;
        SaveConfig();
    }

    public void CommitMigration()
    {
        if (_pendingDataDirectory is null)
            throw new InvalidOperationException("No pending migration to commit.");

        _dataDirectory = _pendingDataDirectory;
        _pendingDataDirectory = null;
        SaveConfig();
    }

    public void CancelPendingMigration()
    {
        _pendingDataDirectory = null;
        SaveConfig();
    }

    private (string dataDir, string? pendingDir) LoadConfig()
    {
        // A saved config means the user explicitly chose a data directory (the default or a custom
        // location, via the migration flow) — respect it and do nothing else. This covers the
        // "user put a directory somewhere on purpose" case.
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<DataPathConfig>(json, JsonOptions);

            var configuredDir = string.IsNullOrWhiteSpace(config?.DataDirectory)
                ? _defaultDataDirectory
                : config.DataDirectory;

            return (configuredDir, config?.PendingDataDirectory);
        }

        // No explicit choice yet. If a legacy TCGCardScanner data directory already exists, keep
        // using it (upgrade in place, preserving the user's existing data). Note we do NOT require
        // the OmniCard folder to be absent: it is always created early for settings, so gating on
        // its absence would wrongly hide legacy data. Otherwise fall back to the OmniCard-branded
        // default, which startup creates on demand.
        if (Directory.Exists(_legacyDataDirectory))
            return (_legacyDataDirectory, null);

        return (_defaultDataDirectory, null);
    }

    private void SaveConfig()
    {
        var config = new DataPathConfig
        {
            DataDirectory = _dataDirectory,
            PendingDataDirectory = _pendingDataDirectory,
        };

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tempPath = _configPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _configPath, overwrite: true);
    }

    private sealed class DataPathConfig
    {
        public string? DataDirectory { get; set; }
        public string? PendingDataDirectory { get; set; }
    }
}
