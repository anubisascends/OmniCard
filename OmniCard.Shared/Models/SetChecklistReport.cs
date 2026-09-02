namespace OmniCard.Models;

/// <summary>The printable "want list": the cards in a set the user does NOT own, with standard
/// and foil market prices and a blank tick-box, for hunting cards away from a computer.
/// Built from a <see cref="SetChecklist"/> by filtering to unowned printings.</summary>
public class SetChecklistReport
{
    public CardGame Game { get; init; }
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";

    /// <summary>Distinct collector numbers owned, for the header summary.</summary>
    public int OwnedCount { get; init; }

    /// <summary>Total distinct collector numbers in the set.</summary>
    public int TotalCount { get; init; }

    public double CompletionPercent => TotalCount > 0 ? (double)OwnedCount / TotalCount * 100 : 0;

    /// <summary>Unowned printings, ordered by collector number.</summary>
    public IReadOnlyList<WantListRow> Rows { get; init; } = [];

    /// <summary>Whether any missing card has a distinct foil price (drives showing the Foil column).</summary>
    public bool AnyFoil { get; init; }
}

/// <summary>One line on the want list.</summary>
public record WantListRow(
    string CollectorNumber,
    string Name,
    string Rarity,
    decimal? NormalPrice,
    decimal? FoilPrice);
