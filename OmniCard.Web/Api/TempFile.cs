namespace OmniCard.Web.Api;

/// <summary>
/// Bridges the desktop's file-path-based exporters (CSV, QuestPDF) to HTTP downloads: runs the
/// writer into a throwaway temp file, reads the bytes back, and deletes it. Lets the web reuse the
/// exact same exporters without changing their shared interfaces.
/// </summary>
public static class TempFile
{
    public static byte[] Produce(string extension, Action<string> writeToPath)
    {
        var path = Path.Combine(Path.GetTempPath(), $"omnicard-{Guid.NewGuid():N}{extension}");
        try
        {
            writeToPath(path);
            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
