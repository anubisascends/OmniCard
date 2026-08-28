using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings page's "Sales Workflow" section: the customizable kanban lanes for the
/// Orders board. Users add, remove, rename, recolor, reorder (drag/drop) lanes and choose each
/// lane's behavior. Persisted via <see cref="ISalesSettingsService.SaveWorkflowLanes"/>.
/// </summary>
public partial class SalesWorkflowSettingsViewModel(ISalesSettingsService salesSettings) : ObservableObject
{
    /// <summary>Editable lanes in board order. Reordering this collection (drag/drop or the
    /// Move up/down commands) is what defines the board order on save.</summary>
    public ObservableCollection<LaneEditItem> Lanes { get; } = [];

    /// <summary>Behavior choices for the per-lane picker — the fixed accounting/lifecycle semantics
    /// a lane can follow.</summary>
    public OrderStatus[] Behaviors { get; } = Enum.GetValues<OrderStatus>();

    [ObservableProperty]
    public partial LaneEditItem? SelectedLane { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>Loads the persisted lanes (or built-in defaults). Safe to call on every activation.</summary>
    public void Load()
    {
        Lanes.Clear();
        foreach (var lane in salesSettings.GetWorkflowLanes())
            Lanes.Add(LaneEditItem.From(lane));
    }

    [RelayCommand]
    public void AddLane()
    {
        var lane = new LaneEditItem
        {
            Key = "lane-" + Guid.NewGuid().ToString("N")[..8],
            Name = "New Lane",
            Color = "#607D8B",
            Behavior = OrderStatus.Created,
        };
        Lanes.Add(lane);
        SelectedLane = lane;
    }

    [RelayCommand]
    public void RemoveLane(LaneEditItem? lane)
    {
        if (lane is null) return;
        Lanes.Remove(lane);
    }

    [RelayCommand]
    public void MoveLaneUp(LaneEditItem? lane)
    {
        if (lane is null) return;
        var i = Lanes.IndexOf(lane);
        if (i > 0) Lanes.Move(i, i - 1);
    }

    [RelayCommand]
    public void MoveLaneDown(LaneEditItem? lane)
    {
        if (lane is null) return;
        var i = Lanes.IndexOf(lane);
        if (i >= 0 && i < Lanes.Count - 1) Lanes.Move(i, i + 1);
    }

    /// <summary>Reorders a lane to a new index (drag/drop entry point from the view).</summary>
    public void MoveLane(LaneEditItem lane, int newIndex)
    {
        var oldIndex = Lanes.IndexOf(lane);
        if (oldIndex < 0) return;
        newIndex = Math.Clamp(newIndex, 0, Lanes.Count - 1);
        if (oldIndex != newIndex) Lanes.Move(oldIndex, newIndex);
    }

    [RelayCommand]
    public void Save()
    {
        if (Lanes.Count == 0) { StatusMessage = "Add at least one lane before saving."; return; }
        if (Lanes.Any(l => string.IsNullOrWhiteSpace(l.Name)))
        {
            StatusMessage = "Every lane needs a name.";
            return;
        }

        salesSettings.SaveWorkflowLanes(Lanes.Select(l => l.ToModel()));
        StatusMessage = "Saved. Reopen the Orders tab to see the new board.";
    }

    [RelayCommand]
    public void RestoreDefaults()
    {
        Lanes.Clear();
        foreach (var lane in WorkflowLane.Defaults())
            Lanes.Add(LaneEditItem.From(lane));
        StatusMessage = "Defaults restored (remember to Save).";
    }
}

/// <summary>Mutable, observable editing surface for a single <see cref="WorkflowLane"/>.</summary>
public partial class LaneEditItem : ObservableObject
{
    /// <summary>Stable identity, preserved across renames so orders keep their lane. Not user-visible.</summary>
    public string Key { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Color { get; set; } = "#9E9E9E";

    [ObservableProperty]
    public partial OrderStatus Behavior { get; set; } = OrderStatus.Created;

    public static LaneEditItem From(WorkflowLane lane) => new()
    {
        Key = lane.Key,
        Name = lane.Name,
        Color = lane.Color,
        Behavior = lane.Behavior,
    };

    public WorkflowLane ToModel() => new()
    {
        Key = Key,
        Name = Name,
        Color = Color,
        Behavior = Behavior,
    };
}
