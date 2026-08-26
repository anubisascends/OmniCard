using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.Binder;

/// <summary>Backs the per-page "Shift Cards" dialog opened from a page header: pick a direction
/// (toward the front / back), how many pages to move by, and a scope (only this page, this page and
/// everything before it, or this page and everything after it). Used to fix an off-by-a-page
/// data-entry mistake starting from a chosen page.</summary>
public sealed partial class ShiftBinderPageViewModel : ObservableObject
{
    public ShiftBinderPageViewModel(int page) => Page = page;

    public int Page { get; }
    public string HeaderLabel => $"Shift cards from page {Page}";

    /// <summary>True = shift toward the front of the binder (lower page numbers, "left"); false =
    /// toward the back (higher page numbers, "right"). Defaults to back, the "make room here" case.</summary>
    [ObservableProperty]
    public partial bool ShiftLeft { get; set; }

    [ObservableProperty]
    public partial int PageCount { get; set; } = 1;

    // Scope radios — mutually exclusive via a shared GroupName in the XAML; two-way IsChecked
    // bindings keep exactly one of these true.
    [ObservableProperty]
    public partial bool ScopeOnlyThisPage { get; set; }

    [ObservableProperty]
    public partial bool ScopeThisAndBefore { get; set; }

    [ObservableProperty]
    public partial bool ScopeThisAndAfter { get; set; } = true;

    public BinderShiftScope Scope =>
        ScopeOnlyThisPage ? BinderShiftScope.OnlyThisPage :
        ScopeThisAndBefore ? BinderShiftScope.ThisAndBefore :
        BinderShiftScope.ThisAndAfter;

    /// <summary>Signed page delta (negative = toward the front, positive = toward the back) plus the
    /// chosen scope, or null when the count isn't a positive number.</summary>
    public (int DeltaPages, BinderShiftScope Scope)? ToResult()
        => PageCount <= 0 ? null : (ShiftLeft ? -PageCount : PageCount, Scope);
}
