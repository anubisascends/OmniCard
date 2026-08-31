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
    // Default location for saved scan-session files, and the crash-recovery autosave path. Default-
    // implemented so existing implementers (incl. test stubs) don't have to change.
    string SessionsDirectory => Path.Combine(DataDirectory, "sessions");
    string ScanSessionRecoveryPath => Path.Combine(DataDirectory, "scan-session.recovery.ocss");

    string? PendingDataDirectory { get; }
    bool IsMigrationPending { get; }

    void SetPendingDataDirectory(string path);
    void CommitMigration();
    void CancelPendingMigration();
}
