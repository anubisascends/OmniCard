# UX Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an "All Games" selector option (unfiltered collection/dashboard, disabled scanner), make dashboard set tiles click-to-filter the collection, and fix the oversized matched-art image in the collection card editor.

**Architecture:** Three independent, localized changes. Feature 1 threads a nullable game selection (`null` = All Games) that the `ICardService` layer *already supports* — only the ViewModel links are non-nullable today. Feature 2 adds one `CollectionViewModel.BrowseSet` method and repurposes the dashboard tile-selection handler to navigate. Feature 3 is a symmetric XAML sizing fix. No new services or projects.

**Tech Stack:** .NET 10 (net10.0-windows), WPF, CommunityToolkit.Mvvm (`[ObservableProperty]` source generators), MaterialDesignThemes. Tests: xUnit 2.9 + Moq + Xunit.StaFact in `OmniCard.Tests`.

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`. Do not change TFMs or package versions.
- MVVM: observable properties use `[ObservableProperty] public partial T Name { get; set; }`; change hooks are `partial void OnNameChanged(T value)` / `OnNameChanging(T value)`. Commands use `[RelayCommand]`.
- Async VM test determinism: fire-and-forget + `Task.Run` in ViewModels flakes tests that use `Task.Yield`. Signal completion from the Moq mock via a `TaskCompletionSource` and `await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5))` — follow the existing pattern in `OmniCard.Tests/ViewModels/CollectionViewModelTests.cs`.
- Copy rule (org): render "Innergy" as "INNERGY" and "DESIGN" as "ENGINEERING" in any user-facing copy. (No such strings appear in this plan's UI text, but honor it for any new copy.)
- Commit after each task. Run `dotnet build OmniCard.sln` and `dotnet test OmniCard.Tests` before committing a task that changes code with tests.
- `docs/` is gitignored in this repo; the plan/spec files are not committed. Do not `git add -f` them.

## File Structure

| File | Responsibility | Change |
| --- | --- | --- |
| `OmniCard.Shared/Interfaces/ICardService.cs` | Card service contract | Add nullable-game `CalculateSetCompletionAsync` overload + per-game `GetCurrentPrices` |
| `OmniCard.Collection/CardService.cs` | Card service impl | Implement the two new members (all-games loop; per-game price routing) |
| `OmniCard/Views/Root/CollectionViewModel.cs` | Collection tab VM | `SetGame(CardGame?)`, `GameFilter`, `_allGames`, new `BrowseSet` |
| `OmniCard/Views/Root/RootViewModel.cs` | Root/shell VM | Nullable `SelectedGame`, `AvailableGameOptions`, `IsScannerEnabled`, all-games stats, tile→collection nav |
| `OmniCard/Views/Root/RootView.xaml` | Shell layout | ComboBox rebind; Scanner `TabItem.IsEnabled` |
| `OmniCard/Views/Root/ScannerTabView.xaml` | Scanner tab body | Disabled-state placeholder overlay |
| `OmniCard.Controls/Converters/RootConverters.cs` | Display converters | `CardGameDisplayConverter` handles `null` → "All Games" |
| `OmniCard/Views/CollectionCardEditor/CollectionCardEditorView.xaml` | Card editor dialog | Fit both images to pane |
| `OmniCard.Tests/ViewModels/CollectionViewModelTests.cs` | VM tests | New tests for `SetGame(null)`, `BrowseSet` |
| `OmniCard.Tests/Services/SetCompletionTests.cs` | Service tests | New test for all-games set completion |

---

## Feature 3 first — Fix oversized matched-art image (smallest, isolated)

### Task 1: Fit both card-editor images to their pane

**Files:**
- Modify: `OmniCard/Views/CollectionCardEditor/CollectionCardEditorView.xaml` (scan image ~85–93, api image ~172–180)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing (XAML-only visual fix).

**Why:** Each image sits in a `ScrollViewer` with `Auto` scrollbars, which hands the child an infinite measure constraint, so at zoom=1 the `Image Stretch="Uniform"` renders at natural decoded size. The scan is small (fits); the API art is larger (overflows). Constraining each image to its `ScrollViewer`'s actual size with `StretchDirection="DownOnly"` shrinks large art to fit while never upscaling the small scan. The `ScaleTransform`-based zoom/pan is a render transform (independent of layout measure) and is unaffected.

- [ ] **Step 1: Constrain the scan image**

In `ScanImageElement` (`<Image x:Name="ScanImageElement" …>`), add two attributes alongside the existing `Stretch="Uniform"`:

```xml
<Image x:Name="ScanImageElement"
       Source="{Binding ViewModel.ScanImage}"
       Stretch="Uniform"
       StretchDirection="DownOnly"
       MaxWidth="{Binding ActualWidth, ElementName=ScanScrollViewer}"
       MaxHeight="{Binding ActualHeight, ElementName=ScanScrollViewer}"
       RenderOptions.BitmapScalingMode="HighQuality"
       RenderTransformOrigin="0,0">
    <Image.RenderTransform>
        <ScaleTransform x:Name="ScanImageScale" ScaleX="1" ScaleY="1"/>
    </Image.RenderTransform>
