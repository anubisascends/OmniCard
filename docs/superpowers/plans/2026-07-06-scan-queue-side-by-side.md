# Scan Queue Side-by-Side Card Art Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display scanned card image and matched card art side by side in the scan queue, with card metadata below both images, for fast visual comparison.

**Architecture:** A new `CardArtCache` (LRU, same pattern as `ScanImageCache`) caches frozen `BitmapImage` instances keyed by image path/URI. A `MatchedArtConverter` (`IMultiValueConverter`) resolves the matched card's art from the cache. The existing `ScannerTabView.xaml` item template is restructured to a two-row grid layout.

**Tech Stack:** WPF, MVVM Toolkit (CommunityToolkit.Mvvm), C# 13 / .NET 10

## Global Constraints

- Follow existing MVVM Toolkit patterns (`[ObservableProperty]`, `[RelayCommand]`)
- Match existing code style (same namespace patterns, same using order)
- No new NuGet packages
- Image cache uses `BitmapImage.Freeze()` for thread safety and memory sharing
- Local image path preferred over remote URI

---

### Task 1: Create CardArtCache and MatchedArtConverter

**Files:**
- Create: `OmniCard/Services/CardArtCache.cs`
- Create: `OmniCard/Views/Root/MatchedArtConverter.cs`
- Modify: `OmniCard/App.xaml.cs:69` (DI registration) and `OmniCard/App.xaml.cs:231-232` (initialization)

**Interfaces:**
- Consumes: Nothing from other tasks
- Produces: `CardArtCache.Instance.GetImage(string? localPath, string? imageUri) -> BitmapImage?` — used by `MatchedArtConverter`, consumed by XAML in Task 2

- [ ] **Step 1: Create `CardArtCache.cs`**

Create `OmniCard/Services/CardArtCache.cs`:

```csharp
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace OmniCard.Services;

public sealed class CardArtCache
{
    public static CardArtCache? Instance { get; private set; }

    public static void Initialize(CardArtCache instance) => Instance = instance;

    private readonly ILogger<CardArtCache> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Image)>> _map = new();
    private readonly LinkedList<(string Key, BitmapImage Image)> _order = new();

    public CardArtCache(ILogger<CardArtCache> logger, IHttpClientFactory httpClientFactory, int capacity = 200)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _capacity = capacity;
    }

    public int Count => _map.Count;

    public BitmapImage? GetImage(string? localPath, string? imageUri)
    {
        // Determine cache key and source
        string? key = null;
        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            key = localPath;
        else if (!string.IsNullOrEmpty(imageUri))
            key = imageUri;

        if (key is null)
            return null;

        // Check cache
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Image;
        }

        // Load image
        try
        {
            BitmapImage bmp;
            if (key == localPath)
            {
                bmp = LoadFromFile(localPath!);
            }
            else
            {
                bmp = LoadFromUri(imageUri!);
            }

            var newNode = _order.AddFirst((key, bmp));
            _map[key] = newNode;

            if (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _map.Remove(last.Value.Key);
                _order.RemoveLast();
            }

            return bmp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load card art: {Key}", key);
            return null;
        }
    }

    private static BitmapImage LoadFromFile(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 500;
        bmp.StreamSource = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        bmp.EndInit();
        bmp.StreamSource.Dispose();
        bmp.Freeze();
        return bmp;
    }

    private BitmapImage LoadFromUri(string uri)
    {
        var client = _httpClientFactory.CreateClient();
        var bytes = client.GetByteArrayAsync(uri).GetAwaiter().GetResult();
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 500;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.StreamSource.Dispose();
        bmp.Freeze();
        return bmp;
    }

    public void Evict(string key)
    {
        if (_map.Remove(key, out var node))
            _order.Remove(node);
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }
}
```

- [ ] **Step 2: Create `MatchedArtConverter.cs`**

Create `OmniCard/Views/Root/MatchedArtConverter.cs`:

```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using OmniCard.Services;

namespace OmniCard.Views.Root;

public class MatchedArtConverter : MarkupExtension, IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (CardArtCache.Instance is null)
            return null;

        var localPath = values.Length > 0 ? values[0] as string : null;
        var imageUri = values.Length > 1 ? values[1] as string : null;

        return CardArtCache.Instance.GetImage(localPath, imageUri);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
```

