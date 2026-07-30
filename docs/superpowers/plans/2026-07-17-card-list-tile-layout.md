# Card List Tile Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the collection/location `DataGrid` card list with a tile/wrap-panel layout showing scan (or downloaded) art plus Name, Set (Code), Market price, and — when stacking is on — Quantity.

**Architecture:** Extract a pure art-source decision helper into `OmniCard.Shared` (unit-tested), consume it from a new WPF `IMultiValueConverter` that loads the chosen `ImageSource` via the existing image caches, then rewrite `CardListView` from a `DataGrid` into a `ListBox` whose `ItemsPanel` is a `WrapPanel`. The `ListBox` preserves multi-select, context menu, double-click-to-edit, and incremental scroll-loading. The column chooser UI is removed from the toolbar (the view-model's column-visibility machinery stays, since it is also used for settings persistence).

**Tech Stack:** .NET 10 (net10.0-windows), WPF, MaterialDesignInXaml, CommunityToolkit.Mvvm, xUnit + Xunit.StaFact.

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`; nullable enabled; implicit usings enabled.
- Branding copy rule (org): render "Innergy" as "INNERGY" and "DESIGN" as "ENGINEERING" in any user-facing copy. (No such strings appear in this work; noted for compliance.)
- Follow existing converter pattern: converters are `MarkupExtension` + `IValueConverter`/`IMultiValueConverter` in `OmniCard.Controls/Converters/RootConverters.cs`, used in XAML as `{conv:ConverterName}`.
- Scan images are stored as paths relative to `CollectionViewModel.DataDirectory`; resolve with `Path.Combine(dataDir, card.ScanImagePath)`. Downloaded art loads via `CardArtCache.Instance.GetImage(null, card.ImageUri)`.
- WPF bitmap tests must use `[StaFact]` (STA thread required for `BitmapImage`).
- Commit after each task. Branch: `feat/card-list-tile-layout` (already checked out).

---

### Task 1: Pure art-source decision helper

Decides, for a given card and stacking mode, the ordered list of art sources to try. Pure (no WPF), so it is unit-tested directly.

**Files:**
- Create: `OmniCard.Shared/Models/CardArtCandidateResolver.cs`
- Test: `OmniCard.Tests/Services/CardArtCandidateResolverTests.cs`

**Interfaces:**
- Consumes: `OmniCard.Models.CollectionCard` (existing; fields `ScanImagePath`, `ImageUri`).
- Produces:
  - `enum OmniCard.Models.CardArtKind { Scan, Downloaded }`
  - `readonly record struct OmniCard.Models.CardArtCandidate(CardArtKind Kind, string Value)`
  - `static IReadOnlyList<CardArtCandidate> CardArtCandidateResolver.Resolve(CollectionCard card, bool isStacked)`

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/CardArtCandidateResolverTests.cs`:

```csharp
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class CardArtCandidateResolverTests
{
    [Fact]
    public void Unstacked_WithScan_ReturnsScanOnly()
    {
        var card = new CollectionCard { ScanImagePath = "scan.png", ImageUri = "http://x/art.png" };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: false);
        Assert.Single(result);
        Assert.Equal(CardArtKind.Scan, result[0].Kind);
        Assert.Equal("scan.png", result[0].Value);
    }

    [Fact]
    public void Unstacked_WithoutScan_ReturnsEmpty_EvenWhenDownloadedExists()
    {
        var card = new CollectionCard { ScanImagePath = null, ImageUri = "http://x/art.png" };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: false);
        Assert.Empty(result);
    }

    [Fact]
    public void Stacked_WithBoth_ReturnsDownloadedThenScan()
    {
        var card = new CollectionCard { ScanImagePath = "scan.png", ImageUri = "http://x/art.png" };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: true);
        Assert.Equal(2, result.Count);
        Assert.Equal(CardArtKind.Downloaded, result[0].Kind);
        Assert.Equal("http://x/art.png", result[0].Value);
        Assert.Equal(CardArtKind.Scan, result[1].Kind);
        Assert.Equal("scan.png", result[1].Value);
    }

    [Fact]
    public void Stacked_WithOnlyScan_ReturnsScan()
    {
        var card = new CollectionCard { ScanImagePath = "scan.png", ImageUri = null };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: true);
        Assert.Single(result);
        Assert.Equal(CardArtKind.Scan, result[0].Kind);
    }

    [Fact]
    public void Stacked_WithNeither_ReturnsEmpty()
    {
        var card = new CollectionCard { ScanImagePath = null, ImageUri = null };
        var result = CardArtCandidateResolver.Resolve(card, isStacked: true);
        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CardArtCandidateResolverTests"`
Expected: FAIL to build — `CardArtCandidateResolver`/`CardArtKind`/`CardArtCandidate` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `OmniCard.Shared/Models/CardArtCandidateResolver.cs`:

```csharp
namespace OmniCard.Models;

/// <summary>Which art source a candidate refers to.</summary>
public enum CardArtKind
{
    Scan,
    Downloaded
}

/// <summary>An ordered art source to try. Value is a scan path (relative to the data dir) or a download URI.</summary>
public readonly record struct CardArtCandidate(CardArtKind Kind, string Value);

/// <summary>
/// Decides which art sources to try, in order, for a collection card.
/// Not stacked: scanned art only.
/// Stacked: downloaded art first, then the stack representative's scanned art.
/// Empty result means no art is available -> the view shows a placeholder.
/// </summary>
public static class CardArtCandidateResolver
{
    public static IReadOnlyList<CardArtCandidate> Resolve(CollectionCard card, bool isStacked)
    {
        var candidates = new List<CardArtCandidate>();

        if (isStacked)
        {
            if (!string.IsNullOrEmpty(card.ImageUri))
                candidates.Add(new CardArtCandidate(CardArtKind.Downloaded, card.ImageUri));
            if (!string.IsNullOrEmpty(card.ScanImagePath))
                candidates.Add(new CardArtCandidate(CardArtKind.Scan, card.ScanImagePath));
        }
        else
        {
            if (!string.IsNullOrEmpty(card.ScanImagePath))
                candidates.Add(new CardArtCandidate(CardArtKind.Scan, card.ScanImagePath));
        }

        return candidates;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CardArtCandidateResolverTests"`
Expected: PASS (5 passed).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Shared/Models/CardArtCandidateResolver.cs OmniCard.Tests/Services/CardArtCandidateResolverTests.cs
git commit -m "feat: add card art source resolver for tile layout"
```

---

### Task 2: Tile art multi-value converter

Thin WPF glue: runs the resolver, then loads the first candidate that yields an image via the existing caches. Logic is already covered by Task 1; this task is build-verified (no unit test — the converter depends on global cache singletons, and driving real bitmaps is verified end-to-end in Task 5).

**Files:**
- Modify: `OmniCard.Controls/Converters/RootConverters.cs` (append new class; add `using System.IO;` and `using System.Windows.Media;` if not present at top)

**Interfaces:**
- Consumes: `CardArtCandidateResolver.Resolve` (Task 1); `ScanImageCache.Instance`, `CardArtCache.Instance` (existing).
- Produces: `class OmniCard.Controls.Converters.TileArtConverter : MarkupExtension, IMultiValueConverter`. Bind order: `[0]=CollectionCard`, `[1]=bool isStacked`, `[2]=string dataDirectory`. Returns `ImageSource?`.

- [ ] **Step 1: Confirm required usings at top of `RootConverters.cs`**

The file already has `using System.Globalization;`, `using System.Windows.Data;`, `using System.Windows.Markup;`, `using OmniCard.Imaging;`, `using OmniCard.Models;`. Add these two if missing (place with the other `using`s at the top):

```csharp
using System.IO;
using System.Windows.Media;
```

- [ ] **Step 2: Append the converter class**

Add at the end of `OmniCard.Controls/Converters/RootConverters.cs` (before or after the last class, inside the namespace):

```csharp
/// <summary>
/// Resolves the tile image for a collection card. Bindings, in order:
/// [0] CollectionCard, [1] bool IsStacked, [2] string data directory.
/// Returns the first available ImageSource (see <see cref="CardArtCandidateResolver"/>),
/// or null when no art is available (the tile then shows a placeholder).
/// </summary>
public class TileArtConverter : MarkupExtension, IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not CollectionCard card)
            return null;

        var isStacked = values[1] is true;
        var dataDir = values[2] as string ?? "";

        foreach (var candidate in CardArtCandidateResolver.Resolve(card, isStacked))
        {
            ImageSource? image = candidate.Kind switch
            {
                CardArtKind.Scan =>
                    ScanImageCache.Instance?.GetImage(Path.Combine(dataDir, candidate.Value)),
                CardArtKind.Downloaded =>
                    CardArtCache.Instance?.GetImage(null, candidate.Value),
                _ => null
            };

            if (image is not null)
                return image;
        }

        return null;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build OmniCard.Controls/OmniCard.Controls.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Controls/Converters/RootConverters.cs
