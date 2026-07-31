namespace OmniCard.Models;

public class ScanCorrectionDto
{
    public string ScanHash { get; init; } = "";
    public CardGame Game { get; init; }
    public string GameSpecificId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string CollectorNumber { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string? ImageUri { get; init; }
}
