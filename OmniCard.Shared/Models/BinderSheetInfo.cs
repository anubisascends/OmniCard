namespace OmniCard.Models;

/// <summary>Describes one physical sheet of a binder for the page-management UI — remove / insert /
/// move confirmations and position pickers. A double-sided sheet spans two logical pages, a
/// single-sided sheet one.</summary>
public sealed class BinderSheetInfo
{
    /// <summary>0-based index of this sheet within the binder, in reading order.</summary>
    public int SheetIndex { get; init; }

    /// <summary>1-based logical page number of this sheet's front.</summary>
    public int FirstPage { get; init; }

    /// <summary>Usable sides on this sheet — 1 or 2.</summary>
    public int Sides { get; init; }

    /// <summary>Total number of sheets in the binder (so callers can tell if this is the last one).</summary>
    public int TotalSheets { get; init; }

    /// <summary>Cards currently placed anywhere on this sheet (front + back).</summary>
    public int CardCount { get; init; }

    /// <summary>The 1-based logical page numbers this sheet occupies (one or two).</summary>
    public IReadOnlyList<int> Pages { get; init; } = [];
}
