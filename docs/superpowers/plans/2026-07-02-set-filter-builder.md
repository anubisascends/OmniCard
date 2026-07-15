# Set Filter Builder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the inline ComboBox set filter with a TextBox + builder dialog that uses a two-panel (available/selected) layout with SVG set symbols.

**Architecture:** New `SetFilterBuilderView` Window dialog following the existing `SortFilterBuilderView` pattern. The toolbar gets a TextBox (directly editable, comma-separated set codes) plus a "..." button to open the builder. The dialog uses `SetSymbolCache` to load Common-rarity SVG icons for each set row. The old `CheckableSetInfo` model and ComboBox infrastructure are removed.

**Tech Stack:** WPF, CommunityToolkit.Mvvm, MaterialDesignThemes, SharpVectors (via existing `SetSymbolCache`)

## Global Constraints

- Follow existing dialog patterns: Window + ViewModel via DI, `DialogService` method, transient registration
- Use `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`, `partial`)
- Material Design theming via `{DynamicResource MaterialDesign.Brush.*}` resources
- SVG rendering via existing `SetSymbolCache.GetSetSymbolAsync(setCode, "common")`
- Existing `SetSymbolConverter` attached-property pattern for Border-based SVG display

---

### Task 1: Create SetFilterItem Model

**Files:**
- Create: `OmniCard/Models/SetFilterItem.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `SetFilterItem` class with `SetCode`, `SetName`, `DisplayName`, `Symbol` properties — used by Tasks 2, 3, 4

- [ ] **Step 1: Create the SetFilterItem model**

```csharp
// OmniCard/Models/SetFilterItem.cs
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniCard.Models;

public partial class SetFilterItem : ObservableObject
{
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string DisplayName => $"{SetName} ({SetCode})";

    [ObservableProperty]
    public partial DrawingImage? Symbol { get; set; }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OmniCard/Models/SetFilterItem.cs
git commit -m "feat: add SetFilterItem model for set filter builder"
```

---

### Task 2: Create SetFilterBuilderViewModel

**Files:**
- Create: `OmniCard/Views/SetFilterBuilder/SetFilterBuilderViewModel.cs`

**Interfaces:**
- Consumes: `SetFilterItem` (Task 1), `SetInfo` record (existing), `SetSymbolCache.GetSetSymbolAsync(string, string)` (existing)
- Produces: `SetFilterBuilderViewModel` with `Initialize(IReadOnlyList<SetInfo>, IReadOnlySet<string>?)`, `AvailableSets`, `SelectedSets`, `SelectedAvailableItem`, `SelectedSelectedItem`, `SearchText`, `AddCommand`, `RemoveCommand` — used by Tasks 3, 4, 5

- [ ] **Step 1: Create the ViewModel**

```csharp
// OmniCard/Views/SetFilterBuilder/SetFilterBuilderViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Helpers;
using OmniCard.Models;

namespace OmniCard.Views.SetFilterBuilder;

public partial class SetFilterBuilderViewModel : ObservableObject
{
    private readonly SetSymbolCache _symbolCache;
    private List<SetFilterItem> _allAvailable = [];

    public ObservableCollection<SetFilterItem> AvailableSets { get; } = [];
    public ObservableCollection<SetFilterItem> SelectedSets { get; } = [];

    [ObservableProperty]
    public partial SetFilterItem? SelectedAvailableItem { get; set; }

