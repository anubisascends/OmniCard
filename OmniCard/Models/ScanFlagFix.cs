namespace OmniCard.Models;

public class ScanFlagFix
{
    public string FixType { get; set; } = "";
    public string OriginalData { get; set; } = "";
    public string ResolvedData { get; set; } = "";
    public FlagReason OriginalFlagReason { get; set; }
    public DateTime FixedAt { get; set; } = DateTime.UtcNow;
}
