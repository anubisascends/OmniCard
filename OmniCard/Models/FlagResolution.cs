namespace OmniCard.Models;

public class FlagResolution
{
    public int Id { get; set; }
    public int CollectionCardId { get; set; }
    public CollectionCard? CollectionCard { get; set; }
    public string FlagReason { get; set; } = "";
    public string FixType { get; set; } = "";
    public string OriginalData { get; set; } = "";
    public string ResolvedData { get; set; } = "";
    public ulong ScanHash { get; set; }
    public double? Confidence { get; set; }
    public DateTime FixedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