    [ObservableProperty]
    public partial SetFilterItem? SelectedSelectedItem { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    public string SelectedCountText => SelectedSets.Count > 0
        ? $"Selected Sets ({SelectedSets.Count})"
        : "Selected Sets";

    public bool Confirmed { get; private set; }

    public SetFilterBuilderViewModel(SetSymbolCache symbolCache)
    {
        _symbolCache = symbolCache;
    }

    public void Initialize(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter)
    {
        _allAvailable = [];
        AvailableSets.Clear();
        SelectedSets.Clear();

        var selectedCodes = currentFilter ?? new HashSet<string>();

        foreach (var set in allSets)
        {
            var item = new SetFilterItem { SetCode = set.SetCode, SetName = set.SetName };

            if (selectedCodes.Contains(set.SetCode))
                SelectedSets.Add(item);
            else
                _allAvailable.Add(item);
        }

        SearchText = "";
        RefreshAvailable();
        OnPropertyChanged(nameof(SelectedCountText));

        // Load SVG symbols async (non-blocking)
        _ = LoadSymbolsAsync([.._allAvailable, ..SelectedSets]);
    }

    private async Task LoadSymbolsAsync(List<SetFilterItem> items)
    {
        foreach (var item in items)
        {
            var symbol = await _symbolCache.GetSetSymbolAsync(item.SetCode, "common");
            if (symbol != null)
                item.Symbol = symbol;
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshAvailable();

    private void RefreshAvailable()
    {
        AvailableSets.Clear();
        var search = SearchText;
        foreach (var item in _allAvailable)
        {
            if (string.IsNullOrEmpty(search)
                || item.SetName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.SetCode.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                AvailableSets.Add(item);
            }
        }
    }

    [RelayCommand]
    public void Add()
    {
        if (SelectedAvailableItem is null) return;
        var item = SelectedAvailableItem;
        _allAvailable.Remove(item);
        SelectedSets.Add(item);
        SelectedAvailableItem = null;
        RefreshAvailable();
        OnPropertyChanged(nameof(SelectedCountText));
    }

    [RelayCommand]
    public void Remove()
    {
        if (SelectedSelectedItem is null) return;
        var item = SelectedSelectedItem;
        SelectedSets.Remove(item);
        _allAvailable.Add(item);
        _allAvailable.Sort((a, b) => string.Compare(a.SetName, b.SetName, StringComparison.OrdinalIgnoreCase));
        SelectedSelectedItem = null;
        RefreshAvailable();
        OnPropertyChanged(nameof(SelectedCountText));
    }

    public void ConfirmSelection()
    {
        Confirmed = true;
    }

    public IReadOnlyList<string> GetSelectedCodes() =>
        SelectedSets.Select(s => s.SetCode).ToList();
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OmniCard/Views/SetFilterBuilder/SetFilterBuilderViewModel.cs
git commit -m "feat: add SetFilterBuilderViewModel with two-panel logic"
```

---

### Task 3: Create SetFilterBuilderView (XAML + code-behind)

**Files:**
- Create: `OmniCard/Views/SetFilterBuilder/SetFilterBuilderView.xaml`
- Create: `OmniCard/Views/SetFilterBuilder/SetFilterBuilderView.xaml.cs`

**Interfaces:**
- Consumes: `SetFilterBuilderViewModel` (Task 2), `SetFilterItem.DisplayName` and `SetFilterItem.Symbol` (Task 1)
- Produces: `SetFilterBuilderView` Window — used by Tasks 4, 5

- [ ] **Step 1: Create the code-behind**

```csharp
// OmniCard/Views/SetFilterBuilder/SetFilterBuilderView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OmniCard.Views.SetFilterBuilder;

public partial class SetFilterBuilderView : Window
{
    public SetFilterBuilderViewModel ViewModel { get; }

    public SetFilterBuilderView(SetFilterBuilderViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmSelection();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void AvailableList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedAvailableItem is not null)
            ViewModel.Add();
    }

    private void SelectedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedSelectedItem is not null)
            ViewModel.Remove();
    }
}
```

- [ ] **Step 2: Create the XAML**

```xml
<!-- OmniCard/Views/SetFilterBuilder/SetFilterBuilderView.xaml -->
<Window x:Class="OmniCard.Views.SetFilterBuilder.SetFilterBuilderView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:OmniCard.Views.SetFilterBuilder"
        xmlns:models="clr-namespace:OmniCard.Models"
        ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        d:DataContext="{d:DesignInstance {x:Type local:SetFilterBuilderView}}"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="Set Filter Builder"
        Width="700" Height="500"
        Background="{DynamicResource MaterialDesign.Brush.Background}"
        TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
        TextElement.FontWeight="Regular"
        TextElement.FontSize="13"
        FontFamily="{StaticResource AppFont}">

