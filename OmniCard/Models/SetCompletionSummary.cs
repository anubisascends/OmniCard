using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniCard.Models;

public partial class SetCompletionSummary : ObservableObject
{
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public int OwnedCount { get; init; }
    public int TotalCount { get; init; }
    public double CompletionPercent => TotalCount > 0 ? (double)OwnedCount / TotalCount * 100 : 0;
    public string CompletionGroup => OwnedCount > 0 ? "In Collection" : "Not Started";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<MissingCard>? MissingCards { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMissing { get; set; }
}
