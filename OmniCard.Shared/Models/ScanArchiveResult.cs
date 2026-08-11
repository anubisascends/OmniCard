namespace OmniCard.Models;

public class ScanArchiveResult
{
    public bool Success { get; set; }
    public string? ArchivePath { get; set; }
    public int ImageCount { get; set; }
    public string? ErrorMessage { get; set; }
}
