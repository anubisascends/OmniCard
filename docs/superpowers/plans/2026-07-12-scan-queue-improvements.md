# Scan Queue Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve scan verification speed and accuracy with missing-from-DB flagging, flag navigation, auto-rotation, manual rotation, and missing card collection tracking.

**Architecture:** Five focused features added to existing scan pipeline and UI. New `MissingFromDatabase` flag reason, `IsMissing` field on `CollectionCard`, rotation logic in `CardService`, navigation/rotate commands in `RootViewModel`, and UI controls in the scanner XAML views. All changes follow established patterns.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, System.Drawing (rotation), Entity Framework Core + SQLite, xUnit + Moq

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`
- Test framework: xUnit 2.9.3 with Moq 4.x
- MVVM pattern: CommunityToolkit.Mvvm (source generators for `[ObservableProperty]`, `[RelayCommand]`)
- All user-facing dialogs use `System.Windows.MessageBox`
- Logging via `ILogger` with Serilog structured logging
- Image manipulation via `System.Drawing.Bitmap`
- SQLite schema upgrades use inline `ExecuteSqlRaw` in `CardService` constructor (not EF migrations)

---

### Task 1: MissingFromDatabase Flag + IsMissing Collection Field + Schema

**Files:**
- Modify: `OmniCard.Shared/Models/FlagReason.cs`
- Modify: `OmniCard.Shared/Models/CollectionCard.cs`
- Modify: `OmniCard.Collection/CardService.cs` (schema upgrade + CommitScans + BuildIsExpression + auto-flag upgrade)
- Test: `OmniCard.Tests/Services/CollectionCardCrudTests.cs` (add test for missing card commit)

**Interfaces:**
- Consumes: existing `FlagReason` enum, `CollectionCard` model, `CardService.CommitScans`
- Produces: `FlagReason.MissingFromDatabase` enum value, `CollectionCard.IsMissing` property, `is:missing` query support, ability to commit unmatched scans as missing cards

- [ ] **Step 1: Add MissingFromDatabase to FlagReason**

In `OmniCard.Shared/Models/FlagReason.cs`, add the new value:

```csharp
public enum FlagReason
{
    None,
    NoMatch,
    VeryLowConfidence,
    Manual,
    MissingFromDatabase
}
```

- [ ] **Step 2: Add IsMissing to CollectionCard**

In `OmniCard.Shared/Models/CollectionCard.cs`, add after the `CardType` property (line 28):

```csharp
    public bool IsMissing { get; set; }
```

- [ ] **Step 3: Add IsMissing column schema upgrade**

In `OmniCard.Collection/CardService.cs`, in the constructor after the existing `ExecuteSqlRaw` blocks (around line 116), add:

```csharp
        // Add IsMissing column for cards not found in card database
        try
        {
            ctx.Database.ExecuteSqlRaw("ALTER TABLE Cards ADD COLUMN IsMissing INTEGER NOT NULL DEFAULT 0");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column name"))
        {
            // Column already exists
        }
```

- [ ] **Step 4: Update CommitScans to handle missing cards**

In `OmniCard.Collection/CardService.cs`, in `CommitScans` method (around line 433-438), replace the skip-on-null-match logic:

```csharp
        foreach (var scan in scannedCards)
        {
            // Use per-card override if set, otherwise use toolbar defaults
            var container = scan.OverrideContainer ?? activeContainer;

            CollectionCard card;
            if (scan.Match is null)
            {
                // Commit as missing card if flagged MissingFromDatabase
                if (scan.FlagReason != FlagReason.MissingFromDatabase)
                {
                    skipped++;
                    continue;
                }

                card = new CollectionCard
                {
                    Game = scan.Game,
                    Name = "Unknown Card",
                    GameCardId = "",
                    SetCode = "",
                    SetName = "",
                    Number = "",
                    Rarity = "",
                    Condition = scan.Condition,
                    IsFoil = scan.IsFoil,
                    PurchasePrice = scan.PurchasePrice,
                    ContainerId = container?.Id,
                    IsMissing = true,
                };
            }
            else
            {
                card = new CollectionCard
                {
                    Game = scan.Game,
                    Name = scan.Match.Name,
                    SetCode = scan.Match.SetCode,
                    SetName = scan.Match.SetName,
                    Number = scan.Match.CollectorNumber,
                    Rarity = scan.Match.Rarity,
                    ImageUri = scan.Match.ImageUri,
                    GameCardId = scan.Match.GameSpecificId,
                    Condition = scan.Condition,
                    IsFoil = scan.IsFoil,
                    PurchasePrice = scan.PurchasePrice,
                    ContainerId = container?.Id,
                };

                card.Color = CardAttributeExtractor.ExtractColor(scan.Match, scan.Game);
                card.CardType = CardAttributeExtractor.ExtractCardType(scan.Match, scan.Game);
            }
```

Keep the location assignment logic that follows unchanged — it applies to both matched and missing cards.

- [ ] **Step 5: Add is:missing to BuildIsExpression**

In `OmniCard.Collection/CardService.cs`, in `BuildIsExpression` (around line 1158), add the `"missing"` case:

```csharp
    private static LinqExpression BuildIsExpression(System.Linq.Expressions.ParameterExpression param, string value)
    {
        return value.ToLowerInvariant() switch
        {
            "foil" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsFoil)),
                LinqExpression.Constant(true)),
            "missing" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsMissing)),
                LinqExpression.Constant(true)),
            _ => LinqExpression.Constant(true),
        };
    }
