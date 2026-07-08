namespace OmniCard.Interfaces;

public interface IDataMigrationService
{
    Task<MigrationPlan> PrepareMigrationAsync();
    Task<MigrationResult> ExecuteMigrationAsync(
        IProgress<MigrationProgress> progress,
        CancellationToken ct);
}

public record MigrationPlan(long TotalBytes, int FileCount, List<string> Warnings);

public record MigrationProgress(
    int FilesCompleted,
    int TotalFiles,
    long BytesCopied,
    long TotalBytes,
    string CurrentFile);

public record MigrationResult(bool Success, string? ErrorMessage);
