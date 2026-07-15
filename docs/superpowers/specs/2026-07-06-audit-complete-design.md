# Audit Complete Button — Design Spec

## Overview

Add an "Audit Complete" toggle button to the Scanner tab toolbar, positioned to the left of the "Commit Scans to Collection" button. This button allows users to confirm all scanned card matches at once (bulk confirm to 100% confidence), asserting that every card is properly matched to its set, card name, and printing. After auditing, the scanner is locked until the user either commits or undoes the audit.

## Requirements

1. **Button placement:** Immediately left of "Commit Scans to Collection" in the toolbar, separated by a `<Separator/>`.
2. **Toggle behavior:**
   - Default text: "Audit Complete"
   - After clicking: text changes to "Undo Audit"
3. **Disabled when:** No scanned cards exist, or any scanned card has a `null` Match.
4. **Audit action (click "Audit Complete"):**
   - Snapshot all cards' original confidence values into a dictionary for undo.
   - For each scanned card, apply the same logic as `ConfirmMatch`: call `RecordCorrection`, handle flagged cards (`ScanFlagFix`), log user confirmation, set confidence to 100%.
   - Set `IsAuditComplete = true`.
5. **Scanner lock (while `IsAuditComplete` is true):**
   - "Scan Cards..." button disabled.
   - Ctrl+N keyboard shortcut disabled.
   - "Reprocess Unmatched" button disabled.
   - "Clear Queue" button disabled.
6. **Undo action (click "Undo Audit"):**
   - Revert all cards' Match confidence to their pre-audit snapshot values.
   - Clear the snapshot dictionary.
   - Set `IsAuditComplete = false`.
   - Scanner unlocks (Scan, Reprocess, Clear re-enabled).
7. **Commit clears audit state:** When "Commit Scans to Collection" completes, `IsAuditComplete` resets to false and the snapshot is cleared (natural consequence of the queue being emptied).

## Approach: Audit State Toggle (Single Button)

A single button that toggles between "Audit Complete" and "Undo Audit" states, driven by the `IsAuditComplete` observable property. This minimizes UI clutter and matches the existing toggle pattern used by "Commit Scans" / "Committing...".

## ViewModel Changes — `RootViewModel.cs`

### New Properties

- `[ObservableProperty] bool IsAuditComplete` — drives button text, scanner lock, and undo logic.
- `private Dictionary<ScannedCard, double?> _preAuditConfidences` — stores original confidence values for undo. Not observable; internal bookkeeping only.

### New Command: `AuditCompleteCommand`

```
[RelayCommand]
public void ToggleAuditComplete()
```

**If `IsAuditComplete` is false (perform audit):**
1. Snapshot each card's current `Match.Confidence` into `_preAuditConfidences`.
2. For each scanned card with a non-null Match, apply the full `ConfirmMatch` logic:
   - `RecordCorrection(card.Hash, match.GameSpecificId, bestArtHash)`
   - Handle flagged cards (create `ScanFlagFix`, clear flag reason)
   - Log user confirmation via diagnostic service
   - Replace `card.Match` with a copy at `Confidence = 100`
3. Set `IsAuditComplete = true`.
4. Set status message: `"Audit complete — confirmed {count} cards."`.

**If `IsAuditComplete` is true (undo audit):**
1. For each entry in `_preAuditConfidences`, revert `card.Match` to a copy with the original confidence value.
2. Clear `_preAuditConfidences`.
3. Set `IsAuditComplete = false`.
4. Set status message: `"Audit undone — reverted {count} cards."`.

### Scanner Lock

The following commands check `IsAuditComplete` and early-return (or are disabled via CanExecute) when true:
- `Scan()` — early return if `IsAuditComplete`
- `ReprocessScans()` — early return if `IsAuditComplete`
- `ClearScans()` — early return if `IsAuditComplete`

### Commit Cleanup

In `CommitScans()`, after the queue is cleared:
- `_preAuditConfidences?.Clear()`
- `IsAuditComplete = false`

## XAML Changes — `ScannerTabView.xaml`

Insert before the existing "Commit Scans to Collection" button (around line 129):

```xml
<Separator/>
<Button Command="{Binding ViewModel.ToggleAuditCompleteCommand}">
    <Button.Style>
        <Style TargetType="Button"
               BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Content"
                    Value="Audit Complete"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding ViewModel.IsAuditComplete}"
                             Value="True">
                    <Setter Property="Content"
                            Value="Undo Audit"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>
```

Disable scanner-related buttons when audit is active:

- "Scan Cards..." button: add `IsEnabled` binding or style trigger on `IsAuditComplete`.
- "Reprocess Unmatched" button: same.
- "Clear Queue" button: same.

## Testing

- Scan several cards with varying confidence levels.
- Click "Audit Complete" — verify all cards show 100% confidence, Match buttons disappear, scanner buttons are disabled.
- Click "Undo Audit" — verify all cards revert to original confidence, scanner re-enabled.
- Click "Audit Complete" again, then "Commit Scans to Collection" — verify commit works normally and audit state resets.
- Verify button is disabled when no cards are scanned or when any card has no match.
