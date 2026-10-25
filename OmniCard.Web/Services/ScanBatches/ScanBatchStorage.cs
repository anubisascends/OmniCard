using System.Text.Json;
using OmniCard.Shared.Settings;

namespace OmniCard.Web.Services.ScanBatches;

/// <summary>Where batch images live on disk (<c>{data}/scan-batches/{batchId}/</c>) and how batch JSON
/// columns are serialized. Shared by the ingestor, processor and <see cref="ScanBatchService"/>.</summary>
public sealed class ScanBatchStorage(IDataPathService dataPaths)
{
    public const string FolderName = "scan-batches";

    /// <summary>Camel-case web JSON, so the stored match round-trips exactly as the SPA sees it.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Root => Path.Combine(dataPaths.DataDirectory, FolderName);

    public string BatchDirectory(int batchId) => Path.Combine(Root, batchId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public string ItemPath(int batchId, string fileName) => Path.Combine(BatchDirectory(batchId), fileName);

    /// <summary>Delete a batch's directory; best-effort (a locked file is retried by the next purge).</summary>
    public void DeleteBatchDirectory(int batchId)
    {
        try
        {
            var dir = BatchDirectory(batchId);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Delete one stored file; best-effort.</summary>
    public void DeleteFile(int batchId, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        try { File.Delete(ItemPath(batchId, fileName)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Delete a closed batch's original files from the watched folder's <c>_processed</c>
    /// subfolder, then any folders under <c>_processed</c> that leaves empty. Best-effort: a file that
    /// can't be deleted is logged and left. Returns how many were deleted.</summary>
    public static int DeleteOriginals(IEnumerable<string?> processedPaths, ILogger logger)
    {
        var deleted = 0;
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in processedPaths)
        {
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
                if (Path.GetDirectoryName(path) is { } dir)
                    dirs.Add(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Couldn't delete processed scan {File}", path);
            }
        }

        // Prune emptied folders, deepest first, but never _processed itself or anything above it.
        foreach (var start in dirs.OrderByDescending(d => d.Length))
        {
            for (var dir = start; dir is not null && IsUnderProcessed(dir); dir = Path.GetDirectoryName(dir))
            {
                try
                {
                    if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any())
                        break;
                    Directory.Delete(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }
            }
        }
        return deleted;
    }

    /// <summary>True for a folder strictly inside a <c>_processed</c> folder.</summary>
    private static bool IsUnderProcessed(string dir) =>
        Path.GetDirectoryName(dir) is { } parent
        && (Path.GetFileName(parent).Equals(ScanFolderIngestor.ProcessedFolderName, StringComparison.OrdinalIgnoreCase)
            || IsUnderProcessed(parent));

    public static bool IsTiff(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }
}
