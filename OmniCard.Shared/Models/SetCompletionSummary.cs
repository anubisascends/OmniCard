using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniCard.Models;

public partial class SetCompletionSummary : ObservableObject
{
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public CardGame Game { get; init; }
    public int OwnedCount { get; init; }
    public int TotalCount { get; init; }

    /// <summary>Total physical copies owned in this set (including duplicates), as opposed to
    /// <see cref="OwnedCount"/> which counts distinct card numbers toward completion.</summary>
    public int OwnedPhysicalCount { get; init; }

    public double CompletionPercent => TotalCount > 0 ? (double)OwnedCount / TotalCount * 100 : 0;
    public string CompletionGroup => OwnedCount > 0 ? "In Collection" : "Not Started";

    /// <summary>True when the set contains duplicate copies (physical &gt; distinct owned).</summary>
    public bool HasDuplicateCopies => OwnedPhysicalCount > OwnedCount;

    /// <summary>Display string for total physical copies, e.g. "143 copies"; empty when no duplicates.</summary>
    public string CopiesDisplay => HasDuplicateCopies ? $"{OwnedPhysicalCount} copies" : "";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<MissingCard>? MissingCards { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMissing { get; set; }
}
