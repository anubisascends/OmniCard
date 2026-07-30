# UX Enhancements Design — All-Games selector, set-tile drill-in, card-editor art fit

**Date:** 2026-07-26
**Status:** Approved (pending spec review)

Three independent UX enhancements to the OmniCard WPF app. Each is localized; no new services or projects.

---

## Feature 1 — "All Games" option in the Game Selector

### Goal
Add an "All Games" choice to the game selector. When active, the collection and dashboard are not filtered by game, and the Scanner is disabled (we don't want the scanner sifting across every supported game).

### Current behavior
- `RootViewModel.SelectedGame` is a non-nullable `CardGame` bound to a `ComboBox` in `OmniCard/Views/Root/RootView.xaml` (lines ~231–253), source `RootViewModel.AvailableGames` (`IReadOnlyList<CardGame>`).
- `OnSelectedGameChanged` (`RootViewModel.cs` ~492–521) propagates to `CardService.SelectedGame`, `LoadAvailableSets()`, `Collection.SetGame(value)`, `InvalidateHomeTab()`.
- `CollectionViewModel._selectedGame` (non-nullable `CardGame`) flows into `CardService.SearchCollection(query, game, …)`. The underlying `CardService.BuildFilteredQuery` already accepts a **nullable** game filter: `if (gameFilter.HasValue) cards = cards.Where(c => c.Game == gameFilter.Value)`.
- The Scanner `TabItem` (`RootView.xaml` ~282–291) is always enabled; scanner routing (`CardService.FindBestMatch`, `AddFromStream`) reads the concrete `SelectedGame`.

### Design
Represent "All Games" as `null`.

- **Selector binding:** Add `AvailableGameOptions` (`IReadOnlyList<CardGame?>`) = `[null, …AvailableGames]`, and `SelectedGameOption` (`CardGame?`). Rebind the ComboBox to these. `CardGameDisplayConverter` maps `null → "All Games"`.
- **Change handler:** On `SelectedGameOption` change:
  - **Collection filter:** `Collection.SetGame(CardGame?)`. Change `CollectionViewModel._selectedGame` and `SetGame` to `CardGame?`; pass it through to `SearchCollection` / `GetSearchCount` / overview calls. `null` = no game filter (already supported by `BuildFilteredQuery`).
  - **Scanner enablement:** Add derived `IsScannerEnabled => SelectedGameOption.HasValue`. Bind the Scanner `TabItem.IsEnabled` to it. When disabled, show a placeholder message in the scanner tab body (e.g. "Select a specific game to scan."). Because the tab is disabled, scanner routing paths are unreachable and keep using a concrete game — no null-handling needed in `FindBestMatch`/`AddFromStream`.
  - **Set filter dropdown:** Under All Games, `LoadAvailableSets()` yields nothing / the set-code filter is hidden (set codes are a per-game concept).
  - **Dashboard:** Recompute set completion across all games (see Feature 2).
  - Preserve the existing guard that blocks switching games while unconfirmed scans exist.
- **Default:** "All Games" is an option only. The app still starts on the first concrete game.

### Out of scope
- Scanning across multiple games. Scanner remains single-game and is simply disabled under All Games.

---

## Feature 2 — Click a dashboard set tile to filter the collection

### Goal
Single-clicking a set-completion tile on the dashboard shows that set's owned cards, of that tile's game, as tiles in the Collection tab — a quick set filter.

### Current behavior
- Set-completion tiles: `ListView` of `SetCompletionSummary` in `OmniCard/Views/Dashboard/DashboardView.xaml` (~518–641). Each tile carries `SetCode` and `Game`.
- `ListView.SelectedItem` → `RootViewModel.SelectedSetCompletion` → `OnSelectedSetCompletionChanged` → `ExpandSetCompletion` (lazy-loads missing cards, which are **not rendered** in this view — a no-visible-op).
- Navigation is a retemplated `TabControl`; `RootViewModel.SelectedTabIndex` (0=Dashboard, 1=Collection, 2=Scanner, 3=Sales). Precedent: `StartAudit` sets `SelectedTabIndex = 2`.
- The collection card list (`OmniCard/Views/Root/CardListView.xaml`) is already a tile grid (`VirtualizingWrapPanel` of card tiles), shown when `Collection.ShowCardList` is true.
- Query grammar: `set:<code>` → exact (case-insensitive) `SetCode` match (`CardService.BuildSetExpression`).

### Design
Repurpose the dashboard tile selection to drill into the collection.

1. Add `CollectionViewModel.BrowseSet(CardGame game, string setCode)`:
   - `ShowCardList = true` (enter card-tile mode).
   - Apply the game filter to `game` (the tile's game), independent of the global selector.
   - Set `CollectionSearchQuery = "set:" + setCode` **after** entering card-list mode (note: `ResetSearchState()` clears the query, so ordering matters).
   - Run the search (`SearchCollection`).
2. In `RootViewModel`, on set-tile selection, call `Collection.BrowseSet(summary.Game, summary.SetCode)` then set `SelectedTabIndex = 1`.

Result: owned cards from that set render in the existing `CardListView` tile grid. Works under "All Games" too — the drill-in filters to the tile's own game without changing the global selector.

### All-games dashboard
`CalculateSetCompletion` gains an all-games path: when `SelectedGameOption` is `null`, loop `AvailableGames` calling the existing per-game `CardService.CalculateSetCompletionAsync(game)` and concatenate results. Tiles already carry `Game`, so mixed-game tiles render correctly.

### Scope
- Drill-in shows **owned cards in that set** (the quick set filter), not the full set checklist.

---

## Feature 3 — Fix oversized art card in the Collection Card Editor

### Goal
When double-clicking a collection card tile, the editor shows the scan (left) and matched card art (right). The scan fits its pane correctly; the art renders too large. Make the art fit like the scan.

### Current behavior
- View: `OmniCard/Views/CollectionCardEditor/CollectionCardEditorView.xaml`. Scan pane (Grid.Column 0) and API-art pane (Grid.Column 2), each: `ScrollViewer` (scrollbars `Auto`, for pan/zoom) → `Image Stretch="Uniform"` with a `ScaleTransform` render transform.
- Root cause: a `ScrollViewer` with `Auto` scrollbars hands its child an **infinite** measure constraint, so at zoom=1 each `Image` renders at its **natural decoded size**. The scan is small (fits); the API art is decoded larger (`CollectionCardEditorViewModel` loads it with no/large `DecodePixelWidth`) and overflows the pane.

### Design
Constrain both images to fit their pane at rest, without upscaling the small scan:
- On both `ScanImageElement` and `ApiImageElement`:
  - Add `StretchDirection="DownOnly"`.
  - Bind `MaxWidth` / `MaxHeight` to the owning `ScrollViewer`'s `ActualWidth` / `ActualHeight`.
- Effect: large art shrinks uniformly to fit the pane; the small scan stays at natural size (unchanged). Existing wheel-zoom, double-click-reset, and pan behavior are untouched — they drive the `ScaleTransform`, which is independent of layout measure.

This is a symmetric, XAML-only change (`CollectionCardEditorView.xaml`, image elements ~85–93 and ~172–180).

---

## Architecture summary

| Feature | Files touched | Nature |
| --- | --- | --- |
| 1 — All Games | `RootViewModel` (selector wiring, `IsScannerEnabled`), `CollectionViewModel` (game type → `CardGame?`), `RootView.xaml` (ComboBox + Scanner `TabItem.IsEnabled`/placeholder), `CardGameDisplayConverter` | Type/wiring change |
| 2 — Set drill-in | `RootViewModel` (tile selection handler, all-games set completion), `CollectionViewModel` (`BrowseSet`) | New method + repurposed handler |
| 3 — Art fit | `CollectionCardEditorView.xaml` | XAML-only |

No new services, projects, or data-model changes.

## Testing
- **Feature 1:** Selecting "All Games" shows unfiltered collection; Scanner tab is disabled with placeholder; selecting a concrete game restores per-game filtering and re-enables the scanner. Guard against switching away from a game with pending scans still fires.
- **Feature 2:** Clicking a set tile lands on the Collection tab, tile view, filtered to that set's owned cards of the correct game. Verify under both a concrete game and "All Games."
- **Feature 3:** Open a collection card with a large API art; art fits its pane at rest matching the scan; wheel-zoom / double-click-reset / pan still work on both panes; a small scan is not upscaled.
