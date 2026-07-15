# Collection Tab: Remove Set Symbols & Add Overview Search — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove set symbol clutter from location tiles and add a Scryfall-syntax search that filters which location tiles are visible in the overview.

**Architecture:** The existing `ScryfallQueryParser` and `BuildFilteredQuery` infrastructure in `CardSevice` gets a new method that returns distinct container IDs matching a query. The `CollectionViewModel` gains a `_matchingContainerIds` field that gates tile visibility via its `GroupedLocations` and `IsBulkVisible` computed properties. The toolbar in `CollectionTabView.xaml` becomes visible in overview mode (showing only the search box), and tiles in `LocationOverviewView.xaml` lose their set symbol `ItemsControl`.

**Tech Stack:** C# / .NET 10, WPF (MVVM with CommunityToolkit.Mvvm), EF Core (SQLite), xUnit

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`
- Test project: `OmniCard.Tests` using xUnit + in-memory SQLite
- MVVM: ViewModels use `[ObservableProperty]` / `[RelayCommand]` from CommunityToolkit.Mvvm
- XAML bindings use `DataContext.ViewModel.Collection.*` pattern via `RelativeSource AncestorType=Window`

---

### Task 1: Add `GetMatchingContainerIds` to `CardSevice`

**Files:**
- Modify: `OmniCard/Services/CardSevice.cs` (interface at line ~35, implementation after `GetSearchCount` at line ~694)
- Test: `OmniCard.Tests/Services/CardServiceCollectionTests.cs`

**Interfaces:**
- Consumes: `BuildFilteredQuery` (private, already exists in `CardSevice`)
- Produces: `ICardService.GetMatchingContainerIds(string query, CardGame? gameFilter) -> HashSet<int>`

- [ ] **Step 1: Write the failing test**

Add to `OmniCard.Tests/Services/CardServiceCollectionTests.cs`, after the existing tests and before the stub classes:

```csharp
[Fact]
public void GetMatchingContainerIds_ReturnsOnlyContainersWithMatchingCards()
{
    using (var ctx = new CollectionDbContext(_options))
    {
        var binder = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
        var box = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
        ctx.StorageContainers.AddRange(binder, box);
        ctx.SaveChanges();

        ctx.Cards.AddRange(
            new CollectionCard { Game = CardGame.Mtg, GameCardId = "id1", Name = "Lightning Bolt", SetCode = "LEA", ContainerId = binder.Id },
            new CollectionCard { Game = CardGame.Mtg, GameCardId = "id2", Name = "Counterspell", SetCode = "LEA", ContainerId = box.Id }
        );
        ctx.SaveChanges();
    }

    var service = new CardSevice(
        new StubHashService(),
        [],
        CreateFactory(),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardSevice>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService());

    var result = service.GetMatchingContainerIds("Lightning Bolt", CardGame.Mtg);

    Assert.Single(result);
    // The binder has "Lightning Bolt", the box does not
    using var ctx2 = new CollectionDbContext(_options);
    var binderId = ctx2.StorageContainers.First(c => c.Name == "Binder").Id;
    Assert.Contains(binderId, result);
}