    <Window.Resources>
        <DataTemplate DataType="{x:Type models:SetFilterItem}">
            <StackPanel Orientation="Horizontal" Margin="2">
                <!-- SVG symbol rendered as OpacityMask on a Border, same pattern as SetSymbolConverter -->
                <Border Width="16" Height="16" VerticalAlignment="Center" Margin="0,0,6,0">
                    <Border.Style>
                        <Style TargetType="Border">
                            <Setter Property="Background" Value="#1A1A1A"/>
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding Symbol}" Value="{x:Null}">
                                    <Setter Property="OpacityMask" Value="{x:Null}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Border.Style>
                    <Border.OpacityMask>
                        <MultiBinding Converter="{local:SymbolToDrawingBrushConverter}">
                            <Binding Path="Symbol"/>
                        </MultiBinding>
                    </Border.OpacityMask>
                </Border>
                <TextBlock Text="{Binding DisplayName}" VerticalAlignment="Center"/>
            </StackPanel>
        </DataTemplate>
    </Window.Resources>

    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Two-panel layout -->
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- Left panel: Available Sets -->
            <DockPanel>
                <TextBlock DockPanel.Dock="Top" Text="Available Sets" FontWeight="SemiBold" Margin="0,0,0,4"/>
                <TextBox DockPanel.Dock="Top"
                         Text="{Binding ViewModel.SearchText, UpdateSourceTrigger=PropertyChanged}"
                         Margin="0,0,0,4"
                         VerticalContentAlignment="Center"
                         Tag="Search sets..."/>
                <ListBox ItemsSource="{Binding ViewModel.AvailableSets}"
                         SelectedItem="{Binding ViewModel.SelectedAvailableItem}"
                         MouseDoubleClick="AvailableList_MouseDoubleClick"
                         VirtualizingStackPanel.IsVirtualizing="True"
                         VirtualizingStackPanel.VirtualizationMode="Recycling"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled"/>
            </DockPanel>

            <!-- Center buttons -->
            <StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="8,0">
                <Button Content="&gt;&gt;" Padding="12,6" Margin="0,0,0,4"
                        Command="{Binding ViewModel.AddCommand}"
                        ToolTip="Add selected set"/>
                <Button Content="&lt;&lt;" Padding="12,6"
                        Command="{Binding ViewModel.RemoveCommand}"
                        ToolTip="Remove selected set"/>
            </StackPanel>

            <!-- Right panel: Selected Sets -->
            <DockPanel Grid.Column="2">
                <TextBlock DockPanel.Dock="Top" Text="{Binding ViewModel.SelectedCountText}"
                           FontWeight="SemiBold" Margin="0,0,0,4"/>
                <ListBox ItemsSource="{Binding ViewModel.SelectedSets}"
                         SelectedItem="{Binding ViewModel.SelectedSelectedItem}"
                         MouseDoubleClick="SelectedList_MouseDoubleClick"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled"/>
            </DockPanel>
        </Grid>

