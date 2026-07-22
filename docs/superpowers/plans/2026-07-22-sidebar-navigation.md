# Sidebar Navigation + Dashboard/Home Merge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the top `TabControl` strip with a collapsible left sidebar, and merge the Home view into the Dashboard view (financials on top, collection stats below) as the default-selected first item.

**Architecture:** Keep the existing `TabControl` as the navigation control (preserves the `SelectedIndex` binding, view lifetimes, and all index-based logic). Retemplate it so its header strip renders as a left sidebar with a hamburger collapse toggle. Grow `DashboardView` into a single merged view whose root inherits `RootView`'s DataContext (so Home's `ViewModel.*` bindings work) with the financial block scoped to `ViewModel.Dashboard`. No ViewModel logic is relocated.

**Tech Stack:** WPF, .NET, C#, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]` source generators), MaterialDesignThemes (`PackIcon`), xUnit + Moq (tests).

## Global Constraints

- Organization copy rules: render "Innergy" as "INNERGY" and "DESIGN" as "ENGINEERING" in any user-facing copy. (No such strings appear in this feature's copy, but honor it if any are added.)
- Preserve the integer tab-index contract: after the merge the order is **Dashboard=0, Collection=1, Scanner=2, Sales=3**. Collection must stay index 1 and Scanner must stay index 2 so `SelectedTabIndex = 2` ([RootViewModel.cs:1755](../../../OmniCard/Views/Root/RootViewModel.cs#L1755)) and the `case 1`/`case 2` switches in [RootView.xaml.cs](../../../OmniCard/Views/Root/RootView.xaml.cs) keep working.
- Use theme `DynamicResource` brushes (e.g. `MaterialDesign.Brush.Primary`, `MaterialDesign.Brush.Foreground`, `MaterialDesign.Brush.Card.Background`) for all sidebar colors so light and dark themes both render correctly.
- Follow existing patterns: `[ObservableProperty] public partial` properties with `partial void On<Name>Changed`, persistence via `PersistDisplaySettings()`.

---

## File Structure

- `OmniCard.Shared/Models/DisplaySettings.cs` — add `SidebarExpanded` persisted setting.
- `OmniCard/Views/Root/RootViewModel.cs` — add `IsSidebarExpanded` (persisted) + `RefreshDashboardCommand` (combined refresh); persist the new setting.
- `OmniCard/Views/Dashboard/DashboardView.xaml` — becomes the merged view (financials + collection stats, one scroll, one Refresh).
- `OmniCard/Views/Dashboard/DashboardView.xaml.cs` — drop `WireUp` (DataContext now inherited).
- `OmniCard/Views/Root/RootView.xaml` — remove Home tab; Dashboard first; retemplate `TabControl` as the sidebar.
- `OmniCard/Views/Root/RootView.xaml.cs` — remove the `WireUp` call; trigger `Dashboard.Load()` at startup; keep activation wiring.
- `OmniCard/Views/Root/HomeTabView.xaml` + `.xaml.cs` — deleted (content absorbed).
- `OmniCard.Tests/Models/DisplaySettingsTests.cs` — new unit test for the setting default.

**Testing reality:** The only unit-testable logic here is the `DisplaySettings` default (Task 1). The rest is XAML + wiring with no view-test harness — verified by building and running the app (the spec's Testing section lists the manual checks). Each XAML task ends with a `dotnet build` gate plus a manual run-and-observe checklist.

---

## Task 1: Persisted `IsSidebarExpanded` setting + combined refresh command

**Files:**
- Modify: `OmniCard.Shared/Models/DisplaySettings.cs`
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (property near line 325-328; persistence near line 392; command near line 2215)
- Test: `OmniCard.Tests/Models/DisplaySettingsTests.cs`

**Interfaces:**
- Produces: `DisplaySettings.SidebarExpanded` (bool, default `true`); `RootViewModel.IsSidebarExpanded` (bool, `[ObservableProperty]`, two-way bindable, persisted on change); `RootViewModel.RefreshDashboardCommand` (`IAsyncRelayCommand`, generated from `RefreshDashboard()`).

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Models/DisplaySettingsTests.cs`:

```csharp
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Models;

public class DisplaySettingsTests
{
    [Fact]
    public void SidebarExpanded_DefaultsToTrue()
    {
        var settings = new DisplaySettings();
        Assert.True(settings.SidebarExpanded);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~DisplaySettingsTests"`
Expected: FAIL — compile error, `DisplaySettings` has no `SidebarExpanded` member.

- [ ] **Step 3: Add the setting**

In `OmniCard.Shared/Models/DisplaySettings.cs`, add after the `ShowScannerUI` line:

```csharp
    public bool SidebarExpanded { get; set; } = true;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~DisplaySettingsTests"`
Expected: PASS.

- [ ] **Step 5: Add the `IsSidebarExpanded` VM property + persistence**

In `OmniCard/Views/Root/RootViewModel.cs`, add immediately after the `ShowScannerUI` block (after line 328):

```csharp
    [ObservableProperty]
    public partial bool IsSidebarExpanded { get; set; } = displaySettings.Value.SidebarExpanded;

    partial void OnIsSidebarExpandedChanged(bool value) => PersistDisplaySettings();
```

In the same file, in `WriteDisplaySection`, add after the `ShowScannerUI` write (after line 392, `writer.WriteBoolean("ShowScannerUI", ShowScannerUI);`):

```csharp
        writer.WriteBoolean("SidebarExpanded", IsSidebarExpanded);
```

- [ ] **Step 6: Add the combined `RefreshDashboard` command**

In `OmniCard/Views/Root/RootViewModel.cs`, add directly after the `RefreshHomeTab` command (after line 2215):

```csharp
    /// <summary>Single Refresh for the merged Dashboard view: recomputes the collection stats
    /// (Home section) and the financial valuation (Dashboard section) together.</summary>
    [RelayCommand]
    private async Task RefreshDashboard()
    {
        RefreshHomeTab();                                  // recompute collection stats (fires now; index 0)
        await Dashboard.RefreshCommand.ExecuteAsync(null); // recompute financial valuation
    }
```

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -c Debug`
Expected: Build succeeded (the generated `IsSidebarExpanded`, `OnIsSidebarExpandedChanged`, and `RefreshDashboardCommand` resolve).

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/DisplaySettings.cs OmniCard/Views/Root/RootViewModel.cs OmniCard.Tests/Models/DisplaySettingsTests.cs
git commit -m "feat: add persisted IsSidebarExpanded setting and combined dashboard refresh"
```

---

## Task 2: Merge Home into the Dashboard view

This turns `DashboardView` into the merged view and removes the separate Home tab. The merged view's root inherits `RootView`'s DataContext (exposing `.ViewModel`), so Home's `ViewModel.*` bindings work; the financial block is wrapped in a container scoped to `ViewModel.Dashboard`.

**Files:**
- Modify: `OmniCard/Views/Dashboard/DashboardView.xaml` (full rewrite of the layout; keep the 6 `UserControl.Resources` styles)
- Modify: `OmniCard/Views/Dashboard/DashboardView.xaml.cs`
- Modify: `OmniCard/Views/Root/RootView.xaml` (remove Home `TabItem`; put Dashboard first)
- Modify: `OmniCard/Views/Root/RootView.xaml.cs` (remove `WireUp` call; add startup load)
- Delete: `OmniCard/Views/Root/HomeTabView.xaml`, `OmniCard/Views/Root/HomeTabView.xaml.cs`

**Interfaces:**
- Consumes: `RootViewModel.RefreshDashboardCommand` (Task 1); `RootViewModel.Dashboard` (`DashboardViewModel`, existing); Home stats members on `RootViewModel` (`StatTotalCards`, `StatTotalSets`, `StatFoilCount`, `StatTotalValue`, `StatCommonCount`, `StatUncommonCount`, `StatRareCount`, `StatMythicCount`, `StatOtherRarityCount`, `IsCalculatingCompletion`, `CompletionStatusMessage`, `SetCompletionResults`, `SelectedSetCompletion` — all existing).
- Produces: a `DashboardView` whose runtime DataContext is the `RootView` (i.e. `WireUp` is no longer called).

- [ ] **Step 1: Rewrite `DashboardView.xaml.cs` to drop `WireUp`**

Replace the entire file with:

```csharp
using System.Windows.Controls;

namespace OmniCard.Views.Dashboard;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: Rewrite `DashboardView.xaml` as the merged view**

Replace the file with the structure below. The two large verbatim blocks are copied from the current files with the specific edits called out — copy the exact markup from the referenced line ranges.

```xml
<UserControl x:Class="OmniCard.Views.Dashboard.DashboardView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:OmniCard.Views.Dashboard"
             xmlns:conv="clr-namespace:OmniCard.Controls.Converters;assembly=OmniCard.Controls"
             xmlns:helpers="clr-namespace:OmniCard.Controls;assembly=OmniCard.Controls"
             xmlns:models="clr-namespace:OmniCard.Models;assembly=OmniCard.Shared"
             xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d">

    <UserControl.Resources>
        <!-- COPY VERBATIM: the six styles from DashboardView.xaml lines 13-52
             (TileBorderStyle, BreakdownHeaderStyle, BreakdownGridStyle,
              ChartRowLabelStyle, ChartRowValueStyle, ChartBarStyle). -->
    </UserControl.Resources>

    <Grid Margin="8">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- Refresh + busy -->
            <RowDefinition Height="Auto"/>  <!-- progress bar -->
            <RowDefinition Height="Auto"/>  <!-- status message -->
            <RowDefinition Height="*"/>     <!-- unified scroll -->
        </Grid.RowDefinitions>

        <!-- Single Refresh (refreshes both sections) + busy indicator -->
        <DockPanel Grid.Row="0" Margin="0,0,0,4">
            <Button DockPanel.Dock="Left"
                    Content="Refresh"
                    Command="{Binding ViewModel.RefreshDashboardCommand}"
                    Padding="12,4"
                    FontWeight="SemiBold"/>
            <TextBlock DockPanel.Dock="Left"
                       Text="Refreshing..."
                       VerticalAlignment="Center"
                       Margin="12,0,0,0"
                       FontStyle="Italic"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                       Visibility="{Binding ViewModel.Dashboard.IsBusy, Converter={conv:BoolToVisibilityConverter}}"/>
        </DockPanel>

        <ProgressBar Grid.Row="1"
                     Height="4"
                     Margin="0,0,0,8"
                     IsIndeterminate="True"
                     Visibility="{Binding ViewModel.Dashboard.IsBusy, Converter={conv:BoolToVisibilityConverter}}"/>

        <TextBlock Grid.Row="2"
                   Text="{Binding ViewModel.Dashboard.StatusMessage}"
                   Margin="0,0,0,8"
                   FontStyle="Italic"
                   Foreground="OrangeRed"
                   Visibility="{Binding ViewModel.Dashboard.StatusMessage, Converter={conv:StringToVisibilityConverter}}"/>

        <ScrollViewer Grid.Row="3" VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="4,0,4,8">

                <!-- ============ FINANCIALS (scoped to Dashboard VM) ============ -->
                <StackPanel DataContext="{Binding ViewModel.Dashboard}">
                    <!-- COPY VERBATIM: the summary-tiles Grid from DashboardView.xaml lines 94-160
                         (the <Grid Grid.Row="3"> ... </Grid> block) but REMOVE its `Grid.Row="3"`
                         attribute (it now sits in a StackPanel). -->

                    <!-- COPY VERBATIM: the breakdown content from DashboardView.xaml lines 164-427
                         — i.e. the inner <StackPanel Margin="4,0,4,8"> ... </StackPanel> CONTENTS
                         (Charts header, the three ItemsControl charts, the four DataGrids, and the
                         empty-state TextBlock). Paste those child elements directly here. Do NOT
                         include the outer ScrollViewer (lines 163/428) — this section now relies on
                         the merged view's single ScrollViewer. -->
                </StackPanel>

                <Separator Margin="0,16,0,8"/>

                <!-- ============ COLLECTION STATS (RootView DataContext) ============ -->

                <!-- Loading indicator (from HomeTabView.xaml lines 19-25) -->
                <StackPanel Margin="8"
                            Visibility="{Binding ViewModel.IsCalculatingCompletion, Converter={StaticResource BoolToVis}}">
                    <ProgressBar IsIndeterminate="True" Height="4" Margin="0,0,0,4"/>
                    <TextBlock Text="{Binding ViewModel.CompletionStatusMessage}"
                               Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
                               HorizontalAlignment="Center" Margin="0,8,0,0"/>
                </StackPanel>

                <!-- COPY VERBATIM: the stats Border from HomeTabView.xaml lines 28-84
                     (<Border Grid.Row="1" ...> ... </Border>) but REMOVE its `Grid.Row="1"` attribute. -->

                <!-- Sets-in-collection header (no per-section refresh button; unified Refresh is at top) -->
                <TextBlock Text="Sets in Collection"
                           FontWeight="SemiBold" FontSize="14" Margin="0,8,0,4"
                           Visibility="{Binding ViewModel.IsCalculatingCompletion, Converter={conv:InverseBoolToVisibilityConverter}}"/>

                <!-- Set-completion list: inner scroll DISABLED so the outer ScrollViewer pages it,
                     while SelectedItem -> ExpandSetCompletionCommand click-to-expand is preserved. -->
                <ListView ItemsSource="{Binding ViewModel.SetCompletionResults}"
                          SelectedItem="{Binding ViewModel.SelectedSetCompletion}"
                          ScrollViewer.VerticalScrollBarVisibility="Disabled"
                          ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                          BorderThickness="0"
                          HorizontalContentAlignment="Stretch">
                    <!-- COPY VERBATIM: the ListView children from HomeTabView.xaml lines 111-215
                         (ItemContainerStyle, ItemsPanel with WrapPanel, and the full ItemTemplate). -->
                </ListView>
            </StackPanel>
        </ScrollViewer>
    </Grid>
</UserControl>
```

Notes for the copy blocks:
- The financial bindings (`TotalCost`, `MarketByGameRows`, `Holdings.*`, `Realized.*`, `HasNoHoldings`, etc.) resolve because that whole block's DataContext is `ViewModel.Dashboard`. The `RelativeSource FindAncestor, AncestorType=UserControl` bindings inside the charts still find this `DashboardView` — its DataContext is `ViewModel.Dashboard`, so `DataContext.MarketByGameMax` etc. resolve correctly.
- The collection-stats bindings use `ViewModel.*` against the inherited `RootView` DataContext (same as the old HomeTabView).

- [ ] **Step 3: Remove the Home tab and reorder in `RootView.xaml`**

In `OmniCard/Views/Root/RootView.xaml`, in the `<TabControl>` (lines 167-196): delete the entire `Home` `TabItem` (lines 171-174) and move the `Dashboard` `TabItem` to be first. Resulting tab order and content (leave the `TabControl` element/attributes unchanged in this task — retemplating happens in Task 3):

```xml
        <TabControl x:Name="MainTabControl"
                    Grid.Row="2"
                    SelectedIndex="{Binding ViewModel.SelectedTabIndex}">

            <TabItem Header="Dashboard" x:Name="tabItemDashboard">
                <dashboard:DashboardView x:Name="DashboardTab"/>
            </TabItem>

            <TabItem Header="Collection" x:Name="tabItemCollection">
                <local:CollectionTabView x:Name="CollectionTab"/>
            </TabItem>

            <TabItem Header="Scanner" x:Name="tabItemScanner">
                <local:ScannerTabView x:Name="ScannerTab"/>
            </TabItem>

            <TabItem Header="Sales" x:Name="tabItemSales">
                <sales:SalesView DataContext="{Binding ViewModel.Sales}"/>
            </TabItem>

        </TabControl>
```

- [ ] **Step 4: Update `RootView.xaml.cs` — drop `WireUp`, add startup load**

In `OmniCard/Views/Root/RootView.xaml.cs`, remove the `WireUp` call on line 28:

```csharp
        DashboardTab.WireUp(viewModel.Dashboard);
```

Delete that line. (The `DashboardView` now inherits the Window's DataContext, which is `this` RootView, exposing `.ViewModel` and `.ViewModel.Dashboard`.)

The activation handler at lines 43-44 stays as-is — `MainTabControl.SelectedItem == tabItemDashboard` still calls `viewModel.Dashboard.Load()` when the (now first) Dashboard tab is activated.

Add a startup load so the default-selected Dashboard's financials populate on launch. In `StartAsync`, immediately after `ViewModel.Initialize();` (line 89), add:

```csharp
        // Dashboard is the default-selected tab (index 0); no SelectionChanged fires for the
        // initial selection, so load its financials explicitly. Collection stats are already
        // triggered by InvalidateHomeTab() inside Initialize().
        ViewModel.Dashboard.Load();
```

- [ ] **Step 5: Delete the Home view files**

```bash
git rm OmniCard/Views/Root/HomeTabView.xaml OmniCard/Views/Root/HomeTabView.xaml.cs
```

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -c Debug`
Expected: Build succeeded. If it fails with "HomeTabView could not be found", search for any remaining `HomeTabView` / `local:HomeTabView` references and remove them:
Run: `git grep -n "HomeTabView"`
Expected after cleanup: no matches.

- [ ] **Step 7: Run the app and verify the merge (manual)**

Run the app (use the `run` skill, or `dotnet run --project OmniCard/OmniCard.csproj`). Confirm:
- The Dashboard tab is first and selected on launch.
- Financials (Cost Basis / Market / Unrealized / Realized tiles, charts, holdings tables) show on top and are populated.
- Collection stats (Total Cards / Sets / Foils / Total Value + rarity chips) and "Sets in Collection" tiles show below.
- One scrollbar scrolls the whole page (no nested inner scrollbars fighting).
- The single Refresh button updates both sections.
- Clicking a "Sets in Collection" tile still triggers its expand/navigate behavior.
- Collection (2nd), Scanner (3rd), Sales (4th) tabs still open their views; after a scan the app still jumps to Scanner.

- [ ] **Step 8: Commit**

```bash
git add OmniCard/Views/Dashboard/DashboardView.xaml OmniCard/Views/Dashboard/DashboardView.xaml.cs OmniCard/Views/Root/RootView.xaml OmniCard/Views/Root/RootView.xaml.cs
git commit -m "feat: merge Home view into Dashboard as default first tab"
```

---

## Task 3: Retemplate the `TabControl` as a collapsible left sidebar

Override the `TabControl`'s `ControlTemplate` so the header strip becomes a left sidebar (icon + label per item) with a hamburger toggle that collapses it to an icon rail. Give each `TabItem` an icon+label header whose label hides when collapsed.

**Files:**
- Modify: `OmniCard/Views/Root/RootView.xaml` (add `xmlns:materialDesign`; add sidebar styles to `Window.Resources`; retemplate the `TabControl`; give each `TabItem` an icon+label header).

**Interfaces:**
- Consumes: `RootViewModel.IsSidebarExpanded` (Task 1, two-way).

- [ ] **Step 1: Add the MaterialDesign namespace to the Window**

In `OmniCard/Views/Root/RootView.xaml`, add to the `<Window ...>` opening tag (alongside the other `xmlns:` lines, e.g. after line 9):

```xml
        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
```

- [ ] **Step 2: Add sidebar styles to `Window.Resources`**

In `OmniCard/Views/Root/RootView.xaml`, add a `Window.Resources` block immediately after the `<Window ...>` opening tag closes and before `<Window.InputBindings>` (before line 22):

```xml
    <Window.Resources>
        <!-- Sidebar item: icon + label button chrome with selection / hover states. -->
        <Style x:Key="SidebarTabItemStyle" TargetType="TabItem">
            <Setter Property="Foreground" Value="{DynamicResource MaterialDesign.Brush.Foreground}"/>
            <Setter Property="HorizontalContentAlignment" Value="Left"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="TabItem">
                        <Border x:Name="Bd" Background="Transparent" Padding="14,10" Margin="0,1">
                            <ContentPresenter ContentSource="Header"
                                              VerticalAlignment="Center"
                                              HorizontalAlignment="Left"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bd" Property="Background"
                                        Value="{DynamicResource MaterialDesign.Brush.Card.Background}"/>
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Bd" Property="Background"
                                        Value="{DynamicResource MaterialDesign.Brush.Primary}"/>
                                <Setter Property="Foreground"
                                        Value="{DynamicResource MaterialDesign.Brush.Primary.Foreground}"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- TabControl retemplated: left sidebar (hamburger + vertical item host) + content area. -->
        <Style x:Key="SidebarTabControlStyle" TargetType="TabControl">
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="TabControl">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>

                            <!-- Sidebar -->
                            <Border Grid.Column="0"
                                    Background="{DynamicResource MaterialDesign.Brush.Background}"
                                    BorderBrush="{DynamicResource MaterialDesign.Brush.Card.Background}"
                                    BorderThickness="0,0,1,0">
                                <Border.Style>
                                    <Style TargetType="Border">
                                        <Setter Property="Width" Value="180"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding ViewModel.IsSidebarExpanded}" Value="False">
                                                <Setter Property="Width" Value="56"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <DockPanel>
                                    <ToggleButton DockPanel.Dock="Top"
                                                  HorizontalAlignment="Left"
                                                  Margin="10,8"
                                                  Background="Transparent"
                                                  BorderThickness="0"
                                                  ToolTip="Toggle sidebar"
                                                  IsChecked="{Binding ViewModel.IsSidebarExpanded, Mode=TwoWay}">
                                        <materialDesign:PackIcon Kind="Menu" Width="22" Height="22"/>
                                    </ToggleButton>
                                    <StackPanel IsItemsHost="True" DockPanel.Dock="Top"/>
                                </DockPanel>
                            </Border>

                            <!-- Active view content -->
                            <Border Grid.Column="1">
                                <ContentPresenter ContentSource="SelectedContent"/>
                            </Border>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
```

- [ ] **Step 3: Apply the styles and give each tab an icon+label header**

In `OmniCard/Views/Root/RootView.xaml`, replace the `TabControl` from Task 2 with:

```xml
        <TabControl x:Name="MainTabControl"
                    Grid.Row="2"
                    Style="{StaticResource SidebarTabControlStyle}"
                    SelectedIndex="{Binding ViewModel.SelectedTabIndex}">

            <TabItem x:Name="tabItemDashboard" Style="{StaticResource SidebarTabItemStyle}">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <materialDesign:PackIcon Kind="ViewDashboard" Width="22" Height="22" VerticalAlignment="Center"/>
                        <TextBlock Text="Dashboard" Margin="12,0,0,0" VerticalAlignment="Center"
                                   Visibility="{Binding ViewModel.IsSidebarExpanded, Converter={conv:BoolToVisibilityConverter}}"/>
                    </StackPanel>
                </TabItem.Header>
                <dashboard:DashboardView x:Name="DashboardTab"/>
            </TabItem>

            <TabItem x:Name="tabItemCollection" Style="{StaticResource SidebarTabItemStyle}">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <materialDesign:PackIcon Kind="ViewGridOutline" Width="22" Height="22" VerticalAlignment="Center"/>
                        <TextBlock Text="Collection" Margin="12,0,0,0" VerticalAlignment="Center"
                                   Visibility="{Binding ViewModel.IsSidebarExpanded, Converter={conv:BoolToVisibilityConverter}}"/>
                    </StackPanel>
                </TabItem.Header>
                <local:CollectionTabView x:Name="CollectionTab"/>
            </TabItem>

            <TabItem x:Name="tabItemScanner" Style="{StaticResource SidebarTabItemStyle}">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <materialDesign:PackIcon Kind="Camera" Width="22" Height="22" VerticalAlignment="Center"/>
                        <TextBlock Text="Scanner" Margin="12,0,0,0" VerticalAlignment="Center"
                                   Visibility="{Binding ViewModel.IsSidebarExpanded, Converter={conv:BoolToVisibilityConverter}}"/>
                    </StackPanel>
                </TabItem.Header>
                <local:ScannerTabView x:Name="ScannerTab"/>
            </TabItem>

            <TabItem x:Name="tabItemSales" Style="{StaticResource SidebarTabItemStyle}">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <materialDesign:PackIcon Kind="CartOutline" Width="22" Height="22" VerticalAlignment="Center"/>
                        <TextBlock Text="Sales" Margin="12,0,0,0" VerticalAlignment="Center"
                                   Visibility="{Binding ViewModel.IsSidebarExpanded, Converter={conv:BoolToVisibilityConverter}}"/>
                    </StackPanel>
                </TabItem.Header>
                <sales:SalesView DataContext="{Binding ViewModel.Sales}"/>
            </TabItem>

        </TabControl>
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -c Debug`
Expected: Build succeeded. If a `PackIcon` `Kind` value is rejected (enum member not in the installed MaterialDesignThemes version), substitute the closest available (e.g. `ViewDashboardOutline`, `ViewGrid`, `CameraOutline`, `Cart`) and rebuild.

- [ ] **Step 5: Run the app and verify the sidebar (manual)**

Run the app. Confirm:
- A left sidebar shows four items — Dashboard, Collection, Scanner, Sales — each with an icon + label.
- Clicking an item switches the content on the right; the selected item is highlighted with the accent background.
- Hovering an unselected item shows a subtle highlight.
- The hamburger at the top toggles the sidebar between the ~180px labeled state and the ~56px icon-only rail; icons remain clickable when collapsed.
- After a scan, the app still selects the Scanner item; Select-All / Delete-Selected still act on the right view.
- Toggle to collapsed, close and relaunch the app → it reopens collapsed (persistence). Toggle back to expanded, relaunch → reopens expanded.
- Switch theme (Light/Dark) and confirm the sidebar colors read correctly in both.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/RootView.xaml
git commit -m "feat: replace tab strip with collapsible left sidebar navigation"
```

---

## Self-Review (completed by plan author)

**Spec coverage:**
- Left sidebar with icon+label → Task 3 (styles + headers). ✓
- Collapsible + persisted state → Task 1 (setting/property/persistence) + Task 3 (hamburger, width trigger, label visibility). ✓
- Selected highlight + hover → Task 3 (`SidebarTabItemStyle` triggers). ✓
- Merge Home into Dashboard, financials on top, one scroll, one Refresh → Task 2. ✓
- Dashboard first + default-selected → Task 2 reorder (index 0 = default). ✓
- Preserve index contract / Scanner jump / Select-All / Delete → Collection stays 1, Scanner stays 2 (Tasks 2-3), verified in manual steps. ✓
- Startup load of default Dashboard → Task 2 Step 4. ✓
- Set-tile click-to-expand preserved → Task 2 Step 2 (ListView kept, inner scroll disabled). ✓
- Light/dark theming → `DynamicResource` throughout; manual check Task 3 Step 5. ✓

**Placeholder scan:** The three "COPY VERBATIM" markers reference exact files and line ranges of existing, committed markup with explicit edits — not undefined behavior. All new/scaffolding code is shown in full. No TBD/TODO.

**Type consistency:** `IsSidebarExpanded`, `RefreshDashboardCommand`, `SidebarExpanded`, `SidebarTabItemStyle`, `SidebarTabControlStyle`, `tabItemDashboard`/`DashboardTab` names are used consistently across tasks. `Dashboard.RefreshCommand`/`Dashboard.Load`/`Dashboard.IsBusy`/`Dashboard.StatusMessage` match `DashboardViewModel`. Converters (`BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `StringToVisibilityConverter`, `NullToCollapsedConverter`, `CompletionPercentConverter`) and `BoolToVis` (App.xaml) all already exist.