[Fact]
public void GetMatchingContainerIds_EmptyQuery_ReturnsAllContainers()
{
    using (var ctx = new CollectionDbContext(_options))
    {
        var binder = new StorageContainer { Name = "Binder2", ContainerType = ContainerType.Binder };
        var box = new StorageContainer { Name = "Box2", ContainerType = ContainerType.Box };
        ctx.StorageContainers.AddRange(binder, box);
        ctx.SaveChanges();

        ctx.Cards.AddRange(
            new CollectionCard { Game = CardGame.Mtg, GameCardId = "id10", Name = "Card A", ContainerId = binder.Id },
            new CollectionCard { Game = CardGame.Mtg, GameCardId = "id11", Name = "Card B", ContainerId = box.Id }
        );
        ctx.SaveChanges();
    }

    var service = new CardSevice(
        new StubHashService(),
        [],
        CreateFactory(),
        new StubOcrService(),
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardSevice>.Instance,
        new DataPathService(Path.GetTempPath()),
        new NullScanDiagnosticService());

    var result = service.GetMatchingContainerIds("", CardGame.Mtg);

    Assert.True(result.Count >= 2);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~GetMatchingContainerIds" -v minimal`
Expected: Build error — `CardSevice` does not contain `GetMatchingContainerIds`

- [ ] **Step 3: Add interface method**

In `OmniCard/Services/CardSevice.cs`, add to the `ICardService` interface (after `GetSearchCount` at line ~35):

```csharp
HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter);
```

- [ ] **Step 4: Write minimal implementation**

In `OmniCard/Services/CardSevice.cs`, add after the `GetSearchCount` method (after line ~694):

```csharp
public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter)
{
    using var context = _collectionDbContextFactory.CreateDbContext();
    var cards = BuildFilteredQuery(context, query, gameFilter, containerFilter: null, filterPreset: null);
    return cards
        .Where(c => c.ContainerId != null)
        .Select(c => c.ContainerId!.Value)
        .Distinct()
        .ToHashSet();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~GetMatchingContainerIds" -v minimal`
Expected: 2 tests PASS

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/CardSevice.cs OmniCard.Tests/Services/CardServiceCollectionTests.cs
git commit -m "feat: add GetMatchingContainerIds to CardService for overview search"
```

---

### Task 2: Remove Set Symbols from Location Tiles

**Files:**
- Modify: `OmniCard/Views/Root/LocationOverviewView.xaml` (lines 87-107)
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (lines 236-245, 289)
- Modify: `OmniCard/Models/LocationTileSummary.cs` (remove `SetSymbols` property and `SetCodeRarity` class)

**Interfaces:**
- Consumes: Nothing new
- Produces: `LocationTileSummary` without `SetSymbols` property (used by Task 3's XAML)

- [ ] **Step 1: Remove the set symbols ItemsControl from LocationOverviewView.xaml**

In `OmniCard/Views/Root/LocationOverviewView.xaml`, delete lines 87-107 (the entire `ItemsControl` block that renders set symbols):

```xml
<!-- DELETE THIS ENTIRE BLOCK -->
<ItemsControl Grid.Row="2" Grid.ColumnSpan="2"
              ItemsSource="{Binding SetSymbols}"
              VerticalAlignment="Bottom">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel/>
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate DataType="{x:Type models:SetCodeRarity}">
            <Border Width="28" Height="28"
                    Margin="0,0,4,0"
                    Padding="4"
                    CornerRadius="4"
                    Background="#DD000000">
                <Border helpers:SetSymbol.SetCode="{Binding SetCode}"
                        helpers:SetSymbol.Rarity="{Binding Rarity}"/>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

Also remove the `xmlns:helpers` namespace declaration on line 6 since it's no longer used:

```xml
<!-- DELETE THIS LINE -->
xmlns:helpers="clr-namespace:OmniCard.Helpers"
```

And remove the `xmlns:models` declaration on line 5 if it is no longer referenced (check: `SetCodeRarity` was the only `models:` usage in this file — the `LocationTileSummary` DataType on line 10 also uses it). Keep `xmlns:models` since `LocationTileSummary` still uses it on line 10.

- [ ] **Step 2: Remove set symbol data fetching from CollectionViewModel.cs**

In `OmniCard/Views/Root/CollectionViewModel.cs`, within `LoadOverview()`:

Delete the set symbols query block (lines 236-245):
```csharp
// DELETE THIS BLOCK
var setsByContainer = cardsQuery
    .Select(c => new { c.ContainerId, c.SetCode })
    .Distinct()
    .ToList()
    .GroupBy(c => c.ContainerId)
    .ToDictionary(
        g => g.Key,
        g => g.OrderBy(c => c.SetCode)
              .Select(c => new SetCodeRarity { SetCode = c.SetCode, Rarity = "uncommon" })
              .ToList());
```

Change the `SetSymbols` line in the summary initializer (line 289) from:
```csharp
SetSymbols = setsByContainer.GetValueOrDefault(container.Id, []),
```
to simply remove it (delete the line).

- [ ] **Step 3: Remove SetSymbols property and SetCodeRarity class**

Replace the entire contents of `OmniCard/Models/LocationTileSummary.cs` with:

```csharp
namespace OmniCard.Models;

public class LocationTileSummary
{
    public required StorageContainer Container { get; init; }
    public int CardCount { get; init; }
    public decimal TotalMarketValue { get; init; }
    public decimal TotalPurchaseCost { get; init; }
    public decimal PriceDelta { get; init; }
    public double PriceDeltaPercent { get; init; }
    public string? CoverImageUri { get; init; }
}
```

- [ ] **Step 4: Build to verify no compile errors**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeds with no errors. Verify no remaining references to `SetCodeRarity` or `SetSymbols`.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/LocationOverviewView.xaml OmniCard/Views/Root/CollectionViewModel.cs OmniCard/Models/LocationTileSummary.cs
git commit -m "feat: remove set symbols from location tiles"
```

---

### Task 3: Add Overview Search to ViewModel

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs`

**Interfaces:**
- Consumes: `ICardService.GetMatchingContainerIds(string query, CardGame? gameFilter)` from Task 1
- Produces:
  - `CollectionViewModel.IsOverviewSearchActive` (bool, observable) — true when `_matchingContainerIds` is non-null
  - `CollectionViewModel.IsBulkVisible` (bool, computed) — true when search inactive or bulk container matches
  - `CollectionViewModel.GroupedLocations` (updated to filter by matching IDs)
  - `CollectionViewModel.SearchCollection()` — updated to handle overview mode

- [ ] **Step 1: Add `_matchingContainerIds` field and `IsOverviewSearchActive` / `IsBulkVisible` properties**

In `OmniCard/Views/Root/CollectionViewModel.cs`, add after the `BulkSummary` property (after line 182):

```csharp
private HashSet<int>? _matchingContainerIds;

public bool IsOverviewSearchActive => _matchingContainerIds is not null;

public bool IsBulkVisible =>
    _matchingContainerIds is null ||
    (BulkSummary is not null && _matchingContainerIds.Contains(BulkSummary.Container.Id));
```

- [ ] **Step 2: Update `GroupedLocations` to filter by matching IDs**

Replace the existing `GroupedLocations` property (line ~184):

From:
```csharp
public IEnumerable<IGrouping<ContainerType, LocationTileSummary>> GroupedLocations =>
    LocationSummaries
        .OrderBy(s => s.Container.Name, StringComparer.OrdinalIgnoreCase)
        .GroupBy(s => s.Container.ContainerType);
```

To:
```csharp
public IEnumerable<IGrouping<ContainerType, LocationTileSummary>> GroupedLocations
{
    get
    {
        var source = _matchingContainerIds is not null
            ? LocationSummaries.Where(s => _matchingContainerIds.Contains(s.Container.Id))
            : LocationSummaries;

        return source
            .OrderBy(s => s.Container.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(s => s.Container.ContainerType);
    }
}
```

- [ ] **Step 3: Update `SearchCollection` to handle overview mode**

In `SearchCollection()` (line ~395), add an overview-mode branch at the top of the method, before the existing card-list search logic:

```csharp
[RelayCommand]
public async Task SearchCollection()
{
    // Overview mode: filter location tiles instead of searching cards
    if (!ShowCardList)
    {
        var query = CollectionSearchQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            _matchingContainerIds = null;
        }
        else
        {
            _matchingContainerIds = await Task.Run(() =>
                _cardService.GetMatchingContainerIds(query, _selectedGame));
        }
        OnPropertyChanged(nameof(GroupedLocations));
        OnPropertyChanged(nameof(IsBulkVisible));
        OnPropertyChanged(nameof(IsOverviewSearchActive));
        return;
    }

    // --- existing card-list search code below (unchanged) ---
    var sortPreset = IsAdHocSortActive
```

- [ ] **Step 4: Clear overview search state in `ResetSearchState` and `NavigateBack`**

In `ResetSearchState()` (line ~168), add clearing of the overview filter:

```csharp
private void ResetSearchState()
{
    CollectionSearchQuery = "";
    SelectedSortPreset = null;
    SelectedFilterPreset = null;
    _adHocSortLevels.Clear();
    IsAdHocSortActive = false;
    _matchingContainerIds = null;
}
```

In `NavigateBack()` (line ~155), add property change notifications after `LoadOverview()`:

```csharp
[RelayCommand]
public void NavigateBack()
{
    ShowCardList = false;
    ShowAllCards = false;
    CurrentLocationId = null;
    CurrentLocationName = "";
    ResetSearchState();
    CollectionSearchResults.Clear();
    MarketPrices.Clear();
    TotalCardCount = 0;
    LoadOverview();
    OnPropertyChanged(nameof(GroupedLocations));
    OnPropertyChanged(nameof(IsBulkVisible));
    OnPropertyChanged(nameof(IsOverviewSearchActive));
}
```

- [ ] **Step 5: Build to verify no compile errors**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeds

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/CollectionViewModel.cs
git commit -m "feat: add overview search filtering to CollectionViewModel"
```

---

### Task 4: Update XAML — Toolbar Visibility & Overview Empty State

**Files:**
- Modify: `OmniCard/Views/Root/CollectionTabView.xaml`
- Modify: `OmniCard/Views/Root/LocationOverviewView.xaml`
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (add `HasVisibleLocations` property)

**Interfaces:**
- Consumes:
  - `CollectionViewModel.ShowCardList` (bool)
  - `CollectionViewModel.IsOverviewSearchActive` (bool) from Task 3
  - `CollectionViewModel.IsBulkVisible` (bool) from Task 3
  - `CollectionViewModel.GroupedLocations` (filtered) from Task 3
  - `CollectionViewModel.CollectionSearchQuery` (string)
  - `CollectionViewModel.SearchCollectionCommand` (ICommand)
- Produces: Updated UI layout (no code interfaces)

- [ ] **Step 1: Make toolbar visible in overview mode, hide card-list-only controls**

In `OmniCard/Views/Root/CollectionTabView.xaml`, change the `ToolBarPanel` style (lines 33-45).

Replace:
```xml
<ToolBarPanel.Style>
    <Style TargetType="ToolBarPanel">
        <Setter Property="Visibility" Value="{Binding DataContext.ViewModel.Collection.ShowCardList,
            RelativeSource={RelativeSource AncestorType=Window},
            Converter={local:BoolToVisibilityConverter}}"/>
        <Style.Triggers>
            <DataTrigger Binding="{Binding DataContext.ViewModel.Sealed.ShowSealed,
                RelativeSource={RelativeSource AncestorType=Window}}" Value="True">
                <Setter Property="Visibility" Value="Collapsed"/>
            </DataTrigger>
        </Style.Triggers>
    </Style>