</Image>
```

- [ ] **Step 2: Constrain the API-art image**

In `ApiImageElement`, add the same three attributes, bound to `ApiScrollViewer`:

```xml
<Image x:Name="ApiImageElement"
       Source="{Binding ViewModel.ApiImage}"
       Stretch="Uniform"
       StretchDirection="DownOnly"
       MaxWidth="{Binding ActualWidth, ElementName=ApiScrollViewer}"
       MaxHeight="{Binding ActualHeight, ElementName=ApiScrollViewer}"
       RenderOptions.BitmapScalingMode="HighQuality"
       RenderTransformOrigin="0,0">
    <Image.RenderTransform>
        <ScaleTransform x:Name="ApiImageScale" ScaleX="1" ScaleY="1"/>
    </Image.RenderTransform>
</Image>
```

- [ ] **Step 3: Build**

Run: `dotnet build OmniCard.sln`
Expected: build succeeds.

- [ ] **Step 4: Manual visual verification** (XAML rendering is not unit-testable)

Run the app, open the Collection tab, double-click a card whose matched art is large (e.g. an MTG card). Confirm:
- The Card Art (right) now fits its pane at the same visual scale as the Scan (left) — no overflow.
- A small scan on the left is NOT upscaled (unchanged from before).
- Mouse-wheel zoom, double-click-to-reset, and click-drag pan still work on BOTH panes.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/CollectionCardEditor/CollectionCardEditorView.xaml
git commit -m "fix(ui): fit matched-art image to pane in collection card editor"
```

---

## Feature 1 — "All Games" selector option

### Task 2: Nullable game filter in CollectionViewModel

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (`_selectedGame`/`SetGame` ~876–905; `GetLocationOverviewsAsync` call ~266; `GetMatchingContainerIds` ~432; `game` capture ~451)
- Test: `OmniCard.Tests/ViewModels/CollectionViewModelTests.cs`

**Interfaces:**
- Consumes: `ICardService.SearchCollection(string, CardGame?, int?, SortPreset?, FilterPreset?, bool, int, int, ObservableCollection<CollectionCard>)`, `ICardService.GetSearchCount(string, CardGame?, int?, FilterPreset?, bool)`, `ICardService.GetMatchingContainerIds(string, CardGame?)`, `ICollectionQueryService.GetLocationOverviewsAsync(CardGame?)` — all already nullable.
- Produces: `public void SetGame(CardGame? game)` — `null` means All Games (no game filter). Called by `RootViewModel`.

- [ ] **Step 1: Write the failing test**

Add to `CollectionViewModelTests.cs`:

