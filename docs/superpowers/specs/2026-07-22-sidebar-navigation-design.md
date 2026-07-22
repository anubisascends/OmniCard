# Sidebar Navigation + Dashboard/Home Merge — Design

**Date:** 2026-07-22
**Status:** Approved (design), pending implementation plan
**Author:** Andrew Riebe

## Problem

The main window uses a top `TabControl` (Home, Collection, Scanner, Dashboard, Sales) to switch
between primary views. The tab strip is a poor UX. Two changes:

1. Replace the tab strip with a collapsible **left sidebar** that acts as the primary navigation.
2. **Merge the Home view into the Dashboard view**, make the merged Dashboard the first sidebar
   item, and select it by default.

## Goals

- Left sidebar navigation with an icon + text label per view.
- Collapsible: a toggle shrinks the sidebar to an icon-only rail and back; state remembered across
  restarts.
- Selected item highlighted; hover gives subtle feedback.
- Home's content is absorbed into a single **Dashboard** view (financials on top, collection stats
  below) with **one unified scroll** and **one Refresh** button.
- Dashboard is the first item and the default selection.
- No regression to tab-switching behavior, keybindings, per-view refresh logic, or the set-tile
  click-to-expand behavior.

## Non-Goals

- No change to the menu bar, the Game-selector toolbar, or the status bar.
- No reordering of the remaining views beyond removing Home and putting Dashboard first.
- No new MVVM navigation framework — reuse the existing index-driven switching.
- No move of Home's stats/set-completion logic out of `RootViewModel` (see Approach A below).

## View consolidation & new index scheme

Home is removed as a separate item; its content moves into the Dashboard view. New sidebar order:

| Position | View                 | Index |
|----------|----------------------|-------|
| 1        | Dashboard (+ Home)   | 0     |
| 2        | Collection           | 1     |
| 3        | Scanner              | 2     |
| 4        | Sales                | 3     |

