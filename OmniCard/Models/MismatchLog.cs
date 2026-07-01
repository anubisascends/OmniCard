namespace OmniCard.Models;

public class MismatchLog
{
    public int Id { get; set; }
    public ulong ScanHash { get; set; }
    public string? ScanImagePath { get; set; }

    // What the algorithm matched
    public string OriginalCardId { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string OriginalSetCode { get; set; } = "";
    public string OriginalNumber { get; set; } = "";
    public double OriginalConfidence { get; set; }

    // What the user corrected to
    public string CorrectedCardId { get; set; } = "";
    public string CorrectedName { get; set; } = "";
    public string CorrectedSetCode { get; set; } = "";
    public string CorrectedNumber { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