git commit -m "feat: add TileArtConverter for card tile art"
```

---

### Task 3: Rewrite CardListView.xaml as a tile ListBox

Replace the entire `DataGrid` with a `ListBox` using a `WrapPanel` items panel, a tile item template (art + placeholder + text + qty), a selection-highlight container style, and the existing context menu moved onto the `ListBox`.

**Files:**
- Modify: `OmniCard/Views/Root/CardListView.xaml` (full body replacement)

**Interfaces:**
- Consumes: `TileArtConverter` (Task 2); existing converters `BoolToVisibilityConverter`, `NullToVisibleConverter`; view-model bindings `CollectionSearchResults`, `SelectedCollectionCard`, `IsStacked`, `DataDirectory`, and the context-menu commands (unchanged from the old grid).
- Produces: a `ListBox` named `CollectionListBox` (referenced by Task 4 code-behind), replacing `CollectionDataGrid`.

- [ ] **Step 1: Replace the file contents**

Overwrite `OmniCard/Views/Root/CardListView.xaml` with:

```xml
<UserControl x:Class="OmniCard.Views.Root.CardListView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:OmniCard.Controls.Converters;assembly=OmniCard.Controls"
             xmlns:i="http://schemas.microsoft.com/xaml/behaviors">

    <ListBox x:Name="CollectionListBox"
             ItemsSource="{Binding CollectionSearchResults}"
             SelectedItem="{Binding SelectedCollectionCard}"
             SelectionMode="Extended"
             HorizontalContentAlignment="Stretch"
             ScrollViewer.HorizontalScrollBarVisibility="Disabled"
             ScrollViewer.VerticalScrollBarVisibility="Auto"
             VirtualizingPanel.IsVirtualizing="False"
             SelectionChanged="CollectionListBox_SelectionChanged"
             PreviewMouseRightButtonDown="CollectionListBox_PreviewMouseRightButtonDown">

        <ListBox.ItemsPanel>
            <ItemsPanelTemplate>
                <WrapPanel Orientation="Horizontal"/>
            </ItemsPanelTemplate>
        </ListBox.ItemsPanel>

        <!-- Tile container: flat, with a selection highlight border -->
        <ListBox.ItemContainerStyle>
            <Style TargetType="ListBoxItem">
                <Setter Property="Margin" Value="0"/>
                <Setter Property="Padding" Value="0"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ListBoxItem">
                            <Border x:Name="SelBorder"
                                    BorderThickness="2"
                                    BorderBrush="Transparent"
                                    CornerRadius="8"
                                    Background="Transparent">
                                <ContentPresenter/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsSelected" Value="True">
                                    <Setter TargetName="SelBorder" Property="BorderBrush"
                                            Value="{DynamicResource MaterialDesign.Brush.Primary}"/>
                                </Trigger>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="SelBorder" Property="BorderBrush"
                                            Value="{DynamicResource MaterialDesign.Brush.TextBox.HoverBackground}"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ListBox.ItemContainerStyle>

        <!-- Tile: art (or placeholder) + name + set + price + qty -->
        <ListBox.ItemTemplate>
            <DataTemplate>
                <Border Width="160"
                        Margin="6"
                        Padding="6"
                        Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                        BorderBrush="{DynamicResource MaterialDesign.Brush.TextBox.HoverBackground}"
                        BorderThickness="1"
                        CornerRadius="6">
                    <StackPanel>
                        <!-- Art area (63:88 card ratio) -->
                        <Grid Width="148" Height="207" Margin="0,0,0,6">
                            <!-- Placeholder shown when the image resolves to null -->
                            <Border CornerRadius="6"
                                    Background="{DynamicResource MaterialDesign.Brush.TextBox.HoverBackground}"
                                    Visibility="{Binding Source, ElementName=TileImage,
                                        Converter={conv:NullToVisibleConverter}}">
                                <TextBlock Text="No Image"
                                           HorizontalAlignment="Center"
                                           VerticalAlignment="Center"
                                           FontStyle="Italic"
                                           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                            </Border>
                            <Image x:Name="TileImage" Stretch="Uniform">
                                <Image.Source>
                                    <MultiBinding Converter="{conv:TileArtConverter}">
                                        <Binding/>
                                        <Binding Path="DataContext.IsStacked"
                                                 RelativeSource="{RelativeSource AncestorType=ListBox}"/>
                                        <Binding Path="DataContext.DataDirectory"
                                                 RelativeSource="{RelativeSource AncestorType=ListBox}"/>
                                    </MultiBinding>
                                </Image.Source>
                            </Image>
                        </Grid>

                        <!-- Name -->
                        <TextBlock Text="{Binding Name}"
                                   FontWeight="Bold"
                                   TextWrapping="Wrap"
                                   MaxHeight="36"
                                   TextTrimming="CharacterEllipsis"
                                   ToolTip="{Binding Name}"/>

                        <!-- Set Name (Set Code) -->
                        <TextBlock Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                                   TextTrimming="CharacterEllipsis">
                            <Run Text="{Binding SetName, Mode=OneWay}"/><Run Text=" ("/><Run Text="{Binding SetCode, Mode=OneWay}"/><Run Text=")"/>
                        </TextBlock>

                        <!-- Market price -->
                        <TextBlock Text="{Binding MarketPrice, StringFormat=${0:F2}}"
                                   FontWeight="SemiBold"/>

                        <!-- Quantity (stacked mode only) -->
                        <TextBlock Text="{Binding Quantity, StringFormat=×{0}}"
                                   FontWeight="SemiBold"
                                   Foreground="{DynamicResource MaterialDesign.Brush.Primary}"
                                   Visibility="{Binding DataContext.IsStacked,
                                       RelativeSource={RelativeSource AncestorType=ListBox},
                                       Converter={conv:BoolToVisibilityConverter}}"/>
                    </StackPanel>
                </Border>
            </DataTemplate>
        </ListBox.ItemTemplate>

        <ListBox.ContextMenu>
            <ContextMenu DataContext="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}">
                <MenuItem Header="Open Card Editor"
                          Command="{Binding CollectionCardDoubleClickCommand}"/>
                <MenuItem Header="Copy Card Name(s)"
                          Command="{Binding CopyCollectionCardNamesCommand}"/>
                <Separator/>
                <MenuItem Header="Move to Location..."
                          Command="{Binding MoveSelectedToLocationCommand}"/>
                <Separator/>
                <MenuItem Header="Set Condition">
                    <MenuItem Header="NM" Command="{Binding BulkSetCollectionConditionCommand}" CommandParameter="NM"/>
                    <MenuItem Header="LP" Command="{Binding BulkSetCollectionConditionCommand}" CommandParameter="LP"/>
                    <MenuItem Header="MP" Command="{Binding BulkSetCollectionConditionCommand}" CommandParameter="MP"/>
                    <MenuItem Header="HP" Command="{Binding BulkSetCollectionConditionCommand}" CommandParameter="HP"/>
                    <MenuItem Header="D"  Command="{Binding BulkSetCollectionConditionCommand}" CommandParameter="D"/>
                </MenuItem>
                <MenuItem Header="Set Foil"
                          Command="{Binding BulkSetCollectionFoilCommand}"
                          CommandParameter="True"/>
                <MenuItem Header="Set Non-Foil"
                          Command="{Binding BulkSetCollectionFoilCommand}"
                          CommandParameter="False"/>
                <Separator/>
                <MenuItem Header="List on eBay"
                          Command="{Binding ListOnEbayCommand}"/>
                <MenuItem Header="View on eBay"
                          Command="{Binding ViewOnEbayCommand}"/>
                <MenuItem Header="End eBay Listing"
                          Command="{Binding EndEbayListingCommand}"/>
                <Separator/>
                <MenuItem Header="Delete Selected"
                          Command="{Binding BulkDeleteCollectionCommand}"/>
            </ContextMenu>
        </ListBox.ContextMenu>

        <i:Interaction.Triggers>
            <i:EventTrigger EventName="MouseDoubleClick">
                <i:InvokeCommandAction Command="{Binding CollectionCardDoubleClickCommand}"/>
            </i:EventTrigger>
        </i:Interaction.Triggers>

    </ListBox>
