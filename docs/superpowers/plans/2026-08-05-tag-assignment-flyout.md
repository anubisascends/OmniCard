# Tag Assignment Flyout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the type-first "Add Tag(s)..." dialog with a right-click "Tags..." flyout — a filterable, checkable list of every existing tag that toggles on/off across the current selection (including stacks) — on every card list: Collection, Locations, and the Scanner review queue.

**Architecture:** A new `OmniCard.Controls.TagFlyout` `UserControl` (filter box + tri-state checkable list + inline "+ New Tag..." row) is hosted inside a `Popup` at four XAML locations (Collection context menu, Collection "Selection" main menu, Scanner context menu, Scanner "_Scanner" main menu). Each location's code-behind opens the popup on a "Tags..." `MenuItem` click, after calling a synchronous VM "Load" method that (re)computes the tag list's checked/unchecked/indeterminate state for the current selection. Toggling a row fires an `ICommand` back into the owning ViewModel (`CollectionViewModel` for Collection/Locations, `RootViewModel` for Scanner), which persists the change (`ITagService.AddTagToLots`/new `RemoveTagFromLots` for Collection; direct `ScannedCard.Tags` mutation for Scanner) and updates the already-bound row objects in place — no full list re-query, so the open popup and scroll position are undisturbed.

**Tech Stack:** WPF (.NET 10), CommunityToolkit.Mvvm (`RelayCommand`, `ObservableObject`), EF Core/SQLite (`OmniCardDbContext`), xUnit + Moq.

## Global Constraints

