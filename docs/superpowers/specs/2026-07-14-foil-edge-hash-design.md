# Color-Robust Edge Hash for Foil Card Matching

**Date:** 2026-07-14
**Status:** Approved

## Summary

Foil One Piece cards scan with a regional holographic **color shift** (e.g. a yellow
card's frame scans green) plus specular texture. The perceptual hash is computed on
luminance, so the shift moves the hash and foil scans fail to match by pHash. The card's
**structure** (frame, art, text, number outlines), however, is preserved regardless of
color. This feature adds a color-robust **edge hash** and routes scans marked "Is Foil"
to match on it, leaving the (100%-working) luminance pHash untouched for non-foil scans.

## Evidence

- Foil scan `X:\TCG Card Scanner\scans\31948.jpg` (OP16-106, a yellow card) shows a green
  frame. Channel means R=108, G=109, B=94 — nearly balanced, so the green is **not a
  global tint** correctable by white balance; it's a regional foil shift. Color
  normalization is therefore ruled out.
- A gradient/edge view of that scan renders the card's frame, art, text blocks, and
  numbers crisply with color removed — near-identical to what the non-foil reference art's
  edges would produce. Structure is the robust signal.

## Design

### 1. Edge hash (`PerceptualHashService`)

Add `ulong ComputeEdgeHash(Stream imageStream, Action<HashStageResult>? onStage = null)` to
`IPerceptualHashService` and implement it in `PerceptualHashService`. It reuses the existing
hash pipeline (via `LoadBitmap` → grayscale → resize 32×32 → DCT → median-threshold hash)
but inserts a **gradient-magnitude** step: after grayscale, compute per-pixel gradient
magnitude (absolute horizontal + vertical differences, i.e. a simple Sobel-like operator),
and hash that gradient image instead of raw luminance. The gradient reflects shape
boundaries, so a hue/brightness shift that preserves structure yields a near-identical
edge hash.

`ComputeHash` (luminance pHash) is unchanged. `LoadBitmap` (the WebP-capable loader) is
reused so edge hashing also works on WebP references.

### 2. Reference edge hashes (`OptcgCard` + `OptcgDbContext`)

Add a nullable `ulong? EdgeHash` column to `OptcgCard`, added to existing databases via
`OptcgDbContext.ApplySchemaUpgrades` (`ALTER TABLE Cards ADD COLUMN EdgeHash INTEGER`,
following the `LocalImagePath`/variant-column pattern). Index it like `ImageHash`.

Edge hashes are computed for **every** reference card (not just foils): foil-ness is a
property of the *scan*, and a foil scan matches against the non-foil reference art's
structure. `OptcgService.ComputeImageHashesAsync` computes **both** `ImageHash` and
`EdgeHash` from the single downloaded/cached image per card (one download, two hashes) and
`SaveHashBatchAsync` persists both. The `forceAll:false` ("Compute Missing Hashes") path
treats a card as needing work when **either** hash is null, so existing installs backfill
`EdgeHash` on the next hash pass. A one-time "Recompute All Hashes" also populates it.

### 3. Matching (`OptcgService.FindClosestMatch` + `ICardGameService`)

Add an optional `ulong? scanEdgeHash = null` parameter to `ICardGameService.FindClosestMatch`
(and `CardService.FindBestMatch`). Behavior in `OptcgService.FindClosestMatch`:

- **OCR Phase 0 runs first, unchanged** — the collector number is color-invariant, so it
  already handles most foil cards; the edge hash only improves the hash fallback.
- If `scanEdgeHash` is provided (i.e. the scan is foil), the pHash fallback matches the
  scan's edge hash against an in-memory **edge-hash cache** of references (`CardSetId →
  EdgeHash`, built lazily like `_hashCache`, and invalidated wherever `_hashCache` is), by
  minimum Hamming distance, honoring the set filter and `maxDistance`.
- If `scanEdgeHash` is null (non-foil), matching is exactly as today (luminance pHash).

The resulting `CardMatch` confidence is derived from the edge-hash Hamming distance the
same way the luminance path derives it. `maxDistance` starts at the current 14 and is
tunable later via the existing scan diagnostics.

### 4. Scan-side plumbing (`CardService`)

In the scan pipeline, when `scannedCard.IsFoil` and the game is One Piece, compute the
scan's edge hash (`_hashService.ComputeEdgeHash` on the scan bytes) and pass it through
`FindBestMatch` → `FindClosestMatch`. `IsFoil` is already set on the scan at scan time
(from `DefaultIsFoil`), so no new flag is needed. Non-foil scans pass `scanEdgeHash = null`.
Store the scan's edge hash on `ScannedCard` (e.g. `ScanEdgeHash`) so the 180°-rotation retry
and any re-match reuse it.

### 5. What stays the same

- Non-foil matching (luminance pHash) — untouched.
- OCR-first resolution, corrections, rotation, set filter, the MTG/Scryfall path
  (Scryfall's `FindClosestMatch` accepts and ignores `scanEdgeHash`).

## Testing

- **`ComputeEdgeHash` color-invariance** (the core property): hash a synthetic test image
  and a hue-shifted copy of it (same structure, shifted colors); assert the two edge hashes
  are within a small Hamming distance (and far smaller than the luminance pHashes' distance
  for the same pair). Verifiable without any card scan.
- **`ComputeEdgeHash` determinism** and that it runs on a WebP input (reuses `LoadBitmap`).
- **`OptcgService` foil path**: seed a reference with a known `EdgeHash`; call
  `FindClosestMatch` with a matching `scanEdgeHash` (and a deliberately-far `imageHash`) and
  assert it returns that card via the edge path — proving the edge hash, not the luminance
  pHash, produced the match. Also assert a null `scanEdgeHash` uses the luminance path.
- **Schema**: `ApplySchemaUpgrades` adds `EdgeHash` to a legacy table; a fresh DB has it.
- **Live**: mark OP16-106 as foil, scan it, confirm it matches (via the log/diagnostics)
  where it previously missed. (Edge-hash *matching quality* on real foils can only be
  confirmed on the scanner.)

## Out of scope

- Auto-detecting foil (the user marks it manually at scan time).
- Foil handling for MTG/Scryfall.
- Changing the luminance pHash or its threshold for non-foil cards.
- Tuning the edge-hash `maxDistance` (starts at 14; revisit with diagnostics after live use).