</UserControl>
```

- [ ] **Step 2: Verify (build happens in Task 4)**

This file will not build until the code-behind (Task 4) is updated to match the new element name and handlers. Do NOT build yet; proceed to Task 4, then build there.

- [ ] **Step 3: Commit**

```bash
git add OmniCard/Views/Root/CardListView.xaml
git commit -m "feat: convert card list to tile ListBox layout"
```

---

### Task 4: Update CardListView code-behind for the ListBox

Retarget the code-behind from `CollectionDataGrid` to `CollectionListBox`, drop the column-sync / header-sort / row-tooltip / loading-row logic (no columns or rows now), keep selection-count, scroll-based incremental loading, `SelectAll`, `GetSelectedCards`, and add right-click-selects-tile so the context menu acts on the clicked tile.

**Files:**
- Modify: `OmniCard/Views/Root/CardListView.xaml.cs` (full replacement)

**Interfaces:**
- Consumes: `CollectionListBox` (Task 3); `CollectionViewModel` (existing: `SelectedCardCount`, `HasMoreResults`, `LoadMore()`).
- Produces (unchanged public surface relied on by callers): `void WireUp(CollectionViewModel)`, `void SelectAll()`, `IList<CollectionCard> GetSelectedCards()`.

- [ ] **Step 1: Replace the file contents**

Overwrite `OmniCard/Views/Root/CardListView.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class CardListView : UserControl
{
    public CollectionViewModel? ViewModel { get; set; }
    private ScrollViewer? _scrollViewer;

    public CardListView()
    {
        InitializeComponent();
    }

    public void WireUp(CollectionViewModel vm)
    {
        ViewModel = vm;
        DataContext = vm;

        // Hook scroll detection for incremental loading
        CollectionListBox.Loaded += (_, _) =>
        {
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

            _scrollViewer = FindVisualChild<ScrollViewer>(CollectionListBox);
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        };
    }

    private async void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv || ViewModel is null || !ViewModel.HasMoreResults)
            return;

        // Load more when scrolled within 20% of the bottom
        if (sv.VerticalOffset >= sv.ScrollableHeight * 0.8 && sv.ScrollableHeight > 0)
            await ViewModel.LoadMore();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void CollectionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.SelectedCardCount = CollectionListBox.SelectedItems.Count;
    }

    // Right-clicking a tile selects it (unless it is part of an existing multi-selection),
    // so the context menu operates on the clicked card like the old DataGrid did.
    private void CollectionListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        var item = FindAncestor<ListBoxItem>(source);
        if (item is null) return;

        if (!item.IsSelected)
        {
            CollectionListBox.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    public void SelectAll() => CollectionListBox.SelectAll();

    public IList<CollectionCard> GetSelectedCards()
        => CollectionListBox.SelectedItems.Cast<CollectionCard>().ToList();
}
```

- [ ] **Step 2: Build the WPF app project**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: FAIL — `CollectionTabView.xaml.cs` still references the removed column chooser (`ColumnChooserLink_Click`, `BuildColumnChooser`, `ColumnChooserList`). This is fixed in Task 5. (If it unexpectedly succeeds, that is also fine.)

- [ ] **Step 3: Commit**

```bash
git add OmniCard/Views/Root/CardListView.xaml.cs
git commit -m "feat: retarget card list code-behind to tile ListBox"
```

---

### Task 5: Remove the column chooser and verify end to end

Remove the now-orphaned column-chooser UI from the collection toolbar and its code-behind, build the whole solution, run the tests, and drive the app to confirm both the entire-collection view and a single-location view render tiles correctly with and without stacking.

**Files:**
- Modify: `OmniCard/Views/Root/CollectionTabView.xaml` (remove Columns button + popup)
- Modify: `OmniCard/Views/Root/CollectionTabView.xaml.cs` (remove `BuildColumnChooser`, `ColumnChooserLink_Click`, and the `ColumnVisibility` PropertyChanged hook)

**Interfaces:**
- Consumes: nothing new.
- Produces: `CollectionTabView` with no column-chooser members. `CollectionViewModel.ColumnVisibility` / `ToggleColumnVisibility` / `GetColumnVisibilityForPersistence` are intentionally left intact (used by settings persistence in `RootViewModel.WriteSettings`).

- [ ] **Step 1: Remove the Columns button + popup from `CollectionTabView.xaml`**

Delete this block (the `Column chooser` button and popup, currently around lines 197-227):

```xml
                    <!-- Column chooser -->
                    <Button x:Name="ColumnChooserLink"
                            Click="ColumnChooserLink_Click"
                            VerticalAlignment="Center"
                            Margin="4,0"
                            Cursor="Hand"
                            Style="{StaticResource MaterialDesignFlatButton}"
                            Padding="4,2"
                            Visibility="{Binding DataContext.ViewModel.Collection.ShowCardList,
                                RelativeSource={RelativeSource AncestorType=Window},
                                Converter={conv:BoolToVisibilityConverter}}">
                        <TextBlock TextDecorations="Underline"
                                   Text="Columns"
                                   Foreground="{DynamicResource MaterialDesign.Brush.Primary}"/>
                    </Button>
                    <Popup x:Name="ColumnChooserPopup"
                           PlacementTarget="{Binding ElementName=ColumnChooserLink}"
                           Placement="Bottom"
                           StaysOpen="False"
                           AllowsTransparency="True"
                           Visibility="{Binding DataContext.ViewModel.Collection.ShowCardList,
                               RelativeSource={RelativeSource AncestorType=Window},
                               Converter={conv:BoolToVisibilityConverter}}">
                        <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                                CornerRadius="4"
                                Padding="8"
                                BorderBrush="{DynamicResource MaterialDesign.Brush.TextBox.HoverBackground}"
                                BorderThickness="1">
                            <ItemsControl x:Name="ColumnChooserList"/>
                        </Border>
                    </Popup>