- Target framework `net10.0-windows10.0.22621.0`; build via `dotnet build OmniCard.slnx`, test via `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`.
- Follow existing MVVM conventions: `[RelayCommand]`/`[ObservableProperty]` from CommunityToolkit.Mvvm, no manual `ICommand` implementations.
- WPF `UserControl`s in `OmniCard.Controls` have no unit tests in this repo (verified: no `OmniCard.Tests/Controls/*View*` exist) — coverage for WPF-only pieces is manual GUI smoke, consistent with `TagEditor`'s precedent. Pure C# logic (services, non-visual model classes, ViewModel methods) must still be unit tested per repo convention.
- Preserve the "no command lives only in a context menu" rule already enforced elsewhere in this codebase (`RootView.xaml`'s "Selection" and "_Scanner" menus mirror `CardListView.xaml`/`ScannerTabView.xaml` context menus) — every new "Tags..." entry needs both a context-menu and a main-menu instance.
- New/changed public service methods get XML-doc summary comments matching the style already used in `ITagService.cs`.

---

## Task 1: `ITagService.RemoveTagFromLots`

**Files:**
- Modify: `OmniCard.Shared/Interfaces/ITagService.cs`
- Modify: `OmniCard.Collection/TagService.cs`
- Test: `OmniCard.Tests/Services/TagServiceTests.cs`

**Interfaces:**
- Produces: `void ITagService.RemoveTagFromLots(IEnumerable<int> lotIds, string tagName)` — used by Task 5 (`CollectionViewModel`).

- [ ] **Step 1: Write the failing tests**

Add to `OmniCard.Tests/Services/TagServiceTests.cs`, after `AddTagToLots_CreatesTagAndSkipsAlreadyTagged` (currently ending at line 88):

```csharp
[Fact]
public void RemoveTagFromLots_RemovesJoinRowsButKeepsTagAlive()
{
    var service = CreateService();
    service.SetTagsForLot(1, ["Foil"]);
    service.SetTagsForLot(2, ["Foil"]);

    service.RemoveTagFromLots([1], "Foil");

    Assert.Empty(service.GetTagsForLot(1));
    Assert.Equal(["Foil"], service.GetTagsForLot(2));
    var tag = Assert.Single(service.GetAllTags()); // tag row survives even mid-removal
    Assert.Equal("Foil", tag.Name);
}

[Fact]
public void RemoveTagFromLots_SurvivesZeroRemainingUsages()
{
    var service = CreateService();
    service.SetTagsForLot(1, ["OnlyHere"]);

    service.RemoveTagFromLots([1], "OnlyHere");

    Assert.Empty(service.GetTagsForLot(1));
    var tag = Assert.Single(service.GetAllTags());
    Assert.Equal(0, tag.UsageCount); // tag itself is not deleted, unlike DeleteTag
}

[Fact]
public void RemoveTagFromLots_NoOpWhenLotDoesNotHaveTag()
{
    var service = CreateService();
    service.SetTagsForLot(1, ["Kept"]);

    service.RemoveTagFromLots([2], "Kept"); // lot 2 was never tagged

    Assert.Equal(["Kept"], service.GetTagsForLot(1));
}

[Fact]
public void RemoveTagFromLots_NoOpWhenTagNameUnknown()
{
    var service = CreateService();
    service.SetTagsForLot(1, ["Real"]);

    service.RemoveTagFromLots([1], "DoesNotExist");

    Assert.Equal(["Real"], service.GetTagsForLot(1));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~TagServiceTests.RemoveTagFromLots"`
Expected: build FAILS — `ITagService` has no `RemoveTagFromLots` member yet.

- [ ] **Step 3: Add the interface member**

In `OmniCard.Shared/Interfaces/ITagService.cs`, add after `AddTagToLots`:

```csharp
    /// <summary>Removes the tag from every listed lot. Unlike <see cref="DeleteTag"/>, the tag
    /// row itself is never deleted, even if its usage count drops to zero — mirrors
    /// <see cref="AddTagToLots"/>.</summary>
    void RemoveTagFromLots(IEnumerable<int> lotIds, string tagName);
```

- [ ] **Step 4: Implement in `TagService`**

In `OmniCard.Collection/TagService.cs`, add after `AddTagToLots` (currently ending at line 100):

```csharp
    public void RemoveTagFromLots(IEnumerable<int> lotIds, string tagName)
    {
        var name = tagName.Trim();
        if (name.Length == 0) return;

        using var context = dbContextFactory.CreateDbContext();
        var lotIdList = lotIds.Distinct().ToList();

        var links = context.LotTags
            .Include(lt => lt.Tag)
            .Where(lt => lotIdList.Contains(lt.LotId) && lt.Tag.Name.ToLower() == name.ToLower())
            .ToList();

        context.LotTags.RemoveRange(links);
        context.SaveChanges();
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~TagServiceTests"`
Expected: PASS (all `TagServiceTests`, including the 4 new ones).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/ITagService.cs OmniCard.Collection/TagService.cs OmniCard.Tests/Services/TagServiceTests.cs
git commit -m "Add ITagService.RemoveTagFromLots"
```

---

## Task 2: `TagCheckState` + `TagFlyoutItem` display model + `TagTriState` helper

**Files:**
- Create: `OmniCard.Controls/TagCheckState.cs`
- Create: `OmniCard.Controls/TagFlyoutItem.cs`
- Create: `OmniCard.Controls/TagTriState.cs`
- Test: `OmniCard.Tests/Controls/TagFlyoutItemTests.cs`
- Test: `OmniCard.Tests/Controls/TagTriStateTests.cs`

**Interfaces:**
- Produces: `enum TagCheckState { Unchecked, Checked, Indeterminate }`; `class TagFlyoutItem(string name, TagCheckState state) : INotifyPropertyChanged` with `Name` (string, get-only), `State` (get/set, raises `PropertyChanged` for `State`/`IsChecked`/`IsIndeterminate`), `IsChecked` (bool), `IsIndeterminate` (bool); `static class TagTriState` with `static TagCheckState Compute(int countWithTag, int totalCount)`. Used by Task 4 (`TagFlyout` control), Task 5 (`CollectionViewModel`), Task 9 (`RootViewModel`). `TagTriState.Compute` is the single place the checked/unchecked/indeterminate rule is expressed — both `CollectionViewModel.LoadTagFlyoutItems` and `RootViewModel.LoadScanTagFlyoutItems` call it instead of inlining the same ternary chain twice, and it is independently unit tested here since neither `CollectionViewModel` (indirectly, via mocked `ITagService`) nor `RootViewModel` (no test harness exists in this repo) is a reliable place to pin down its edge cases.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Controls/TagFlyoutItemTests.cs`:

```csharp
using OmniCard.Controls;
using Xunit;

namespace OmniCard.Tests.Controls;

public class TagFlyoutItemTests
{
    [Theory]
    [InlineData(TagCheckState.Checked, true, false)]
    [InlineData(TagCheckState.Unchecked, false, false)]
    [InlineData(TagCheckState.Indeterminate, false, true)]
    public void IsCheckedAndIsIndeterminate_ReflectState(TagCheckState state, bool expectedChecked, bool expectedIndeterminate)
    {
        var item = new TagFlyoutItem("Foil", state);

        Assert.Equal(expectedChecked, item.IsChecked);
        Assert.Equal(expectedIndeterminate, item.IsIndeterminate);
    }

    [Fact]
    public void SettingState_RaisesPropertyChangedForDerivedProperties()
    {
        var item = new TagFlyoutItem("Foil", TagCheckState.Unchecked);
        var raised = new List<string>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        item.State = TagCheckState.Checked;

        Assert.Contains(nameof(TagFlyoutItem.State), raised);
        Assert.Contains(nameof(TagFlyoutItem.IsChecked), raised);
        Assert.Contains(nameof(TagFlyoutItem.IsIndeterminate), raised);
    }

    [Fact]
    public void SettingState_ToSameValue_DoesNotRaisePropertyChanged()
    {
        var item = new TagFlyoutItem("Foil", TagCheckState.Checked);
        var raised = false;
        item.PropertyChanged += (_, _) => raised = true;

        item.State = TagCheckState.Checked;

        Assert.False(raised);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~TagFlyoutItemTests"`
Expected: build FAILS — `OmniCard.Controls.TagFlyoutItem` / `TagCheckState` do not exist yet.

- [ ] **Step 3: Create `TagCheckState.cs`**

```csharp
namespace OmniCard.Controls;

public enum TagCheckState
{
    Unchecked,
    Checked,
    Indeterminate
}
```

- [ ] **Step 4: Create `TagFlyoutItem.cs`**

```csharp
using System.ComponentModel;

namespace OmniCard.Controls;

/// <summary>One row of a <see cref="TagFlyout"/>'s checklist: a tag name plus whether it is
/// present on all, none, or some of the current selection.</summary>
public sealed class TagFlyoutItem(string name, TagCheckState state) : INotifyPropertyChanged
{
    public string Name { get; } = name;

    private TagCheckState _state = state;
    public TagCheckState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIndeterminate)));
        }
    }

    public bool IsChecked => State == TagCheckState.Checked;
    public bool IsIndeterminate => State == TagCheckState.Indeterminate;

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~TagFlyoutItemTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Write the failing `TagTriState` test**

Create `OmniCard.Tests/Controls/TagTriStateTests.cs`:

```csharp
using OmniCard.Controls;
using Xunit;

namespace OmniCard.Tests.Controls;

public class TagTriStateTests
{
    [Fact]
    public void Compute_ZeroOfTotal_ReturnsUnchecked()
        => Assert.Equal(TagCheckState.Unchecked, TagTriState.Compute(countWithTag: 0, totalCount: 3));

    [Fact]
    public void Compute_AllOfTotal_ReturnsChecked()
        => Assert.Equal(TagCheckState.Checked, TagTriState.Compute(countWithTag: 3, totalCount: 3));

    [Fact]
    public void Compute_SomeOfTotal_ReturnsIndeterminate()
        => Assert.Equal(TagCheckState.Indeterminate, TagTriState.Compute(countWithTag: 1, totalCount: 3));

    [Fact]
    public void Compute_SingleItemWithTag_ReturnsChecked()
        => Assert.Equal(TagCheckState.Checked, TagTriState.Compute(countWithTag: 1, totalCount: 1));
}
```

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~TagTriStateTests"`
Expected: build FAILS — `OmniCard.Controls.TagTriState` does not exist yet.

- [ ] **Step 7: Create `TagTriState.cs`**

```csharp
namespace OmniCard.Controls;

/// <summary>Single source of the checked/unchecked/indeterminate rule shared by every
/// <see cref="TagFlyout"/> host (Collection, Locations, Scanner): checked when every item in the
/// selection has the tag, unchecked when none do, indeterminate otherwise.</summary>
public static class TagTriState
{
    public static TagCheckState Compute(int countWithTag, int totalCount) => countWithTag switch
    {
        0 => TagCheckState.Unchecked,
        var n when n == totalCount => TagCheckState.Checked,
        _ => TagCheckState.Indeterminate,
    };
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~TagFlyoutItemTests|FullyQualifiedName~TagTriStateTests"`
Expected: PASS (4 `TagFlyoutItemTests` + 4 `TagTriStateTests`).

- [ ] **Step 9: Commit**

```bash
git add OmniCard.Controls/TagCheckState.cs OmniCard.Controls/TagFlyoutItem.cs OmniCard.Controls/TagTriState.cs OmniCard.Tests/Controls/TagFlyoutItemTests.cs OmniCard.Tests/Controls/TagTriStateTests.cs
git commit -m "Add TagCheckState/TagFlyoutItem display model and TagTriState helper"
```

---

## Task 3: `ScanTagToggle` — testable Scanner-side tag mutation

**Files:**
- Create: `OmniCard.Collection/ScanTagToggle.cs`
- Test: `OmniCard.Tests/Services/ScanTagToggleTests.cs`

**Interfaces:**
- Consumes: `ScannedCard.Tags` (`ObservableCollection<string>`, `OmniCard.Shared/Models/ScannedCard.cs:67`).
- Produces: `static void ScanTagToggle.Apply(IEnumerable<ScannedCard> cards, string tagName, bool apply)`; `static string? ScanTagToggle.CreateAndApply(IEnumerable<ScannedCard> cards, string name)` (trims `name`, returns `null` with no side effect if the trimmed result is empty, otherwise applies it via `Apply` and returns the trimmed name). Both used by Task 9 (`RootViewModel`).

`RootViewModel` (the Scanner-tab ViewModel) has no existing unit test harness in this repo (no `RootViewModelTests.cs` — it has a very large constructor). Rather than stand one up for this feature, the in-memory tag toggle/create logic — including the trim/blank-check orchestration for creating a brand-new tag — is extracted into this small static helper so it's independently testable; `RootViewModel` will just call it and handle the thin UI-list bookkeeping (`ScanTagFlyoutItems`, `AllTagNames`, `Message`) around it.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/ScanTagToggleTests.cs`:

```csharp
using OmniCard.Collection;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ScanTagToggleTests
{
    [Fact]
    public void Apply_True_AddsTagToEveryCard()
    {
        var cards = new[] { new ScannedCard(), new ScannedCard() };

        ScanTagToggle.Apply(cards, "Foil", apply: true);

        Assert.All(cards, c => Assert.Contains("Foil", c.Tags));
    }

    [Fact]
    public void Apply_True_IsCaseInsensitiveAndSkipsDuplicates()
    {
        var card = new ScannedCard();
        card.Tags.Add("foil");

        ScanTagToggle.Apply([card], "Foil", apply: true);

        Assert.Equal(["foil"], card.Tags); // no duplicate added under different casing
    }

    [Fact]
    public void Apply_False_RemovesTagFromEveryCard()
    {
        var cardA = new ScannedCard();
        cardA.Tags.Add("Foil");
        var cardB = new ScannedCard();
        cardB.Tags.Add("Foil");
        cardB.Tags.Add("PSA");

        ScanTagToggle.Apply([cardA, cardB], "Foil", apply: false);

        Assert.Empty(cardA.Tags);
        Assert.Equal(["PSA"], cardB.Tags);
    }

    [Fact]
    public void Apply_False_IsCaseInsensitiveAndNoOpWhenAbsent()
    {
        var card = new ScannedCard();
        card.Tags.Add("Foil");

        ScanTagToggle.Apply([card], "foil", apply: false);
        Assert.Empty(card.Tags);

        ScanTagToggle.Apply([card], "NeverThere", apply: false); // no throw, no change
        Assert.Empty(card.Tags);
    }

    [Fact]
    public void CreateAndApply_TrimsAppliesAndReturnsTrimmedName()
    {
        var card = new ScannedCard();

        var result = ScanTagToggle.CreateAndApply([card], "  Brand New  ");

        Assert.Equal("Brand New", result);
        Assert.Contains("Brand New", card.Tags);
    }

    [Fact]
    public void CreateAndApply_BlankName_ReturnsNullAndDoesNotTouchCards()
    {
        var card = new ScannedCard();

        var result = ScanTagToggle.CreateAndApply([card], "   ");

        Assert.Null(result);
        Assert.Empty(card.Tags);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ScanTagToggleTests"`
Expected: build FAILS — `OmniCard.Collection.ScanTagToggle` does not exist yet.

- [ ] **Step 3: Implement `ScanTagToggle`**

```csharp
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>Applies a tag toggle to pre-commit scanned cards. Pure in-memory mutation of
/// <see cref="ScannedCard.Tags"/> — no DB access, since scans have no lot id until commit.</summary>
public static class ScanTagToggle
{
    public static void Apply(IEnumerable<ScannedCard> cards, string tagName, bool apply)
    {
        foreach (var card in cards)
        {
            if (apply)
            {
                if (!card.Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
                    card.Tags.Add(tagName);
            }
            else
            {
                var existing = card.Tags.FirstOrDefault(t => string.Equals(t, tagName, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    card.Tags.Remove(existing);
            }
        }
    }

    /// <summary>Trims <paramref name="name"/>; if the result is non-empty, applies it to every
    /// card via <see cref="Apply"/> and returns the trimmed name, otherwise returns null and
    /// touches no cards.</summary>
    public static string? CreateAndApply(IEnumerable<ScannedCard> cards, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;

        Apply(cards, trimmed, apply: true);
        return trimmed;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ScanTagToggleTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/ScanTagToggle.cs OmniCard.Tests/Services/ScanTagToggleTests.cs
git commit -m "Add ScanTagToggle helper for scanner-side tag mutation"
```

---

## Task 4: `TagFlyout` WPF control

**Files:**
- Create: `OmniCard.Controls/CheckStateToNullableBoolConverter.cs`
- Create: `OmniCard.Controls/TagFlyout.xaml`
- Create: `OmniCard.Controls/TagFlyout.xaml.cs`

**Interfaces:**
- Consumes: `TagFlyoutItem`/`TagCheckState` (Task 2).
- Produces: `TagFlyout : UserControl` with dependency properties `Tags` (`ObservableCollection<TagFlyoutItem>`), `ToggleCommand` (`ICommand`, executed with a `(string Name, bool Apply)` tuple), `NewTagCommand` (`ICommand`, executed with a `string`). Consumed by Tasks 7, 8, 10, 11 as `<helpers:TagFlyout .../>` (the `helpers` XML namespace alias for `OmniCard.Controls` is already imported in `CardListView.xaml` and `ScannerTabView.xaml`; `RootView.xaml` needs it added in Task 8).

No unit test for this task — per the Global Constraints, WPF `UserControl`s in this repo (see `TagEditor`) are not unit tested; this is covered by the manual GUI smoke pass in Task 12.

- [ ] **Step 1: Create `CheckStateToNullableBoolConverter.cs`**

A `CheckBox.IsChecked` (`bool?`) needs `TagCheckState.Indeterminate` mapped to `null`. This must be done via a value converter, not a `Style.Triggers` `Setter` on `IsChecked` — a `Binding` set directly on the element (as `IsChecked` needs to be, to reflect `State`) always wins over a style-triggered `Setter` for that same property in WPF's property-value precedence, so a trigger-based approach would silently never show the indeterminate glyph.

```csharp
using System.Globalization;
using System.Windows.Data;

namespace OmniCard.Controls;

public sealed class CheckStateToNullableBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TagCheckState.Checked => true,
        TagCheckState.Indeterminate => null,
        _ => false,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Create `TagFlyout.xaml`**

```xml
<UserControl x:Class="OmniCard.Controls.TagFlyout"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:OmniCard.Controls"
             x:Name="Self"
             MinWidth="220" MaxWidth="320">

    <UserControl.Resources>
        <local:CheckStateToNullableBoolConverter x:Key="CheckStateToNullableBool"/>
    </UserControl.Resources>

    <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
            BorderBrush="{DynamicResource MaterialDesign.Brush.TextBox.HoverBackground}"
            BorderThickness="1"
            Padding="6">
        <StackPanel>
            <TextBox x:Name="FilterBox"
                     TextChanged="FilterBox_TextChanged"
                     Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
                     ToolTip="Filter tags"
                     Margin="0,0,0,4"/>

            <Grid Margin="0,0,0,4">
                <TextBlock x:Name="NewTagLabel"
                           Text="+ New Tag..."
                           Foreground="{DynamicResource MaterialDesign.Brush.Primary}"
                           Cursor="Hand"
                           MouseLeftButtonUp="NewTagLabel_MouseLeftButtonUp"/>
                <TextBox x:Name="NewTagBox"
                         Visibility="Collapsed"
                         KeyDown="NewTagBox_KeyDown"
                         LostFocus="NewTagBox_LostFocus"
                         Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
                         ToolTip="Type a tag name, Enter to add"/>
            </Grid>

            <ListBox x:Name="TagsList"
                     MaxHeight="260"
                     BorderThickness="0"
                     Background="Transparent"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                     PreviewMouseLeftButtonUp="TagsList_PreviewMouseLeftButtonUp">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Margin="0,2">
                            <CheckBox IsChecked="{Binding State, Mode=OneWay, Converter={StaticResource CheckStateToNullableBool}}"
                                      IsThreeState="True"
                                      IsHitTestVisible="False"/>
                            <TextBlock Text="{Binding Name}"
                                       Margin="4,0,0,0"
                                       VerticalAlignment="Center"
                                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 3: Create `TagFlyout.xaml.cs`**

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OmniCard.Controls;

/// <summary>Filterable, checkable tag picker hosted in a Popup from a "Tags..." context/main
/// menu item. Toggling a row fires <see cref="ToggleCommand"/> with (name, applying); the "+ New
/// Tag..." row fires <see cref="NewTagCommand"/> with the trimmed name. The bound
/// <see cref="Tags"/> collection is expected to already reflect the current selection's
/// checked/unchecked/indeterminate state — the host recomputes and reassigns it before opening
/// the popup on each use. Filtering re-derives <c>TagsList.ItemsSource</c> as a plain
/// <see cref="List{T}"/> snapshot on every keystroke (same pattern as <see cref="TagEditor"/>'s
/// suggestion list) rather than a live <c>CollectionViewSource</c>, since a
/// <c>CollectionViewSource</c>'s <c>View</c> reference is not guaranteed stable across a
/// <c>Source</c> reassignment.</summary>
public partial class TagFlyout : UserControl
{
    public TagFlyout()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TagsProperty =
        DependencyProperty.Register(nameof(Tags), typeof(ObservableCollection<TagFlyoutItem>),
            typeof(TagFlyout), new PropertyMetadata(null, OnTagsChanged));

    public ObservableCollection<TagFlyoutItem> Tags
    {
        get => (ObservableCollection<TagFlyoutItem>)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    private static void OnTagsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TagFlyout)d;
        control.FilterBox.Text = "";
        control.NewTagBox.Text = "";
        control.NewTagBox.Visibility = Visibility.Collapsed;
        control.NewTagLabel.Visibility = Visibility.Visible;
        control.RefreshFilteredList();
    }

    public static readonly DependencyProperty ToggleCommandProperty =
        DependencyProperty.Register(nameof(ToggleCommand), typeof(ICommand), typeof(TagFlyout));

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    public static readonly DependencyProperty NewTagCommandProperty =
        DependencyProperty.Register(nameof(NewTagCommand), typeof(ICommand), typeof(TagFlyout));

    public ICommand? NewTagCommand
    {
        get => (ICommand?)GetValue(NewTagCommandProperty);
        set => SetValue(NewTagCommandProperty, value);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilteredList();

    private void RefreshFilteredList()
    {
        if (Tags is null)
        {
            TagsList.ItemsSource = null;
            return;
        }

        var text = FilterBox.Text;
        TagsList.ItemsSource = string.IsNullOrWhiteSpace(text)
            ? Tags.ToList()
            : Tags.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void TagsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not TagFlyoutItem item) return;

        var applying = item.State != TagCheckState.Checked; // Unchecked or Indeterminate -> apply; Checked -> remove
        item.State = applying ? TagCheckState.Checked : TagCheckState.Unchecked; // optimistic UI update
        ToggleCommand?.Execute((item.Name, applying));
    }

    private void NewTagLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        NewTagLabel.Visibility = Visibility.Collapsed;
        NewTagBox.Visibility = Visibility.Visible;
        NewTagBox.Focus();
    }

    private void NewTagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitNewTag();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelNewTag();
            e.Handled = true;
        }
    }

    private void NewTagBox_LostFocus(object sender, RoutedEventArgs e) => CancelNewTag();

    private void CommitNewTag()
    {
        var name = NewTagBox.Text.Trim();
        if (name.Length > 0)
            NewTagCommand?.Execute(name);
        CancelNewTag();
    }

    private void CancelNewTag()
    {
        NewTagBox.Text = "";
        NewTagBox.Visibility = Visibility.Collapsed;
        NewTagLabel.Visibility = Visibility.Visible;
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build OmniCard.slnx`
Expected: build succeeds with no errors (WPF XAML/code-behind compiles cleanly; there is no runtime test for this step).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Controls/CheckStateToNullableBoolConverter.cs OmniCard.Controls/TagFlyout.xaml OmniCard.Controls/TagFlyout.xaml.cs
git commit -m "Add TagFlyout control"
```

---

## Task 5: `CollectionViewModel` wiring

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs`
- Modify: `OmniCard.Shared/Models/CollectionCard.cs`
- Test: `OmniCard.Tests/ViewModels/CollectionViewModelTests.cs`

**Interfaces:**
- Consumes: `ITagService.GetAllTags()`, `GetTagsByLots(IEnumerable<int>)`, `AddTagToLots(IEnumerable<int>, string)`, `RemoveTagFromLots(IEnumerable<int>, string)` (Task 1); `TagFlyoutItem`/`TagCheckState`/`TagTriState.Compute(int, int)` (Task 2).
- Produces: `CollectionViewModel.TagFlyoutItems` (`ObservableCollection<TagFlyoutItem>`), `LoadTagFlyoutItems()` (void), `ToggleTagFlyoutItemCommand` (`IRelayCommand<(string Name, bool Apply)>`), `CreateTagFlyoutItemCommand` (`IRelayCommand<string>`). Consumed by Tasks 7 and 8.

- [ ] **Step 1: Write the failing tests**

Add to `OmniCard.Tests/ViewModels/CollectionViewModelTests.cs`, after the last test (currently ending at line 186), inside the class:

```csharp
    [Fact]
    public void LoadTagFlyoutItems_NoSelection_ProducesEmptyList()
    {
        var vm = CreateVm();
        vm.GetSelectedCards = () => [];

        vm.LoadTagFlyoutItems();

        Assert.Empty(vm.TagFlyoutItems);
    }

    [Fact]
    public void LoadTagFlyoutItems_ComputesTriStateAcrossSelection()
    {
        _tags.Setup(t => t.GetAllTags()).Returns([
            new TagSummary { Id = 1, Name = "Foil", UsageCount = 2 },
            new TagSummary { Id = 2, Name = "PSA", UsageCount = 1 },
            new TagSummary { Id = 3, Name = "Unused", UsageCount = 0 },
        ]);
        _tags.Setup(t => t.GetTagsByLots(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10, 20 }))))
             .Returns(new Dictionary<int, List<string>>
             {
                 [10] = ["Foil", "PSA"],
                 [20] = ["Foil"],
             });

        var vm = CreateVm();
        vm.GetSelectedCards = () => [new CollectionCard { Id = 10 }, new CollectionCard { Id = 20 }];

        vm.LoadTagFlyoutItems();

        Assert.Equal(TagCheckState.Checked, vm.TagFlyoutItems.Single(t => t.Name == "Foil").State);
        Assert.Equal(TagCheckState.Indeterminate, vm.TagFlyoutItems.Single(t => t.Name == "PSA").State);
        Assert.Equal(TagCheckState.Unchecked, vm.TagFlyoutItems.Single(t => t.Name == "Unused").State);
    }

    [Fact]
    public void ToggleTagFlyoutItem_Apply_WritesTagAndUpdatesDisplayedCard()
    {
        var vm = CreateVm();
        var card = new CollectionCard { Id = 10 };
        vm.GetSelectedCards = () => [card];

        vm.ToggleTagFlyoutItemCommand.Execute(("Foil", true));

        _tags.Verify(t => t.AddTagToLots(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10 })), "Foil"), Times.Once);
        Assert.Contains("Foil", card.Tags);
    }

    [Fact]
    public void ToggleTagFlyoutItem_Remove_WritesRemovalAndUpdatesDisplayedCard()
    {
        var vm = CreateVm();
        var card = new CollectionCard { Id = 10, Tags = ["Foil"] };
        vm.GetSelectedCards = () => [card];

        vm.ToggleTagFlyoutItemCommand.Execute(("Foil", false));

        _tags.Verify(t => t.RemoveTagFromLots(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10 })), "Foil"), Times.Once);
        Assert.DoesNotContain("Foil", card.Tags);
    }

    [Fact]
    public void ToggleTagFlyoutItem_ExpandsStackedIds()
    {
        var vm = CreateVm();
        var card = new CollectionCard { Id = 10, StackedIds = [10, 11, 12] };
        vm.GetSelectedCards = () => [card];

        vm.ToggleTagFlyoutItemCommand.Execute(("Foil", true));

        _tags.Verify(t => t.AddTagToLots(It.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 10, 11, 12 })), "Foil"), Times.Once);
    }

    [Fact]
    public void CreateTagFlyoutItem_TrimsAndAppliesAsNewChecked()
    {
        var vm = CreateVm();
        var card = new CollectionCard { Id = 10 };
        vm.GetSelectedCards = () => [card];

        vm.CreateTagFlyoutItemCommand.Execute("  Brand New  ");

        _tags.Verify(t => t.AddTagToLots(It.IsAny<IEnumerable<int>>(), "Brand New"), Times.Once);
        Assert.Contains("Brand New", card.Tags);
        Assert.Equal(TagCheckState.Checked, vm.TagFlyoutItems.Single(t => t.Name == "Brand New").State);
    }

    [Fact]
    public void CreateTagFlyoutItem_BlankName_IsNoOp()
    {
        var vm = CreateVm();
        vm.GetSelectedCards = () => [new CollectionCard { Id = 10 }];

        vm.CreateTagFlyoutItemCommand.Execute("   ");

        _tags.Verify(t => t.AddTagToLots(It.IsAny<IEnumerable<int>>(), It.IsAny<string>()), Times.Never);
    }
