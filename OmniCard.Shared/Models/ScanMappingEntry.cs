namespace OmniCard.Models;

/// <summary>One row of the <c>scan_mapping.json</c> file stored inside a scan archive zip,
/// linking an archived image file back to the <see cref="InventoryLot"/> it came from.</summary>
public class ScanMappingEntry
{
    public string FileName { get; set; } = "";
    public int LotId { get; set; }
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public DateTime ArchivedAt { get; set; }
}