```csharp
[Fact]
public async Task SetGame_AllGames_SearchesWithNullGameFilter()
{
    var vm = CreateVm();
    vm.ShowCardList = true;

    var searched = new TaskCompletionSource();
    _card.Setup(c => c.SearchCollection(
            It.IsAny<string>(), It.IsAny<CardGame?>(), It.IsAny<int?>(),
            It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()))
         .Callback(() => searched.TrySetResult());
    _card.Invocations.Clear();

    vm.SetGame(null); // All Games
    await searched.Task.WaitAsync(TimeSpan.FromSeconds(5));

    _card.Verify(c => c.SearchCollection(
        It.IsAny<string>(), (CardGame?)null, It.IsAny<int?>(),
        It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
        0, It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()), Times.Once);
}
```

Also update the existing `GetSearchCount` setup in `CreateVm()` to use the nullable matcher so the count path still matches under a null game:

```csharp
_card.Setup(c => c.GetSearchCount(It.IsAny<string>(), It.IsAny<CardGame?>(), It.IsAny<int?>(),
                                  It.IsAny<FilterPreset?>(), It.IsAny<bool>()))
     .Returns(0);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~SetGame_AllGames_SearchesWithNullGameFilter"`
Expected: FAIL — `SetGame(CardGame?)` does not exist / `SetGame(null)` ambiguous.

- [ ] **Step 3: Implement nullable game filter**

In `CollectionViewModel.cs`, replace the game-context region (~876–905):

```csharp
// --- Game context (set by RootViewModel when game changes) ---

private CardGame _selectedGame;   // last concrete game (for per-game presets/sort)
private bool _allGames;           // true when "All Games" is selected

/// <summary>Game filter passed to the card service: null = All Games (no filter).</summary>
private CardGame? GameFilter => _allGames ? null : _selectedGame;

public void SetGame(CardGame? game)
{
    _allGames = game is null;
    if (game is not null)
    {
        _selectedGame = game.Value;
        LoadPresets();
    }
    else
    {
        // All Games: sort/filter presets are per-game — clear them.
        AvailableSortPresets.Clear();
        AvailableFilterPresets.Clear();
        SelectedSortPreset = null;
        SelectedFilterPreset = null;
    }

    if (ShowCardList)
        _ = SearchCollection();
    else
        LoadOverview();
}
```

Then replace the three read sites that pass the game to the service with `GameFilter`:
- Line ~266: `await _collectionQueryService.GetLocationOverviewsAsync(GameFilter);`
- Line ~432: `_cardService.GetMatchingContainerIds(overviewQuery, GameFilter));`
- Line ~451: `var game = GameFilter;`

