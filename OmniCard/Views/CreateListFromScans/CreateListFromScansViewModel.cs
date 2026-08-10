using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.CreateListFromScans;

public sealed partial class CreateListFromScansViewModel(IListService listService) : ViewModel
{
    public ObservableCollection<ScanListTargetRow> Rows { get; } = [];

    [ObservableProperty]
    public partial bool CanConfirm { get; set; }

    public Action<bool>? CloseDialog { get; set; }

    public IReadOnlyList<ScanListTargetResult>? Result { get; private set; }

    public void Load(IReadOnlyList<(CardGame Game, int Count)> groups, string defaultName)
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        Result = null;

        foreach (var (game, count) in groups)
        {
            var lists = listService.GetLists(game);
            var name = groups.Count > 1 ? $"{defaultName} ({game})" : defaultName;
            var row = new ScanListTargetRow(game, count, lists, name);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }
        UpdateCanConfirm();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanListTargetRow.HasTarget))
            UpdateCanConfirm();
    }

    private void UpdateCanConfirm() => CanConfirm = Rows.Count > 0 && Rows.All(r => r.HasTarget);

    [RelayCommand]
    private void Confirm()
    {
        if (!CanConfirm) return;
        Result = Rows.Select(r => new ScanListTargetResult(
            r.Game, r.CreateNew ? null : r.SelectedList, r.CreateNew, r.NewName.Trim())).ToList();
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(false);
}
