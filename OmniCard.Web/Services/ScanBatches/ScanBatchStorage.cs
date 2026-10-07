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

    public static bool IsTiff(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }
}
