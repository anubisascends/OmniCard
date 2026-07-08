using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Interfaces;

namespace OmniCard.Services;

public sealed class DataPathService : IDataPathService
{
    private static readonly string DefaultDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OmniCard");

    // Legacy path for users upgrading from TCGCardScanner
    private static readonly string LegacyDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TCGCardScanner");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _configPath;
    private string _dataDirectory;
    private string? _pendingDataDirectory;

    public DataPathService(string baseDirectory)
    {
        _configPath = Path.Combine(baseDirectory, "datapath.json");
        (_dataDirectory, _pendingDataDirectory) = LoadConfig();
    }

    public string DataDirectory => _dataDirectory;
    public string ScansDirectory => Path.Combine(_dataDirectory, "scans");
    public string TempScansDirectory => Path.Combine(_dataDirectory, "temp_scans");
    public string SymbolsCacheDirectory => Path.Combine(_dataDirectory, "symbols", "sets");
    public string LogsDirectory => Path.Combine(_dataDirectory, "logs");

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
        if (!File.Exists(_configPath))
        {
            // If legacy data directory exists but new one doesn't, use legacy path
            if (Directory.Exists(LegacyDataDirectory) && !Directory.Exists(DefaultDataDirectory))
                return (LegacyDataDirectory, null);

            return (DefaultDataDirectory, null);
        }

        var json = File.ReadAllText(_configPath);
        var config = JsonSerializer.Deserialize<DataPathConfig>(json, JsonOptions);

        var dataDir = string.IsNullOrWhiteSpace(config?.DataDirectory)
            ? DefaultDataDirectory
            : config.DataDirectory;

        return (dataDir, config?.PendingDataDirectory);
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