```

Add the required `using` at the top of the test file if not already present: `using OmniCard.Controls;` (for `TagCheckState`) — `OmniCard.Tests` already references `OmniCard.Controls.csproj` (verified in the `.csproj`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CollectionViewModelTests"`
Expected: build FAILS — `TagFlyoutItems`, `LoadTagFlyoutItems`, `ToggleTagFlyoutItemCommand`, `CreateTagFlyoutItemCommand` do not exist on `CollectionViewModel` yet.

- [ ] **Step 3: Make `CollectionCard.Tags` a notifying property**

In `OmniCard.Shared/Models/CollectionCard.cs`, replace (lines 36-39):

```csharp
    /// <summary>User-defined tags on this physical copy. Populated in a separate pass after the
    /// base query, same as <see cref="ListingStatus"/> — there's no scalar tags column to
    /// project directly.</summary>
    public List<string> Tags { get; set; } = [];
```

with:

```csharp
    /// <summary>User-defined tags on this physical copy. Populated in a separate pass after the
    /// base query, same as <see cref="ListingStatus"/> — there's no scalar tags column to
    /// project directly. Reassigning (not mutating in place) notifies bound tile badges.</summary>
    public List<string> Tags
    {
        get => _tags;
        set
        {
            _tags = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tags)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTags)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TagsDisplay)));
        }
    }
    private List<string> _tags = [];
```

