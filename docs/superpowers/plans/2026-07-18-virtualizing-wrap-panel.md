# Virtualizing Wrap Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the collection card tile view virtualize (bounded realized tiles + bounded memory on large collections) and stop it rebuilding/reloading the whole list when nothing changed.

**Architecture:** Swap the non-virtualized `WrapPanel` for the `VirtualizingWrapPanel` NuGet control with container recycling, so only visible tiles (plus a small buffer) realize and the existing bitmap LRU caches finally bound memory. Add a redundant-search guard in `CollectionViewModel` so navigation into an already-loaded view doesn't re-query, while every data-mutating command still forces a refresh. Bump the image-cache capacity modestly so scroll-recycling hits cache instead of re-decoding.

**Tech Stack:** WPF (.NET), MaterialDesignThemes, CommunityToolkit.Mvvm, `VirtualizingWrapPanel` (WpfToolkit.Controls), xUnit + Moq for tests.

## Global Constraints

- Target framework and language: match the existing `OmniCard` WPF project (do not change TFM or `LangVersion`).
- MVVM: `CollectionViewModel` uses CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`). Preserve the generated `SearchCollectionCommand` name — XAML/code may bind it.
- Do not change the tile visual design, art-resolution order (`CardArtCandidateResolver` / `TileArt`), or the editor scan-vs-art flow.
- Preserve all existing card-list behavior: Extended selection, right-click context menu, double-click-to-edit, `SelectAll`, incremental `LoadMore` paging, stacked/unstacked mode.
- Tests live in `OmniCard.Tests` (xUnit). Run with `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`.
- Build with `dotnet build OmniCard/OmniCard.csproj`.

---

## File Structure

- `OmniCard/OmniCard.csproj` — add the `VirtualizingWrapPanel` package reference.
- `OmniCard/Views/Root/CardListView.xaml` — swap ItemsPanel to the virtualizing panel; enable recycling/pixel-scroll.
- `OmniCard/Views/Root/CollectionViewModel.cs` — add `SearchParameters` key, refactor search into a guarded core, invalidate on navigate-back, route callers.
- `OmniCard.Imaging/CardArtCache.cs`, `OmniCard.Imaging/ScanImageCache.cs` — raise default LRU capacity 100 → 200.
- `OmniCard.Tests/Services/CardArtCacheTests.cs`, `OmniCard.Tests/Services/ScanImageCacheTests.cs` — assert new default capacity.
- `OmniCard.Tests/ViewModels/SearchParametersTests.cs` (new) — unit-test the search-key equality.

---

## Task 1: Cache default capacity bump (100 → 200)

**Why:** Once tiles virtualize, the realized set is ~115 tiles on a maximized window (≈55 visible + a 30-item buffer each side, see Task 4). A 100-item cache would evict still-realized art and re-decode on scroll. 200 covers the realized set plus churn. Per-image cost is ~1.4 MB (`DecodePixelWidth=500`, 63:88 ratio), so 200 ≈ ~280 MB per cache — the deliberate ceiling chosen over the spec's earlier "~400" (which would have doubled that).

**Files:**
- Modify: `OmniCard.Imaging/CardArtCache.cs:20`
- Modify: `OmniCard.Imaging/ScanImageCache.cs:21`
- Test: `OmniCard.Tests/Services/CardArtCacheTests.cs`, `OmniCard.Tests/Services/ScanImageCacheTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `CardArtCache` and `ScanImageCache` now default to `capacity: 200`. DI registrations in `App.xaml.cs:86-87` (`AddSingleton<...>()`) resolve the constructor's default value, so they pick up 200 with no registration change.

- [ ] **Step 1: Write the failing test for `CardArtCache` default capacity**

Add to `OmniCard.Tests/Services/CardArtCacheTests.cs`:

```csharp
[Fact]
public void DefaultCapacity_IsTwoHundred()
{
    // Construct with the default capacity (no capacity argument).
    var cache = new CardArtCache(
        NullLogger<CardArtCache>.Instance,
        new Mock<IHttpClientFactory>().Object);

    // Insert 201 distinct local images; the oldest must be evicted at 200.
    for (int i = 0; i < 201; i++)
    {
        var path = CreateTestImage(_tempDir, $"cap-{i}.png");
        Assert.NotNull(cache.GetImage(path, null));
    }

    Assert.Equal(200, cache.Count);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CardArtCacheTests.DefaultCapacity_IsTwoHundred"`