Leave line ~443 (`new SortPreset { … Game = _selectedGame … }`) unchanged — it uses the concrete last game, which is valid (ad-hoc sort is not active under All Games since presets are cleared).

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~CollectionViewModelTests"`
Expected: PASS (new test plus the two existing `SetGame_*` tests still green).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/CollectionViewModel.cs OmniCard.Tests/ViewModels/CollectionViewModelTests.cs
git commit -m "feat(collection): support null game filter (All Games) in CollectionViewModel"
```

### Task 3: All-games set completion + per-game pricing in CardService

**Files:**
- Modify: `OmniCard.Shared/Interfaces/ICardService.cs` (~28–35)
- Modify: `OmniCard.Collection/CardService.cs` (near `CalculateSetCompletionAsync` ~1767; add `GetCurrentPrices`)
- Test: `OmniCard.Tests/Services/SetCompletionTests.cs`

**Interfaces:**
- Consumes: existing `CalculateSetCompletionAsync(CardGame, IProgress<string>?)`, `AvailableGames`, and the private `_gameServices` dictionary + `_omniDbContextFactory`.
- Produces:
  - `Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame? game, IProgress<string>? progress = null)` — when `game` is `null`, aggregates across `AvailableGames`.
  - `IReadOnlyDictionary<string, decimal> GetCurrentPrices(CardGame game, IEnumerable<string> gameCardIds, bool foil)` — routes to that game's service.

- [ ] **Step 1: Write the failing test**

Add to `SetCompletionTests.cs` (follow the file's existing construction pattern for `CardService`/mocks). If two game services are already registered in the test fixture, assert the null overload returns their union; otherwise seed owned cards for two games and assert both appear:

```csharp
[Fact]
public async Task CalculateSetCompletionAsync_NullGame_AggregatesAllGames()
{
    // Arrange: owned cards seeded for two games (Mtg + OnePiece) via the test's DB/context helper.
    var service = CreateCardServiceWithTwoGames(); // existing helper or inline setup in this file

    // Act
    var all = await service.CalculateSetCompletionAsync((CardGame?)null);

    // Assert: results include sets from both games.
    Assert.Contains(all, s => s.Game == CardGame.Mtg);
    Assert.Contains(all, s => s.Game == CardGame.OnePiece);
}
```

> If the existing fixture only registers one game service, adapt: register a second stub `ICardGameService` whose `GetSetCompletionAsync` returns one summary, and assert the null overload calls each registered game once. Match whatever mocking style already exists in `SetCompletionTests.cs`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~CalculateSetCompletionAsync_NullGame"`
Expected: FAIL — no `CalculateSetCompletionAsync(CardGame?)` overload.

- [ ] **Step 3: Add interface members**

In `ICardService.cs`, next to the existing `CalculateSetCompletionAsync`:

```csharp
Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame? game, IProgress<string>? progress = null);
IReadOnlyDictionary<string, decimal> GetCurrentPrices(CardGame game, IEnumerable<string> gameCardIds, bool foil);
```

- [ ] **Step 4: Implement in CardService**

Add near the existing `CalculateSetCompletionAsync(CardGame …)`:

```csharp
public async Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame? game, IProgress<string>? progress = null)
{
    if (game is not null)
        return await CalculateSetCompletionAsync(game.Value, progress);

    // All Games: aggregate each supported game's set completion.
    var all = new List<SetCompletionSummary>();
    foreach (var g in AvailableGames)
        all.AddRange(await CalculateSetCompletionAsync(g, progress));
    return all;
}

public IReadOnlyDictionary<string, decimal> GetCurrentPrices(CardGame game, IEnumerable<string> gameCardIds, bool foil)
    => _gameServices[game].GetCurrentPrices(gameCardIds, foil);
```

> Verify the exact return type of `ICardGameService.GetCurrentPrices` and match it (adjust `IReadOnlyDictionary<string, decimal>` if the existing signature differs, e.g. `Dictionary<string, decimal>`). Keep both the interface and impl in sync.

- [ ] **Step 5: Run tests to verify pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~SetCompletion"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/ICardService.cs OmniCard.Collection/CardService.cs OmniCard.Tests/Services/SetCompletionTests.cs
git commit -m "feat(collection): all-games set completion + per-game price routing"
```

### Task 4: Nullable game selection + scanner enable/disable in RootViewModel

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (game selection ~481–521; `Initialize` ~1187/1211; stats/set-completion command ~2244–2320)

**Interfaces:**
- Consumes: `Collection.SetGame(CardGame?)` (Task 2), `CardService.CalculateSetCompletionAsync(CardGame?)` + `CardService.GetCurrentPrices(CardGame, …)` (Task 3).
- Produces:
  - `IReadOnlyList<CardGame?> AvailableGameOptions` (`[null, …AvailableGames]`) for the ComboBox.
  - `CardGame? SelectedGame` — `null` = All Games.
  - `bool IsScannerEnabled` — `SelectedGame.HasValue`.

**Why manual (not unit) verification:** `RootViewModel` has no unit-test fixture in this repo (heavy dependency graph); this task is verified by build + running the app.

- [ ] **Step 1: Make the selection nullable and expose options + scanner flag**

Replace the game-selection block (~481–521):

```csharp
// Game selection
public IReadOnlyList<CardGame> AvailableGames => CardService.AvailableGames;

