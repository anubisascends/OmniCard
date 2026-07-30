# Missing from Database — Clear Match, Flag, and Filter

**Date:** 2026-07-13
**Status:** Approved

## Problem

Three related issues:

1. **Cross-game matching bug:** The matching pipeline can return a match from the wrong game's database (e.g., a One Piece scan matching to a Magic card). The existing game isolation design (2026-07-12) addresses UI-level guardrails but not the matching pipeline itself.

2. **No way to mark a card as "missing from database":** When a card exists physically but isn't in the reference database, users have no way to flag it during scan review. The `FlagReason.MissingFromDatabase` enum value exists but is only set automatically after OCR exhaustion — users cannot set it manually.

3. **No way to clear an incorrect match:** If the app selects a wrong match, users can reassign to a different card but cannot clear the match entirely. For cards missing from the database, there is no correct card to reassign to.

4. **No way to find missing cards later:** After committing, cards flagged as missing from database are saved as "Unknown Card" entries but there's no filter to locate them in the collection. When bulk scanning 200-300 cards, users need to find these without scrolling through the entire collection.

## Scope

**In scope:**
- Cross-game guard in the matching pipeline
- "Clear Match" button in scan review detail panel
- Clearing a match automatically sets `FlagReason.MissingFromDatabase`
- Persist `FlagReason` on `CollectionCard` so it survives commit
- Collection filter to show missing-from-database cards

