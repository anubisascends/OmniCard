using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Serilog;

namespace OmniCard.Services;

public sealed class DataMigrationService(
    IDataPathService dataPathService,
    ILogger<DataMigrationService> logger) : IDataMigrationService
{
    // Files and directories to migrate, relative to DataDirectory
    private static readonly string[] KnownFiles = ["collection.db", "scryfall.db", "optcg.db", "collection-presets.json"];
    private static readonly string[] KnownDirectories = ["scans", "symbols", "logs", "art"];

    public Task<MigrationPlan> PrepareMigrationAsync()
    {
        var sourceDir = dataPathService.DataDirectory;
        var warnings = new List<string>();
        long totalBytes = 0;
        int fileCount = 0;

        // Count known files
        foreach (var file in KnownFiles)
        {
            var path = Path.Combine(sourceDir, file);
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                totalBytes += info.Length;
                fileCount++;
            }
        }

        // Count known directories recursively
        foreach (var dir in KnownDirectories)
        {
            var path = Path.Combine(sourceDir, dir);
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(file);
                    totalBytes += info.Length;
                    fileCount++;
                }
            }
        }

        // Check for UNC path
        var target = dataPathService.PendingDataDirectory;
        if (target is not null && target.StartsWith(@"\\"))
            warnings.Add("Target is a UNC network path. Migration may be slower and SQLite performance may be reduced.");

        // Check available space on target drive
        if (target is not null)
        {
            try
            {
                var targetRoot = Path.GetPathRoot(target);
                if (targetRoot is not null && !target.StartsWith(@"\\"))
                {
                    var driveInfo = new DriveInfo(targetRoot);
                    var requiredSpace = (long)(totalBytes * 1.1); // 10% buffer
                    if (driveInfo.AvailableFreeSpace < requiredSpace)
                    {
                        warnings.Add($"Target drive may not have enough space. Required: {requiredSpace / (1024.0 * 1024):F1} MB, Available: {driveInfo.AvailableFreeSpace / (1024.0 * 1024):F1} MB");
                    }
                }
            }
            catch
            {
                // Best effort — can't check space on some drives/shares
            }
        }

        return Task.FromResult(new MigrationPlan(totalBytes, fileCount, warnings));
    }

    public async Task<MigrationResult> ExecuteMigrationAsync(
        IProgress<MigrationProgress> progress,
        CancellationToken ct)
    {
        if (!dataPathService.IsMigrationPending)
            return new MigrationResult(false, "No pending migration.");

        var sourceDir = dataPathService.DataDirectory;
        var targetDir = dataPathService.PendingDataDirectory!;

        try
        {
            // Release all pooled SQLite connections so database files are not locked
            SqliteConnection.ClearAllPools();
            logger.LogInformation("Cleared SQLite connection pools before migration");

            // Flush and close Serilog file sink so log files are not locked
            Log.CloseAndFlush();

            Directory.CreateDirectory(targetDir);

            // Build file list: (sourceAbsolute, relativePath)
            var filesToCopy = new List<(string Source, string Relative)>();

            foreach (var file in KnownFiles)
            {
                var sourcePath = Path.Combine(sourceDir, file);
                if (File.Exists(sourcePath))
                    filesToCopy.Add((sourcePath, file));
            }

            foreach (var dir in KnownDirectories)
            {
                var sourceDirPath = Path.Combine(sourceDir, dir);
                if (!Directory.Exists(sourceDirPath))
                    continue;

                foreach (var file in Directory.EnumerateFiles(sourceDirPath, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceDir, file);
                    filesToCopy.Add((file, relative));
                }
            }

            long totalBytes = filesToCopy.Sum(f => new FileInfo(f.Source).Length);
            long bytesCopied = 0;

            // Copy phase
            for (int i = 0; i < filesToCopy.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var (source, relative) = filesToCopy[i];
                var dest = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                await CopyFileAsync(source, dest, ct);

                bytesCopied += new FileInfo(source).Length;
                progress.Report(new MigrationProgress(
                    i + 1, filesToCopy.Count, bytesCopied, totalBytes, relative));
            }

            // Verify phase
            for (int i = 0; i < filesToCopy.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var (source, relative) = filesToCopy[i];
                var dest = Path.Combine(targetDir, relative);

                var sourceHash = await ComputeHashAsync(source, ct);
                var destHash = await ComputeHashAsync(dest, ct);

                if (!sourceHash.SequenceEqual(destHash))
                {
                    throw new InvalidOperationException(
                        $"Checksum mismatch for {relative}. Migration aborted.");
                }
            }

            // SQLite integrity check for databases (skip non-SQLite files)
            foreach (var dbFile in KnownFiles.Where(f => f.EndsWith(".db")))
            {
                var destDb = Path.Combine(targetDir, dbFile);
                if (!File.Exists(destDb) || !IsSqliteFile(destDb))
                    continue;

                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={destDb}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check";
                var integrityResult = cmd.ExecuteScalar()?.ToString();
                conn.Close();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                if (integrityResult != "ok")
                {
                    throw new InvalidOperationException(
                        $"SQLite integrity check failed for {dbFile}: {integrityResult}");
                }
            }

            // Commit — swap datapath.json to point to new location
            dataPathService.CommitMigration();
            logger.LogInformation("Migration committed. New data directory: {Path}", targetDir);

            // Cleanup — delete source files (best effort)
            foreach (var (source, relative) in filesToCopy)
            {
                try { File.Delete(source); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete source file: {Path}", source);
                }
            }

            // Remove empty source directories (best effort, reverse order for depth-first)
            foreach (var dir in KnownDirectories.Reverse())
            {
                var sourceDirPath = Path.Combine(sourceDir, dir);
                if (!Directory.Exists(sourceDirPath))
                    continue;
                try { Directory.Delete(sourceDirPath, true); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete source directory: {Path}", sourceDirPath);
                }
            }

            return new MigrationResult(true, null);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Migration cancelled by user");
            CleanupPartialTarget(targetDir, sourceDir);
            return new MigrationResult(false, "Migration cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration failed");
            CleanupPartialTarget(targetDir, sourceDir);
            return new MigrationResult(false, ex.Message);
        }
        finally
        {
            // Restore Serilog file logging (points at whichever directory is now active)
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    Path.Combine(dataPathService.LogsDirectory, "tcgcardscanner-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
    }

    private static bool IsSqliteFile(string filePath)
    {
        // SQLite files start with the 16-byte magic header "SQLite format 3\000"
        Span<byte> header = stackalloc byte[16];
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 16)
                return false;
            fs.ReadExactly(header);
        }
        catch
        {
            return false;
        }

        ReadOnlySpan<byte> magic = "SQLite format 3\0"u8;
        return header.SequenceEqual(magic);
    }

    private static async Task CopyFileAsync(string source, string dest, CancellationToken ct)
    {
        const int bufferSize = 81920;
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var destStream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await sourceStream.CopyToAsync(destStream, bufferSize, ct);
    }

    private static async Task<byte[]> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return await SHA256.HashDataAsync(stream, ct);
    }

    private void CleanupPartialTarget(string targetDir, string sourceDir)
    {
        if (targetDir == sourceDir)
            return;

        try
        {
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up partial target directory: {Path}", targetDir);
        }
    }
}