/// <summary>ComboBox source: null (All Games) followed by each supported game.</summary>
public IReadOnlyList<CardGame?> AvailableGameOptions =>
    new CardGame?[] { null }.Concat(CardService.AvailableGames.Select(g => (CardGame?)g)).ToList();

[ObservableProperty]
public partial CardGame? SelectedGame { get; set; }

/// <summary>Scanner is single-game; disabled while "All Games" is active.</summary>
public bool IsScannerEnabled => SelectedGame.HasValue;

private CardGame? _previousGame;

partial void OnSelectedGameChanging(CardGame? value)
{
    _previousGame = SelectedGame;
}

partial void OnSelectedGameChanged(CardGame? value)
{
    if (_suppressGameChangeHandler)
        return;

    if (CardService.ScannedCards.Count > 0)
    {
        _logger.LogWarning("Blocked game switch from {Old} to {New}: {Count} pending scan(s)",
            _previousGame, value, CardService.ScannedCards.Count);

        MessageBox.Show(
            $"You have {CardService.ScannedCards.Count} unconfirmed scan(s). " +
            "Please commit or discard them before switching games.",
            "Game Switch Blocked",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        _suppressGameChangeHandler = true;
        SelectedGame = _previousGame;
        _suppressGameChangeHandler = false;
        return;
    }

    if (value.HasValue)
    {
        _logger.LogInformation("Switched active game to {Game}", value.Value);
        CardService.SelectedGame = value.Value;   // scanner routing stays concrete
        SetFilterText = "";
        LoadAvailableSets();
    }
    else
    {
        _logger.LogInformation("Switched to All Games (scanner disabled)");
        SetFilterText = "";
        _allSets = [];
        UpdateSetFilter();
        // If the scanner tab is active, move off it (it is about to be disabled).
        if (SelectedTabIndex == 2)
            SelectedTabIndex = 0;
    }

    OnPropertyChanged(nameof(IsScannerEnabled));
    Collection.SetGame(value);
    InvalidateHomeTab();
}
```

> `_previousGame` was previously `CardGame`; ensure any other reference to it compiles as `CardGame?`. Confirm `_suppressGameChangeHandler` already exists (it does).

- [ ] **Step 2: Fix Initialize sync**

In `Initialize()` (~1187), `SelectedGame = CardService.SelectedGame;` still compiles (`CardGame` → `CardGame?`). Confirm `Collection.SetGame(SelectedGame)` (~1211) now passes `CardGame?` — matches Task 2. No change needed beyond confirming it builds.

- [ ] **Step 3: All-games stats in CalculateSetCompletion**

In the `CalculateSetCompletion` command (~2244–2320), two call sites take a game:

Replace the set-completion call (~2290–2291):

```csharp
var results = await Task.Run<Task<List<SetCompletionSummary>>>(() =>
    CardService.CalculateSetCompletionAsync(SelectedGame, progress)).Unwrap();
```
(`SelectedGame` is now `CardGame?`; the null overload from Task 3 handles All Games.)

Replace the total-value pricing block (~2263–2279) to route prices per game (correct for both single and All Games):

```csharp
decimal totalValue = 0;
var cardsNeedingPrice = allCards.Where(c => !c.PurchasePrice.HasValue).ToList();
var batchPrices = new Dictionary<(string GameCardId, bool Foil), decimal>();
foreach (var grp in cardsNeedingPrice.GroupBy(c => (c.Game, c.IsFoil)))
{
    var prices = CardService.GetCurrentPrices(
        grp.Key.Game, grp.Select(c => c.GameCardId).Distinct(), grp.Key.IsFoil);
    foreach (var kvp in prices)
        batchPrices.TryAdd((kvp.Key, grp.Key.IsFoil), kvp.Value);
}
foreach (var card in allCards)
{
    if (card.PurchasePrice.HasValue)
        totalValue += card.PurchasePrice.Value;
    else
        totalValue += batchPrices.GetValueOrDefault((card.GameCardId, card.IsFoil));
}
StatTotalValue = totalValue;
```

The `SearchCollection("", SelectedGame, null, allCards)` call (~2256) already accepts `CardGame?` — no change.

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard.sln`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat(shell): nullable game selection (All Games) with scanner gating and all-games stats"
```

### Task 5: Selector ComboBox + Scanner tab XAML wiring

**Files:**
- Modify: `OmniCard.Controls/Converters/RootConverters.cs` (`CardGameDisplayConverter` ~148–166)
- Modify: `OmniCard/Views/Root/RootView.xaml` (ComboBox ~231–253; Scanner `TabItem` ~282–291)
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml` (add disabled placeholder overlay)

**Interfaces:**
- Consumes: `RootViewModel.AvailableGameOptions`, `RootViewModel.SelectedGame` (`CardGame?`), `RootViewModel.IsScannerEnabled` (Task 4).
- Produces: nothing.

- [ ] **Step 1: Null-safe display converter**

In `CardGameDisplayConverter.Convert`, add a null case (returns "All Games"). The `switch` already falls through `_ => value?.ToString() ?? ""`; add an explicit arm before it:

```csharp
public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    => value switch
    {
        null => "All Games",
        CardGame.Mtg => "Magic: The Gathering",
        CardGame.OnePiece => "One Piece TCG",
        CardGame.Riftbound => "Riftbound",
        CardGame.Pokemon => "Pokémon",
        CardGame.YuGiOh => "Yu-Gi-Oh!",
        CardGame.FinalFantasy => "Final Fantasy TCG",
        _ => value?.ToString() ?? ""
    };
```

- [ ] **Step 2: Rebind the ComboBox**

In `RootView.xaml`, point the game ComboBox at the nullable options:

```xml
<ComboBox ItemsSource="{Binding ViewModel.AvailableGameOptions}"
          SelectedItem="{Binding ViewModel.SelectedGame}"
          Width="180" ...>
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Converter={conv:CardGameDisplayConverter}}" .../>
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

(The `DataTemplate` binds the whole item through the converter, so a `null` item renders "All Games".)

- [ ] **Step 3: Gate the Scanner tab**

On the Scanner `<TabItem>` in `RootView.xaml` (~282–291), bind enablement:

```xml
<TabItem Header="Scanner" IsEnabled="{Binding ViewModel.IsScannerEnabled}">
    <local:ScannerTabView x:Name="ScannerTab"/>
</TabItem>
```

- [ ] **Step 4: Placeholder overlay in the scanner body**

In `ScannerTabView.xaml`, wrap the existing root in a `Grid` (if not already) and add a sibling overlay shown when the scanner is disabled. `ScannerTabView` inherits `RootView`'s DataContext, so `ViewModel.IsScannerEnabled` binds directly. Add as the LAST child of the root grid so it overlays:

```xml
<Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
        Visibility="{Binding ViewModel.IsScannerEnabled,
                     Converter={StaticResource InverseBoolToVis}}"
        Panel.ZIndex="100">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
        <TextBlock Text="&#xE722;" FontFamily="Segoe MDL2 Assets" FontSize="40"
                   HorizontalAlignment="Center" Opacity="0.5"
                   Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
        <TextBlock Text="Select a specific game to scan."
                   Margin="0,12,0,0" FontSize="16"
                   Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
    </StackPanel>