</ToolBarPanel.Style>
```

With:
```xml
<ToolBarPanel.Style>
    <Style TargetType="ToolBarPanel">
        <Setter Property="Visibility" Value="Visible"/>
        <Style.Triggers>
            <DataTrigger Binding="{Binding DataContext.ViewModel.Sealed.ShowSealed,
                RelativeSource={RelativeSource AncestorType=Window}}" Value="True">
                <Setter Property="Visibility" Value="Collapsed"/>
            </DataTrigger>
        </Style.Triggers>
    </Style>
</ToolBarPanel.Style>
```

- [ ] **Step 2: Wrap card-list-only toolbar controls with a visibility binding**

In the same `ToolBar` element, wrap the card-list-only controls (Back button, separator, location name, separator, and everything after the Go button) in visibility bindings. The simplest approach: add `Visibility` bindings to each card-list-only element.

Wrap the **Back button and its separator** (lines 48-54) — add visibility to Back button:
```xml
<Button Content="&lt; Back"
        Command="{Binding DataContext.ViewModel.Collection.NavigateBackCommand,
            RelativeSource={RelativeSource AncestorType=Window}}"
        Padding="8,4"
        Margin="0,0,8,0"
        Visibility="{Binding DataContext.ViewModel.Collection.ShowCardList,
            RelativeSource={RelativeSource AncestorType=Window},
            Converter={local:BoolToVisibilityConverter}}"/>