```

Also delete the `<Separator .../>` immediately preceding it (the one after the "Stack duplicates toggle" block, currently around lines 193-195):

```xml
                    <Separator Visibility="{Binding DataContext.ViewModel.Collection.ShowCardList,
                        RelativeSource={RelativeSource AncestorType=Window},
                        Converter={conv:BoolToVisibilityConverter}}"/>
```

- [ ] **Step 2: Update `CollectionTabView.xaml.cs`**

Replace the `WireUp` method and remove `BuildColumnChooser` and `ColumnChooserLink_Click`. The class should become:

```csharp
using System.Windows.Controls;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public partial class CollectionTabView : UserControl
{
    public RootViewModel? ViewModel { get; set; }

    public CollectionTabView()
    {
        InitializeComponent();
    }

    public void WireUp(CollectionViewModel vm)
    {
        CardList.WireUp(vm);
    }

    public void FocusSearchBox()
    {
        CollectionSearchBox.Focus();
        CollectionSearchBox.SelectAll();
    }

    public void WireUpSealed(SealedProductViewModel vm)
    {
        SealedList.WireUp(vm);
    }

    public void SelectAll() => CardList.SelectAll();

    public IList<CollectionCard> GetSelectedCards() => CardList.GetSelectedCards();
}
```

(Note: the old `_wiredVm` / `_vmHandler` fields and `System.ComponentModel` / `System.Windows` usings are removed because the only PropertyChanged subscription — rebuilding the column chooser — is gone.)

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (If the build tool reports the solution file name explicitly, use `dotnet build OmniCard.sln`.)

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS — all tests green, including `CardArtCandidateResolverTests` (5).

- [ ] **Step 5: Drive the app to verify behavior**

Launch the app (`dotnet run --project OmniCard/OmniCard.csproj`) and confirm:
1. Collection tab → open a location: cards render as tiles in a wrap layout, each showing scan art (or the "No Image" placeholder), Name, "SetName (SetCode)", and "$price". No Quantity line.
2. Toggle **Stack Duplicates** on: duplicate cards collapse into one tile, a "×N" quantity line appears, and art now prefers downloaded art (falling back to scan, then placeholder).
3. **Browse Entire Collection**: same tile layout works across all locations.
4. Multi-select (Ctrl+Click / Shift+Click) updates the "Selected: N" stat; right-click a tile shows the context menu and it acts on the clicked/selected tile(s); double-click opens the Collection Card Editor.
5. Scroll to the bottom of a large collection: more tiles load (incremental loading still fires).
6. Confirm the toolbar no longer shows a "Columns" link and the app does not crash when switching between overview and card-list modes.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/CollectionTabView.xaml OmniCard/Views/Root/CollectionTabView.xaml.cs
git commit -m "feat: remove column chooser now that card list uses tiles"
```