        <!-- Footer: OK / Cancel -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="OK" Padding="20,6" Margin="0,0,8,0"
                    Click="OK_Click" IsDefault="True"/>
            <Button Content="Cancel" Padding="20,6"
                    Click="Cancel_Click" IsCancel="True"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: Create the SymbolToDrawingBrushConverter**

The XAML DataTemplate needs a converter to turn a `DrawingImage?` into a `DrawingBrush` for the OpacityMask. Add this as a simple IMultiValueConverter inside the SetFilterBuilderView.xaml.cs file (or a separate small file). A simpler approach: use an IValueConverter instead of MultiBinding.

Replace the OpacityMask binding in the XAML with a simpler approach. Update the DataTemplate in the XAML to avoid the MultiBinding complexity — instead, use a dedicated `IValueConverter`:

Create file `OmniCard/Views/SetFilterBuilder/SymbolToDrawingBrushConverter.cs`:

```csharp
// OmniCard/Views/SetFilterBuilder/SymbolToDrawingBrushConverter.cs
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OmniCard.Views.SetFilterBuilder;

public class SymbolToDrawingBrushConverter : IValueConverter
{
    public static readonly SymbolToDrawingBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DrawingImage drawingImage)
            return null;

        var brush = new DrawingBrush(drawingImage.Drawing) { Stretch = Stretch.Uniform };
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Then update the XAML DataTemplate to use this simpler converter. Replace the Border's OpacityMask section with:

```xml
<Border Width="16" Height="16" VerticalAlignment="Center" Margin="0,0,6,0"
        Background="#1A1A1A"
        OpacityMask="{Binding Symbol, Converter={StaticResource SymbolToBrush}}"/>
```

And add to `Window.Resources`:

```xml
<local:SymbolToDrawingBrushConverter x:Key="SymbolToBrush"/>
```

Full corrected XAML `Window.Resources`:

```xml
<Window.Resources>
    <local:SymbolToDrawingBrushConverter x:Key="SymbolToBrush"/>
    <DataTemplate DataType="{x:Type models:SetFilterItem}">
        <StackPanel Orientation="Horizontal" Margin="2">
            <Border Width="16" Height="16" VerticalAlignment="Center" Margin="0,0,6,0"
                    Background="#1A1A1A"
                    OpacityMask="{Binding Symbol, Converter={StaticResource SymbolToBrush}}"/>
            <TextBlock Text="{Binding DisplayName}" VerticalAlignment="Center"/>
        </StackPanel>
    </DataTemplate>
</Window.Resources>
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/SetFilterBuilder/
git commit -m "feat: add SetFilterBuilderView dialog with two-panel layout and SVG symbols"
```

---

### Task 4: Wire Up DialogService and DI Registration

**Files:**
- Modify: `OmniCard/Services/DialogService.cs` — add `IDialogService.OpenSetFilterBuilder` method and interface member
- Modify: `OmniCard/App.xaml.cs` — register `SetFilterBuilderView` and `SetFilterBuilderViewModel` as transient

**Interfaces:**
- Consumes: `SetFilterBuilderView` (Task 3), `SetFilterBuilderViewModel` (Task 2)
- Produces: `IDialogService.OpenSetFilterBuilder(IReadOnlyList<SetInfo>, IReadOnlySet<string>?)` returning `IReadOnlyList<string>?` — used by Task 5

- [ ] **Step 1: Add interface method to IDialogService**

In `OmniCard/Services/DialogService.cs`, add to the `IDialogService` interface:

```csharp
IReadOnlyList<string>? OpenSetFilterBuilder(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter);
```

- [ ] **Step 2: Implement in DialogService**

Add to the `DialogService` class:

```csharp
public IReadOnlyList<string>? OpenSetFilterBuilder(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter)
{
    var wnd = Services.GetRequiredService<SetFilterBuilderView>();
    wnd.ViewModel.Initialize(allSets, currentFilter);
    wnd.Owner = Application.Current.MainWindow;
    var result = wnd.ShowDialog();
    return result == true ? wnd.ViewModel.GetSelectedCodes() : null;
}
```

Add the required using at the top:

```csharp
using OmniCard.Views.SetFilterBuilder;
```

- [ ] **Step 3: Register in DI**

In `OmniCard/App.xaml.cs`, add after the `SortFilterBuilderViewModel` registration (around line 124):

```csharp
services.AddTransient<SetFilterBuilderView>();
services.AddTransient<SetFilterBuilderViewModel>();
```

Add the required using:

```csharp
using OmniCard.Views.SetFilterBuilder;
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Services/DialogService.cs OmniCard/App.xaml.cs
git commit -m "feat: wire up SetFilterBuilder dialog in DialogService and DI"
```

---

### Task 5: Replace Toolbar ComboBox and Refactor RootViewModel

**Files:**
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml` — replace ComboBox with TextBox + "..." button
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml.cs` — remove ComboBox event handlers
- Modify: `OmniCard/Views/Root/RootViewModel.cs` — replace old set filter properties with `SetFilterText`, add `OpenSetFilterBuilderCommand`, refactor `LoadAvailableSets` and `UpdateSetFilter`

**Interfaces:**
- Consumes: `IDialogService.OpenSetFilterBuilder(...)` (Task 4), `SetInfo` (existing), `CardService.SelectedSetFilter` (existing)
- Produces: Working toolbar with TextBox + builder button; `SetFilterText` property; `OpenSetFilterBuilderCommand`

- [ ] **Step 1: Update ScannerTabView.xaml — replace ComboBox block**

Replace lines 18-61 (the `TextBlock "Only match from set(s):"`, the `ComboBox`, and the clear `Button`) with:

```xml
<TextBlock Text="Only match from set(s):"
           VerticalAlignment="Center"
           Margin="8,0,4,0"/>
<TextBox x:Name="SetFilterTextBox"
         Text="{Binding ViewModel.SetFilterText, UpdateSourceTrigger=LostFocus}"
         MinWidth="200"
         VerticalAlignment="Center"
         VerticalContentAlignment="Center"
         Margin="0,0,4,0"
         ToolTip="Comma-separated set codes (e.g. vow, dmu, tla). Press Enter to apply.">
    <TextBox.InputBindings>
        <KeyBinding Key="Return"
                    Command="{Binding ViewModel.ApplySetFilterTextCommand}"/>
    </TextBox.InputBindings>
</TextBox>
<Button Content="..."
        ToolTip="Open Set Filter Builder"
        Padding="8,2"
        VerticalAlignment="Center"
        Margin="0,0,4,0"
        Command="{Binding ViewModel.OpenSetFilterBuilderCommand}"/>
<Button Content="X"
        ToolTip="Clear set filter"
        FontSize="10"
        Padding="4,2"
        VerticalAlignment="Center"
        Margin="0,0,8,0"
        Command="{Binding ViewModel.ClearSetFilterCommand}"
        Style="{StaticResource MaterialDesignFlatButton}"/>
```

Also fix the typo: "Only macth from set(s):" -> "Only match from set(s):"

- [ ] **Step 2: Update ScannerTabView.xaml.cs — remove ComboBox handlers**

Remove the `SetFilterComboBox_DropDownOpened` method (lines 139-148) and the `SetFilterComboBox_DropDownClosed` method (lines 150-156).

- [ ] **Step 3: Refactor RootViewModel — remove old set filter code, add new**

In `OmniCard/Views/Root/RootViewModel.cs`, replace the entire set filter section (lines 342-444) with:

```csharp
// Set filter — comma-separated set codes
private List<SetInfo> _allSets = [];

[ObservableProperty]
public partial string SetFilterText { get; set; } = "";

[RelayCommand]
public void ApplySetFilterText()
{
    UpdateSetFilter();
}

partial void OnSetFilterTextChanged(string value)
{
    // No-op here; filter applied on LostFocus (via binding) or Enter (via command)
    // This is intentional — we don't want to filter on every keystroke
}

private void LoadAvailableSets()
{
    _allSets = CardService.ActiveGameService.GetAvailableSets().ToList();

    // Register set names for tooltip display on set symbols
    foreach (var set in _allSets)
        setSymbolCache.RegisterSetName(set.SetCode, set.SetName);

    SetFilterText = "";
    UpdateSetFilter();
}

private void UpdateSetFilter()
{
    var text = SetFilterText.Trim();
    if (string.IsNullOrEmpty(text))
    {
        CardService.SelectedSetFilter = null;
        _logger.LogInformation("Set filter cleared");
        return;
    }

    var codes = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var knownCodes = _allSets.Select(s => s.SetCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var validCodes = codes
        .Where(c => knownCodes.Contains(c))
        .Select(c => _allSets.First(s => s.SetCode.Equals(c, StringComparison.OrdinalIgnoreCase)).SetCode)
        .ToHashSet();

    if (validCodes.Count == 0)
    {
        CardService.SelectedSetFilter = null;
        _logger.LogInformation("Set filter: no valid codes in '{Text}', filter cleared", text);
    }
    else
    {
        CardService.SelectedSetFilter = validCodes;
        _logger.LogInformation("Set filter changed to: {Codes}", string.Join(", ", validCodes));
    }
}

[RelayCommand]
public void ClearSetFilter()
{
    SetFilterText = "";
    UpdateSetFilter();
}

[RelayCommand]
public void OpenSetFilterBuilder()
{
    var currentFilter = CardService.SelectedSetFilter;
    var result = DialogService.OpenSetFilterBuilder(_allSets, currentFilter);
    if (result is not null)
    {
        SetFilterText = string.Join(", ", result);
        UpdateSetFilter();
    }
}
```

- [ ] **Step 4: Remove unused usings and the CheckableSetInfo import if present**

Verify there are no remaining references to `CheckableSetInfo`, `FilteredSets`, `SetSearchText`, `IsSetFilterOpen`, `SetFilterSummary`, `SelectAllVisibleSets`, or `ClearSetSearch` in the ViewModel. Remove any dead code.

- [ ] **Step 5: Verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/ScannerTabView.xaml OmniCard/Views/Root/ScannerTabView.xaml.cs OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat: replace ComboBox set filter with TextBox + builder dialog"
```

---

### Task 6: Delete CheckableSetInfo and Clean Up

**Files:**
- Delete: `OmniCard/Models/CheckableSetInfo.cs`
- Verify: no remaining references to `CheckableSetInfo` anywhere

**Interfaces:**
- Consumes: nothing
- Produces: clean codebase with no dead code

- [ ] **Step 1: Search for remaining references**

Run: `grep -r "CheckableSetInfo" OmniCard/ --include="*.cs" --include="*.xaml"`
Expected: No results (or only the file itself)

- [ ] **Step 2: Delete the file**

```bash
rm OmniCard/Models/CheckableSetInfo.cs
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Models/CheckableSetInfo.cs
git commit -m "refactor: remove obsolete CheckableSetInfo model"
```

---

### Task 7: Manual Smoke Test

**Files:** None (testing only)

- [ ] **Step 1: Run the application**

Run: `dotnet run --project OmniCard/OmniCard.csproj`

- [ ] **Step 2: Verify toolbar**

1. Scanner tab shows TextBox with placeholder behavior (empty = "All Sets" equivalent)
2. Type `vow, dmu` in TextBox, press Enter — filter should apply
3. Click "X" button — TextBox clears, filter resets

- [ ] **Step 3: Verify builder dialog**

1. Click "..." button — Set Filter Builder dialog opens
2. Left panel shows all available sets with SVG symbols and "SetName (setcode)" format
3. Search box filters the left panel by name and code
4. Select a set, click ">>" — moves to right panel
5. Double-click a set in left panel — moves to right panel
6. Select in right panel, click "<<" — moves back to left
7. Double-click in right panel — moves back to left
8. Click OK — TextBox updates with comma-separated codes, filter applies
9. Click Cancel — no changes

- [ ] **Step 4: Verify round-trip**

1. Open builder with existing filter text — right panel should be pre-populated
2. Modify selection, OK — TextBox updates correctly
3. Manually edit TextBox to add/remove a code — filter applies on Enter/focus loss