</Border>
```

> Verify an inverse bool→visibility converter exists in scope (search for `InverseBool`/`BoolToVisibilityConverter` with a parameter). If none, reuse the existing `BoolToVis` with a `Style`/`DataTrigger` that sets `Visibility=Visible` when `IsScannerEnabled` is `False`, mirroring the `HasScanImage` trigger pattern already used in `CollectionCardEditorView.xaml`.

- [ ] **Step 5: Build**

Run: `dotnet build OmniCard.sln`
Expected: build succeeds.

- [ ] **Step 6: Manual verification**

Run the app:
- Game ComboBox lists "All Games" first, then each game.
- Select "All Games": Collection tab shows cards from every game (no game filter); Scanner tab is greyed/disabled; if you were on the Scanner tab, you're moved to the Dashboard; the Dashboard shows set tiles across all games.
- Select a concrete game: filtering returns; Scanner tab is enabled again.
- With an unconfirmed scan pending, switching the game is still blocked by the existing warning dialog.

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Controls/Converters/RootConverters.cs OmniCard/Views/Root/RootView.xaml OmniCard/Views/Root/ScannerTabView.xaml
git commit -m "feat(shell): All Games selector option and scanner-disabled placeholder"
```

---

## Feature 2 — Dashboard set tile → collection filter