- [ ] **Step 3: Register `CardArtCache` in DI and initialize it**

In `OmniCard/App.xaml.cs`, after the existing `ScanImageCache` registration (line 69), add:

```csharp
            services.AddSingleton<ScanImageCache>();
            services.AddSingleton<CardArtCache>();  // <-- add this line
```

After the existing `ScanImageCache.Initialize` block (around line 232), add:

```csharp
            ScanImageCache.Initialize(scanImageCache);

            // Initialize card art cache
            var cardArtCache = Host.Services.GetRequiredService<CardArtCache>();
            CardArtCache.Initialize(cardArtCache);
```

- [ ] **Step 4: Build and verify compilation**

Run:
```bash
cd d:/source/repos/OmniCard && dotnet build OmniCard/OmniCard.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Services/CardArtCache.cs OmniCard/Views/Root/MatchedArtConverter.cs OmniCard/App.xaml.cs
git commit -m "feat: add CardArtCache and MatchedArtConverter for matched card art display"
```

---

### Task 2: Restructure Scan Queue Item Template

**Files:**
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml:440-550` (item template)

**Interfaces:**
- Consumes: `MatchedArtConverter` from Task 1, existing `ScanImageConverter`, existing ViewModel properties (`CardPreviewWidth`, `ScannerFontSize`, `ScannerFontSizeSmall`)
- Produces: Updated UI layout (no downstream code depends on this)

- [ ] **Step 1: Replace the item template**

In `OmniCard/Views/Root/ScannerTabView.xaml`, replace the entire `<ListView.ItemTemplate>` block (lines 440-550) with:

```xml
                    <ListView.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="4">
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="Auto"/>
                                </Grid.RowDefinitions>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>

                                <!-- Scanned image (top left) -->
                                <Image Grid.Row="0" Grid.Column="0"
                                       Source="{Binding TempImagePath, Converter={local:ScanImageConverter}}"
                                       Width="{Binding DataContext.ViewModel.CardPreviewWidth,
                                           RelativeSource={RelativeSource AncestorType=ListView}}"
                                       HorizontalAlignment="Left"
                                       VerticalAlignment="Top"
                                       Stretch="Uniform"
                                       Margin="0,0,4,0"/>

                                <!-- Matched card art (top right) -->
                                <Image Grid.Row="0" Grid.Column="1"
                                       Width="{Binding DataContext.ViewModel.CardPreviewWidth,
                                           RelativeSource={RelativeSource AncestorType=ListView}}"
                                       HorizontalAlignment="Left"
                                       VerticalAlignment="Top"
                                       Stretch="Uniform"
                                       Margin="4,0,0,0">
                                    <Image.Source>
                                        <MultiBinding Converter="{local:MatchedArtConverter}">
                                            <Binding Path="Match.LocalImagePath"/>
                                            <Binding Path="Match.ImageUri"/>
                                        </MultiBinding>
                                    </Image.Source>
                                </Image>

                                <!-- Card info below both images -->
                                <StackPanel Grid.Row="1" Grid.ColumnSpan="2"
                                            Margin="0,4,0,0">
                                    <!-- Card Name -->
                                    <TextBlock Text="{Binding Match.Name}"
                                               FontWeight="Bold"
                                               FontSize="{Binding DataContext.ViewModel.ScannerFontSize,
                                                   RelativeSource={RelativeSource AncestorType=ListView}}"/>

                                    <!-- Set Name (Set Code) [symbol] -->
                                    <StackPanel Orientation="Horizontal"
                                                Margin="0,2,0,0">
                                        <TextBlock Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                                                   FontSize="{Binding DataContext.ViewModel.ScannerFontSizeSmall,
                                                       RelativeSource={RelativeSource AncestorType=ListView}}"
                                                   VerticalAlignment="Center">
                                            <TextBlock.Text>
                                                <MultiBinding StringFormat="{}{0} ({1})">
                                                    <Binding Path="Match.SetName"/>
                                                    <Binding Path="Match.SetCode"/>
                                                </MultiBinding>
                                            </TextBlock.Text>
                                        </TextBlock>
                                        <Border Width="{Binding DataContext.ViewModel.ScannerFontSizeSmall,
                                                    RelativeSource={RelativeSource AncestorType=ListView}}"
                                                Height="{Binding DataContext.ViewModel.ScannerFontSizeSmall,
                                                    RelativeSource={RelativeSource AncestorType=ListView}}"
                                                Margin="6,0,0,0"
                                                VerticalAlignment="Center"
                                                helpers:SetSymbol.SetCode="{Binding Match.SetCode}"
                                                helpers:SetSymbol.Rarity="{Binding Match.Rarity}"/>
                                    </StackPanel>

                                    <!-- #: Collector Number -->
                                    <TextBlock Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                                               FontSize="{Binding DataContext.ViewModel.ScannerFontSizeSmall,
                                                   RelativeSource={RelativeSource AncestorType=ListView}}"
                                               Margin="0,2,0,0">
                                        <Run Text="#:"/>
                                        <Run Text="{Binding Match.CollectorNumber, Mode=OneWay}"/>
                                    </TextBlock>

                                    <!-- Match Confidence -->
                                    <TextBlock Margin="0,2,0,0"
                                               FontSize="{Binding DataContext.ViewModel.ScannerFontSizeSmall,
                                                   RelativeSource={RelativeSource AncestorType=ListView}}"
                                               Visibility="{Binding Match.Confidence, Converter={local:NullToCollapsedConverter}}">
                                        <Run Text="Match Confidence:"
                                             Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                                        <Run Text="{Binding Match.Confidence, StringFormat='{}{0:F0}%', Mode=OneWay}"
                                             Foreground="{Binding Match.Confidence, Converter={local:ConfidenceToColorConverter}}"/>
                                    </TextBlock>

                                    <!-- Match and Flag buttons side by side -->
                                    <StackPanel Orientation="Horizontal"
                                                Margin="0,4,0,0">
                                        <!-- Match button (low confidence only) -->
                                        <Button Content="Match"
                                                Padding="8,2"
                                                HorizontalAlignment="Left"
                                                Command="{Binding DataContext.ViewModel.ConfirmMatchCommand,
                                                    RelativeSource={RelativeSource AncestorType=ListView}}"
                                                CommandParameter="{Binding}"
                                                Visibility="{Binding Match.Confidence, Converter={local:LowConfidenceToVisibleConverter}}"
                                                Style="{StaticResource MaterialDesignFlatButton}"/>

                                        <!-- Flag button -->
                                        <Button Padding="8,2"
                                                Margin="4,0,0,0"
                                                HorizontalAlignment="Left"
                                                Command="{Binding DataContext.ViewModel.ToggleFlagCommand,
                                                    RelativeSource={RelativeSource AncestorType=ListView}}"
                                                CommandParameter="{Binding}"
                                                Style="{StaticResource MaterialDesignFlatButton}">
                                            <Button.Content>
                                                <TextBlock>
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Setter Property="Text"
                                                                    Value="Flag"/>
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding IsFlagged}"
                                                                        Value="True">
                                                                    <Setter Property="Text"
                                                                            Value="Unflag"/>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                            </Button.Content>
                                        </Button>
                                    </StackPanel>
                                </StackPanel>
                            </Grid>
                        </DataTemplate>
                    </ListView.ItemTemplate>
```

- [ ] **Step 2: Build and verify compilation**

Run:
```bash
cd d:/source/repos/OmniCard && dotnet build OmniCard/OmniCard.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add OmniCard/Views/Root/ScannerTabView.xaml
git commit -m "feat: restructure scan queue to show scanned image and matched art side by side"
```

---

### Task 3: Manual Smoke Test

**Files:** None (testing only)

- [ ] **Step 1: Launch and scan cards**

Run the app. Scan 3+ cards. Verify the new layout:
- Scanned image appears on the left
- Matched card art appears on the right, same width
- Both images are above the card text
- Card name, set, collector number, confidence, and buttons appear below both images

- [ ] **Step 2: Verify image caching**

Scan two copies of the same card. Both should display matched card art. The `CardArtCache` should only have one entry for that card's image (shared frozen BitmapImage).

- [ ] **Step 3: Verify empty state**

If any card has no match (null Match), the matched card art slot should be empty (no crash, no placeholder).

- [ ] **Step 4: Verify size scaling**

Adjust the `CardPreviewScale` slider. Both images (scanned and matched art) should resize together.

- [ ] **Step 5: Verify local path preference**

Cards with a `LocalImagePath` should load art from the local file. Cards with only `ImageUri` should load from the remote URL.