```

- [ ] **Step 6: Add auto-flag upgrade to MissingFromDatabase**

In `OmniCard.Collection/CardService.cs`, in the `Dispatcher.BeginInvoke` async block of `AddFromStream`, after both OCR branches complete and before the diagnostic logging (the `catch` block for OCR analysis), add:

```csharp
                // Upgrade NoMatch to MissingFromDatabase after all matching attempts exhausted
                if (scannedCard.Match is null && scannedCard.FlagReason == FlagReason.NoMatch)
                {
                    scannedCard.FlagReason = FlagReason.MissingFromDatabase;
                    _logger.LogInformation("Card flagged as missing from database (pHash + OCR exhausted)");
                }
```

Note: This will be placed after the auto-rotation logic (Task 3), which also tries to find a match. For now, place it right after the OCR try/catch block, before the diagnostic logging.

- [ ] **Step 7: Build and run tests**

Run: `dotnet build -v minimal && dotnet test OmniCard.Tests -v minimal`
Expected: Build succeeded, all tests pass

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/FlagReason.cs OmniCard.Shared/Models/CollectionCard.cs OmniCard.Collection/CardService.cs
git commit -m "feat: add MissingFromDatabase flag, IsMissing collection field, and is:missing query"
```

---

### Task 2: Flag Navigation (Previous/Next)

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (add navigation commands)
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml` (add navigation buttons)
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml.cs` (add scroll-to-item helper)

**Interfaces:**
- Consumes: `CardService.ScannedCards`, `ScannedCard.IsFlagged`, `RootViewModel.SelectedScannedCard`
- Produces: `NavigateToNextFlagCommand`, `NavigateToPreviousFlagCommand` relay commands

- [ ] **Step 1: Add navigation commands to RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, add after the `RefreshScanStats` method (around line 856):

```csharp
    [RelayCommand]
    public void NavigateToNextFlag()
    {
        var cards = CardService.ScannedCards;
        if (cards.Count == 0) return;

        var startIndex = SelectedScannedCard is not null
            ? cards.IndexOf(SelectedScannedCard) + 1
            : 0;

        // Search forward, wrapping around
        for (var i = 0; i < cards.Count; i++)
        {
            var idx = (startIndex + i) % cards.Count;
            if (cards[idx].IsFlagged)
            {
                SelectedScannedCard = cards[idx];
                OnPropertyChanged(nameof(SelectedScannedCard));
                return;
            }
        }
    }

    [RelayCommand]
    public void NavigateToPreviousFlag()
    {
        var cards = CardService.ScannedCards;
        if (cards.Count == 0) return;

        var startIndex = SelectedScannedCard is not null
            ? cards.IndexOf(SelectedScannedCard) - 1
            : cards.Count - 1;

        // Search backward, wrapping around
        for (var i = 0; i < cards.Count; i++)
        {
            var idx = (startIndex - i + cards.Count) % cards.Count;
            if (cards[idx].IsFlagged)
            {
                SelectedScannedCard = cards[idx];
                OnPropertyChanged(nameof(SelectedScannedCard));
                return;
            }
        }
    }
```

