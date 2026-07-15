# Set Filter Builder Dialog

**Date:** 2026-07-02
**Status:** Approved

## Summary

Replace the inline ComboBox set filter in the scanner tab with a TextBox + builder dialog pattern. The TextBox displays and accepts a comma-separated string of set codes (e.g. `vow, dmu, tla`). A "..." button opens a two-panel builder dialog for visually selecting sets with SVG symbols.

## Toolbar Changes (ScannerTabView.xaml)

Replace the current ComboBox + clear button with:

- **TextBox** showing the current filter string (e.g. `vow, dmu, tla`). Directly editable by the user. Bound two-way to a `SetFilterText` property on the ViewModel. Placeholder text: "All Sets". When the user edits and focus leaves (or presses Enter), the filter is parsed and applied.
- **"..." button** next to the TextBox that opens the builder dialog via `DialogService`.
- **"X" clear button** stays, clearing both the TextBox and the active filter.

Manual TextBox editing: split by comma, trim, lowercase, match against known set codes, update `CardService.SelectedSetFilter`. Invalid codes are silently ignored.

## Builder Dialog — Layout

**SetFilterBuilderView** — new Window, ~700x500:

- Title: "Set Filter Builder"
- Style: Matches SortFilterBuilderView — CenterOwner, no taskbar, Material Design theme

**Two-panel layout:**

- **Left panel (Available Sets):** Search TextBox at top. Scrollable list of all sets for the active game. Each row: 16x16 SVG set symbol (Common variant via SetSymbolCache), then "Set Name (set_code)" text. Filtered by search box (name or code, case-insensitive). Single-click to select, double-click or ">>" button to add to right panel.
- **Center buttons:** ">>" (add selected) and "<<" (remove selected).
- **Right panel (Selected Sets):** Header showing count. Scrollable list of selected sets with same row template. Double-click or "<<" button to remove.
- **Footer:** OK and Cancel buttons. OK returns selected set codes. Cancel discards changes.

When opened, right panel is pre-populated with any currently active set filter codes.

## Data Flow & Integration

### New Files

- `Views/SetFilterBuilder/SetFilterBuilderView.xaml` — the dialog Window
- `Views/SetFilterBuilder/SetFilterBuilderView.xaml.cs` — code-behind (minimal)
- `Views/SetFilterBuilder/SetFilterBuilderViewModel.cs` — dialog logic

### SetFilterItem Model

New file: `Models/SetFilterItem.cs`. Simple display model (ObservableObject for async Symbol binding):

- `string SetCode`
- `string SetName`
- `string DisplayName` — "Set Name (set_code)"
- `DrawingImage? Symbol` — SVG icon, loaded async via `SetSymbolCache.GetSetSymbolAsync(setCode, "common")`

### ViewModel Properties

- `ObservableCollection<SetFilterItem> AvailableSets` — left panel (filtered)
- `ObservableCollection<SetFilterItem> SelectedSets` — right panel
- `string SearchText` — filters AvailableSets
- Commands: `AddCommand`, `RemoveCommand`, `OkCommand`, `CancelCommand`

### Flow

1. User clicks "..." button -> `DialogService.OpenSetFilterBuilder(currentCodes)` called
2. Dialog `Initialize(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter)` — populates both panels
3. SVG symbols loaded async as dialog opens (non-blocking — items appear immediately, icons fill in)
4. User adds/removes sets between panels
5. OK -> returns `IReadOnlyList<string>` of selected set codes
6. `RootViewModel` receives codes -> updates `SetFilterText` -> parses and applies to `CardService.SelectedSetFilter`

### DI Registration

`SetFilterBuilderView` and `SetFilterBuilderViewModel` registered as transient services, same pattern as `SortFilterBuilderView`.

## What Gets Removed

- **CheckableSetInfo model** (`Models/CheckableSetInfo.cs`) — replaced by SetFilterItem
- **ComboBox in ScannerTabView.xaml** (lines 21-53) — replaced by TextBox + button
- **ComboBox event handlers in ScannerTabView.xaml.cs** (`SetFilterComboBox_DropDownOpened`, `SetFilterComboBox_DropDownClosed`)
- **RootViewModel properties/methods** supporting the old ComboBox:
  - `FilteredSets` ObservableCollection
  - `SetSearchText` property
  - `IsSetFilterOpen` property
  - `RefreshFilteredSets()` method
  - `OnCheckableSetChanged()` handler
  - `SelectAllVisibleSetsCommand`
  - `SetFilterSummary` — replaced by `SetFilterText`

## What Stays

- `RootViewModel.LoadGameSets()` — still needed, simplified (no more CheckableSetInfo creation/subscription)
- `RootViewModel.UpdateSetFilter()` — refactored to parse from the text string
- `RootViewModel.ClearSetFilter()` — simplified to clear the TextBox
- `SetInfo` record — unchanged
- All `CardService.SelectedSetFilter` usage — unchanged
- All `SetSymbolCache` infrastructure — reused by the builder
