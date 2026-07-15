# Audit Complete Button Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an "Audit Complete" toggle button to the Scanner toolbar that bulk-confirms all scanned cards to 100% confidence, locks the scanner until commit or undo, and supports full undo to original confidence values.

**Architecture:** Single toggle button in the toolbar driven by an `IsAuditComplete` observable property. A private dictionary snapshots pre-audit confidence values for undo. The existing `ConfirmMatch` logic is reused in a loop. Scanner lock is enforced via early-returns in `Scan()`, `ReprocessScans()`, and `ClearScans()`, plus XAML `IsEnabled` bindings on the corresponding toolbar buttons.

**Tech Stack:** WPF, MVVM Toolkit (CommunityToolkit.Mvvm), C# 13 / .NET 10

## Global Constraints

- Follow existing MVVM Toolkit patterns (`[ObservableProperty]`, `[RelayCommand]`)
- Match existing XAML style conventions (BasedOn, DataTrigger patterns)
- No new NuGet packages required
- No new files — all changes are in existing files

---

### Task 1: Add ViewModel State and Toggle Command

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs`

**Interfaces:**
- Consumes: `CardService.ScannedCards`, `ConfirmMatch(ScannedCard)` logic (lines 1013-1070)
- Produces: `IsAuditComplete` property, `ToggleAuditCompleteCommand` command — consumed by XAML in Task 2

- [ ] **Step 1: Add the `IsAuditComplete` observable property and snapshot dictionary**

After the existing `IsCommitting` property (line 1105), add:

```csharp
[ObservableProperty]
public partial bool IsAuditComplete { get; set; }

