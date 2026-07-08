namespace OmniCard.Interfaces;

public interface IDataPathService
{
    string DataDirectory { get; }
    string ScansDirectory { get; }
    string TempScansDirectory { get; }
    string SymbolsCacheDirectory { get; }
    string LogsDirectory { get; }

    string? PendingDataDirectory { get; }
    bool IsMigrationPending { get; }

    void SetPendingDataDirectory(string path);
    void CommitMigration();
    void CancelPendingMigration();
}