Expected: FAIL — `Assert.Equal() Failure: Expected 200, Actual 100`.

- [ ] **Step 3: Change the `CardArtCache` default capacity**

In `OmniCard.Imaging/CardArtCache.cs:20`, change the constructor signature:

```csharp
public CardArtCache(ILogger<CardArtCache> logger, IHttpClientFactory httpClientFactory, int capacity = 200)
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CardArtCacheTests.DefaultCapacity_IsTwoHundred"`
Expected: PASS.

- [ ] **Step 5: Write the failing test for `ScanImageCache` default capacity**

Add to `OmniCard.Tests/Services/ScanImageCacheTests.cs` (uses the file's existing `CreateTestImage`/temp-dir helpers — mirror the pattern already used by the capacity:2 eviction test around line 119):

```csharp
[Fact]
public void DefaultCapacity_IsTwoHundred()
{
    var dir = Path.Combine(Path.GetTempPath(), $"scancache-default-{Guid.NewGuid()}");
    Directory.CreateDirectory(dir);
    try
    {
        var cache = new ScanImageCache(
            new DataPathService(dir),
            NullLogger<ScanImageCache>.Instance);

        for (int i = 0; i < 201; i++)
        {
            var path = CreateTestImage(dir, $"cap-{i}.png");
            Assert.NotNull(cache.GetImage(path));
        }

        Assert.Equal(200, cache.Count);
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}
```

Note: if `ScanImageCacheTests` has no `CreateTestImage` helper, copy the one from `CardArtCacheTests.cs:26-35` verbatim into this test class.

- [ ] **Step 6: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ScanImageCacheTests.DefaultCapacity_IsTwoHundred"`
Expected: FAIL — Expected 200, Actual 100.

- [ ] **Step 7: Change the `ScanImageCache` default capacity**

In `OmniCard.Imaging/ScanImageCache.cs:21`:

```csharp
public ScanImageCache(IDataPathService dataPathService, ILogger<ScanImageCache> logger, int capacity = 200)
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ScanImageCacheTests.DefaultCapacity_IsTwoHundred"`
Expected: PASS.

- [ ] **Step 9: Run the full cache test suites to confirm no regressions**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CacheTests"`
Expected: PASS (all `CardArtCacheTests` and `ScanImageCacheTests`).

- [ ] **Step 10: Commit**

```bash
git add OmniCard.Imaging/CardArtCache.cs OmniCard.Imaging/ScanImageCache.cs OmniCard.Tests/Services/CardArtCacheTests.cs OmniCard.Tests/Services/ScanImageCacheTests.cs
git commit -m "perf: raise image cache default capacity to 200 for virtualized tiles"
```

---

## Task 2: Search-parameter key (`SearchParameters`)

**Why:** The redundant-search guard (Task 4) needs a value that captures everything a card-list search depends on, with correct equality: value equality for the query/game/container/stacked, reference equality for the preset objects (so an ad-hoc sort — a fresh `SortPreset` instance each call — always reads as "changed").

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (add the nested type near the `--- Card List ---` region, ~line 304)
- Test: `OmniCard/../OmniCard.Tests/ViewModels/SearchParametersTests.cs` (create)

**Interfaces:**
- Consumes: `CardGame` (enum, `OmniCard.Shared.Models`), `SortPreset`, `FilterPreset` (classes, `OmniCard.Shared.Models`).
- Produces: `CollectionViewModel.SearchParameters` — `public readonly record struct SearchParameters(string Query, CardGame Game, int? ContainerFilter, SortPreset? SortPreset, FilterPreset? FilterPreset, bool Stacked)`. Public (the test project references the app project but has no `InternalsVisibleTo`). Value equality on `Query`/`Game`/`ContainerFilter`/`Stacked`; reference equality on `SortPreset`/`FilterPreset` (plain classes, no `Equals` override). Used by Task 4.

- [ ] **Step 1: Write the failing equality test**

Create `OmniCard.Tests/ViewModels/SearchParametersTests.cs`:

```csharp
using OmniCard.Shared.Models;
using OmniCard.Views.Root;
using Xunit;

namespace OmniCard.Tests.ViewModels;

public class SearchParametersTests
{
    [Fact]
    public void Equal_WhenAllFieldsMatch_AndSamePresetInstances()
    {
        var sort = new SortPreset { Name = "A", Game = CardGame.Magic };
        var filter = new FilterPreset { Name = "F", Game = CardGame.Magic };

        var a = new CollectionViewModel.SearchParameters("goblin", CardGame.Magic, 5, sort, filter, true);
        var b = new CollectionViewModel.SearchParameters("goblin", CardGame.Magic, 5, sort, filter, true);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void NotEqual_WhenQueryDiffers()
    {
        var a = new CollectionViewModel.SearchParameters("goblin", CardGame.Magic, null, null, null, false);
        var b = new CollectionViewModel.SearchParameters("elf", CardGame.Magic, null, null, null, false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NotEqual_WhenContainerFilterDiffers()
    {
        var a = new CollectionViewModel.SearchParameters("", CardGame.Magic, 1, null, null, false);
        var b = new CollectionViewModel.SearchParameters("", CardGame.Magic, 2, null, null, false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NotEqual_WhenSortPresetIsDifferentInstance_EvenWithSameName()
    {
        // Ad-hoc sort creates a fresh SortPreset instance each search; it must read as changed.
        var s1 = new SortPreset { Name = "Ad-hoc", Game = CardGame.Magic };
        var s2 = new SortPreset { Name = "Ad-hoc", Game = CardGame.Magic };

        var a = new CollectionViewModel.SearchParameters("", CardGame.Magic, null, s1, null, false);
        var b = new CollectionViewModel.SearchParameters("", CardGame.Magic, null, s2, null, false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NotEqual_WhenStackedDiffers()
    {
        var a = new CollectionViewModel.SearchParameters("", CardGame.Magic, null, null, null, true);
        var b = new CollectionViewModel.SearchParameters("", CardGame.Magic, null, null, null, false);

        Assert.NotEqual(a, b);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~SearchParametersTests"`
Expected: FAIL to compile — `SearchParameters` does not exist on `CollectionViewModel`.

- [ ] **Step 3: Add the `SearchParameters` type**

In `OmniCard/Views/Root/CollectionViewModel.cs`, inside the `CollectionViewModel` class near the `// --- Card List ---` region (~line 304, above the `CollectionSearchResults` property), add:

```csharp
/// <summary>
/// Immutable snapshot of everything a card-list search depends on. Used to skip a
/// redundant reload when navigation re-triggers a search with identical parameters.
/// Presets compare by reference (plain classes): an ad-hoc sort builds a fresh
/// <see cref="SortPreset"/> each search, so it correctly reads as "changed".
/// </summary>
public readonly record struct SearchParameters(
    string Query,
    CardGame Game,
    int? ContainerFilter,
    SortPreset? SortPreset,
    FilterPreset? FilterPreset,
    bool Stacked);
```

Confirm the file already has `using OmniCard.Shared.Models;` (or the namespace that holds `CardGame`/`SortPreset`/`FilterPreset`); it does, since the class already references these types.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~SearchParametersTests"`
Expected: PASS (all 5 facts).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/CollectionViewModel.cs OmniCard.Tests/ViewModels/SearchParametersTests.cs
git commit -m "feat: add SearchParameters key for redundant-search guard"
```

---

## Task 3: Virtualizing wrap panel (NuGet + XAML)

**Why:** Replace the non-virtualized `WrapPanel` so only visible tiles realize. This is verified by running the app (WPF virtualization is not unit-testable).

**Files:**
- Modify: `OmniCard/OmniCard.csproj` (add package reference)
- Modify: `OmniCard/Views/Root/CardListView.xaml`

**Interfaces:**
- Consumes: nothing from other tasks. Benefits from Task 1's larger cache but does not require it.
- Produces: a virtualized tile list. No API surface for other tasks.

- [ ] **Step 1: Add the `VirtualizingWrapPanel` package**

Run: `dotnet add OmniCard/OmniCard.csproj package VirtualizingWrapPanel`
Expected: adds a `<PackageReference Include="VirtualizingWrapPanel" Version="2.*" />` line to `OmniCard/OmniCard.csproj` and restores successfully.

- [ ] **Step 2: Verify it builds with the new dependency**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded (no code uses the package yet — this just confirms restore).

- [ ] **Step 3: Add the panel namespace to CardListView.xaml**

In `OmniCard/Views/Root/CardListView.xaml`, add to the `UserControl` opening tag (after the existing `xmlns:i=` line):

```xml
             xmlns:vwp="clr-namespace:WpfToolkit.Controls;assembly=VirtualizingWrapPanel"
```

- [ ] **Step 4: Enable virtualization on the ListBox and swap the ItemsPanel**

In `OmniCard/Views/Root/CardListView.xaml`, replace the `ListBox` opening tag attribute block (lines 8-17) — change `VirtualizingPanel.IsVirtualizing="False"` to the virtualization settings below:

```xml
    <ListBox x:Name="CollectionListBox"
             ItemsSource="{Binding CollectionSearchResults}"
             SelectedItem="{Binding SelectedCollectionCard}"
             SelectionMode="Extended"
             HorizontalContentAlignment="Stretch"
             ScrollViewer.HorizontalScrollBarVisibility="Disabled"
             ScrollViewer.VerticalScrollBarVisibility="Auto"
             ScrollViewer.CanContentScroll="True"
             VirtualizingPanel.IsVirtualizing="True"
             VirtualizingPanel.VirtualizationMode="Recycling"
             VirtualizingPanel.ScrollUnit="Pixel"
             VirtualizingPanel.CacheLengthUnit="Item"
             VirtualizingPanel.CacheLength="30,30"
             SelectionChanged="CollectionListBox_SelectionChanged"
             PreviewMouseRightButtonDown="CollectionListBox_PreviewMouseRightButtonDown">
```

Then replace the ItemsPanel (lines 19-23):

```xml
        <ListBox.ItemsPanel>
            <ItemsPanelTemplate>
                <vwp:VirtualizingWrapPanel Orientation="Horizontal" SpacingMode="None"/>
            </ItemsPanelTemplate>
        </ListBox.ItemsPanel>
```

Leave the `ItemContainerStyle`, `ItemTemplate`, `ContextMenu`, and `Interaction.Triggers` blocks unchanged.

- [ ] **Step 5: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Manual verification — run the app and exercise the tile view**

Launch the app (use the `run` skill, or `dotnet run --project OmniCard/OmniCard.csproj`). Then verify, checking each:

- Open a location with many cards, and "Browse Entire Collection": tiles render in a left-aligned wrapping grid that looks the same as before (tile size, spacing, no horizontal scrollbar appears; the grid reflows when the window is resized).
- Scroll top→bottom→top on a large set: scrolling is smooth; watch Task Manager memory — it stays bounded (does not climb without limit the way it did before).
- Scroll a tile out of view and back: its art reappears without a lingering "No Image" flash (cache hit). A brief placeholder on first scroll into never-seen tiles is expected.
- Selection: single click, Ctrl+click, Shift+click select as before; the selection border shows.
- Right-click a tile: it selects that tile (unless part of a multi-selection) and the context menu opens.
- Double-click a tile: the card editor opens.
- Select All (via the existing UI/shortcut): selects the whole result set.

If any tile shows the wrong card's art after recycling (stale image), note it — Task 4 does not fix this; instead re-check `TileArtBehavior.OnChanged` fires on container reuse. The generation-token guard should already prevent it; if not, file it as a follow-up before merge.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/OmniCard.csproj OmniCard/Views/Root/CardListView.xaml
git commit -m "feat: virtualize collection tile view with VirtualizingWrapPanel"
```

---

## Task 4: Redundant-search guard (navigation + param changes)

**Why:** Kill the "whole list rebuilds when returning to / re-filtering the same view" churn. Refactor `SearchCollection` into a guarded core; navigation and pure filter/sort changes skip when the `SearchParameters` key is unchanged and results are already loaded. The user-facing `SearchCollection` command keeps forcing a refresh (Task 5 confirms mutations do too), so this task never introduces stale data.

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (search method, navigate-back, the four filter/sort/stack/container handlers, `LoadCardList`)

**Interfaces:**
- Consumes: `SearchParameters` (Task 2).
- Produces: `private Task SearchCollectionCore(bool forceRefresh)` (the real search body). `SearchCollection()` remains a parameterless `[RelayCommand]` delegating to `SearchCollectionCore(forceRefresh: true)`. Used by Task 5.

- [ ] **Step 1: Add the loaded-key field**

In `OmniCard/Views/Root/CollectionViewModel.cs`, near the existing `_last*` cached-parameter fields (~line 363), add:

```csharp
// Parameters of the results currently displayed; used to skip redundant reloads.
// Null means "nothing loaded" (initial state, or invalidated by NavigateBack).
private SearchParameters? _loadedSearch;
```

- [ ] **Step 2: Split `SearchCollection` into a guarded core**

In `OmniCard/Views/Root/CollectionViewModel.cs`, replace the method header at line 372-373:

```csharp
    [RelayCommand]
    public async Task SearchCollection()
    {
```

with a parameterless command that delegates, plus the core method header:

```csharp
    [RelayCommand]
    public Task SearchCollection() => SearchCollectionCore(forceRefresh: true);

    private async Task SearchCollectionCore(bool forceRefresh)
    {
```

The existing body (overview branch + card-list search) stays as-is except for Steps 3-4 below.

- [ ] **Step 3: Add the guard check before the DB query**

In the card-list branch of `SearchCollectionCore`, the locals `query`, `game`, `containerFilter`, `sortPreset`, `filterPreset`, `stacked` are captured at ~lines 404-407. Immediately after those captures and before the `_lastSortPreset = sortPreset;` assignments (line 408), insert:

```csharp
        var currentParams = new SearchParameters(query, game, containerFilter, sortPreset, filterPreset, stacked);
        if (!forceRefresh && _loadedSearch == currentParams)
        {
            _logger.LogDebug("SearchCollection skipped: parameters unchanged");
            return;
        }
```

- [ ] **Step 4: Record the loaded key after a successful search**

At the end of the card-list branch of `SearchCollectionCore`, after `CollectionSearchResults = displayResults;` and the `OnPropertyChanged` calls (after line 447), add:

```csharp
        _loadedSearch = currentParams;
```

- [ ] **Step 5: Invalidate the key when results are cleared on navigate-back**

In `NavigateBack()`, after `CollectionSearchResults.Clear();` (line 193), add:

```csharp
        _loadedSearch = null;
```

(This ensures re-entering the same location — whose params would otherwise match — still reloads, since the results were cleared.)

- [ ] **Step 6: Route navigation and filter/sort/stack/container changes through the guarded path**

Change these six call sites from `SearchCollection()` (which forces) to `SearchCollectionCore(forceRefresh: false)` so identical-parameter re-fires (e.g. `LoadPresets` assigning sort *and* filter back-to-back) coalesce:

- `LoadCardList()` at line 360: `_ = SearchCollectionCore(forceRefresh: false);`
- `OnIsStackedChanged` at line 354: `if (ShowCardList) _ = SearchCollectionCore(forceRefresh: false);`
- `OnSelectedSortPresetChanged` at line 547: `if (ShowCardList) _ = SearchCollectionCore(forceRefresh: false);`
- `OnSelectedFilterPresetChanged` at line 562: `if (ShowCardList) _ = SearchCollectionCore(forceRefresh: false);`
- `ApplyColumnSort` at line 594: `_ = SearchCollectionCore(forceRefresh: false);`
- `ClearAdHocSort` at line 602: `_ = SearchCollectionCore(forceRefresh: false);`
- `OnSelectedContainerFilterChanged` at line 700: `if (ShowCardList) _ = SearchCollectionCore(forceRefresh: false);`

(These are safe: each changes a field in the key — query/stacked/sort/filter/container — so the guard runs the search; the guard only *skips* the genuinely-identical duplicate.)

- [ ] **Step 7: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded.

- [ ] **Step 8: Run the full test suite (nothing should regress)**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS (existing tests + `SearchParametersTests` + cache tests).

- [ ] **Step 9: Manual verification — guard behavior**

Launch the app with debug logging visible (the app uses Serilog console/file sinks). Verify:

- Open a location, then change the sort preset and back to the same preset: the second identical selection logs `SearchCollection skipped: parameters unchanged` (or does not re-query). Changing to a *different* preset re-queries and the list updates.
- Change game while a card list is showing (triggers `LoadPresets` → sort+filter assignment): confirm it does not fire two full searches (at most one real reload; the redundant one logs "skipped").
- Navigate into a location, back to overview, and into the same location again: the list reloads and shows cards (NOT empty) — confirms the NavigateBack invalidation.
- Type a search query and search: list updates. Filter/sort/stack toggles: list updates each time.

- [ ] **Step 10: Commit**

```bash
git add OmniCard/Views/Root/CollectionViewModel.cs
git commit -m "feat: guard redundant collection searches on navigation and filter changes"
```

---

## Task 5: Force-refresh audit for mutating commands

**Why:** Every command that changes collection data leaves the search *parameters* identical but the *rows* stale. Those must force a refresh so the guard from Task 4 never hides a data change. This task is a deliberate audit: confirm each mutating caller uses the forcing path (`SearchCollection()` = `SearchCollectionCore(forceRefresh: true)`), and leave them explicitly forcing.

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (mutation command call sites, only where clarity requires)

**Interfaces:**
- Consumes: `SearchCollection()` / `SearchCollectionCore` (Task 4).
- Produces: no new API. Guarantees mutations always refresh.

- [ ] **Step 1: Audit each mutating call site**

Confirm each of the following still calls `SearchCollection()` (the forcing command) — NOT the guarded core. After Task 4 they already do (Task 4 only changed the seven navigation/filter sites). This step is to verify and, if any were changed by mistake, restore them to `SearchCollection()`:

- `OpenManualAdd` (line 130) — card added.
- `CollectionCardDoubleClick` (line 621) — card edited.
- `MoveSelectedToLocation` (line 635) — cards moved.
- `BulkSetCollectionCondition` (line 645) — condition changed.
- `BulkSetCollectionFoil` (line 656) — foil changed.
- `BulkDeleteCollection` (line 667) — cards deleted.
- `OpenSortFilterBuilder` (line 687) — presets edited (also calls `LoadPresets`).
- `ListOnEbay` (line 748) — listing state changed.
- `EndEbayListing` (line 791) — listing state changed.

For each: the line must read `_ = SearchCollection();` (or `if (...) _ = SearchCollection();`). No change needed if already correct.

- [ ] **Step 2: Add a code comment marking the invariant**

At `OpenManualAdd` (line 130) and directly above `BulkDeleteCollection` (line 660), add a one-line comment documenting the invariant so future edits don't switch these to the guarded path:

```csharp
        // Data changed but search params are identical — force a refresh (bypass the guard).
        _ = SearchCollection();
```

Apply the comment to at least these two representative mutation sites (the rest follow the same rule).

- [ ] **Step 3: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Manual verification — mutations always refresh**

Launch the app. With a location open (so search params stay fixed), verify each still updates the visible list:

- Select cards → Set Condition (e.g. LP): tiles/rows reflect the new condition immediately.
- Select cards → Set Foil / Set Non-Foil: reflected immediately.
- Move to Location: moved cards leave the current location view.
- Delete Selected: cards disappear immediately.
- Double-click → edit a card → save: the change is reflected.
- Manual Add a card into the current location: it appears.
- (If eBay configured) List on eBay / End Listing: listing state reflected.

None of these should be silently skipped by the guard.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/CollectionViewModel.cs
git commit -m "docs: mark force-refresh invariant on mutating collection commands"
```

---

## Task 6: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: Full build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded, 0 warnings introduced by these changes.

- [ ] **Step 2: Full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: All PASS.

- [ ] **Step 3: End-to-end manual scenario on a large collection**

Use the `verify` skill. Drive: Browse Entire Collection → scroll fully → apply a filter → change sort → toggle stacked → bulk-edit a selection → navigate back and re-enter. Confirm: bounded memory, no rebuild flash on re-navigation, correct data after every mutation, no stale/wrong tile art after recycling.

- [ ] **Step 4: Final commit (if any verification fixes were needed)**

```bash
git add -A
git commit -m "test: verify virtualized tile view end-to-end"
```

---

## Self-Review Notes

- **Spec coverage:** Part 1 virtualizing panel → Task 3. Part 2A guard → Tasks 2+4. Part 2C cache bump → Task 1. TileArt-under-recycling verification → Task 3 Step 6. Paging interplay preserved (no `LoadMore` changes) → confirmed in Task 3/6 manual scroll. Force-refresh of mutations → Task 5.
- **Deviation from spec:** cache capacity set to 200 (not "~400") for the memory reason documented in Task 1.
- **Type consistency:** `SearchParameters` defined in Task 2 with the exact member list used by Task 4's `new SearchParameters(query, game, containerFilter, sortPreset, filterPreset, stacked)`. `SearchCollectionCore(bool)` / `SearchCollection()` names consistent across Tasks 4-5.
- **Not unit-testable, verified manually (by design):** WPF virtualization/XAML (Task 3), guard/force-refresh wiring in the DB-backed ViewModel (Tasks 4-5). Pure logic (`SearchParameters` equality, cache capacity) is unit-tested.
