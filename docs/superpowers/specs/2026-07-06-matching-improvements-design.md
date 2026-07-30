# Scan Matching Improvements — Design Spec

**Date:** 2026-07-06
**Status:** Draft
**Goal:** Improve card matching confidence and diagnostic visibility based on analysis of real scan diagnostic data (250-card MOM scan session).

---

## 1. Problem Statement

Diagnostic analysis of a 250-card MOM scan session revealed:
- OCR data never appears in diagnostics (zero OCR results across all scans)
- Confidence is systematically low (average 45.9%) despite correct matches — the formula is too harsh for scanner-quality images
- Art hash distances show dramatically better discrimination than pHash (correct: 0-8 vs wrong: 18-34) but art hash is only used for tiebreaking
- Tie zones contain up to 35 candidates, which is noise rather than signal
- Only 17 of ~250 scans were captured in diagnostics due to session tracking issues

## 2. Fix 1: Always Log OCR Results to Diagnostics

### Problem
The first `LogScanCompleted` in `CardSevice.AddFromStream()` fires before OCR runs (so OCR data is null). The second `LogScanCompleted` only fires if OCR *changes* the match. When OCR confirms the same match or fails, no OCR data is ever recorded.

### Change
After OCR completes inside the `BeginInvoke` callback in `AddFromStream()`, always log an updated `ScanCompleted` event with the OCR results — regardless of whether OCR changed the match. This replaces the current conditional-only second log.

The updated log should include:
- The `OcrMatchResult` from `AnalyzeCardAsync`
- The `LastMatchDiagnostics` from the re-match (if OCR triggered one) or from the original match
- The current match (whether OCR-improved or original)

### Files
- Modify: `OmniCard/Services/CardSevice.cs` — `AddFromStream()` BeginInvoke callback

## 3. Fix 2: Verify and Warn on Empty Symbol Hashes

### Problem
`OcrMatchingService.SymbolHashes` may be empty, causing `DetectSetSymbol` and `MatchSymbol` to silently return nothing. The loading path in `AddFromStream` tries to populate them from `ScryfallService.GetSymbolHashes()` but this may fail silently.

### Change
- Add a warning log in `OcrMatchingService.DetectSetSymbol()` when `SymbolHashes` is empty at call time
- Add a warning log in `OcrMatchingService.AnalyzeCardAsync()` when `SymbolHashes` is empty
- In `CardSevice.AddFromStream()`, log whether symbol hash loading succeeded or failed, and how many hashes were loaded

### Files
- Modify: `OmniCard/Services/OcrMatchingService.cs` — `DetectSetSymbol()`, `AnalyzeCardAsync()`
- Modify: `OmniCard/Services/CardSevice.cs` — symbol hash loading block

## 4. Fix 3: Fix Diagnostic Capture Completeness

### Problem
Only 17 of ~250 scans had ScanCompleted events. The "orphaned" session had 23 user corrections with no matching scan events.

### Change
- Ensure `StartNewDiagnosticSession()` is called early — verify it runs before the first scan arrives, not after
- In the user action log methods (`LogUserFlagged`, `LogUserConfirmed`, `LogUserCorrected`, `LogUserUnflagged`), if no `ScanCompleted` event exists for the scan hash, create a minimal backfill `ScanCompleted` event with the data available from the `ScannedCard` — this ensures the export can always render the card's story even if the original scan event was missed

### Files
- Modify: `OmniCard/Services/ScanDiagnosticService.cs` — user action log methods
- Modify: `OmniCard/Services/CardSevice.cs` or `OmniCard/Services/ScannerService.cs` — session start timing

## 5. Fix 4: Incorporate Art Hash into Confidence

### Problem
Art hash distances discriminate correct matches (0-8) from incorrect candidates (18-34) much better than pHash, but art hash is only used for tiebreaking within the tie zone. Confidence is computed solely from pHash distance.

### Change
At the final return point in `ScryfallService.FindClosestMatch()`, when art hashes are available, compute a blended confidence:

```
artHashDistance = best art hash distance between scan art hashes and the winner's art hash
artConfidence = max(0, (1 - artHashDistance / 20)) * 100
pHashConfidence = max(0, (1 - pHashDistance / maxDistance)) * 100
blendedConfidence = 0.5 * pHashConfidence + 0.5 * artConfidence
```

This replaces the current pHash-only confidence at the final return. The `maxDistance` used for *filtering candidates* stays unchanged — only the *reported confidence* changes.

Also apply this blended confidence at the Phase 3 confident hash return point, where art hash data is available.

### Files
- Modify: `OmniCard/Services/ScryfallService.cs` — confidence calculation at final return and Phase 3 return

## 6. Fix 5: Raise maxDistance from 10 to 14

### Problem
Correct matches at pHash distances 8-10 are being rejected or auto-flagged as VeryLowConfidence. Scanner-quality images produce noisier hashes than reference images.

### Changes
- `FindClosestMatch` default `maxDistance` parameter: 10 → 14
- `ConfidentHashThreshold` constant: 6 → 8
- Auto-flag threshold in `CardSevice.AddFromStream()`: confidence `< 20` → confidence `< 15`

### Impact
With `maxDistance = 14` and blended confidence (Fix 4):
- pHash distance 8, art hash distance 6: pHash confidence = 43%, art confidence = 70%, blended = 56% (was 20%)
- pHash distance 10, art hash distance 4: pHash confidence = 29%, art confidence = 80%, blended = 54% (was 0%)
- pHash distance 4, art hash distance 6: pHash confidence = 71%, art confidence = 70%, blended = 71% (was 60%)

### Files
- Modify: `OmniCard/Services/ScryfallService.cs` — `FindClosestMatch()` parameter default, `ConfidentHashThreshold`
- Modify: `OmniCard/Services/CardSevice.cs` — auto-flag threshold

## 7. Fix 6: Tighten Tie Zone

### Problem
With `TieZone = 4`, tie zones contain up to 35 candidates — too many to meaningfully disambiguate. A 35-candidate tie zone with pHash distances 5-16 is noise.

### Changes
- Reduce `TieZone` constant from 4 to 2
- After collecting tie zone candidates, keep only the top 10 sorted by distance (discard the rest)

### Files
- Modify: `OmniCard/Services/ScryfallService.cs` — `TieZone` constant, add cap after candidate collection

## 8. Testing

### Updated Tests
- Existing `FindClosestMatch` tests updated for new `maxDistance` default (14 instead of 10) and new `ConfidentHashThreshold` (8 instead of 6)
- Tie zone scoring tests updated for `TieZone = 2` and 10-candidate cap
- Auto-flag threshold tests updated for confidence `< 15`

### New Tests
- Blended confidence calculation: verify correct weighting of pHash + art hash
- Blended confidence with no art hash: verify pHash-only fallback
- Tie zone cap: verify at most 10 candidates retained

## 9. Out of Scope
- Changing the pHash algorithm itself (DCT, resize, histogram equalization)
- Changing the art hash crop regions
- Modifying OCR text recognition (only verifying it runs)
- Scanner hardware settings or DPI changes
