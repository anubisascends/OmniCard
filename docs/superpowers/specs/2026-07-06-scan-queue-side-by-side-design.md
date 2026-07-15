# Scan Queue Side-by-Side Card Art — Design Spec

## Overview

Redesign the scanned card queue item layout so that the scanned image and the matched card art are displayed side by side, with card metadata text below both images. This enables fast visual comparison — the user can immediately see if the scan matches the correct card without clicking into details.

## Requirements

1. **Layout:** Each scanned card row uses a two-row grid:
   - **Top row:** Scanned image (left) and matched card art (right), same width, side by side.
   - **Bottom row:** Card name, set info, collector number, confidence, Match/Flag buttons — spanning the full width below both images.

2. **Image sizing:** Both images use `CardPreviewWidth` (currently `150 * CardPreviewScale / 100`) so they match in width and respect the user's display settings.

3. **Card art source priority:** Use `Match.LocalImagePath` first. If null or file missing, fall back to `Match.ImageUri` (remote URL). If both are null/unavailable, the right image slot is simply empty.

4. **Card art caching:**
   - A new `CardArtCache` class using the same LRU pattern as the existing `ScanImageCache`.
   - Keyed by `GameSpecificId` so duplicate printings of the same card share one frozen `BitmapImage` in memory.
   - Capacity: 200 images. Decode pixel width: 500px.
   - Images are `Freeze()`d for thread safety and shared across all `Image` controls referencing the same card.
   - Singleton pattern, initialized in `App.xaml.cs` alongside `ScanImageCache`.

5. **Converter:** A `MatchedArtConverter` (MarkupExtension + IMultiValueConverter) bound via `MultiBinding` to `Match.LocalImagePath` and `Match.ImageUri`. Returns the cached/shared frozen `BitmapImage`.

6. **Empty state:** When `Match` is null (no match found), the matched card art slot shows nothing (no placeholder image needed).

## Files

- **Create:** `OmniCard/Services/CardArtCache.cs` — LRU cache for matched card art, same pattern as `ScanImageCache`.
- **Create:** `OmniCard/Views/Root/MatchedArtConverter.cs` — MultiBinding converter that calls `CardArtCache.Instance.GetImage(localPath, imageUri)`.
- **Modify:** `OmniCard/Views/Root/ScannerTabView.xaml` — Replace the existing item template with the new two-row layout (images on top, text below).
- **Modify:** `OmniCard/App.xaml.cs` — Register `CardArtCache` in DI and initialize the singleton.

## CardArtCache API

```csharp
public sealed class CardArtCache
{
    public static CardArtCache? Instance { get; private set; }
    public static void Initialize(CardArtCache instance) => Instance = instance;

    // Returns a frozen, shared BitmapImage.
    // Tries localPath first (file must exist), then imageUri.
    // Returns null if neither is available.
    public BitmapImage? GetImage(string? localPath, string? imageUri);

    // Evict a specific key from the cache.
    public void Evict(string key);

    // Clear all cached images.
    public void Clear();
}
```

Cache key: `GameSpecificId` is not directly available to the converter. Instead, key by whichever path/URI was used to load the image — `localPath` if it existed, otherwise `imageUri`. This naturally deduplicates since all cards with the same `LocalImagePath` or `ImageUri` share the same cache entry.

## Item Template Layout (Conceptual)

```xml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>  <!-- Images -->
    <RowDefinition Height="Auto"/>  <!-- Text + buttons -->
  </Grid.RowDefinitions>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="Auto"/>
    <ColumnDefinition Width="Auto"/>
  </Grid.ColumnDefinitions>

  <!-- Scanned image -->
  <Image Grid.Row="0" Grid.Column="0"
         Source="{Binding TempImagePath, Converter={local:ScanImageConverter}}"
         Width="{CardPreviewWidth}"/>

  <!-- Matched card art -->
  <Image Grid.Row="0" Grid.Column="1"
         Width="{CardPreviewWidth}">
    <Image.Source>
      <MultiBinding Converter="{local:MatchedArtConverter}">
        <Binding Path="Match.LocalImagePath"/>
        <Binding Path="Match.ImageUri"/>
      </MultiBinding>
    </Image.Source>
  </Image>

  <!-- Card info spanning both columns -->
  <StackPanel Grid.Row="1" Grid.ColumnSpan="2">
    <!-- Name, set, collector number, confidence, Match/Flag buttons -->
  </StackPanel>
</Grid>
```

## Testing

- Scan several cards. Verify scanned image and matched card art appear side by side.
- Verify both images are the same width and respect `CardPreviewScale` setting.
- Verify cards with the same `LocalImagePath` share the same cached image (check `CardArtCache.Count`).
- Verify cards with no match show empty right image slot.
- Verify cards with only `ImageUri` (no local path) load from the remote URL.
- Adjust `CardPreviewScale` slider and verify both images resize together.
