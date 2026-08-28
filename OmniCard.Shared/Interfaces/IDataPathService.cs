using System.IO;

namespace OmniCard.Interfaces;

public interface IDataPathService
{
    string DataDirectory { get; }
    string ScansDirectory { get; }
    string TempScansDirectory { get; }
    string SymbolsCacheDirectory { get; }
    // Default-implemented so existing implementers (incl. test stubs) don't have to change; the
    // real services override it, but the default keeps it consistent for any that don't.
    string ArtCacheDirectory => Path.Combine(DataDirectory, "art_cache");
    string LogsDirectory { get; }
    string TradesDirectory { get; }

    string? PendingDataDirectory { get; }
    bool IsMigrationPending { get; }

    void SetPendingDataDirectory(string path);
    void CommitMigration();
    void CancelPendingMigration();
}
