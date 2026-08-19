using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.Binder;

/// <summary>One choice in the insert-position picker: where a new sheet would be inserted, plus the
/// 0-based sheet index to hand the service.</summary>
public sealed record InsertPositionOption(int InsertIndex, string Label);

/// <summary>Backs the "Insert Page" dialog: pick where the new sheet goes and whether it's
/// double-sided (front + back) or single-sided.</summary>
public sealed partial class InsertBinderPageViewModel : ObservableObject
{
    public InsertBinderPageViewModel(IReadOnlyList<BinderSheetInfo> sheets, int? nearPage)
    {
        foreach (var sheet in sheets)
        {
            var label = sheet.Sides == 2
                ? $"Before pages {sheet.FirstPage}–{sheet.FirstPage + 1}"
                : $"Before page {sheet.FirstPage}";
            Positions.Add(new InsertPositionOption(sheet.SheetIndex, label));
        }
        Positions.Add(new InsertPositionOption(sheets.Count, "At the end"));

        // Default to inserting before the sheet the user is currently looking at, else the end.
        var near = nearPage is int p ? sheets.FirstOrDefault(s => s.Pages.Contains(p)) : null;
        SelectedPosition = near is not null
            ? Positions.First(o => o.InsertIndex == near.SheetIndex)
            : Positions[^1];
    }

    public ObservableCollection<InsertPositionOption> Positions { get; } = [];

    [ObservableProperty]
    public partial InsertPositionOption? SelectedPosition { get; set; }

    /// <summary>True = front + back (two pages); false = single-sided (one page). Matches the
    /// Add-page default.</summary>
    [ObservableProperty]
    public partial bool DoubleSided { get; set; } = true;

    public (int InsertIndex, bool DoubleSided)? ToResult()
        => SelectedPosition is null ? null : (SelectedPosition.InsertIndex, DoubleSided);
}