- [ ] **Step 2: Add scroll-to-selected helper in code-behind**

In `OmniCard/Views/Root/ScannerTabView.xaml.cs`, add a method and wire it to selection changes. Add after the existing `IsScrolledToBottom` method:

```csharp
    public void ScrollToSelected()
    {
        if (ScannedCardsListView.SelectedItem is not null)
            ScannedCardsListView.ScrollIntoView(ScannedCardsListView.SelectedItem);
    }
```

Then in `ScannedCardsListView_SelectionChanged`, add at the end:

```csharp
        ScrollToSelected();
```

- [ ] **Step 3: Add navigation buttons to ScannerTabView.xaml**

In `OmniCard/Views/Root/ScannerTabView.xaml`, after the "Flagged:" button (around line 423, just before the closing `</StackPanel>`), add:

```xml
                        <!-- Flag navigation -->
                        <TextBlock Text="|" VerticalAlignment="Center" Margin="2,0"
                                   Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}" FontSize="11"/>
                        <Button Command="{Binding ViewModel.NavigateToPreviousFlagCommand}"
                                ToolTip="Previous flagged card"
                                Padding="4,2" Margin="0"
                                Style="{StaticResource MaterialDesignFlatButton}"
                                FontSize="11" Content="◄"/>
                        <Button Command="{Binding ViewModel.NavigateToNextFlagCommand}"
                                ToolTip="Next flagged card"
                                Padding="4,2" Margin="0"
                                Style="{StaticResource MaterialDesignFlatButton}"
                                FontSize="11" Content="►"/>
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build -v minimal`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/ScannerTabView.xaml OmniCard/Views/Root/ScannerTabView.xaml.cs
git commit -m "feat: add previous/next flag navigation buttons in scan stats bar"
```

---

### Task 3: Auto-Rotation on No Match

**Files:**
- Modify: `OmniCard.Shared/Models/ScannedCard.cs` (change `Hash` from `init` to `set`)
- Modify: `OmniCard.Collection/CardService.cs` (add rotation + re-match logic in async block)

**Interfaces:**
- Consumes: `IPerceptualHashService.ComputeHash`, `FindBestMatch`, `IOcrMatchingService.DetectOptcgCollectorNumberAsync`
- Produces: Auto-rotation behavior integrated into scan pipeline (no new public API)

- [ ] **Step 1: Make ScannedCard.Hash settable**

In `OmniCard.Shared/Models/ScannedCard.cs`, change line 8:

```csharp
    public required ulong Hash { get; set; }
```

- [ ] **Step 2: Add auto-rotation logic in the async dispatcher block**

In `OmniCard.Collection/CardService.cs`, in the `Dispatcher.BeginInvoke` async block, after the OCR try/catch block and before the MissingFromDatabase upgrade (added in Task 1), add:

```csharp
                // Auto-rotate 180° and retry if still no match
                if (scannedCard.Match is null)
                {
                    try
                    {
                        _logger.LogInformation("No match found, attempting 180° rotation for hash {Hash:X16}", scannedCard.Hash);
                        using var bmp = new System.Drawing.Bitmap(new MemoryStream(rawBytes));
                        bmp.RotateFlip(System.Drawing.Drawing2D.RotateFlipType.Rotate180FlipNone);

                        using var rotatedStream = new MemoryStream();
                        bmp.Save(rotatedStream, System.Drawing.Imaging.ImageFormat.Png);
                        rotatedStream.Position = 0;
                        var rotatedHash = _hashService.ComputeHash(rotatedStream);
                        rotatedStream.Position = 0;
                        var rotatedBytes = rotatedStream.ToArray();

                        // Try OCR on rotated image for One Piece
                        OcrMatchResult? rotatedOcr = null;
                        if (game == CardGame.OnePiece)
                        {
                            var (cn, cnConf) = await _ocrService.DetectOptcgCollectorNumberAsync(rotatedBytes);
                            if (cn is not null && cnConf >= 0.5)
                                rotatedOcr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = cnConf };
                        }

                        var (rotatedMatch, rotatedGame) = FindBestMatch(rotatedHash, null, rotatedOcr, capturedSetFilter, null);
                        if (rotatedMatch is not null)
                        {
                            scannedCard.Match = rotatedMatch;
                            scannedCard.Game = rotatedGame;
                            scannedCard.Hash = rotatedHash;
                            scannedCard.FlagReason = FlagReason.None;

                            // Overwrite temp file with rotated image
                            try { File.WriteAllBytes(scannedCard.TempImagePath, rotatedBytes); }
                            catch (Exception ex) { _logger.LogWarning(ex, "Failed to save rotated image"); }

                            _logger.LogInformation("180° rotation matched to \"{CardName}\" ({SetCode} #{Number})",
                                rotatedMatch.Name, rotatedMatch.SetCode, rotatedMatch.CollectorNumber);
                        }
                        else
                        {
                            _logger.LogDebug("180° rotation did not produce a match");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto-rotation failed");
                    }
                }
