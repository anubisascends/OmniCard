using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.Binder;

/// <summary>One choice in the move-destination picker: where the moved sheet lands, expressed as an
/// insertion index into the list of the other sheets.</summary>
public sealed record MovePositionOption(int ToIndex, string Label);

/// <summary>Backs the "Move Page" dialog: the sheet being moved is fixed (the page the user grabbed);
/// this only picks the destination among the remaining sheets.</summary>
public sealed partial class MoveBinderPageViewModel : ObservableObject
{
    public MoveBinderPageViewModel(IReadOnlyList<BinderSheetInfo> sheets, int movingSheetIndex)
    {
        var moving = sheets.FirstOrDefault(s => s.SheetIndex == movingSheetIndex);
        MovingLabel = moving is { Sides: 2 }
            ? $"Moving pages {moving.FirstPage}–{moving.FirstPage + 1}"
            : moving is not null ? $"Moving page {moving.FirstPage}" : "Moving page";

        var remaining = sheets.Where(s => s.SheetIndex != movingSheetIndex).ToList();
        for (var j = 0; j < remaining.Count; j++)
        {
            var s = remaining[j];
            var label = s.Sides == 2
                ? $"Before pages {s.FirstPage}–{s.FirstPage + 1}"
                : $"Before page {s.FirstPage}";
            Positions.Add(new MovePositionOption(j, label));
        }
        Positions.Add(new MovePositionOption(remaining.Count, "To the end"));

        SelectedPosition = Positions[^1];
    }

    public string MovingLabel { get; }

    public ObservableCollection<MovePositionOption> Positions { get; } = [];

    [ObservableProperty]
    public partial MovePositionOption? SelectedPosition { get; set; }

    public int? ToResult() => SelectedPosition?.ToIndex;
}
