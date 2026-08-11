namespace OmniCard.Models;

public class ScanRestoreResult
{
    public bool Success { get; set; }
    public int ImagesExtracted { get; set; }
    public int LinkedToLots { get; set; }
    public int Orphaned { get; set; }
    public List<string> OrphanedFileNames { get; set; } = [];
    public string? ErrorMessage { get; set; }
}