- [ ] **Step 4: Add the flyout members to `CollectionViewModel`**

In `OmniCard/Views/Root/CollectionViewModel.cs`, replace the existing `AddTagsToSelected` method (lines 829-843):

```csharp
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void AddTagsToSelected()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;

        var tags = _dialogService.PickTags();
        if (tags is null or { Count: 0 }) return;

        foreach (var tag in tags)
            _tagService.AddTagToLots(ids, tag);

        ReportMessage?.Invoke($"Added {tags.Count} tag(s) to {ids.Count} card(s).");
        _ = SearchCollection();
    }
```

with:

```csharp
    /// <summary>Backing list for the "Tags..." flyout — recomputed by <see cref="LoadTagFlyoutItems"/>
    /// immediately before the flyout's popup opens, so it reflects the current selection.</summary>
    public ObservableCollection<Controls.TagFlyoutItem> TagFlyoutItems { get; } = [];

    public void LoadTagFlyoutItems()
    {
        TagFlyoutItems.Clear();
        var selectedIds = GetAllSelectedCardIds();
        if (selectedIds.Count == 0) return;

        var tagsByLot = _tagService.GetTagsByLots(selectedIds);
        foreach (var tag in _tagService.GetAllTags())
        {
            var lotsWithTag = selectedIds.Count(id =>
                tagsByLot.TryGetValue(id, out var lotTags) && lotTags.Contains(tag.Name, StringComparer.OrdinalIgnoreCase));

            var state = Controls.TagTriState.Compute(lotsWithTag, selectedIds.Count);
            TagFlyoutItems.Add(new Controls.TagFlyoutItem(tag.Name, state));
        }
    }

    [RelayCommand]
    public void ToggleTagFlyoutItem((string Name, bool Apply) arg)
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;

        if (arg.Apply)
            _tagService.AddTagToLots(ids, arg.Name);
        else
            _tagService.RemoveTagFromLots(ids, arg.Name);

        ApplyTagToDisplayedSelection(arg.Name, arg.Apply);
        ReportMessage?.Invoke(arg.Apply
            ? $"Added tag \"{arg.Name}\" to {ids.Count} card(s)."
            : $"Removed tag \"{arg.Name}\" from {ids.Count} card(s).");
    }

    [RelayCommand]
    public void CreateTagFlyoutItem(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return;

        ToggleTagFlyoutItem((trimmed, true));
        TagFlyoutItems.Add(new Controls.TagFlyoutItem(trimmed, Controls.TagCheckState.Checked));
    }

    /// <summary>Updates the already-bound <see cref="CollectionCard"/> row objects in place so
    /// tile badges refresh without a full <see cref="SearchCollection"/> re-query (which would
    /// disturb scroll position and could close the still-open Tags popup).</summary>
    private void ApplyTagToDisplayedSelection(string tagName, bool applied)
    {
        var selectedCards = GetSelectedCards?.Invoke();
        if (selectedCards is null) return;

        foreach (var card in selectedCards)
        {
            card.Tags = applied
                ? (card.Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase) ? card.Tags : [.. card.Tags, tagName])
                : card.Tags.Where(t => !string.Equals(t, tagName, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
```

