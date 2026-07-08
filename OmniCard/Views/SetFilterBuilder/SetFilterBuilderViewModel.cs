using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Helpers;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.SetFilterBuilder;

public partial class SetFilterBuilderViewModel : ObservableObject
{
    private readonly SetSymbolCache _symbolCache;
    private List<WpfSetFilterItem> _allAvailable = [];

    public ObservableCollection<WpfSetFilterItem> AvailableSets { get; } = [];
    public ObservableCollection<WpfSetFilterItem> SelectedSets { get; } = [];

    [ObservableProperty]
    public partial WpfSetFilterItem? SelectedAvailableItem { get; set; }

    [ObservableProperty]
    public partial WpfSetFilterItem? SelectedSelectedItem { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    public string SelectedCountText => SelectedSets.Count > 0
        ? $"Selected Sets ({SelectedSets.Count})"
        : "Selected Sets";

    public bool Confirmed { get; private set; }

    public SetFilterBuilderViewModel(SetSymbolCache symbolCache)
    {
        _symbolCache = symbolCache;
    }

    public void Initialize(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter)
    {
        _allAvailable = [];
        AvailableSets.Clear();
        SelectedSets.Clear();

        var selectedCodes = currentFilter ?? new HashSet<string>();

        foreach (var set in allSets)
        {
            var item = new WpfSetFilterItem { SetCode = set.SetCode, SetName = set.SetName };

            if (selectedCodes.Contains(set.SetCode))
                SelectedSets.Add(item);
            else
                _allAvailable.Add(item);
        }

        SearchText = "";
        RefreshAvailable();
        OnPropertyChanged(nameof(SelectedCountText));

        // Load SVG symbols async (non-blocking)
        _ = LoadSymbolsAsync([.._allAvailable, ..SelectedSets]);
    }

    private async Task LoadSymbolsAsync(List<WpfSetFilterItem> items)
    {
        foreach (var item in items)
        {
            var symbol = await _symbolCache.GetSetSymbolAsync(item.SetCode, "common");
            if (symbol != null)
                item.Symbol = symbol;
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshAvailable();

    private void RefreshAvailable()
    {
        AvailableSets.Clear();
        var search = SearchText;
        foreach (var item in _allAvailable)
        {
            if (string.IsNullOrEmpty(search)
                || item.SetName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.SetCode.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                AvailableSets.Add(item);
            }
        }
    }

    [RelayCommand]
    public void Add()
    {
        if (SelectedAvailableItem is null) return;
        var item = SelectedAvailableItem;
        _allAvailable.Remove(item);
        SelectedSets.Add(item);
        SelectedAvailableItem = null;
        RefreshAvailable();
        OnPropertyChanged(nameof(SelectedCountText));
    }

    [RelayCommand]
    public void Remove()
    {
        if (SelectedSelectedItem is null) return;
        var item = SelectedSelectedItem;
        SelectedSets.Remove(item);
        _allAvailable.Add(item);
        _allAvailable.Sort((a, b) => string.Compare(a.SetName, b.SetName, StringComparison.OrdinalIgnoreCase));
        SelectedSelectedItem = null;
        RefreshAvailable();
        OnPropertyChanged(nameof(SelectedCountText));
    }

    public void ConfirmSelection()
    {
        Confirmed = true;
    }

    public IReadOnlyList<string> GetSelectedCodes() =>
        SelectedSets.Select(s => s.SetCode).ToList();
}
