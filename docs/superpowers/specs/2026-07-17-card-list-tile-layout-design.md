# Card List Tile Layout — Design

**Date:** 2026-07-17
**Status:** Approved

## Goal

Replace the tabular `DataGrid` card list with a tile / wrap-panel layout. The
same control ([CardListView](../../../OmniCard/Views/Root/CardListView.xaml))
renders both the **entire collection** and a **single location's** cards, so one
change covers both views.

## Tile

Fixed-width tile (~160px) laid out in a `WrapPanel`, stacked vertically:

```
┌────────────────┐
│                │   art: fixed ~150×209 (63:88 card ratio)
│     [art]      │   placeholder card-back shown if no art
│                │
├────────────────┤
│ Sol Ring       │   Name (bold, wraps to max 2 lines, ellipsis)
│ Commander (CMR)│   SetName (SetCode)
│ $1.20          │   Market price  ${0:F2}
│ ×4             │   Qty — ONLY when Stack Duplicates is on
└────────────────┘
```

- **Name:** `Name`, bold, `TextWrapping` with max 2 lines + ellipsis.
- **Set:** `SetName (SetCode)`.
- **Market price:** bind `CollectionCard.MarketPrice` (populated per-card in
  `CollectionViewModel.SearchCollection`/`FetchBatchPrices`), `StringFormat=${0:F2}`.
- **Qty:** `×{Quantity}`, visible only when `CollectionViewModel.IsStacked` is
  true (reached via `RelativeSource AncestorType=ListBox` → `DataContext.IsStacked`).

## Art resolution (new converter)

A new `IMultiValueConverter` (in `OmniCard.Controls/Converters/RootConverters.cs`)
taking `[card, isStacked, dataDirectory]` → `ImageSource?`:

- **Not stacked:** scanned art (`ScanImagePath` via `ScanImageCache`). If none → placeholder.
- **Stacked:** downloaded art (`ImageUri` via `CardArtCache`) → fall back to the
  stack representative's scanned art → placeholder if neither.

Returns `null` when no art; the template shows an in-XAML placeholder (rounded
border + subtle icon/label, theme-aware, no new binary asset). Placeholder
visibility keys off the bound `Image.Source` being null
(`NullToVisibleConverter` on `ElementName`).

## Control choice & preserved behavior

Use a **`ListBox`** with `ItemsPanel` = `WrapPanel` (not a bare `ItemsControl`),
because it provides the behaviors to keep:

- **Multi-select** — `SelectionMode="Extended"` (Ctrl+Click / Shift+Click).
  Rubber-band drag-select is NOT native to `ListBox` (the `DataGrid` had it);
  Ctrl/Shift-click covers multi-select. Drag-box can be added later if needed.
- **Context menu** — existing menu moves to the tile `ItemContainerStyle`.
- **Double-click to edit** — `MouseDoubleClick` → `CollectionCardDoubleClickCommand`.
- **Incremental scroll-load** — code-behind still finds the inner `ScrollViewer`
  and calls `LoadMore()` near the bottom.

`SelectedItem` binds to `SelectedCollectionCard`; `SelectAll()` and
`GetSelectedCards()` map to `ListBox.SelectedItems`. A selection highlight is
applied via `ItemContainerStyle` so selected tiles read clearly.

## Cleanup / integration points

- **Code-behind** (`CardListView.xaml.cs`): remove `SyncColumnVisibility`,
  `CollectionColumnHeader_Click`, `CollectionDataGrid_LoadingRow`, the tooltip
  handlers (`Row_ToolTipOpening`), and the column/IsStacked `PropertyChanged`
  hook (IsStacked already re-runs the search). Keep selection-changed →
  `SelectedCardCount`, scroll-load wiring, `SelectAll`, `GetSelectedCards`.
- **Toolbar** (`CollectionTabView.xaml` + `.xaml.cs`): remove the **Columns**
  button + popup and `ColumnChooserLink_Click`, since there are no columns.
- Verify callers of `SelectAll` / `GetSelectedCards` / column visibility in
  `RootViewModel` / `RootView` still compile after removal.

## Removed functionality (accepted)

- Column chooser.
- Click-header sorting (sort/filter preset dropdowns in the toolbar remain).

## Tradeoffs

- A plain `WrapPanel` does not virtualize (WPF has no built-in virtualizing wrap
  panel). Incremental loading bounds the initially realized tiles, but scrolling
  to the bottom of a very large location realizes all tiles/images. Mitigation:
  bounded image decode + the existing paged loader. A custom virtualizing wrap
  panel is a possible later optimization, out of scope here. **Accepted.**
- Tile width ~160px, art ratio 63:88. Fixed size; the existing
  **View ▸ Card Preview Size** slider stays but does not affect these tiles. **Accepted.**