### Task 6: BrowseSet on CollectionViewModel

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (near `BrowseAll` ~175–190)
- Test: `OmniCard.Tests/ViewModels/CollectionViewModelTests.cs`

**Interfaces:**
- Consumes: `ResetSearchState()`, `LoadPresets()`, `SearchCollection()` (existing private/relay members), `CollectionSearchQuery`, `ShowCardList`, `ShowAllCards`.
- Produces: `public void BrowseSet(CardGame game, string setCode)` — enters card-tile mode filtered to `game` + `set:<setCode>`. Called by `RootViewModel`.

- [ ] **Step 1: Write the failing test**

Add to `CollectionViewModelTests.cs`:

```csharp
[Fact]
public async Task BrowseSet_FiltersToGameAndSet()
{
    var vm = CreateVm();

    var searched = new TaskCompletionSource();
    _card.Setup(c => c.SearchCollection(
            It.IsAny<string>(), It.IsAny<CardGame?>(), It.IsAny<int?>(),
            It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()))
         .Callback(() => searched.TrySetResult());
    _card.Invocations.Clear();

    vm.BrowseSet(CardGame.OnePiece, "OP01");
    await searched.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.True(vm.ShowCardList);
    Assert.Equal("set:OP01", vm.CollectionSearchQuery);
    _card.Verify(c => c.SearchCollection(
        "set:OP01", CardGame.OnePiece, It.IsAny<int?>(),
        It.IsAny<SortPreset?>(), It.IsAny<FilterPreset?>(), It.IsAny<bool>(),
        0, It.IsAny<int>(), It.IsAny<ObservableCollection<CollectionCard>>()), Times.Once);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~BrowseSet_FiltersToGameAndSet"`
Expected: FAIL — `BrowseSet` does not exist.

- [ ] **Step 3: Implement BrowseSet**

Add after `BrowseAll()`:

```csharp
/// <summary>Enter card-tile mode filtered to a single set of a single game (dashboard drill-in).</summary>
public void BrowseSet(CardGame game, string setCode)
{
    // Filter to the tile's own game regardless of the global selector.
    _allGames = false;
    _selectedGame = game;
    LoadPresets();

    CurrentLocationId = null;
    CurrentLocationName = "Entire Collection";
    ShowAllCards = true;

    ResetSearchState();      // clears CollectionSearchQuery — set the query AFTER this
    ShowCardList = true;

    OnPropertyChanged(nameof(ColumnVisibility));

    CollectionSearchQuery = $"set:{setCode}";
    _ = SearchCollection();
}
```