private Dictionary<ScannedCard, double?>? _preAuditConfidences;
```

- [ ] **Step 2: Add the `ToggleAuditComplete` relay command**

After the `ConfirmMatch` method (after line 1070), add the new command. This reuses the same confirmation logic as `ConfirmMatch` but applies it to all cards:

```csharp
[RelayCommand]
public void ToggleAuditComplete()
{
    if (!IsAuditComplete)
    {
        // --- Perform audit ---
        var cards = CardService.ScannedCards
            .Where(c => c.Match is not null)
            .ToList();

        if (cards.Count == 0) return;

        _preAuditConfidences = new Dictionary<ScannedCard, double?>(cards.Count);

        foreach (var card in cards)
        {
            _preAuditConfidences[card] = card.Match!.Confidence;
            ConfirmMatch(card);
        }

        IsAuditComplete = true;
        Message = $"Audit complete — confirmed {cards.Count} cards.";
        _logger.LogInformation("Audit complete: confirmed {Count} cards", cards.Count);
    }
    else
    {
        // --- Undo audit ---
        if (_preAuditConfidences is not null)
        {
            foreach (var (card, originalConfidence) in _preAuditConfidences)
            {
                if (card.Match is null) continue;
                var match = card.Match;
                card.Match = new CardMatch
                {
                    Name = match.Name,
                    SetCode = match.SetCode,
                    SetName = match.SetName,
                    CollectorNumber = match.CollectorNumber,
                    Rarity = match.Rarity,
                    ImageUri = match.ImageUri,
                    GameSpecificId = match.GameSpecificId,
                    LocalImagePath = match.LocalImagePath,
                    Confidence = originalConfidence,
                    Source = match.Source
                };
            }

            var count = _preAuditConfidences.Count;
            _preAuditConfidences.Clear();
            _preAuditConfidences = null;
            Message = $"Audit undone — reverted {count} cards.";
            _logger.LogInformation("Audit undone: reverted {Count} cards", count);
        }

        IsAuditComplete = false;
    }
}
```

- [ ] **Step 3: Add scanner lock guards to `Scan()`, `ReprocessScans()`, and `ClearScans()`**

In the `Scan()` method (line 857), add an early return as the first line of the method body:

```csharp
public void Scan()
{
    if (IsAuditComplete) return;  // <-- add this line
    if (ConnectToScanner(false) ?? false)
    {
```

In the `ReprocessScans()` method (line 1422), add an early return as the first line:

```csharp
public void ReprocessScans()
{
    if (IsAuditComplete) return;  // <-- add this line
    _logger.LogInformation("User initiated reprocess of unmatched scans");
```

In the `ClearScans()` method (line 1180), add an early return as the first line:

```csharp
public void ClearScans()
{
    if (IsAuditComplete) return;  // <-- add this line
    _logger.LogInformation("Clearing {Count} scanned cards from queue", CardService.ScannedCards.Count);
```

- [ ] **Step 4: Clear audit state in `CommitScans()` after queue is cleared**

In the `CommitScans()` method, after `CardService.ScannedCards.Clear();` (line 1143), add:

```csharp
CardService.ScannedCards.Clear();

// Clear audit state
_preAuditConfidences?.Clear();
_preAuditConfidences = null;
IsAuditComplete = false;
```

- [ ] **Step 5: Build and verify compilation**

Run:
```bash
cd d:/source/repos/OmniCard && dotnet build OmniCard/OmniCard.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat: add Audit Complete toggle command with undo and scanner lock"
```

---

### Task 2: Add XAML Button and Disable Scanner Controls During Audit

**Files:**
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml` (lines 126-153)

**Interfaces:**
- Consumes: `IsAuditComplete` property, `ToggleAuditCompleteCommand` from Task 1
- Produces: UI button and scanner lock visual state

- [ ] **Step 1: Insert the Audit Complete toggle button before the Commit button**

Between the existing `<Separator/>` and the Commit button (line 129-130), insert:

```xml
                    <Button Command="{Binding ViewModel.ToggleAuditCompleteCommand}">
                        <Button.Style>
                            <Style TargetType="Button"
                                   BasedOn="{StaticResource {x:Type Button}}">
                                <Setter Property="Content"
                                        Value="Audit Complete"/>
                                <Setter Property="IsEnabled"
                                        Value="{Binding ViewModel.HasMatchedScans}"/>
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding ViewModel.IsAuditComplete}"
                                                 Value="True">
                                        <Setter Property="Content"
                                                Value="Undo Audit"/>
                                        <Setter Property="IsEnabled"
                                                Value="True"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                    </Button>
                    <Separator/>
```

- [ ] **Step 2: Disable "Scan Cards..." button when audit is active**

Replace the existing Scan button (lines 127-128):

```xml
                    <Button Command="{Binding ViewModel.ScanCommand}"
                            Content="Scan Cards..."
                            IsEnabled="{Binding ViewModel.IsAuditComplete, Converter={StaticResource InverseBooleanConverter}}"/>
```

- [ ] **Step 3: Disable "Reprocess Unmatched" button when audit is active**

Replace the existing Reprocess button (lines 149-150):

```xml
                    <Button Command="{Binding ViewModel.ReprocessScansCommand}"
                            Content="Reprocess Unmatched"
                            IsEnabled="{Binding ViewModel.IsAuditComplete, Converter={StaticResource InverseBooleanConverter}}"/>
```

- [ ] **Step 4: Disable "Clear Queue" button when audit is active**

Replace the existing Clear button (lines 152-153):

```xml
                    <Button Command="{Binding ViewModel.ClearScansCommand}"
                            Content="Clear Queue"
                            IsEnabled="{Binding ViewModel.IsAuditComplete, Converter={StaticResource InverseBooleanConverter}}"/>
```

- [ ] **Step 5: Verify InverseBooleanConverter exists, or add HasMatchedScans property**

Check if `InverseBooleanConverter` is already registered. If not, the scanner lock is already handled by early-returns in Task 1, so the `IsEnabled` bindings can be simplified to:

```xml
IsEnabled="{Binding ViewModel.IsAuditComplete, Converter={local:InverseBoolConverter}}"
```

If no inverse boolean converter exists in the project, add a simple one to `Converters.cs`:

```csharp
public class InverseBoolConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
```

Also add the `HasMatchedScans` computed property to `RootViewModel.cs` for the button's enabled state:

```csharp
public bool HasMatchedScans =>
    CardService.ScannedCards.Count > 0 &&
    CardService.ScannedCards.All(c => c.Match is not null);
```

This property needs to be notified when `ScannedCards` changes. In the constructor or initialization, subscribe to `CardService.ScannedCards.CollectionChanged` and call `OnPropertyChanged(nameof(HasMatchedScans))`.

- [ ] **Step 6: Build and verify compilation**

Run:
```bash
cd d:/source/repos/OmniCard && dotnet build OmniCard/OmniCard.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Root/ScannerTabView.xaml OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/Converters.cs
git commit -m "feat: add Audit Complete button to scanner toolbar with lock UI"
```

---

### Task 3: Manual Smoke Test

**Files:** None (testing only)

- [ ] **Step 1: Launch the app and scan several cards**

Run the app. Scan 3+ cards with varying confidence levels. Verify the "Audit Complete" button is visible in the toolbar to the left of "Commit Scans to Collection".

- [ ] **Step 2: Test button disabled state**

Verify "Audit Complete" is disabled when:
- No cards are scanned (empty queue)
- Any scanned card has no match (null Match)

- [ ] **Step 3: Test audit action**

Click "Audit Complete". Verify:
- All cards now show 100% confidence
- Individual "Match" buttons disappear (confidence >= 80%)
- Button text changes to "Undo Audit"
- "Scan Cards...", "Reprocess Unmatched", and "Clear Queue" buttons are disabled
- Status message reads "Audit complete — confirmed N cards."

- [ ] **Step 4: Test undo action**

Click "Undo Audit". Verify:
- All cards revert to their original confidence values
- "Match" buttons reappear for low-confidence cards
- Button text reverts to "Audit Complete"
- Scanner buttons re-enabled
- Status message reads "Audit undone — reverted N cards."

- [ ] **Step 5: Test audit + commit flow**

Click "Audit Complete" again, then click "Commit Scans to Collection". Verify:
- Cards commit successfully
- Queue is emptied
- Button reverts to "Audit Complete" (disabled, since no cards)
- Scanner buttons re-enabled
