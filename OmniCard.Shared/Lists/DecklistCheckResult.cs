using OmniCard.Shared.Storage;

namespace OmniCard.Shared.Lists;

public record DecklistCardLocation(
    string ContainerName,
    int? Page,
    int? Slot,
    string? Section,
    string SetCode,
    bool IsFoil,
    bool IsExactSetMatch);

/// <summary>One owned lot (or part of a stacked lot) allocated to fill a decklist entry — the exact
/// copy to pull, and where it lives. <see cref="Quantity"/> is how many copies to take from the lot
/// (≤ the lot's quantity). Drives the printable pull list and the bulk move-to-deck-box.</summary>
public record DecklistPick(
    int LotId,
    int? ContainerId,
    string ContainerName,
    ContainerType? ContainerType,
    int? Page,
    int? Slot,
    string? Section,
    string SetCode,
    string CollectorNumber,
    bool IsFoil,
    string Condition,
    int Quantity,
    bool IsListed);

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
    string? LocalImagePath = null,
    List<DecklistPick>? Picks = null);

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
