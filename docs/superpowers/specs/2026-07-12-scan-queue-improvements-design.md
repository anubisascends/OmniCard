# Scan Queue Improvements Design

**Date:** 2026-07-12
**Status:** Approved

## Problem

When scanning One Piece (and MTG) cards, several pain points slow down the verification workflow:

- Cards missing from the API database are flagged as "NoMatch" — indistinguishable from a bad scan
- No quick way to jump between flagged cards in a large scan queue
- No persistent record of cards that genuinely don't exist in the database, with their physical locations
- Cards scanned upside-down produce no match, requiring manual intervention
- No way to rotate a scan image without re-scanning

## Scope

Five focused features that improve scan verification speed and accuracy, applicable to all card games (MTG and One Piece).

**In scope:**
1. "Missing from Database" auto-flag
2. Previous/Next flag navigation buttons
3. Committing missing cards to collection with location info + `is:missing` query
4. Auto-rotation (180 degrees) on no match
5. Manual rotation controls (90-degree increments)

**Out of scope:**
- Resolving missing cards when API data is updated (future)
- Rotating multiple selected cards at once
- Trying all four orientations automatically (only 180)

## Design

### 1. "Missing from Database" Flag

**FlagReason enum:** Add `MissingFromDatabase` value to `FlagReason`.

**Auto-flagging logic:** In `CardService.AddFromStream`, in the `Dispatcher.BeginInvoke` async block — after the async OCR phase completes (and auto-rotation has been attempted, see section 4), if `scannedCard.Match` is still null, upgrade the flag from `NoMatch` to `MissingFromDatabase`. This ensures pHash, OCR, and rotation have all had their chance before declaring the card truly missing.

**UI:** Flagged cards with `MissingFromDatabase` show the same orange border as other flags. The existing "Flagged: N" counter includes them. The flag filter shows them alongside other flagged cards.

### 2. Flag Navigation

**Controls:** Two buttons in the scan stats bar, adjacent to the existing "Flagged: N" label — "Previous Flag" and "Next Flag" with arrow icons.

**Behavior:** Starting from the currently selected card's index in `CardService.ScannedCards` (or index 0 if nothing selected), scan backward/forward to find the next card where `IsFlagged == true`. Select it and scroll it into view.

**Wrap-around:** When reaching the end/beginning of the list, wrap to the other side. If no flagged cards exist, do nothing.

**Scope:** Applies to all flag types — `NoMatch`, `VeryLowConfidence`, `Manual`, and `MissingFromDatabase`.

### 3. Missing Cards in Collection

**CollectionCard model:** Add `bool IsMissing` property (default false).

**Schema upgrade:** Add `IsMissing` column to the collection database via the existing `ALTER TABLE ... ADD COLUMN` pattern.

**Committing:** When `CommitScans` processes a `ScannedCard` with `FlagReason.MissingFromDatabase` and `Match is null`, create a `CollectionCard` with:
- `IsMissing = true`
- `Name = "Unknown Card"`
- `GameCardId = ""` (empty)
- `Game` = the selected game
- `ScanImagePath` = committed scan image (same copy flow as matched cards)
- Location fields from active container/overrides

**Querying:** Add `is:missing` to the Scryfall-like query syntax in `BuildFilteredQuery`. Displays like any other card — scan image, location, "Unknown Card" name.

### 4. Auto-Rotation on No Match

**Location:** In the `Dispatcher.BeginInvoke` async block of `CardService.AddFromStream`, after OCR completes and the card still has no match.

**Flow:**
1. `scannedCard.Match` is still null after pHash + OCR
2. Rotate the raw image bytes 180 degrees in-memory using `Bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone)`
3. Recompute pHash on the rotated image
4. Re-run matching: pHash for all games, OCR collector number for One Piece
5. If a match is found:
   - Update `scannedCard.Match`, `scannedCard.Hash`, `scannedCard.Game`
   - Clear the auto-flag (`FlagReason = None`)
   - Overwrite the temp file on disk with the rotated image
6. If no match: leave everything as-is (original orientation preserved), then upgrade flag to `MissingFromDatabase` (per section 1)

**ScannedCard.Hash:** Change from `required init` to `{ get; set; }` to allow updating after rotation.

**Single attempt only:** Only try 180 degrees. Cards on a flatbed scanner are either right-side-up or upside-down.

### 5. Manual Rotation Controls

**Location:** Detail panel (`ScannerDetailPanelView.xaml`), near the top, visible when a single card is selected.

**Controls:** Two buttons — "Rotate Left" (90 CCW) and "Rotate Right" (90 CW).

**Behavior on click:**
1. Rotate the temp file image on disk by the specified 90-degree increment
2. Recompute pHash on the rotated image
3. Re-run matching (pHash + OCR for One Piece, pHash + symbol detection for MTG)
4. Update `scannedCard.Hash`, `scannedCard.Match`
5. Clear any auto-flag if a match is found
6. Force the scan image in the ListView to refresh by toggling `TempImagePath` (set to empty then back to the real path), which triggers the WPF binding to reload the file from disk

**Multi-select:** Disabled — rotation is a per-card operation.

## Files Affected

| File | Change |
|------|--------|
| `OmniCard.Shared/Models/FlagReason.cs` | Add `MissingFromDatabase` value |
| `OmniCard.Shared/Models/ScannedCard.cs` | Change `Hash` from `init` to `set` |
| `OmniCard.Shared/Models/CollectionCard.cs` | Add `bool IsMissing` property |
| `OmniCard.Data/CollectionDbContext.cs` | Schema upgrade for `IsMissing` column |
| `OmniCard.Collection/CardService.cs` | Auto-rotation logic, missing card commit, `is:missing` query support |
| `OmniCard/Views/Root/RootViewModel.cs` | Flag navigation commands, rotate commands, MissingFromDatabase upgrade |
| `OmniCard/Views/Root/ScannerTabView.xaml` | Flag navigation buttons in stats bar |
| `OmniCard/Views/Root/ScannerDetailPanelView.xaml` | Rotate left/right buttons |
| `OmniCard/Views/Root/ScannerTabView.xaml.cs` | Scroll-to-selected helper for flag navigation |

## Testing

- Scan with no match + OCR fail → auto-rotation attempted → still no match → flagged as `MissingFromDatabase`
- Scan upside-down card → auto-rotation finds match → temp file overwritten rotated, flag cleared
- Flag navigation wraps around list, skips unflagged cards
- Commit missing card → `CollectionCard` created with `IsMissing = true` and correct location
- `is:missing` query returns only missing cards
- Manual rotate updates image, recomputes hash, re-runs matching
- Manual rotate refreshes the scan image in the UI