```

- [ ] **Step 3: Build and run tests**

Run: `dotnet build -v minimal && dotnet test OmniCard.Tests -v minimal`
Expected: Build succeeded, all tests pass

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Shared/Models/ScannedCard.cs OmniCard.Collection/CardService.cs
git commit -m "feat: auto-rotate scans 180° and retry matching when no match found"
```

---

### Task 4: Manual Rotation Controls

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (add rotate commands)
- Modify: `OmniCard/Views/Root/ScannerDetailPanelView.xaml` (add rotate buttons)

**Interfaces:**
- Consumes: `ScannedCard.TempImagePath`, `IPerceptualHashService.ComputeHash`, `FindBestMatch`, `IOcrMatchingService`
- Produces: `RotateLeftCommand`, `RotateRightCommand` relay commands

- [ ] **Step 1: Add rotate commands to RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, add after the flag navigation commands:

```csharp
    [RelayCommand]
    public async Task RotateLeft()
    {
        if (SelectedScannedCard is null) return;
        await RotateScan(SelectedScannedCard, System.Drawing.RotateFlipType.Rotate270FlipNone);
    }

    [RelayCommand]
    public async Task RotateRight()
    {
        if (SelectedScannedCard is null) return;
        await RotateScan(SelectedScannedCard, System.Drawing.RotateFlipType.Rotate90FlipNone);
    }

    private async Task RotateScan(ScannedCard scan, System.Drawing.RotateFlipType rotation)
    {
        try
        {
            // Read, rotate, save
            var imageBytes = File.ReadAllBytes(scan.TempImagePath);
            using var bmp = new System.Drawing.Bitmap(new MemoryStream(imageBytes));
            bmp.RotateFlip(rotation);

            using var rotatedStream = new MemoryStream();
            bmp.Save(rotatedStream, System.Drawing.Imaging.ImageFormat.Png);
            var rotatedBytes = rotatedStream.ToArray();

            File.WriteAllBytes(scan.TempImagePath, rotatedBytes);

            // Recompute hash
            rotatedStream.Position = 0;
            var newHash = CardService.ComputeHashFromStream(rotatedStream);
            scan.Hash = newHash;

            // Re-run matching
            OcrMatchResult? ocrResult = null;
            if (scan.Game == CardGame.OnePiece)
            {
                var (cn, conf) = await CardService.OcrService.DetectOptcgCollectorNumberAsync(rotatedBytes);
                if (cn is not null && conf >= 0.5)
                    ocrResult = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = conf };
            }

            var (match, matchedGame) = CardService.FindBestMatch(newHash, null, ocrResult, CardService.SelectedSetFilter, null);
            scan.Match = match;
            scan.Game = matchedGame;

            if (match is not null && scan.FlagReason is FlagReason.NoMatch or FlagReason.VeryLowConfidence or FlagReason.MissingFromDatabase)
                scan.FlagReason = FlagReason.None;

            // Force image refresh by toggling path
            var path = scan.TempImagePath;
            scan.TempImagePath = "";
            scan.TempImagePath = path;

            _logger.LogInformation("Manually rotated scan {Path}, new hash {Hash:X16}, match: {Match}",
                path, newHash, match?.Name ?? "(none)");

            RefreshScanStats();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual rotation failed");
        }
    }
```

