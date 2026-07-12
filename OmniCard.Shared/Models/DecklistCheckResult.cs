namespace OmniCard.Models;

public record DecklistCardLocation(
    string ContainerName,
    int? Page,
    int? Slot,
    string? Section,
    string SetCode,
    bool IsFoil,
    bool IsExactSetMatch);

public record OwnedDecklistEntry(
    string CardName,
    string? SetCode,
    string? CollectorNumber,
    int QuantityNeeded,
    List<DecklistCardLocation> Locations,
    string? TypeCategory = null,
    string? TypeLine = null,
    string? ManaCost = null,
    string? OracleText = null,
    string? Power = null,
    string? Toughness = null,
    string? Rarity = null,
    string? ImageUri = null,
    string? LocalImagePath = null);

public record MissingDecklistEntry(
    string CardName,
    string? SetCode,
    string? CollectorNumber,
    int QuantityNeeded,
    decimal? MarketPrice,
    string? TypeCategory = null,
    string? TypeLine = null,
    string? ManaCost = null,
    string? OracleText = null,
    string? Power = null,
    string? Toughness = null,
    string? Rarity = null,
    string? ImageUri = null,
    string? LocalImagePath = null);

public class DecklistCheckResult
{
    public required string DeckName { get; init; }
    public required string DeckSource { get; init; }
    public required List<OwnedDecklistEntry> OwnedEntries { get; init; }
    public required List<MissingDecklistEntry> MissingEntries { get; init; }
    public int TotalOwned => OwnedEntries.Sum(e => e.QuantityNeeded);
    public int TotalMissing => MissingEntries.Sum(e => e.QuantityNeeded);
    public int TotalCards => TotalOwned + TotalMissing;
    public decimal EstimatedCost => MissingEntries
        .Where(e => e.MarketPrice.HasValue)
        .Sum(e => e.MarketPrice!.Value * e.QuantityNeeded);
}
