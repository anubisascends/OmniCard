using System.Collections.ObjectModel;

namespace OmniCard.Models;

/// <summary>A full set rendered as an ownership checklist: every printing (sorted by collector
/// number) paired with how many copies the user owns. Produced by
/// <see cref="OmniCard.Interfaces.ISetChecklistService"/> for the Sets tab.</summary>
public class SetChecklist
{
    public CardGame Game { get; init; }
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public IReadOnlyList<SetChecklistCard> Cards { get; init; } = [];

    /// <summary>Distinct collector numbers owned (toward completion).</summary>
    public int OwnedCount { get; init; }

    /// <summary>Total distinct collector numbers in the set.</summary>
    public int TotalCount { get; init; }

    /// <summary>Total physical copies owned across the set (incl. duplicates).</summary>
    public int OwnedPhysicalCount { get; init; }

    public double CompletionPercent => TotalCount > 0 ? (double)OwnedCount / TotalCount * 100 : 0;

    /// <summary>e.g. "184 / 271 owned (67.9%)".</summary>
    public string CompletionText =>
        TotalCount == 0 ? "No cards in this set"
        : $"{OwnedCount} / {TotalCount} owned ({CompletionPercent:F1}%)";
}

/// <summary>One printing on the checklist: the catalog card plus the user's owned quantity and
/// prices. Wraps a <see cref="CollectionCard"/> (<see cref="Card"/>) purely so the existing
/// card-tile template can render its art/quantity without a new template.</summary>
public class SetChecklistCard
{
    /// <summary>Backing card used by the shared tile template (art, name, quantity badge).</summary>
    public CollectionCard Card { get; init; } = new();

    public string GameCardId { get; init; } = "";
    public string CollectorNumber { get; init; } = "";
    public string Name { get; init; } = "";
    public string Rarity { get; init; } = "";

    /// <summary>Copies owned of this printing (0 = missing).</summary>
    public int OwnedQuantity { get; init; }

    public bool Owned => OwnedQuantity > 0;

    /// <summary>Show a "×N" badge only when more than one copy is owned.</summary>
    public bool ShowQuantityBadge => OwnedQuantity > 1;

    public string QuantityText => $"×{OwnedQuantity}";

    public decimal? NormalPrice { get; init; }
    public decimal? FoilPrice { get; init; }
    public bool HasFoil { get; init; }
}