Note: This requires exposing `ComputeHashFromStream` and the OCR service from `CardService`. Add these to `ICardService`:

In `OmniCard.Shared/Interfaces/ICardService.cs`, add:

```csharp
    ulong ComputeHashFromStream(Stream stream);
    IOcrMatchingService OcrService { get; }
```

In `OmniCard.Collection/CardService.cs`, add the implementations:

```csharp
    public ulong ComputeHashFromStream(Stream stream) => _hashService.ComputeHash(stream);
    public IOcrMatchingService OcrService => _ocrService;
```

Also, `ScannedCard.TempImagePath` needs to change from `init` to `set` for the image refresh toggle to work. In `OmniCard.Shared/Models/ScannedCard.cs`:

```csharp
    [ObservableProperty]
    public partial string TempImagePath { get; set; }
```

Remove the `required` keyword and `init` accessor, add `[ObservableProperty]` so the UI refreshes. This changes it from a plain property to a source-generated observable one.

- [ ] **Step 2: Add rotate buttons to ScannerDetailPanelView.xaml**

In `OmniCard/Views/Root/ScannerDetailPanelView.xaml`, after the selection header TextBlock (around line 35) and before the manual search control, add:

```xml
            <!-- Rotation controls (single selection only) -->
            <StackPanel Orientation="Horizontal"
                        Margin="0,0,0,8"
                        Visibility="{Binding ViewModel.HasSingleSelection, Converter={StaticResource BoolToVis}}">
                <Button Command="{Binding ViewModel.RotateLeftCommand}"
                        ToolTip="Rotate 90° left"
                        Padding="8,4" Margin="0,0,4,0"
                        Style="{StaticResource MaterialDesignFlatButton}"
                        Content="↶ Rotate Left"/>
                <Button Command="{Binding ViewModel.RotateRightCommand}"
                        ToolTip="Rotate 90° right"
                        Padding="8,4"
                        Style="{StaticResource MaterialDesignFlatButton}"
                        Content="↷ Rotate Right"/>
            </StackPanel>
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build -v minimal && dotnet test OmniCard.Tests -v minimal`
Expected: Build succeeded, all tests pass

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/ScannerDetailPanelView.xaml OmniCard.Shared/Models/ScannedCard.cs OmniCard.Shared/Interfaces/ICardService.cs OmniCard.Collection/CardService.cs
git commit -m "feat: add manual rotation controls (90° increments) in scan detail panel"
```

---

### Task 5: Verification

**Files:**
- Review: All files modified in Tasks 1-4

- [ ] **Step 1: Full build**

Run: `dotnet build -v minimal`
Expected: Build succeeded

- [ ] **Step 2: Full test suite**

Run: `dotnet test OmniCard.Tests -v minimal`
Expected: All tests pass

- [ ] **Step 3: Verify the complete flow**

Review the async block ordering in `CardService.AddFromStream`:
1. Card added to queue → `ScannedCards.Add(scannedCard)`
2. OCR phase (MTG name recognition or OPTCG collector number)
3. Auto-rotation if still no match (180°)
4. Upgrade to `MissingFromDatabase` if still no match after rotation
5. Diagnostic logging

Verify:
- `FlagReason.MissingFromDatabase` exists in enum
- `CollectionCard.IsMissing` exists with schema upgrade
- `CommitScans` handles missing cards (creates `CollectionCard` with `IsMissing = true`)
- `BuildIsExpression` handles `"missing"` value
- Flag navigation commands select and scroll to flagged cards
- Auto-rotation runs after OCR, before MissingFromDatabase upgrade
- Manual rotation updates temp file, hash, match, and refreshes image
- `ScannedCard.Hash` is `{ get; set; }` (not `init`)
- `ScannedCard.TempImagePath` is `[ObservableProperty]` with `{ get; set; }`