<Separator Visibility="{Binding DataContext.ViewModel.Collection.ShowCardList,
    RelativeSource={RelativeSource AncestorType=Window},
    Converter={local:BoolToVisibilityConverter}}"/>
```

Add visibility to the **location name TextBlock and its separator** (lines 57-63):
```xml
<TextBlock Text="{Binding DataContext.ViewModel.Collection.CurrentLocationName,
                       RelativeSource={RelativeSource AncestorType=Window}}"
           FontWeight="SemiBold"
           VerticalAlignment="Center"
           Margin="0,0,12,0"
           Visibility="{Binding DataContext.ViewModel.Collection.ShowCardList,
               RelativeSource={RelativeSource AncestorType=Window},
               Converter={local:BoolToVisibilityConverter}}"/>
<Separator Visibility="{Binding DataContext.ViewModel.Collection.ShowCardList,
    RelativeSource={RelativeSource AncestorType=Window},
    Converter={local:BoolToVisibilityConverter}}"/>
```

The **Search TextBox and Go button** (lines 65-81) stay visible — no change needed.

Add visibility to the **separator after Go, Sort controls, Filter controls, Stack Duplicates, and Column chooser** (lines 83-153). Add to each element:
```xml
Visibility="{Binding DataContext.ViewModel.Collection.ShowCardList,
    RelativeSource={RelativeSource AncestorType=Window},
    Converter={local:BoolToVisibilityConverter}}"
