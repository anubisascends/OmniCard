# OCR-First OPTCG Matching + Robust Collector-Number OCR

**Date:** 2026-07-14
**Status:** Approved

## Summary

One Piece scans miss frequently even for cards that exist in the database. Two
changes fix it:

1. **Robust OCR read (the actual miss-fix).** Harden `DetectOptcgCollectorNumberAsync`
   so it reliably reads the printed collector number from the card crop — grayscale +
   autocontrast preprocessing, a retry, and per-attempt logging.
2. **OCR-first pipeline (One Piece only).** Try the collector-number OCR *before* the
   perceptual hash; only fall back to pHash when OCR yields no resolving number.

## Root cause (evidence)

From the live log (`X:\TCG Card Scanner\logs\tcgcardscanner-20260714.log`) and the
saved scan images:

- The OCR **rescue already works when OCR reads**: OP15-024 ("Usopp") missed pHash
  (distance 16) but OCR read `OP15-024` → "OPTCG OCR direct match". So *ordering* was
  never the blocker — the async OCR already overrides pHash.
- OCR **intermittently returns empty on legible crops**: OP15-011 and EB04-003 both
  show the number clearly in the crop region, but OCR returned empty (the silent
  `IsNullOrWhiteSpace(text)` path in `DetectOptcgCollectorNumberAsync`), so those cards
  fell through to pHash and were flagged missing. Same font/region as the ones that
  worked — the single raw-crop pass is the weak point.
- **Contrast is the lever**: the raw crop is white text on dark red with a bright icon
  blob intruding; grayscale + autocontrast turns it into crisp white-on-near-black,
  which Windows OCR reads far more reliably (validated by inspecting the preprocessed
  crop).
- **pHash coverage is only ~60%** (4005/6712 rows hashed; ~750 *base* cards unhashed,
  including EB04-003 whose base row has no `ImageHash`). Those cards can *only* be
  matched by OCR, so a reliable OCR read is essential — pHash cannot cover them.

## Design

### Part A — Robust collector-number OCR read

Harden `OmniCard.Imaging/OcrMatchingService.DetectOptcgCollectorNumberAsync` (and its
helper `OcrCroppedRegionAsync`):

1. **Preprocess before OCR:** after the existing crop + upscale, convert the region to
   grayscale and apply autocontrast (linear histogram stretch: map the region's min/max
   luminance to 0–255, with a small percentile cutoff to ignore outliers). This is the
   primary fix.
2. **Retry:** if the preprocessed read yields empty text or no regex match, retry once
   on the un-preprocessed (color, upscaled) crop. Return the first read whose text
   matches the collector-number regex.
3. **Diagnostic logging:** log the raw OCR text of each attempt at DBG (e.g.
   `OPTCG OCR attempt (preprocessed): "{Text}"`), so a future miss shows what OCR
   actually saw. Keep the existing "detected" / "did not match pattern" logs.
4. Keep the corrected crop region `(0.68, 0.925, 0.24, 0.055)` unchanged.

The regex and success contract (`(string? CollectorNumber, double Confidence)`) are
unchanged; only the reading is made robust.

**Verification caveat:** Windows OCR (`Windows.Media.Ocr`) cannot run in the unit-test
environment, so Part A is verified by re-scanning OP15-011 and EB04-003 and confirming
the log shows `OPTCG collector number detected` and an OCR match. Automated tests cover
the surrounding logic, not the OCR engine itself.

### Part B — OCR-first pipeline (One Piece only)

For `SelectedGame == OnePiece`, change the match order so OCR is authoritative:

1. Compute the pHash as today (still needed for correction recording and the rotation
   retry) — but do **not** run the pHash match as the authoritative result in the
   synchronous path for One Piece, and do not log "no match" / flag there.
2. In the post-queue async step: run `DetectOptcgCollectorNumberAsync` first.
   - If it returns a number that resolves to a DB row (`FindClosestMatch` via the
     OCR collector number) → assign that match, high confidence, unflagged. **pHash is
     not consulted.**
   - If OCR yields no number, or the number doesn't resolve → run the pHash
     `FindClosestMatch` fallback (unchanged, `maxDistance` stays 14).
3. If both fail, the existing 180° rotation retry runs (OCR-first again, then pHash).
4. If everything fails → flag missing from database (unchanged).

MTG behavior is unchanged (synchronous pHash + async OCR/symbol refinement).

### Threading & structure

The pHash value is still computed synchronously in `AddFromStream` (it's used for
corrections and rotation regardless). Only the *One Piece match decision* moves into the
async step and is reordered OCR-first. The One Piece resolution logic is factored into a
method on `CardService` that takes the hash, the raw image bytes, and uses the injected
`IOcrMatchingService` + game service — so it can be unit-tested with fakes.

### Error handling

- OCR throws → caught and logged (existing `try/catch`), falls through to pHash.
- OCR number doesn't resolve in the DB → pHash fallback (OCR may have misread, or the
  card is genuinely absent).
- Both fail after rotation → `FlagReason.MissingFromDatabase` (existing).

### Testing

`CardService` is constructed with fake services in existing tests, so:

1. **OCR resolves → pHash skipped:** fake `IOcrMatchingService` returns a valid number,
   fake game service resolves it; assert the scan's `Match` is the OCR result and the
   pHash `FindClosestMatch` was not used (e.g. a fake matcher records calls).
2. **OCR empty → pHash fallback:** fake OCR returns `(null, 0)`; assert pHash
   `FindClosestMatch` is consulted and its result assigned.
3. **OCR number unresolved → pHash fallback:** fake OCR returns a number the game
   service doesn't find; assert pHash fallback runs.
4. **Both fail → flagged missing.**

Part A's preprocessing is exercised only via the live re-scan (documented above).

## Out of scope (noted, separate follow-up)

- **Hash coverage (~60%).** Many rows have null/failed image URLs and no `ImageHash`, so
  the pHash fallback is weak. Investigating image-download failures / re-running hashing
  is a separate task; it does not block this fix because OCR-first covers unhashed cards.
- Changing the pHash `maxDistance` threshold.
- MTG matching.