> Confirm `ResetSearchState()` clears `CollectionSearchQuery` (it does per the code near ~199) — hence assigning the query afterward. If `SearchCollection()` short-circuits on unchanged params, the fresh `ShowCardList=true` + new query guarantees a real search; keep the explicit `_ = SearchCollection();`.

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~CollectionViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/CollectionViewModel.cs OmniCard.Tests/ViewModels/CollectionViewModelTests.cs
git commit -m "feat(collection): BrowseSet drill-in filtered to a game + set"
```

### Task 7: Dashboard tile click navigates to the collection

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (`OnSelectedSetCompletionChanged` ~1146–1150)

**Interfaces:**
- Consumes: `Collection.BrowseSet(CardGame, string)` (Task 6), `SelectedTabIndex`, `SetCompletionSummary.Game` / `.SetCode`.
- Produces: nothing.

**Why manual verification:** `RootViewModel` has no unit-test fixture; verified by running the app.

- [ ] **Step 1: Repurpose the selection handler to navigate**

Replace `OnSelectedSetCompletionChanged` (~1146–1150):

```csharp
private bool _suppressSetSelection;

partial void OnSelectedSetCompletionChanged(SetCompletionSummary? value)
{
    if (_suppressSetSelection || value is null)
        return;

    // Drill into the collection: show this set's owned cards (of its game) as tiles.
    Collection.BrowseSet(value.Game, value.SetCode);
    SelectedTabIndex = 1; // Collection tab

    // Reset selection so re-clicking the same tile after returning re-triggers navigation.
    _suppressSetSelection = true;
    SelectedSetCompletion = null;
    _suppressSetSelection = false;
}
```

This removes the old `ExpandSetCompletionCommand` call. The `ExpandSetCompletion` command and `SetCompletionSummary.MissingCards` machinery are now unused by the dashboard (missing cards were never rendered there) — leave the command in place (it may be used elsewhere); only the auto-invoke on selection is removed.

> Verify `ExpandSetCompletionCommand` / `GetMissingCardsForSet` are not referenced by any other view before deleting them; since this task only stops auto-invoking, no deletion is required.

- [ ] **Step 2: Build**

Run: `dotnet build OmniCard.sln`
Expected: build succeeds.

- [ ] **Step 3: Manual verification**

Run the app, go to the Dashboard:
- Single-click a set tile → app switches to the Collection tab, tile view, showing owned cards of that set (correct game).
- Return to the Dashboard and click the SAME tile again → it navigates again (selection reset works).
- Click a DIFFERENT tile → navigates to that set.
- Repeat under "All Games": clicking a tile filters to that tile's own game + set, without changing the global "All Games" selector.

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat(dashboard): click a set tile to filter the collection to that set"
```

---

## Final verification

- [ ] **Full build + test**

Run: `dotnet build OmniCard.sln && dotnet test OmniCard.Tests`
Expected: build succeeds; all tests pass.

- [ ] **End-to-end smoke (run the app)**

1. Feature 3: double-click a collection card with large art → art fits its pane like the scan; zoom/pan/reset work on both.
2. Feature 1: pick "All Games" → unfiltered collection, aggregated dashboard tiles, disabled scanner + placeholder; pick a game → normal behavior; pending-scan guard still blocks switching.
3. Feature 2: single-click a dashboard set tile (both under a game and under All Games) → Collection tab, tile view, that set's owned cards of the correct game.

---

## Self-review notes

- **Spec coverage:** Feature 1 → Tasks 2–5 (nullable filter, all-games stats/completion, scanner gating, selector UI). Feature 2 → Tasks 6–7 (BrowseSet + tile nav) and the all-games set-completion aggregation in Task 3. Feature 3 → Task 1. All spec sections map to tasks.
- **Type consistency:** `SetGame(CardGame?)`, `SelectedGame` (`CardGame?`), `GameFilter` (`CardGame?`), `CalculateSetCompletionAsync(CardGame?)`, `GetCurrentPrices(CardGame, IEnumerable<string>, bool)`, `BrowseSet(CardGame, string)` — used identically across producer/consumer tasks.
- **Placeholders:** none — every code step carries concrete code. Two "verify the exact existing signature" notes (price-dictionary return type; inverse-bool converter presence) are guardrails against pre-existing-API drift, not deferred work.
