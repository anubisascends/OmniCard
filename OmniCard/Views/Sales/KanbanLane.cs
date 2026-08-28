using System.Collections.ObjectModel;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// A single column on the customizable Sales/Orders kanban board — the runtime pairing of a
/// persisted <see cref="WorkflowLane"/> definition with the live set of orders currently in it.
/// The board's columns are data-bound to <see cref="OrdersViewModel.Lanes"/>, so adding, removing
/// or reordering lanes in Settings reshapes the board with no XAML changes.
/// </summary>
public class KanbanLane(WorkflowLane definition)
{
    public WorkflowLane Definition { get; } = definition;

    public string Key => Definition.Key;
    public string Name => Definition.Name;
    public string Color => Definition.Color;
    public OrderStatus Behavior => Definition.Behavior;

    /// <summary>Orders currently in this lane. Bound to the column's ListBox; its
    /// <see cref="ObservableCollection{T}.Count"/> drives the header badge.</summary>
    public ObservableCollection<Order> Orders { get; } = [];
}
