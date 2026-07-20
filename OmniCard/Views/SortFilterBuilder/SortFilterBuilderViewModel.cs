using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.SortFilterBuilder;

public partial class SortFilterBuilderViewModel : ObservableObject
{
    private readonly ICollectionPresetService _presetService;
    private readonly ICardService _cardService;
    private Dictionary<string, List<string>> _distinctValues = [];

    public CardGame Game { get; set; }
    public bool PresetsChanged { get; private set; }

    // Sort tab
    public ObservableCollection<SortPreset> SortPresets { get; } = [];
    public ObservableCollection<SortLevel> EditingSortLevels { get; } = [];

    [ObservableProperty]
    public partial SortPreset? SelectedSortPreset { get; set; }

    [ObservableProperty]
    public partial string SortPresetName { get; set; } = "";

    // Filter tab
    public ObservableCollection<FilterPreset> FilterPresets { get; } = [];

    [ObservableProperty]
    public partial FilterPreset? SelectedFilterPreset { get; set; }

    [ObservableProperty]
    public partial string FilterPresetName { get; set; } = "";

    [ObservableProperty]
    public partial string FilterQuery { get; set; } = "";

    // Available fields for the current game (used by Sort tab)
    public ObservableCollection<string> AvailableFields { get; } = [];

    public SortFilterBuilderViewModel(ICollectionPresetService presetService, ICardService cardService)
    {
        _presetService = presetService;
        _cardService = cardService;
    }

    public void Initialize(CardGame game)
    {
        Game = game;

        AvailableFields.Clear();
        foreach (var field in new[]
        {
            "Name", "SetName", "SetCode", "Number", "Rarity", "Color", "CardType",
            "Condition", "IsFoil", "PurchasePrice", "MarketPrice", "Quantity",
            "DateAdded", "Section", "Page", "Slot", "IsMissing",
        })
            AvailableFields.Add(field);

        // Cache distinct values for the Sort tab's "Fill" (custom order) button. Only
        // categorical fields with a manageable set of values are worth filling.
        _distinctValues = [];
        foreach (var field in new[] { "Color", "CardType", "SetName", "SetCode", "Rarity", "Condition", "IsFoil", "Section" })
            _distinctValues[field] = _cardService.GetDistinctFieldValues(field, game);

        RefreshSortPresets();
        RefreshFilterPresets();
    }

    private void RefreshSortPresets()
    {
        SortPresets.Clear();
        foreach (var p in _presetService.GetSortPresets(Game))
            SortPresets.Add(p);
    }

    private void RefreshFilterPresets()
    {
        FilterPresets.Clear();
        foreach (var p in _presetService.GetFilterPresets(Game))
            FilterPresets.Add(p);
    }

    partial void OnSelectedSortPresetChanged(SortPreset? value)
    {
        EditingSortLevels.Clear();
        if (value is not null)
        {
            SortPresetName = value.Name;
            foreach (var level in value.SortLevels)
                EditingSortLevels.Add(new SortLevel
                {
                    Field = level.Field,
                    Direction = level.Direction,
                    CustomOrder = level.CustomOrder?.ToList()
                });
        }
        else
        {
            SortPresetName = "";
        }
    }

    partial void OnSelectedFilterPresetChanged(FilterPreset? value)
    {
        FilterPresetName = value?.Name ?? "";
        FilterQuery = value?.Query ?? "";
    }

    [RelayCommand]
    public void FillSortCustomOrder(SortLevel level)
    {
        if (_distinctValues.TryGetValue(level.Field, out var values) && values.Count > 0)
        {
            level.CustomOrder = values.ToList();
            // Force UI refresh by removing and re-adding the level
            var index = EditingSortLevels.IndexOf(level);
            if (index >= 0)
            {
                EditingSortLevels.RemoveAt(index);
                EditingSortLevels.Insert(index, level);
            }
        }
    }

    [RelayCommand]
    public void AddSortLevel()
    {
        EditingSortLevels.Add(new SortLevel { Field = "Name", Direction = SortDirection.Ascending });
    }

    [RelayCommand]
    public void RemoveSortLevel(SortLevel level)
    {
        EditingSortLevels.Remove(level);
    }

    [RelayCommand]
    public void MoveSortLevelUp(SortLevel level)
    {
        var index = EditingSortLevels.IndexOf(level);
        if (index > 0)
            EditingSortLevels.Move(index, index - 1);
    }

    [RelayCommand]
    public void MoveSortLevelDown(SortLevel level)
    {
        var index = EditingSortLevels.IndexOf(level);
        if (index < EditingSortLevels.Count - 1)
            EditingSortLevels.Move(index, index + 1);
    }

    [RelayCommand]
    public void SaveSortPreset()
    {
        if (string.IsNullOrWhiteSpace(SortPresetName))
            return;

        var preset = new SortPreset
        {
            Name = SortPresetName,
            Game = Game,
            SortLevels = EditingSortLevels.ToList()
        };
        _presetService.SaveSortPreset(preset);
        PresetsChanged = true;
        RefreshSortPresets();
        SelectedSortPreset = SortPresets.FirstOrDefault(p => p.Name == preset.Name);
    }

    [RelayCommand]
    public void DeleteSortPreset()
    {
        if (SelectedSortPreset is null)
            return;

        _presetService.DeleteSortPreset(SelectedSortPreset.Name, Game);
        PresetsChanged = true;
        SelectedSortPreset = null;
        RefreshSortPresets();
    }

    [RelayCommand]
    public void SaveFilterPreset()
    {
        if (string.IsNullOrWhiteSpace(FilterPresetName))
            return;

        var preset = new FilterPreset
        {
            Name = FilterPresetName,
            Game = Game,
            Query = FilterQuery,
        };
        _presetService.SaveFilterPreset(preset);
        PresetsChanged = true;
        RefreshFilterPresets();
        SelectedFilterPreset = FilterPresets.FirstOrDefault(p => p.Name == preset.Name);
    }

    [RelayCommand]
    public void DeleteFilterPreset()
    {
        if (SelectedFilterPreset is null)
            return;

        _presetService.DeleteFilterPreset(SelectedFilterPreset.Name, Game);
        PresetsChanged = true;
        SelectedFilterPreset = null;
        RefreshFilterPresets();
    }
}
