using OmniCard.Interfaces;

namespace OmniCard.Web.Services;

/// <summary>
/// Simple IDataPathService for the web app that uses a pre-configured data directory.
/// Migration features are not supported in the web context.
/// </summary>
public sealed class WebDataPathService(string dataDirectory) : IDataPathService
{
    public string DataDirectory => dataDirectory;
    public string ScansDirectory => Path.Combine(dataDirectory, "scans");
    public string TempScansDirectory => Path.Combine(dataDirectory, "temp_scans");
    public string SymbolsCacheDirectory => Path.Combine(dataDirectory, "symbols", "sets");
    public string ArtCacheDirectory => Path.Combine(dataDirectory, "art_cache");
    public string LogsDirectory => Path.Combine(dataDirectory, "logs");
    public string TradesDirectory => Path.Combine(dataDirectory, "trades");

    public string? PendingDataDirectory => null;
    public bool IsMigrationPending => false;

    public void SetPendingDataDirectory(string path) => throw new NotSupportedException();
    public void CommitMigration() => throw new NotSupportedException();
    public void CancelPendingMigration() => throw new NotSupportedException();
}
