using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.TcgOrderImport;

public sealed partial class TcgOrderImportViewModel(ITcgPlayerOrderImportService importService) : ViewModel
{
    private TcgOrderImportPreview _preview = new();

    public ObservableCollection<TcgOrderImportRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string SummaryLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool CanImport { get; set; }

    public int ImportedCount { get; private set; }

    public Action<bool>? CloseDialog { get; set; }

    public void LoadPreview(TcgOrderImportPreview preview)
    {
        _preview = preview;
        Rows.Clear();
        foreach (var row in preview.Rows)
            Rows.Add(row);

        var newCount = preview.Rows.Count(r => !r.IsDuplicateOrder);
        var dupCount = preview.Rows.Count(r => r.IsDuplicateOrder);
        SummaryLabel = $"{newCount} new order(s), {dupCount} already imported"
                       + (preview.Warnings.Count > 0 ? $", {preview.Warnings.Count} row(s) skipped" : "");
        CanImport = newCount > 0;
    }

    [RelayCommand]
    public void Import()
    {
        ImportedCount = importService.Commit(_preview);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
