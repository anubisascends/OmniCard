# Collection Tab: Remove Set Symbols & Add Overview Search

**Date:** 2026-07-07
**Status:** Approved

## Goal

Make the collection tab smoother and easier to use by removing set symbol clutter from location tiles and adding a Scryfall-syntax search that filters which locations are visible in the overview.

## Requirements

1. Remove set symbol icons from location tiles in the overview
2. Add search functionality to the overview that uses Scryfall syntax
3. Locations with one or more matching cards remain visible; all others are hidden
4. Clicking a visible tile shows all cards in that location (no filter carry-over)
5. The card list DataGrid Set column is unchanged

## Design

### 1. Remove Set Symbols from Location Tiles

**Files changed:**
- `OmniCard/Views/Root/LocationOverviewView.xaml` — Remove the `ItemsControl` (lines 87-107) that renders the set symbol `WrapPanel` at the bottom of each tile
- `OmniCard/Views/Root/CollectionViewModel.cs` — Remove the `setsByContainer` SQL query (lines 236-245) and the `SetSymbols = ...` assignment (line 289) from `LoadOverview()`
- `OmniCard/Models/LocationTileSummary.cs` — Remove the `SetSymbols` property and the `SetCodeRarity` class (both are unused elsewhere; the card list DataGrid uses `SetSymbolConverter` attached properties, not this model)

This frees vertical space on tiles and eliminates a DB query from overview loading.

### 2. Overview Search — Filtering Location Tiles

#### Behavior

- The existing toolbar search box becomes visible in both overview mode and card list mode
- In overview mode, the user types a Scryfall query and presses Enter or clicks Go
- A new `CardService` method runs a DB query to find which containers have matching cards
- The ViewModel stores the resulting container IDs and re-evaluates the tile bindings, hiding non-matching tiles
- Clearing the search and pressing Enter/Go restores all tiles
- Navigating into a tile and clicking Back clears the search (existing `ResetSearchState()` behavior)

#### Service Layer

New method on `ICardService` and `CardSevice`:

```csharp
HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter);
```

Implementation reuses the existing `BuildFilteredQuery` to apply Scryfall parsing, then:

```csharp
return cards.Select(c => c.ContainerId).Distinct().ToHashSet();
```

Single `SELECT DISTINCT ContainerId` with the Scryfall WHERE clause. Fast — `ContainerId` is indexed via the foreign key.

#### ViewModel Layer (`CollectionViewModel.cs`)

- New field: `private HashSet<int>? _matchingContainerIds`
- Modify `SearchCollectionCommand` handler: when `!ShowCardList`, call `GetMatchingContainerIds` instead of card-list search, store the result, and raise `PropertyChanged` for `GroupedLocations` and `BulkSummary`
- Update `GroupedLocations` computed property: when `_matchingContainerIds` is non-null, filter `LocationSummaries` to only include containers in the set
- Add a new computed property `IsBulkVisible` (bool): returns true when `_matchingContainerIds` is null or contains the bulk container's ID. The XAML binds the bulk tile's `Visibility` to this property
- On `NavigateBack()`, clear `_matchingContainerIds` (already resets search state)

#### UI Layer

**CollectionTabView.xaml — Toolbar visibility:**
- Change the toolbar `ToolBarPanel` visibility from requiring `ShowCardList == true` to being visible whenever `ShowSealed == false`
- Card-list-specific controls (Back button, location name, Sort/Filter preset dropdowns, Stack Duplicates checkbox, Columns button) get their own visibility binding on `ShowCardList`
- The search box and Go button are always visible within the toolbar

**LocationOverviewView.xaml — Empty state:**
- Add a "No matching locations" placeholder `TextBlock`, visible when a search is active and `GroupedLocations` is empty and `BulkSummary` is hidden
- The Bulk container tile binds visibility to whether it's in the matching set (or search is inactive)

### 3. Edge Cases

- **Empty search results:** All tiles hidden, "No matching locations" placeholder shown
- **Navigation reset:** Clicking into a tile then Back clears search, all tiles reappear
- **Blank query:** Treated as no filter — all tiles shown (matches existing behavior for card list search)

### 4. What Does Not Change

- Card list search behavior, sort/filter presets, stacking
- Card list DataGrid Set column (still shows set name + symbol)
- `SetSymbolConverter` and `SetSymbolCache` (used by the card list, untouched)
- Sealed products tab
- Stats bar (remains card-list-only)