---

## Self-Review

**Spec coverage:**
- Tile layout with art + Name + SetName (SetCode) + Market price → Task 3 template. ✓
- Quantity shown only when stacked → Task 3 (`Visibility` bound to `IsStacked`). ✓
- Not-stacked shows scan art; stacked shows downloaded art with scan fallback → Tasks 1 + 2. ✓
- Placeholder when no art → Task 3 (`NullToVisibleConverter` placeholder). ✓
- Wrap panel holding card data + image → Task 3 (`WrapPanel` items panel). ✓
- Applies to both entire collection and per-location → same `CardListView`, verified Task 5 Step 5.1/5.3. ✓
- Preserve multi-select, context menu, double-click edit, incremental scroll-load → Tasks 3 + 4, verified Task 5 Step 5.4/5.5. ✓
- Fixed tile size → Task 3 (`Width="160"`, art `148×207`). ✓
- Remove column chooser + header sorting → Tasks 3 (no columns/headers) + 5 (toolbar UI). ✓

**Placeholder scan:** No TBD/TODO/"handle edge cases"; all steps contain concrete code or exact commands. ✓

**Type consistency:** `CardArtCandidateResolver.Resolve` / `CardArtKind` / `CardArtCandidate` defined in Task 1 and consumed with matching signatures in Task 2. `TileArtConverter` bind order (`card, isStacked, dataDir`) defined in Task 2 matches the `MultiBinding` order in Task 3. `CollectionListBox` element name defined in Task 3 matches all references in Task 4. Public methods `SelectAll()` / `GetSelectedCards()` / `WireUp()` kept identical to what `CollectionTabView` and `RootView.xaml.cs` call. ✓
