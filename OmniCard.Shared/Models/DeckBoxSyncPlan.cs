namespace OmniCard.Models;

/// <summary>A card currently in the deck box that the target list no longer wants (or wants fewer of).
/// One row per physical lot; the user chooses to move it out or keep it as sideboard.</summary>
public record DeckBoxCutRow(
    int LotId,
    string CardName,
    string? SetCode,
    bool IsFoil,
    int Quantity,
    string? ImageUri,
    string? LocalImagePath);

/// <summary>A candidate source lot (elsewhere in the collection) that could supply copies for an Add row.</summary>
public record DeckBoxAddSource(
    int LotId,
    int ContainerId,
    string ContainerName,
    int AvailableQty,
    string? SetCode,
    bool IsFoil,
    bool IsExactMatch)
{
    /// <summary>Combobox label, e.g. "Bulk — RNA (×4)" or "Deck A — WAR (×1, foil)".</summary>
    public string Display
    {
        get
        {
            var set = string.IsNullOrWhiteSpace(SetCode) ? "" : $" — {SetCode.ToUpperInvariant()}";
            var foil = IsFoil ? ", foil" : "";
            return $"{ContainerName}{set} (×{AvailableQty}{foil})";
        }
    }
}

/// <summary>A card the target list wants that the deck box doesn't have enough of. The user picks which
/// source location to pull the needed copies from (or leaves it missing).</summary>
public record DeckBoxAddRow(
    string CardName,
    string? SetCode,
    string? CollectorNumber,
    int QuantityNeeded,
    List<DeckBoxAddSource> Sources,
    string? ImageUri,
    string? LocalImagePath);

/// <summary>The computed reconciliation between a deck box's current contents and a target decklist.</summary>
public class DeckBoxSyncPlan
{
    public required int DeckBoxId { get; init; }
    public required string DeckBoxName { get; init; }
    public required List<DeckBoxCutRow> Cuts { get; init; }
    public required List<DeckBoxAddRow> Adds { get; init; }

    /// <summary>Copies already in the box that the list keeps — no action needed for these.</summary>
    public int KeepCount { get; init; }

    public int TotalCut => Cuts.Sum(c => c.Quantity);
    public int TotalAdd => Adds.Sum(a => a.QuantityNeeded);
}

/// <summary>How the user resolved a single Cut row at commit time.</summary>
public record DeckBoxCutDecision(int LotId, int Quantity, bool Sideboard, int? DestinationContainerId);

/// <summary>How the user resolved a single Add row at commit time (which source lot supplies the copies).</summary>
public record DeckBoxAddDecision(int SourceLotId, int Quantity);

/// <summary>Everything <see cref="OmniCard.Interfaces.IDeckBoxSyncService.ApplySync"/> needs to perform the moves.</summary>
public record DeckBoxSyncCommitRequest(
    int DeckBoxId,
    List<DeckBoxCutDecision> Cuts,
    List<DeckBoxAddDecision> Adds);
