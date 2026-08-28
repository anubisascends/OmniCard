namespace OmniCard.Models;

/// <summary>
/// A user-configurable lane on the Sales (Orders) kanban board. Lanes are persisted in
/// <c>sales-settings.json</c> (see <see cref="SalesSettings.WorkflowLanes"/>) and are fully
/// customizable — users can add, remove, rename, recolor and reorder them.
/// <para>
/// Each lane maps to an <see cref="Behavior"/> drawn from the fixed <see cref="OrderStatus"/>
/// vocabulary. That behavior — not the lane itself — is what drives order accounting: entering a
/// <see cref="OrderStatus.Shipped"/>-behavior lane records the sale and removes inventory; the
/// <see cref="OrderStatus.Completed"/> and <see cref="OrderStatus.Cancelled"/> behaviors are
/// terminal. Multiple lanes may share a behavior (e.g. two pre-ship lanes "New" and "Awaiting
/// Payment", both <see cref="OrderStatus.Created"/>). An <see cref="Order"/> remembers the exact
/// lane it sits in via <see cref="Order.StageKey"/>; <see cref="Order.Status"/> holds the lane's
/// behavior so every existing status-keyed code path (accounting, delete rules, analytics,
/// TCGPlayer import) keeps working unchanged.
/// </para>
/// </summary>
public class WorkflowLane
{
    /// <summary>Stable identifier stored on <see cref="Order.StageKey"/>. Never shown to the user;
    /// safe to keep constant across renames. The built-in lanes use the lowercased behavior name.</summary>
    public string Key { get; set; } = "";

    /// <summary>Display label shown as the column/strip header.</summary>
    public string Name { get; set; } = "";

    /// <summary>Accent colour (hex, e.g. <c>#2196F3</c>) used for the column header underline and
    /// each card's status stripe.</summary>
    public string Color { get; set; } = "#9E9E9E";

    /// <summary>Accounting/lifecycle semantics this lane follows. See <see cref="WorkflowLane"/>.</summary>
    public OrderStatus Behavior { get; set; } = OrderStatus.Created;

    /// <summary>Cancel-behavior lanes render as the collapsible bottom strip rather than a board
    /// column, matching the built-in "Cancelled" lane.</summary>
    public bool IsCancelLane => Behavior == OrderStatus.Cancelled;

    /// <summary>The built-in lane set — the hard-coded board as it shipped before lanes became
    /// customizable. Used as the default when no lanes have been saved yet, and to resolve the
    /// fallback lane for a legacy order that has no <see cref="Order.StageKey"/>.</summary>
    public static List<WorkflowLane> Defaults() =>
    [
        new() { Key = "created",   Name = "Created",   Color = "#2196F3", Behavior = OrderStatus.Created },
        new() { Key = "packed",    Name = "Packed",    Color = "#FF9800", Behavior = OrderStatus.Packed },
        new() { Key = "shipped",   Name = "Shipped",   Color = "#009688", Behavior = OrderStatus.Shipped },
        new() { Key = "completed", Name = "Completed", Color = "#4CAF50", Behavior = OrderStatus.Completed },
        new() { Key = "cancelled", Name = "Cancelled", Color = "#9E9E9E", Behavior = OrderStatus.Cancelled },
    ];
}
