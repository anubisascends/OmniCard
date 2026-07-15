# OPTCG Collector-Number OCR: Taller Region + Fuzzy Vocabulary Resolution

**Date:** 2026-07-14
**Status:** Approved

## Summary

One Piece cards still miss because Windows OCR, on the stylized card font,
**misreads digits as letters** (`1→I/t`, `5→S`, `0→O`) — so "OP15-011" comes out
as `OPIS.OIIU` / `opts-0110` — and the strict digit regex never matches. Separately,
the current crop region is too short (height 0.055) and OCR reads nothing at all.

Two changes fix it:
1. **Taller crop region** so Windows OCR engages.
2. **Fuzzy resolution against the known card-number vocabulary** — correct OCR errors by
   snapping the garbled text to the closest real `CardSetId`, instead of requiring a
   perfect digit read.

## Evidence (diagnostic harness)

Running the real Windows OCR over a saved OP15-011 scan across regions × preprocessing:

| Region | height | OCR output | note |
|---|---|---|---|
| tight (current) | 0.055 | `""` | too short — no text detected |
| tight-taller | 0.075 | `OPIS.OIIU` | engages; digits misread as letters |
| wide line+number | 0.065 | `East Blue/Krieg PiratesJ opts-0110` | engages |
| full bottom strip | 0.120 | `Pearl B[lue]/Krieg Pirateo OPIS•OII` | engages |

Decoding: `OPIS.OIIU` = OP15-011 (`1→I`, `5→S`, `-→.`, `0→O`, trailing noise). The digit
misreads are consistent and defeat any strict-digit regex. Contrast/preprocessing was
never the issue.

## Design

### 1. Crop region (engagement)

Raise `OptcgCollectorNumberRegion` height so OCR engages. Target `(0.66, 0.915, 0.28,
0.075)` (taller and slightly wider than the current `(0.68, 0.925, 0.24, 0.055)`). The
harness confirmed this reads the number region; extra surrounding text is harmless
because resolution is fuzzy (§3).

### 2. OCR read contract

`OcrMatchingService.DetectOptcgCollectorNumberAsync` returns the **raw recognized text**
of the region (best-effort) plus a confidence, rather than returning null when a strict
regex fails. Number *interpretation* moves to the resolver (§3) where the card-number
vocabulary lives. It returns `(null, 0)` only when OCR reads nothing or the engine is
unavailable.

`ExtractCollectorNumber` (the strict regex helper) stays — it becomes the fast path
inside the resolver.

### 3. Fuzzy resolver in `OptcgService`

`OptcgService` owns the card list, so resolution lives there. A new internal resolver
takes the raw OCR text and returns a `CardSetId` (or null):

1. **Fast path:** `ExtractCollectorNumber(rawText)` → if it yields a number that exists
   in the DB, return it (clean reads stay exact and instant, `Confidence = 100`).
2. **Fuzzy path:** otherwise, **confusable-collapse** both the OCR text and each
   candidate `CardSetId` into digit-classes, then match:
   - Collapse map (applied after uppercasing and stripping non-alphanumerics):
     `{0,O,Q,D}→0`, `{1,I,L,T,J,|}→1`, `{2,Z}→2`, `{5,S}→5`, `{6,G}→6`, `{8,B}→8`,
     `{9}→9`, others unchanged. (Applied to both sides, so the letter-prefix ambiguity —
     e.g. the `O` in `OP` — cancels out.)
   - For each `CardSetId`, compute the minimum Levenshtein distance between its collapsed
     form and any equal-length window of the collapsed OCR text.
   - Pick the `CardSetId` with the smallest distance, but **only if** distance ≤ threshold
     (proposed: ≤ 1 for card numbers ≤ 8 chars, else ≤ 2) **and** it is uniquely best
     (the next-best candidate is strictly worse). Ties or over-threshold → no match.
3. No match → resolver returns null → `FindClosestMatch` falls back to pHash.

`FindClosestMatch` Phase 0 calls this resolver instead of the current exact
`LookupOptcgCard(ocrResult.CollectorNumber)`.

### 4. Safety / confidence

- **Exact read** (fast path, distance 0): assign, `Confidence = 100`, unflagged.
- **Fuzzy correction** (distance > 0): assign the unique-best match, but flag it
  `VeryLowConfidence` so it surfaces for review rather than committing silently. These
  are cards that would otherwise miss entirely (many are unhashed), so a review-flagged
  correction is strictly better than a miss, while the flag guards against a wrong snap.
- **Ambiguous / over-threshold:** no OCR match; fall back to pHash.

The resolver returns both the matched `CardSetId` and whether it was exact or corrected,
so the caller can set the confidence/flag accordingly.

### 5. Testability

The collapse + fuzzy-match logic is a pure function (input: OCR text + candidate list;
output: best match + exact/corrected flag). Unit-tested against the **real harness
strings**:
- `"OPIS.OIIU"`, `"opts-0110"`, `"East Blue/Krieg PiratesJ opts-0110"`,
  `"Pearl B/Krieg Pirateo OPIS•OII"` → all resolve to `OP15-011` (corrected).
- A clean `"OP15-011"` → resolves exact.
- Negative cases: empty text, and a garble that is ambiguous or too far → no match (must
  NOT snap to a wrong card).
- A near-miss that could collide (e.g. text closer to `OP15-017`) resolves to the correct
  distinct number, proving the collapse keeps real numbers distinguishable.

The taller region's OCR *engagement* can't be unit-tested (Windows OCR engine) and is
verified by re-scanning OP15-011 / EB04-003 and confirming the log shows the number
resolved.

## Out of scope

- Hash coverage (~60%) — separate follow-up; fuzzy OCR now covers unhashed cards.
- MTG matching, pHash threshold.
- The diagnostic harness stays (useful for future OCR tuning) but is a no-op without its
  env var.
