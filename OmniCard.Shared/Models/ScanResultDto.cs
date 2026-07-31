namespace OmniCard.Models;

public class ScanResultDto
{
    public bool Matched { get; init; }
    public string? Name { get; init; }
    public string? SetName { get; init; }
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public string? Rarity { get; init; }
    public string? ImageUri { get; init; }
    public double? Confidence { get; init; }

    /// <summary>Decimal string of the scan's 64-bit pHash — kept as a string so it round-trips
    /// through JSON/JS without losing precision (JS numbers only safely hold 53 bits).</summary>
    public string ScanHash { get; init; } = "";

    public CardGame Game { get; init; }

    /// <summary>Set when the desktop app isn't connected or the round trip failed/timed out.
    /// The phone always has a screen to show, never a hung spinner.</summary>
    public string? Error { get; init; }
}