- [ ] **Step 5: Remove the now-dead `CanExecute` wiring**

In `OmniCard/Views/Root/CollectionViewModel.cs`, in `OnSelectedCardCountChanged` (around line 406-414), remove the line:

```csharp
        AddTagsToSelectedCommand.NotifyCanExecuteChanged();
```

(`ToggleTagFlyoutItemCommand`/`CreateTagFlyoutItemCommand` have no `CanExecute` — the "Tags..." `MenuItem`'s `IsEnabled` binds directly to `HasSelection` in Tasks 7/8, matching how `ManageTagsCommand` and other unconditional commands in this file already work.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CollectionViewModelTests"`
Expected: PASS (all existing tests plus the 7 new ones).

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Root/CollectionViewModel.cs OmniCard.Shared/Models/CollectionCard.cs OmniCard.Tests/ViewModels/CollectionViewModelTests.cs
git commit -m "Wire tag flyout load/toggle/create into CollectionViewModel"
```

---

## Task 6: Retire `AddTagsView` / `AddTagsViewModel` / `IDialogService.PickTags`

**Files:**
- Delete: `OmniCard/Views/AddTags/AddTagsView.xaml`
- Delete: `OmniCard/Views/AddTags/AddTagsView.xaml.cs`
- Delete: `OmniCard/Views/AddTags/AddTagsViewModel.cs`
- Modify: `OmniCard.Shared/Interfaces/IDialogService.cs`
- Modify: `OmniCard/Services/DialogService.cs`
- Modify: `OmniCard/App.xaml.cs`

**Interfaces:**
- No other file references `PickTags`/`AddTagsView`/`AddTagsViewModel` outside this list (verified repo-wide via grep before writing this plan) and no test file references them — this task is pure removal, nothing downstream depends on it.

- [ ] **Step 1: Delete the three `AddTags` files**

```bash
git rm OmniCard/Views/AddTags/AddTagsView.xaml OmniCard/Views/AddTags/AddTagsView.xaml.cs OmniCard/Views/AddTags/AddTagsViewModel.cs
```

- [ ] **Step 2: Remove `PickTags` from `IDialogService`**

In `OmniCard.Shared/Interfaces/IDialogService.cs`, remove (lines 39-41):

```csharp
    /// <summary>Prompts for one or more tags (type-to-add with autocomplete) to apply to a bulk
    /// card selection. Returns the entered tag names, or null if cancelled/empty.</summary>
    List<string>? PickTags();
```

- [ ] **Step 3: Remove `PickTags` from `DialogService`**

In `OmniCard/Services/DialogService.cs`, remove (lines 294-301):

```csharp
    public List<string>? PickTags()
    {
        var wnd = Services.GetRequiredService<AddTagsView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }
```

- [ ] **Step 4: Remove the DI registrations**

In `OmniCard/App.xaml.cs`, remove (lines 245-246):

```csharp
            services.AddTransient<Views.AddTags.AddTagsView>();
            services.AddTransient<Views.AddTags.AddTagsViewModel>();
```

- [ ] **Step 5: Build to verify nothing else references the removed members**

Run: `dotnet build OmniCard.slnx`
Expected: build succeeds — no remaining references to `AddTagsView`, `AddTagsViewModel`, or `IDialogService.PickTags` (Task 5 already replaced `CollectionViewModel`'s only caller).

- [ ] **Step 6: Run full test suite to verify nothing broke**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS, same count as before minus zero (no tests referenced the removed types).

- [ ] **Step 7: Commit**

```bash
git add -u OmniCard/Views/AddTags OmniCard.Shared/Interfaces/IDialogService.cs OmniCard/Services/DialogService.cs OmniCard/App.xaml.cs
git commit -m "Retire AddTagsView dialog, superseded by the tag flyout"
```

---

## Task 7: Wire "Tags..." into `CardListView.xaml` (Collection/Locations context menu)

**Files:**
- Modify: `OmniCard/Views/Root/CardListView.xaml`
- Modify: `OmniCard/Views/Root/CardListView.xaml.cs`

**Interfaces:**
- Consumes: `CollectionViewModel.TagFlyoutItems`, `LoadTagFlyoutItems()`, `ToggleTagFlyoutItemCommand`, `CreateTagFlyoutItemCommand`, `HasSelection` (Task 5); `TagFlyout` control (Task 4).

- [ ] **Step 1: Wrap the `ListBox` in a `Grid` and add the Tags popup**

`CardListView.xaml`'s `UserControl` currently has the `ListBox` as its single root child (lines 10-263). Wrap it in a `Grid` so a `Popup` can be added as a sibling. Change the opening (line 10) from:

```xml
    <ListBox x:Name="CollectionListBox"
```

to:

```xml
    <Grid>
    <ListBox x:Name="CollectionListBox"
```

And change the closing (currently lines 263-264):

```xml
    </ListBox>
</UserControl>
```

to:

```xml
    </ListBox>

    <Popup x:Name="TagsPopup" StaysOpen="False" Placement="Right">
        <helpers:TagFlyout Tags="{Binding TagFlyoutItems}"
                            ToggleCommand="{Binding ToggleTagFlyoutItemCommand}"
                            NewTagCommand="{Binding CreateTagFlyoutItemCommand}"/>
    </Popup>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Replace "Add Tag(s)..." with "Tags..." in the context menu**

In `CardListView.xaml`, replace (lines 221-222):

```xml
                <MenuItem Header="Add Tag(s)..."
                          Command="{Binding AddTagsToSelectedCommand}"/>
```

with:

```xml
                <MenuItem Header="Tags..."
                          IsEnabled="{Binding HasSelection}"
                          Click="TagsMenuItem_Click"/>
```

- [ ] **Step 3: Add the click handler**

In `OmniCard/Views/Root/CardListView.xaml.cs`, add (near `GetSelectedCards`, at the end of the class before the final closing brace):

```csharp
    private void TagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not MenuItem menuItem) return;

        ViewModel.LoadTagFlyoutItems();
        TagsPopup.PlacementTarget = menuItem;
        TagsPopup.IsOpen = true;
    }
```

Add `using System.Windows.Controls;` if not already present (it already is, per the existing `using System.Windows.Controls;` at the top of the file).

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard.slnx`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/CardListView.xaml OmniCard/Views/Root/CardListView.xaml.cs
git commit -m "Add Tags flyout to Collection/Locations context menu"
```

---

## Task 8: Wire "Tags..." into `RootView.xaml` "Selection" main menu (Collection/Locations)

**Files:**
- Modify: `OmniCard/Views/Root/RootView.xaml`
- Modify: `OmniCard/Views/Root/RootView.xaml.cs`

**Interfaces:**
- Consumes: same `CollectionViewModel` members as Task 7, reached via `ViewModel.Collection.*` bindings (the existing pattern in this file's "Selection" menu).

- [ ] **Step 1: Add the `helpers` XML namespace**

`RootView.xaml` does not yet import `OmniCard.Controls`. Add to the `<Window ...>` opening tag's attribute list (after the existing `xmlns:conv=...` on line 10):

```xml
        xmlns:helpers="clr-namespace:OmniCard.Controls;assembly=OmniCard.Controls"
```

- [ ] **Step 2: Add a Tags popup for the Collection main menu**

`RootView.xaml`'s root content is a `<Grid>` spanning lines 116-404. Add, as a new child of that `Grid` (e.g. immediately before its closing `</Grid>` on line 404):

```xml
        <Popup x:Name="CollectionMenuTagsPopup" StaysOpen="False" Placement="Right">
            <helpers:TagFlyout Tags="{Binding ViewModel.Collection.TagFlyoutItems}"
                                ToggleCommand="{Binding ViewModel.Collection.ToggleTagFlyoutItemCommand}"
                                NewTagCommand="{Binding ViewModel.Collection.CreateTagFlyoutItemCommand}"/>
        </Popup>
```

- [ ] **Step 3: Replace "Add _Tag(s)..." in the "Selection" menu**

Replace (lines 199-200):

```xml
                <MenuItem Header="Add _Tag(s)..."
                          Command="{Binding ViewModel.Collection.AddTagsToSelectedCommand}"/>
```

with:

```xml
                <MenuItem Header="_Tags..."
                          IsEnabled="{Binding ViewModel.Collection.HasSelection}"
                          Click="CollectionTagsMenuItem_Click"/>
```

- [ ] **Step 4: Add the click handler**

In `OmniCard/Views/Root/RootView.xaml.cs`, add a handler alongside this file's other menu click handlers:

```csharp
    private void CollectionTagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        ViewModel.Collection.LoadTagFlyoutItems();
        CollectionMenuTagsPopup.PlacementTarget = menuItem;
        CollectionMenuTagsPopup.IsOpen = true;
    }
```

(Match this file's existing convention for accessing the root `ViewModel` — if other handlers in this file use a different accessor than `ViewModel.Collection`, e.g. a field, use that same accessor here instead.)

- [ ] **Step 5: Build**

Run: `dotnet build OmniCard.slnx`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/RootView.xaml OmniCard/Views/Root/RootView.xaml.cs
git commit -m "Add Tags flyout to Collection Selection main menu"
```

---

## Task 9: `RootViewModel` wiring (Scanner)

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs`

**Interfaces:**
- Consumes: `ScanTagToggle.Apply`, `ScanTagToggle.CreateAndApply` (Task 3); `TagFlyoutItem`/`TagCheckState`/`TagTriState.Compute` (Task 2); existing `SelectedScannedCards` (`List<ScannedCard>`, line 697), `HasSelection` (line 699), `AllTagNames` (`ObservableCollection<string>`, line 1163), `tagService` (primary-constructor parameter, line 53).
- Produces: `ScanTagFlyoutItems` (`ObservableCollection<TagFlyoutItem>`), `LoadScanTagFlyoutItems()`, `ToggleScanTagFlyoutItemCommand`, `CreateScanTagFlyoutItemCommand`. Consumed by Tasks 10 and 11.

`RootViewModel` has no existing unit test harness in this repo (large constructor, no `RootViewModelTests.cs`). The logic that would otherwise need pinning down here — the tri-state rule and the create-tag trim/apply orchestration — is pushed into the already-independently-tested `TagTriState.Compute` (Task 2) and `ScanTagToggle.CreateAndApply` (Task 3), so `RootViewModel`'s own methods reduce to thin UI-list bookkeeping (`ScanTagFlyoutItems`, `AllTagNames`, `Message`) around those calls, consistent with this repo's precedent of leaving `RootViewModel` itself untested and covering it via the Task 12 manual smoke pass instead.

- [ ] **Step 1: Add the flyout members**

In `OmniCard/Views/Root/RootViewModel.cs`, add near `AllTagNames`/`RefreshTagSuggestions` (after line 1170):

```csharp
    /// <summary>Backing list for the Scanner "Tags..." flyout — recomputed by
    /// <see cref="LoadScanTagFlyoutItems"/> immediately before the flyout's popup opens.</summary>
    public ObservableCollection<Controls.TagFlyoutItem> ScanTagFlyoutItems { get; } = [];

    public void LoadScanTagFlyoutItems()
    {
        ScanTagFlyoutItems.Clear();
        if (SelectedScannedCards.Count == 0) return;

        foreach (var tagName in tagService.GetAllTags().Select(t => t.Name))
        {
            var countWithTag = SelectedScannedCards.Count(c => c.Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase));
            var state = Controls.TagTriState.Compute(countWithTag, SelectedScannedCards.Count);
            ScanTagFlyoutItems.Add(new Controls.TagFlyoutItem(tagName, state));
        }
    }
```

- [ ] **Step 2: Add the toggle/create commands**

In `OmniCard/Views/Root/RootViewModel.cs`, add near the other "Scanner context menu commands" (after `SetScannedFoil`, currently ending at line 2153):

```csharp
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void ToggleScanTagFlyoutItem((string Name, bool Apply) arg)
    {
        OmniCard.Collection.ScanTagToggle.Apply(SelectedScannedCards, arg.Name, arg.Apply);
        Message = arg.Apply
            ? $"Added tag \"{arg.Name}\" to {SelectedScannedCards.Count} card(s)."
            : $"Removed tag \"{arg.Name}\" from {SelectedScannedCards.Count} card(s).";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void CreateScanTagFlyoutItem(string name)
    {
        var trimmed = OmniCard.Collection.ScanTagToggle.CreateAndApply(SelectedScannedCards, name);
        if (trimmed is null) return;

        ScanTagFlyoutItems.Add(new Controls.TagFlyoutItem(trimmed, Controls.TagCheckState.Checked));
        if (!AllTagNames.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            AllTagNames.Add(trimmed); // keep the scan detail panel's TagEditor autocomplete in sync
        Message = $"Added tag \"{trimmed}\" to {SelectedScannedCards.Count} card(s).";
    }
```

- [ ] **Step 3: Wire `CanExecute` refresh**

`SetScannedConditionCommand`/`SetScannedFoilCommand` are also gated by `HasSelection` — find where their `NotifyCanExecuteChanged` is called on selection change (in `UpdateSelection`, around lines 727-769) and add the two new commands alongside them:

```csharp
        SetScannedConditionCommand.NotifyCanExecuteChanged();
        SetScannedFoilCommand.NotifyCanExecuteChanged();
```

becomes:

```csharp
        SetScannedConditionCommand.NotifyCanExecuteChanged();
        SetScannedFoilCommand.NotifyCanExecuteChanged();
        ToggleScanTagFlyoutItemCommand.NotifyCanExecuteChanged();
        CreateScanTagFlyoutItemCommand.NotifyCanExecuteChanged();
```

(If the two existing lines appear at a different location than lines 768-769 by the time this task is implemented, add the two new lines directly beneath them, wherever they are.)

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard.slnx`
Expected: build succeeds.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs
git commit -m "Wire scan tag flyout load/toggle/create into RootViewModel"
```

---

## Task 10: Wire "Tags..." into `ScannerTabView.xaml` (Scanner context menu)

**Files:**
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml`
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml.cs`

**Interfaces:**
- Consumes: `RootViewModel.ScanTagFlyoutItems`, `LoadScanTagFlyoutItems()`, `ToggleScanTagFlyoutItemCommand`, `CreateScanTagFlyoutItemCommand`, `HasSelection` (Task 9); `TagFlyout` control (Task 4).

- [ ] **Step 1: Add a Tags popup**

`ScannerTabView.xaml`'s root is already a `<Grid>` (lines 11-797). Add, as a new child (e.g. immediately before its closing `</Grid>` on line 797):

```xml
        <Popup x:Name="ScanTagsPopup" StaysOpen="False" Placement="Right">
            <helpers:TagFlyout Tags="{Binding ViewModel.ScanTagFlyoutItems}"
                                ToggleCommand="{Binding ViewModel.ToggleScanTagFlyoutItemCommand}"
                                NewTagCommand="{Binding ViewModel.CreateScanTagFlyoutItemCommand}"/>
        </Popup>
```

(`helpers` is already imported in this file, per line 6: `xmlns:helpers="clr-namespace:OmniCard.Controls;assembly=OmniCard.Controls"`. `Popup` needs no namespace import — it's in the default `presentation` namespace already imported.)

- [ ] **Step 2: Add "Tags..." to the context menu**

In `ScannerTabView.xaml`, add after the "Set Non-Foil" `MenuItem` and before the "Assign Match" `Separator` (currently lines 503-506):

```xml
                            <MenuItem Header="Set Non-Foil"
                                      Command="{Binding ViewModel.SetScannedFoilCommand}"
                                      CommandParameter="False"/>
                            <MenuItem Header="Tags..."
                                      IsEnabled="{Binding ViewModel.HasSelection}"
                                      Click="ScanTagsMenuItem_Click"/>
                            <Separator/>
```

- [ ] **Step 3: Add the click handler**

In `OmniCard/Views/Root/ScannerTabView.xaml.cs`, add (near the other private event handlers, before the final closing brace):

```csharp
    private void ScanTagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not MenuItem menuItem) return;

        ViewModel.LoadScanTagFlyoutItems();
        ScanTagsPopup.PlacementTarget = menuItem;
        ScanTagsPopup.IsOpen = true;
    }
```

Add `using System.Windows.Controls;` if not already present in this file (check the existing `using` list — this file currently imports `System.Windows`, `System.Windows.Controls.Primitives`, etc.; `System.Windows.Controls` for `MenuItem`/`RoutedEventArgs` needs to be present or added).

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard.slnx`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/ScannerTabView.xaml OmniCard/Views/Root/ScannerTabView.xaml.cs
git commit -m "Add Tags flyout to Scanner context menu"
```

---

## Task 11: Wire "Tags..." into `RootView.xaml` "_Scanner" main menu

**Files:**
- Modify: `OmniCard/Views/Root/RootView.xaml`
- Modify: `OmniCard/Views/Root/RootView.xaml.cs`

**Interfaces:**
- Consumes: same `RootViewModel` members as Task 10, reached via the direct `ViewModel.*` bindings this file's "_Scanner" menu already uses (e.g. `ViewModel.SetScannedConditionCommand` at line 260).

- [ ] **Step 1: Add a Tags popup for the Scanner main menu**

Add, as another child of the root `Grid` (alongside `CollectionMenuTagsPopup` from Task 8):

```xml
        <Popup x:Name="ScannerMenuTagsPopup" StaysOpen="False" Placement="Right">
            <helpers:TagFlyout Tags="{Binding ViewModel.ScanTagFlyoutItems}"
                                ToggleCommand="{Binding ViewModel.ToggleScanTagFlyoutItemCommand}"
                                NewTagCommand="{Binding ViewModel.CreateScanTagFlyoutItemCommand}"/>
        </Popup>
```

- [ ] **Step 2: Add "Tags..." to the "_Scanner" menu**

In `RootView.xaml`, add after the "Set Non-Foil" `MenuItem` and before "_Assign Match" (currently around lines 266-272):

```xml
                <MenuItem Header="Tags..."
                          IsEnabled="{Binding ViewModel.HasSelection}"
                          Click="ScannerMenuTagsMenuItem_Click"/>
```

- [ ] **Step 3: Add the click handler**

In `OmniCard/Views/Root/RootView.xaml.cs`:

```csharp
    private void ScannerMenuTagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        ViewModel.LoadScanTagFlyoutItems();
        ScannerMenuTagsPopup.PlacementTarget = menuItem;
        ScannerMenuTagsPopup.IsOpen = true;
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard.slnx`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/RootView.xaml OmniCard/Views/Root/RootView.xaml.cs
git commit -m "Add Tags flyout to Scanner main menu"
```

---

## Task 12: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: Full build**

Run: `dotnet build OmniCard.slnx`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 2: Full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS, including every test added in Tasks 1, 2, 3, and 5 (`RemoveTagFromLots` ×4, `TagFlyoutItem` ×4, `ScanTagToggle` ×4, `CollectionViewModel` tag-flyout tests ×7 — 19 new tests total).

- [ ] **Step 3: Manual GUI smoke — Collection/Locations**

Run: `dotnet run --project OmniCard/OmniCard.csproj`

- Right-click a single untagged card → "Tags..." → flyout opens showing every existing tag unchecked; check one → badge appears on the tile immediately, popup stays open; check a second tag; click away → popup closes; re-open "Tags..." on the same card → both show `Checked`.
- On a stacked tile (`Quantity > 1`), apply a tag → confirm the DB write reaches every id in the stack (spot-check via "Manage Tags..." usage count, or by un-stacking the view and checking each copy).
- Multi-select several different cards with only partially-overlapping tags → open "Tags..." → a tag present on some but not all shows the indeterminate glyph; click it → becomes checked on all; click again → unchecked on all.
- Click "+ New Tag...", type a brand-new name, press Enter → tag is created, applied, and appears checked in the still-open list; open "Manage Tags..." → new tag is present with the expected usage count.
- Type into the flyout's filter box with 10+ tags present → list narrows live, case-insensitively.
- Repeat the single-card and multi-select checks from the "Selection" main menu's "Tags..." entry (not just the context menu) → same behavior.
- Drill into a Location (Locations tab → a location tile) → confirm "Tags..." behaves identically there (same `CardListView`).

- [ ] **Step 4: Manual GUI smoke — Scanner**

- Scan or paste-assign at least two cards into the queue.
- Right-click one scanned card → "Tags..." → apply an existing tag → open the card's detail panel → confirm the `TagEditor` chip for that tag is present (same `ScannedCard.Tags` collection).
- From the detail panel, add a different tag via the existing `TagEditor` → right-click the same card → "Tags..." → confirm that tag shows `Checked` (proves both paths share the same underlying collection).
- Multi-select 2+ scanned cards with different tags → "Tags..." → verify indeterminate/checked states, toggle, and "+ New Tag..." all behave the same as Collection.
- Commit the scan (with tags applied via the flyout) → open the resulting card(s) in Collection → confirm the tags survived the commit.
- Repeat via the "_Scanner" main menu's "Tags..." entry.

- [ ] **Step 5: Report results**

If every check in Steps 3-4 passes, the feature is complete and ready for the user's own review. If anything deviates, note the exact repro steps and file a follow-up rather than silently reworking the design.
