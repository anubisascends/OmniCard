namespace OmniCard.Models;

public class MissingCard
{
    public string Name { get; init; } = "";
    public string CollectorNumber { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string? ImageUri { get; init; }
    public string? LocalImagePath { get; init; }
    public string? TypeLine { get; init; }
    public string? ManaCost { get; init; }
    public string? OracleText { get; init; }
    public string? Power { get; init; }
    public string? Toughness { get; init; }
    public string? Artist { get; init; }
    public string? CardColor { get; init; }
    public string? CardCost { get; init; }
}