**Out of scope:**
- Post-commit match editing (editing collection cards after they're saved)
- Dedicated missing cards dashboard/report page
- Automatic `MissingFromDatabase` detection (the existing OCR exhaustion auto-flag stays as-is)

## Design

### 1. Cross-Game Matching Guard

**File:** `OmniCard.Collection/CardService.cs` — `FindBestMatch()` (line ~148)

**Root cause:** `FindBestMatch()` has an explicit cross-game fallback (lines 166-178) that tries all other game services when the primary game has no match. If a One Piece scan produces a pHash that happens to be within Hamming distance of a Magic card, the fallback returns it as a match.

**Fix:** Remove the cross-game fallback loop entirely. When the primary game service returns no match, `FindBestMatch` should return `(null, SelectedGame)` — the same behavior as when a set filter is active. A scan should only ever match within its own game.

The existing code at lines 162-164 already does this when a set filter is active:
```csharp
if (setFilter is not null)
    return (null, SelectedGame);
```

The fix makes this the unconditional behavior — delete the fallback loop (lines 166-178) and always return `(null, SelectedGame)` when the primary service has no match.

This complements the UI-level isolation from the 2026-07-12 game isolation design, which blocks game switching while scans are pending.

### 2. "Clear Match" Button in Scan Review

**Files:** `OmniCard/Views/Root/ScannerDetailPanelView.xaml`, `OmniCard/Views/Root/RootViewModel.cs`

**UI placement:** Add a "Clear Match" button in the detail panel (`ScannerDetailPanelView.xaml`), visible when the selected scanned card has a match (`Match != null`). Style it consistently with the existing Flag/Unflag button — use a warning/caution color (e.g., material design amber or red) with a clear icon.

**Command: `ClearMatchCommand`** (in `RootViewModel.cs`):

1. Capture the current match data as JSON (`OriginalData`) for the flag fix record
2. Set `SelectedScannedCard.Match = null`
3. Set `SelectedScannedCard.FlagReason = FlagReason.MissingFromDatabase`
4. Create a `ScanFlagFix` on the scanned card:
   - `FixType = "ClearMatch"`
   - `OriginalFlagReason = FlagReason.None` (or whatever it was before)
   - `OriginalData` = serialized original match
   - `ResolvedData = null`
   - `FixedAt = DateTime.UtcNow`
5. Log diagnostic event via `_diagnosticService.LogUserFlagged()` with reason `MissingFromDatabase`
6. Show status message: "Match cleared — marked as missing from database."

**CanExecute:** `SelectedScannedCard != null && SelectedScannedCard.Match != null`

**Detail panel behavior when match is cleared:**
- Match info section hides (no card name, set, confidence to display)
- Scan image remains visible
- Flag indicator shows "Missing from Database"
- The "Clear Match" button hides (no match to clear)
- User can still set condition, foil, container/location overrides

### 3. Verify/Commit Interaction

**Verify ("Scans Verified"):** The `ToggleAuditCompleteCommand` iterates matched scanned cards and confirms them. Cards with `Match == null` (including missing-from-database cards) are skipped by the confirmation logic — they pass through as-is. No change needed; the existing `HasMatchedScans` check ensures verify is still enabled as long as at least one card has a match.

**Commit:** `CardService.CommitScans` already handles `FlagReason.MissingFromDatabase` by creating an "Unknown Card" `CollectionCard` entry. The enhancement is to also persist the `FlagReason` value on the committed `CollectionCard` (see section 4).

### 4. Persist FlagReason on CollectionCard

**Files:** `OmniCard.Shared/Models/CollectionCard.cs`, `OmniCard.Data/CollectionDbContext.cs`, new EF migration

**Data model change:** Add a nullable `FlagReason` property to `CollectionCard`:

```csharp
public FlagReason? FlagReason { get; set; }
```

Using the existing `FlagReason` enum (not a new bool) preserves the distinction between different flag types if filtering by type is ever needed. Nullable because most collection cards have no flag.

**Migration:** Add a nullable `int` column `FlagReason` to the `CollectionCards` table. Nullable columns require no data backfill.

**CommitScans change:** In `CardService.CommitScans`, when creating the `CollectionCard` from a `ScannedCard`, copy the flag reason:

```csharp
collectionCard.FlagReason = scannedCard.FlagReason != FlagReason.None
    ? scannedCard.FlagReason
    : null;
```

### 5. Collection Filter for Missing-from-Database Cards

**Files:** `OmniCard/Views/Root/CollectionTabView.xaml` (or wherever the filter UI lives), collection search/filter logic

**Implementation:** Add a filter option to the existing collection filter system that filters by `FlagReason == FlagReason.MissingFromDatabase`. This could be:

- A toggle/checkbox in the filter bar: "Show Missing from DB Only"
- Or a built-in filter preset added to the preset list

**Recommendation:** A simple toggle/checkbox is more discoverable and quicker to use than a preset. Place it near the existing filter controls.

**Query:** The collection search method adds a `.Where(c => c.FlagReason == FlagReason.MissingFromDatabase)` clause when the toggle is active.

**Display:** Filtered cards show their scan image, storage location (container/page/slot), and date added — everything needed to find the physical card.

## Files Affected

| File | Change |
|------|--------|
| `OmniCard.Collection/CardService.cs` | Remove cross-game fallback in `FindBestMatch()` |
| `OmniCard/Views/Root/ScannerDetailPanelView.xaml` | Add "Clear Match" button |
| `OmniCard/Views/Root/RootViewModel.cs` | Add `ClearMatchCommand` |
| `OmniCard.Shared/Models/CollectionCard.cs` | Add nullable `FlagReason` property |
| `OmniCard.Data/CollectionDbContext.cs` | Configure `FlagReason` column mapping (if needed) |
| New: EF migration | Add `FlagReason` column to `CollectionCards` table |
| `OmniCard.Collection/CardService.cs` | Copy `FlagReason` during commit |
| `OmniCard/Views/Root/CollectionTabView.xaml` | Add "Missing from DB" filter toggle |
| Collection search/filter logic | Add `FlagReason` filter clause |

## Testing

- Scan a One Piece card where no OPTCG match exists → returns null (no cross-game fallback to MTG)
- Scan a card, click "Clear Match" → match cleared, flag set to MissingFromDatabase, orange border shown
- Verify scans with one missing-from-database card → verify succeeds, missing card passes through
- Commit scans with missing-from-database card → saved as "Unknown Card" with FlagReason persisted
- Open collection, toggle "Missing from DB" filter → only flagged cards shown with scan images and locations
- Clear match on a card that was already flagged for another reason → FlagReason updated, original flag reason captured in ScanFlagFix