**Why this order is low-risk:** Collection stays index 1 and Scanner stays index 2, so the
hardcoded jump `SelectedTabIndex = 2` ([RootViewModel.cs:1755](../../../OmniCard/Views/Root/RootViewModel.cs#L1755))
and the `case 1` / `case 2` switches in `SelectAllInActiveTab`
([RootView.xaml.cs:62-66](../../../OmniCard/Views/Root/RootView.xaml.cs#L62-L66)) and
`DeleteSelectedMenuItem_Click` ([RootView.xaml.cs:101-109](../../../OmniCard/Views/Root/RootView.xaml.cs#L101-L109))
all keep working unchanged. Because `SelectedTabIndex` defaults to `0`, the merged Dashboard is the
default selection for free.

## Approach for the sidebar (A — restyle the existing `TabControl`)

Keep the `TabControl` in [RootView.xaml](../../../OmniCard/Views/Root/RootView.xaml) and set
`TabStripPlacement="Left"` so its header strip becomes the left sidebar. Restyle `TabItem` via a
custom `ControlTemplate` so each header renders as a horizontal `[icon] [label]` button. Keeps the
`SelectedIndex` binding and all view instances alive (no re-creation on switch), so `RootViewModel`
logic is untouched. (Rejected: `ListBox`+`ContentControl` recreates views/loses state; custom
sidebar + hidden strip duplicates controls.)

## Approach for the merge (A — composite view, no VM logic moved)

The merged view's root keeps `RootView`'s DataContext, so Home's `ViewModel.*` bindings work as-is.
The financial content is placed under a scoped `DataContext="{Binding ViewModel.Dashboard}"` so the
existing `DashboardViewModel` bindings (`TotalCost`, `MarketByGameRows`, `Holdings.*`, …) work
unchanged. **Neither `RootViewModel` nor `DashboardViewModel` gains or loses state.** (Rejected:
moving Home's stats/set-completion into `DashboardViewModel` — large, risky diff for no user-visible
gain.)

Because `DashboardView` is used only at
[RootView.xaml:188](../../../OmniCard/Views/Root/RootView.xaml#L188), it can be modified freely to
fit inside the unified scroll.

## Layout

Outer `Grid` rows unchanged (menu / toolbar / content / status bar). The sidebar is the
`TabControl`'s left strip inside the content row:

```
+------------------------------------------+
| Menu bar (File Edit View ...)            |  row 0 (unchanged)
+------+-----------------------------------+
| ☰    | Toolbar (Game selector)           |  row 1 toolbar stays right of sidebar
| 📊   +-----------------------------------+
| 📚   |                                   |
| 📷   |   active view content             |  row 2
| 🛒   |                                   |
+------+-----------------------------------+
| Status bar                               |  row 3 (unchanged)
+------------------------------------------+
```

Merged Dashboard content (single outer `ScrollViewer`):

```
[ Refresh ]                      <- one button, refreshes both sections
--- Financials (DataContext = ViewModel.Dashboard) ---
Cost Basis | Market | Unrealized | Realized | Realized(net)
Charts + Holdings tables ...
--- Collection stats (DataContext = ViewModel) ---
Total Cards | Sets | Foils | Total Value   + rarity chips
Sets in Collection (set-completion tiles)
```

## Sidebar items

Custom `TabItem` template: horizontal `StackPanel` with a `materialDesign:PackIcon` + a `TextBlock`
label. `PackIcon` is already used ([HomeTabView.xaml:101](../../../OmniCard/Views/Root/HomeTabView.xaml#L101)).

| View       | Index | Icon (`PackIcon.Kind`) |
|------------|-------|------------------------|
| Dashboard  | 0     | `ViewDashboard`        |
| Collection | 1     | `ViewGridOutline`      |
| Scanner    | 2     | `Camera`               |
| Sales      | 3     | `CartOutline`          |

Icon `Kind` values are subject to availability in the installed MaterialDesignThemes version;
implementation picks the closest available equivalent if any differ.

Visual states (all via theme `DynamicResource` brushes so light/dark both work):

- **Selected:** accent background bar + accent foreground on icon and label.
- **Hover (unselected):** subtle background highlight.
- **Normal:** transparent background, default foreground.

## Collapse behavior

- New bool `IsSidebarExpanded` on `RootViewModel` (`[ObservableProperty]`, default `true`,
  initialized from `displaySettings.Value.SidebarExpanded`), mirroring `ShowScannerUI`
  ([RootViewModel.cs:326](../../../OmniCard/Views/Root/RootViewModel.cs#L326)).
- A hamburger toggle pinned at the top of the sidebar flips `IsSidebarExpanded`.
- Expanded ≈ 180px, labels visible. Collapsed ≈ 48px icon rail, labels hidden. Label
  `TextBlock.Visibility` binds to `IsSidebarExpanded` via the existing `BoolToVisibilityConverter`.

## Refresh & load wiring

- **Unified Refresh:** a single button at the top of the merged view invokes both the financial
  refresh (`Dashboard.RefreshCommand`/`Load`) and the collection-stats refresh
  (`RefreshHomeTabCommand` → `CalculateSetCompletion`). Implemented as a new combined command on
  `RootViewModel` (e.g. `RefreshDashboardCommand`) that calls both, or the button binds both via a
  small relay — chosen in the plan.
- **On tab activation (index 0):** the existing `MainTabControl.SelectionChanged` handler
  ([RootView.xaml.cs:33-47](../../../OmniCard/Views/Root/RootView.xaml.cs#L33-L47)) currently calls
  `Dashboard.Load()` when the Dashboard tab is selected — repoint it to the merged tab. Home's
  refresh already fires from `OnSelectedTabIndexChanged` when `value == 0`
  ([RootViewModel.cs:1161-1167](../../../OmniCard/Views/Root/RootViewModel.cs#L1161-L1167)); index 0
  is now the merged tab, so that branch stays but its comment/intent covers "Dashboard".
- **On startup:** index 0 is the default selection, so no `SelectionChanged` fires for it. Trigger
  the initial financial + stats load explicitly in `Initialize()`/`StartAsync` so the default
  Dashboard is populated on launch.

## Unified-scroll technical notes

- **Dashboard section:** remove `DashboardView`'s own outer `ScrollViewer`
  ([DashboardView.xaml:163](../../../OmniCard/Views/Dashboard/DashboardView.xaml#L163)) and its own
  Refresh toolbar ([DashboardView.xaml:65-78](../../../OmniCard/Views/Dashboard/DashboardView.xaml#L65-L78))
  so its content flows inside the merged view's single outer `ScrollViewer`. The busy indicator /
  status message can stay.
- **Set-completion list:** keep the `ListView`
  ([HomeTabView.xaml:106](../../../OmniCard/Views/Root/HomeTabView.xaml#L106)) to preserve
  `SelectedItem` → `ExpandSetCompletionCommand` click-to-expand
  ([RootViewModel.cs:1134-1137](../../../OmniCard/Views/Root/RootViewModel.cs#L1134-L1137)), but set
  `ScrollViewer.VerticalScrollBarVisibility="Disabled"` so it lays out all tiles at full height and
  the outer scroll handles paging. (Trade-off: disables list virtualization for the set tiles; the
  set count is small, so this is acceptable.)

## Files touched

- `OmniCard/Views/Root/RootView.xaml` — `TabStripPlacement="Left"`, custom `TabItem` style/template,
  hamburger toggle, icons + labels; remove the Home `TabItem`; the Dashboard `TabItem` now hosts the
  merged view.
- New merged view (e.g. `OmniCard/Views/Dashboard/DashboardView.xaml` restructured, or a new
  `DashboardHomeView`) composing financial + collection-stats sections under one scroll. Decide
  file identity in the plan; simplest is to grow `DashboardView` into the merged view and drop
  `HomeTabView` usage.
- `OmniCard/Views/Dashboard/DashboardView.xaml` — remove internal `ScrollViewer` + own Refresh
  toolbar for unified scroll.
- `OmniCard/Views/Root/HomeTabView.xaml` — content relocated into the merged view; file removed if
  fully absorbed.
- `OmniCard/Views/Root/RootView.xaml.cs` — repoint the activation `SelectionChanged` to the merged
  tab; wire initial load on startup; wire the merged view's DataContext scoping if a new view file
  is used.
- `OmniCard/Views/Root/RootViewModel.cs` — `IsSidebarExpanded` property + change handler +
  `WriteDisplaySection` line; optional combined `RefreshDashboardCommand`.
- `OmniCard.Shared/Models/DisplaySettings.cs` — `SidebarExpanded` property (default `true`).
- Possibly `OmniCard.Controls/Themes/AppTheme.xaml` — if the `TabItem` style lives in shared
  resources rather than inline.

## Persistence

Add `SidebarExpanded` (bool, default `true`) to `DisplaySettings`
([DisplaySettings.cs](../../../OmniCard.Shared/Models/DisplaySettings.cs)); initialize
`IsSidebarExpanded` from it; persist via `OnIsSidebarExpandedChanged` → `PersistDisplaySettings` and
a `writer.WriteBoolean("SidebarExpanded", IsSidebarExpanded)` in `WriteDisplaySection`
([RootViewModel.cs:370](../../../OmniCard/Views/Root/RootViewModel.cs#L370)). Mirrors the existing
display-settings round-trip exactly.

## Testing / verification

WPF view change; verification is by running the app and confirming:

1. Sidebar on the left with four items (Dashboard, Collection, Scanner, Sales), icons + labels.
2. Dashboard is selected by default on launch and shows financials on top, collection stats below,
   both populated.
3. One outer scrollbar scrolls the whole merged page; one Refresh button refreshes both sections.
4. Set-completion tiles still respond to clicks (expand/navigate).
5. Clicking each sidebar item switches views; selection highlight tracks the active item.
6. Programmatic jump to Scanner (after a scan) still selects Scanner; Collection refresh on activate
   still fires; Select-All / Delete-Selected still target the right view.
7. Toggle collapses to an icon rail and expands back; state survives an app restart.
8. Both light and dark themes render correctly.