```

The elements that need this binding added:
- Separator after Go button (line 83)
- "Sort:" TextBlock (line 85)
- Sort ComboBox (line 88)
- Sort reset "X" Button (line 96)
- "Filter:" TextBlock (line 102)
- Filter ComboBox (line 105)
- Filter reset "X" Button (line 113)
- Separator before Stack (line 119)
- Stack Duplicates CheckBox (line 122)
- Separator before Columns (line 128)
- Columns Button (line 130)
- Columns Popup (line 141)

- [ ] **Step 3: Add bulk tile visibility binding in LocationOverviewView.xaml**

In `OmniCard/Views/Root/LocationOverviewView.xaml`, update the bulk container `UniformGrid` (line 149) to bind visibility:

Replace:
```xml
<UniformGrid Columns="3" Margin="0,0,0,12">
    <ContentControl Content="{Binding DataContext.ViewModel.Collection.BulkSummary,
                        RelativeSource={RelativeSource AncestorType=Window}}"
                    ContentTemplate="{StaticResource LocationTileTemplate}"/>
</UniformGrid>
```

With:
```xml
<UniformGrid Columns="3" Margin="0,0,0,12"
             Visibility="{Binding DataContext.ViewModel.Collection.IsBulkVisible,
                 RelativeSource={RelativeSource AncestorType=Window},
                 Converter={local:BoolToVisibilityConverter}}">
    <ContentControl Content="{Binding DataContext.ViewModel.Collection.BulkSummary,
                        RelativeSource={RelativeSource AncestorType=Window}}"
                    ContentTemplate="{StaticResource LocationTileTemplate}"/>
</UniformGrid>
```

- [ ] **Step 4: Add `HasVisibleLocations` property and empty state placeholder**

First, add a computed property to `OmniCard/Views/Root/CollectionViewModel.cs` (after `IsBulkVisible`):

```csharp
public bool HasVisibleLocations =>
    IsBulkVisible || GroupedLocations.Any();
```

Also add `OnPropertyChanged(nameof(HasVisibleLocations));` in `SearchCollection()`'s overview branch (alongside the other `OnPropertyChanged` calls) and in `NavigateBack()` (alongside the other notifications added in Task 3).

Then, in `OmniCard/Views/Root/LocationOverviewView.xaml`, add after the grouped containers `ItemsControl` (after line 176), before the closing `</StackPanel>`:

```xml
<!-- No matching locations placeholder -->
<TextBlock Text="No matching locations"
           HorizontalAlignment="Center"
           Margin="0,40,0,0"
           FontSize="18"
           FontStyle="Italic"
           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding DataContext.ViewModel.Collection.IsOverviewSearchActive,
                            RelativeSource={RelativeSource AncestorType=Window}}" Value="True"/>
                        <Condition Binding="{Binding DataContext.ViewModel.Collection.HasVisibleLocations,
                            RelativeSource={RelativeSource AncestorType=Window}}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

- [ ] **Step 5: Build to verify no compile errors**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeds

- [ ] **Step 6: Run all tests to verify nothing broke**

Run: `dotnet test OmniCard.Tests -v minimal`
Expected: All tests pass

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Root/CollectionTabView.xaml OmniCard/Views/Root/LocationOverviewView.xaml OmniCard/Views/Root/CollectionViewModel.cs
git commit -m "feat: show search toolbar in overview mode and add empty state placeholder"
```
