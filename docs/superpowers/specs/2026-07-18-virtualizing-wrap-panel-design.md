# Virtualizing Wrap Panel for the Collection Card Tile View

**Date:** 2026-07-18
**Status:** Approved design, ready for implementation plan

## Problem

The collection card list ([CardListView.xaml](../../../OmniCard/Views/Root/CardListView.xaml))
is a tile layout: a `ListBox` whose `ItemsPanel` is a plain `WrapPanel` with virtualization
explicitly disabled (`VirtualizingPanel.IsVirtualizing="False"`). Incremental scroll paging
(`LoadMore`, 500-card pages) is the only bound on how many tiles get realized.

Two observed problems:

1. **General slowness / memory pressure on large sets.** With virtualization off, every
   realized tile pins a decoded `BitmapImage` alive through `Image.Source`. Scrolling to the
   bottom of a large collection (especially the "Browse Entire Collection" view) realizes
   every tile and its bitmap. The 100-item LRU image caches
   ([CardArtCache](../../../OmniCard.Imaging/CardArtCache.cs),
   [ScanImageCache](../../../OmniCard.Imaging/ScanImageCache.cs)) cannot bound memory, because
   the realized `Image` controls keep evicted bitmaps referenced.

2. **The whole list rebuilds "over and over."** `SearchCollection()` reassigns
   `CollectionSearchResults` to a brand-new `ObservableCollection` on every call
   ([CollectionViewModel.cs:443](../../../OmniCard/Views/Root/CollectionViewModel.cs)), forcing
   the `ListBox` to re-realize every tile. Returning to a card-list view re-runs the search
   unconditionally (`LoadCardList` -> `SearchCollection`), so navigation alone triggers a full
   rebuild and visible flash even when nothing changed.

These are two distinct problems. Virtualization addresses #1; a search guard addresses #2.

## Goals

- Bound realized tiles and memory regardless of collection size.
- Eliminate needless full-list rebuilds; keep genuine refreshes (search/filter/sort/mutation).
- No per-scroll "No Image" flash as tiles recycle in and out.
- Preserve all existing behavior: selection (Extended), right-click context menu,
  double-click-to-edit, Select All, incremental paging, stacked/unstacked mode.

## Non-Goals

- Hand-rolling a custom `VirtualizingPanel` / `IScrollInfo` (a mature NuGet package is used
  instead).
- Changing the tile visual design, art-resolution order, or the editor scan-vs-art flow.
- Reworking the DB query / pricing / paging pipeline beyond the redundant-search guard.

## Approach

**Part 1 — Virtualizing wrap panel (NuGet).** Use the `VirtualizingWrapPanel` package
(S. Bäumlisberger). Tiles are a fixed 160px width, which is the well-supported case. This
bounds realized tiles; the existing bitmap LRU caches then actually bound memory.

**Part 2 — "Not always refreshing" (A + C).**
- **A. Redundant-search guard:** skip `SearchCollection()` when the effective search
  parameters are unchanged and results are already loaded (kills rebuild on re-navigation).
- **C. Cache capacity bump:** raise the image-cache LRU capacity so a full screen of tiles
  plus the virtualization buffer always hits cache (no re-decode flash on scroll).

Approach **B** (in-place `ObservableCollection` mutation/diff) was rejected: with
virtualization, reassignment only re-realizes visible tiles, so B adds machinery for little
gain over A.

## Detailed Design

### 1. Dependency
Add a `VirtualizingWrapPanel` `PackageReference` to
[OmniCard.csproj](../../../OmniCard/OmniCard.csproj), alongside the existing WPF packages.

### 2. CardListView XAML ([CardListView.xaml](../../../OmniCard/Views/Root/CardListView.xaml))
- Add namespace `xmlns:vwp="clr-namespace:WpfToolkit.Controls;assembly=VirtualizingWrapPanel"`.
- Replace the `WrapPanel` ItemsPanel with `<vwp:VirtualizingWrapPanel Orientation="Horizontal"/>`.
- Remove `VirtualizingPanel.IsVirtualizing="False"`. Set on the `ListBox`:
  `VirtualizingPanel.IsVirtualizing="True"`,
  `VirtualizingPanel.VirtualizationMode="Recycling"`,
  `VirtualizingPanel.ScrollUnit="Pixel"`,
  and a cache length (`CacheLengthUnit` + `CacheLength`) that keeps a buffer of off-screen rows
  realized for smooth scrolling.
- Leave `ItemContainerStyle`, `ItemTemplate`, `TileArt` bindings, context menu, and
  double-click trigger unchanged.

### 3. TileArt under recycling ([TileArtBehavior.cs](../../../OmniCard.Controls/TileArtBehavior.cs))
No code change anticipated. The generation-token design already coalesces property sets and
ignores stale async results, which covers a recycled container being reassigned a new `Card`.
Verify during testing that recycling fires the `Card`-changed path and no stale image lingers;
the token guard is the fallback if it does not.

### 4. Redundant-search guard ([CollectionViewModel.cs](../../../OmniCard/Views/Root/CollectionViewModel.cs))
- Represent the effective search parameters (query, game, container filter, sort preset, filter
  preset, stacked) as a comparable value. The `_last*` fields (~line 363) already cache most of
  these for `LoadMore`.
- At the top of the card-list branch of `SearchCollection()`, if the newly-computed parameters
  equal the last successful search's parameters **and** results are already loaded, return early
  without re-querying or reassigning `CollectionSearchResults`.
- Provide a force-refresh path (explicit parameter or bypass flag) so data-mutating operations
  still refresh: bulk delete, move-to-location, condition/foil changes, stacking toggle, and any
  edit that returns to the list. Audit callers so every mutation forces refresh and only pure
  navigation is guarded.

### 5. Cache capacity ([CardArtCache.cs](../../../OmniCard.Imaging/CardArtCache.cs), [ScanImageCache.cs](../../../OmniCard.Imaging/ScanImageCache.cs))
Raise the default `capacity` from 100 to ~400 (a full maximized-window screen of tiles plus the
virtualization buffer). Confirm the DI construction sites so the new default (or explicit
configured value) takes effect.

### 6. Paging interplay ([CardListView.xaml.cs](../../../OmniCard/Views/Root/CardListView.xaml.cs))
Keep the existing `LoadMore` incremental paging — it bounds the *data* load (DB query + price
fetch + URI hydration), which virtualization does not. Scroll detection via
`FindVisualChild<ScrollViewer>` still works because `VirtualizingWrapPanel` scrolls through a
standard `ScrollViewer`.

## Testing / Verification

- Build and launch. Browse a large collection / "Browse Entire Collection": smooth scroll,
  bounded memory (Task Manager), no per-scroll "No Image" flash.
- Re-enter a location view: no rebuild flash and no redundant DB query (confirm via diagnostic
  log).
- Run a real search / filter / sort / stack-toggle and a bulk edit: the list *does* refresh.
- Selection (Extended), right-click context menu, double-click-to-edit, and Select All behave
  as before.

## Risks

- **Guard staleness.** If a mutating command forgets to force-refresh, the list shows stale
  data. Mitigation: audit every mutating command path.
- **Horizontal scrollbar / reflow.** With `HorizontalScrollBarVisibility="Disabled"`, verify
  the panel reflows to the available width and never surfaces a horizontal bar.
- **Recycling + attached behavior.** Confirm `TileArt` re-fires correctly on container reuse
  (see section 3).
