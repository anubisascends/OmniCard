# Sidebar Navigation — Design

**Date:** 2026-07-22
**Status:** Approved (design), pending implementation plan
**Author:** Andrew Riebe

## Problem

The main window uses a top `TabControl` (Home, Collection, Scanner, Dashboard, Sales) to switch
between primary views. The tab strip is a poor UX. Replace it with a collapsible **left sidebar**
that acts as the primary navigation.

## Goals

- Left sidebar navigation with an icon + text label per view.
- Collapsible: a toggle shrinks the sidebar to an icon-only rail and back.
- Selected item is visually highlighted; hover gives subtle feedback.
- No regression to existing tab-switching behavior, keybindings, or per-view refresh logic.
- Remember the expanded/collapsed state across restarts.

## Non-Goals

- No change to the menu bar, the Game-selector toolbar, or the status bar.
- No reordering, adding, or removing of the five views.
- No new MVVM navigation framework — reuse the existing index-driven switching.

## Approach (chosen: A — restyle the existing `TabControl`)

Keep the `TabControl` in [RootView.xaml](../../../OmniCard/Views/Root/RootView.xaml) and set
`TabStripPlacement="Left"` so its header strip becomes the left sidebar. Restyle `TabItem` via a
custom `ControlTemplate` so each header renders as a horizontal `[icon] [label]` button.

This preserves everything that currently works:

- The `SelectedIndex="{Binding ViewModel.SelectedTabIndex}"` two-way binding is untouched, so
  `OnSelectedTabIndexChanged` ([RootViewModel.cs:1161](../../../OmniCard/Views/Root/RootViewModel.cs#L1161))
  and the programmatic jump to Scanner (`SelectedTabIndex = 2`,
  [RootViewModel.cs:1755](../../../OmniCard/Views/Root/RootViewModel.cs#L1755)) keep working with the
  same integer indices.
- `TabControl` keeps all five view instances alive in the visual tree (only the active one is
  visible), so no view state is lost and there is no re-creation cost on switch — identical to
  today's behavior.

Rejected alternatives:

- **B — `ListBox` + `ContentControl`:** "textbook" MVVM nav, but recreates views on each switch
  (lost state, added cost) unless caching is added; larger VM restructure and more risk.
- **C — custom sidebar + hidden tab strip:** full styling freedom but keeps two controls in sync
  redundantly. Approach A gives enough styling control without the duplication.

## Layout

The outer `Grid` row structure is unchanged (menu / toolbar / content / status bar). The sidebar
lives inside the content row as the `TabControl`'s left strip:

```
+------------------------------------------+
| Menu bar (File Edit View ...)            |  row 0 (unchanged)
+------+-----------------------------------+
| ☰    | Toolbar (Game selector)           |  row 1 toolbar stays right of sidebar
| 🏠   +-----------------------------------+
| 📚   |                                   |
| 📷   |   active view content             |  row 2
| 📊   |                                   |
| 🛒   |                                   |
+------+-----------------------------------+
| Status bar                               |  row 3 (unchanged)
+------------------------------------------+
```

The menu bar remains full-width across the top; the sidebar spans from below the menu down to the
status bar.

## Sidebar items

Custom `TabItem` template: a horizontal `StackPanel` with a `materialDesign:PackIcon` followed by a
`TextBlock` label. MaterialDesign `PackIcon` is already in use in the codebase
([HomeTabView.xaml:101](../../../OmniCard/Views/Root/HomeTabView.xaml#L101)).

| View       | Index | Icon (`PackIcon.Kind`) |
|------------|-------|------------------------|
| Home       | 0     | `Home`                 |
| Collection | 1     | `ViewGridOutline`      |
| Scanner    | 2     | `Camera`               |
| Dashboard  | 3     | `ChartBox`             |
| Sales      | 4     | `CartOutline`          |

Icon `Kind` values are subject to availability in the installed MaterialDesignThemes version;
implementation will pick the closest available equivalent if any differ.

Visual states (all via theme `DynamicResource` brushes so light/dark both work):

- **Selected:** accent background bar + accent foreground on icon and label.
- **Hover (unselected):** subtle background highlight.
- **Normal:** transparent background, default foreground.

## Collapse behavior

- New bool `IsSidebarExpanded` on `RootViewModel` as an `[ObservableProperty]`, default `true`,
  initialized from `displaySettings.Value.SidebarExpanded` (matching the existing pattern, e.g.
  `ShowScannerUI` at [RootViewModel.cs:326](../../../OmniCard/Views/Root/RootViewModel.cs#L326)).
- A hamburger/`Menu` toggle button pinned at the top of the sidebar flips `IsSidebarExpanded`.
- Expanded: sidebar ≈ 180px, labels visible. Collapsed: sidebar ≈ 48px icon rail, labels hidden.
  The label `TextBlock.Visibility` binds to `IsSidebarExpanded` via a bool→visibility converter
  (the codebase already has `BoolToVisibilityConverter`).
- Because `TabStripPlacement="Left"` sizes the strip to its content, the width change follows from
  hiding the labels; a fixed collapsed/expanded width may be applied to keep the rail steady.

## Persistence

Add `SidebarExpanded` to `DisplaySettings`
([DisplaySettings.cs](../../../OmniCard.Shared/Models/DisplaySettings.cs), default `true`) and:

- Initialize `IsSidebarExpanded` from it in the VM.
- Add `partial void OnIsSidebarExpandedChanged(...) => PersistDisplaySettings();`.
- Write it in `WriteDisplaySection`
  ([RootViewModel.cs:370](../../../OmniCard/Views/Root/RootViewModel.cs#L370)) via
  `writer.WriteBoolean("SidebarExpanded", IsSidebarExpanded);`.

This mirrors the existing display-settings round-trip exactly.

## Files touched

- `OmniCard/Views/Root/RootView.xaml` — `TabStripPlacement="Left"`, custom `TabItem` style/template,
  hamburger toggle, icons + labels.
- `OmniCard/Views/Root/RootViewModel.cs` — `IsSidebarExpanded` property, change handler,
  `WriteDisplaySection` line.
- `OmniCard.Shared/Models/DisplaySettings.cs` — `SidebarExpanded` property.
- Possibly `OmniCard.Controls/Themes/AppTheme.xaml` — if the `TabItem` style is placed in shared
  resources rather than inline in `RootView.xaml`.

## Testing / verification

This is a WPF view change with no unit-testable logic beyond the persisted bool. Verification is by
running the app and confirming:

1. Sidebar appears on the left with all five items, icons + labels.
2. Clicking each item switches to the correct view; selection highlight tracks the active item.
3. Programmatic jump to Scanner (e.g. after a scan) still selects the Scanner item.
4. Home/Collection refresh-on-activate behavior still fires.
5. Toggle collapses to an icon rail and expands back.
6. Collapsed/expanded state survives an app restart.
7. Both light and dark themes render correctly.
