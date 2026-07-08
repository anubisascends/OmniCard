namespace OmniCard.Models;

public class HashCorrection
{
    public int Id { get; set; }
    public ulong ScanHash { get; set; }
    public string CorrectCardId { get; set; } = "";
    public ulong? ArtScanHash { get; set; }
    public DateTime CreatedAt { get; set; }

    // Identifying fields for re-linking after data refresh (card IDs can change)
    public string? CardName { get; set; }
    public string? SetCode { get; set; }
    public string? CollectorNumber { get; set; }
}
