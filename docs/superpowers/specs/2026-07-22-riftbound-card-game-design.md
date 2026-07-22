# Riftbound Card Game Support — Design

**Date:** 2026-07-22
**Status:** Approved (pending spec review)

## Goal

Add support for the **Riftbound** trading card game to OmniCard, implemented the
same way as One Piece and Magic: download the full card catalog from an external
API, cache card images locally, compute perceptual hashes, and match scanned
cards using **OCR-first, pHash-fallback** logic.

Riftbound is functionally closest to One Piece: cards carry a printed collector
number, so the primary match path is OCR of that number, with pHash to
disambiguate and to handle misses. `OptcgService` is the implementation template.

## Data Source

**API:** Riftcodex — `https://api.riftcodex.com` (docs at https://riftcodex.com/docs/).
No authentication required for reads. No documented rate limit (throttle
conservatively).

### Endpoints used
- `GET /sets?size=100` — list all sets (8 today).
- `GET /cards?set_id={id}&size=100&page={n}` — paged cards for a set. Response
  includes `items`, `total`, `page`, `size`, `pages`. Page until `page > pages`.

### Sets (8 total, all in scope)
| set_id | Name | card_count |
|--------|------|-----------|
| UNL | Unleashed | 280 |
| OGN | Origins | 352 |
| SFD | Spiritforged | 288 |
| VEN | Vendetta | 358 |
| OGS | Origins: Proving Grounds | 24 |
| OPP | Organized Play Promos | 133 |
| JDG | Judge Promos | 3 |
| PR  | Promos | 13 |

### Card object (relevant fields)
```
id                 string  // riftcodex hex id — UNIQUE PER PRINTING → primary key
name               string
riftbound_id       string  // e.g. "ogn-310-298" / alt art "ogn-310*-298"
tcgplayer_id       string
collector_number   integer // e.g. 150 — NOT unique (alt arts share it)
attributes         { energy, might, power }
classification     { type, supertype, rarity, domain[] }
text               { rich, plain, flavour }
set                { set_id, label }
media              { image_url, artist, accessibility_text }
orientation        string  // "portrait" | "landscape"
metadata           { clean_name, updated_on, alternate_art, overnumbered, signature }
```

Card image URLs point at Riot's CDN (`cmsassets.rgpub.io`), full-resolution PNG
(e.g. 744×1039).

### Confirmed data nuances (drive the design)
1. **Printed total ≠ catalog count.** The sample card reads `UNL • 150/219`, but
   the API reports UNL `card_count: 280`. The printed `/total` is the base-set
   size; alternate-art / overnumbered / signature cards push the real count
   higher. **OCR must ignore the printed `/total`.**
2. **Set + collector number is not unique.** Collector numbers repeat across
   printings (alternate arts). OCR of `(set_id, collector_number)` therefore
   yields a *candidate set* that pHash disambiguates.
3. **Landscape cards exist.** Battlefields and some Legends are landscape
   (`orientation: "landscape"`), so the `SET • N/total` text sits in a different
   region than on portrait cards.

## Approach

Mirror the self-contained One Piece slice — a new service + entity + DbContext +
`riftbound.db` — dispatched via the `CardGame` enum and DI. No changes to the
MTG or One Piece services. No shared-base-class refactor (rejected as
out-of-scope / YAGNI).

## Scope Decisions
- **All 8 sets** downloaded (promos included; they lean on pHash where OCR is weak).
- **Pricing deferred.** `UpdatePricesAsync` is a logged no-op this pass;
  TCGPlayer pricing (via `tcgplayer_id`) is a future follow-up.
- **Orientation-aware OCR.** Portrait and landscape crop regions, selected by the
  scanned card's aspect ratio.
- **EdgeHash included** for foil robustness (as One Piece does).

## Components

### 1. Enum & display plumbing
- Add `CardGame.Riftbound` (`OmniCard.Shared/Models/CardGame.cs`).
- `CardGameDisplayConverter` arms → `"Riftbound"`
  (`OmniCard.Controls/Converters/RootConverters.cs`).
- `CardAttributeExtractor` (`OmniCard.CardMatching/CardAttributeExtractor.cs`):
  map Riftbound `domain[]` → color slot, `type` → type slot.

### 2. Data model — `RiftboundCard` (one row per printing)
- **PK:** `Id` (riftcodex hex string).
- Catalog fields: `RiftboundId`, `TcgPlayerId`, `CollectorNumber` (int), `Name`,
  `CleanName`, `SetId`, `SetName`, `Rarity`, `Type`, `Supertype`,
  `Domain` (JSON list), `Energy`, `Might`, `Power`, `TextPlain`, `Flavour`,
  `Artist`, `ImageUrl`, `Orientation`, `AlternateArt`, `Overnumbered`,
  `Signature`.
- Locally computed (`[JsonIgnore]`): `ImageHash`, `EdgeHash`, `LocalImagePath`.
- New `OmniCard.Shared/Models/RiftboundApiModels.cs`: DTOs for the riftcodex
  responses (paged card list + nested `attributes`/`classification`/`text`/
  `set`/`media`/`metadata`, and the set object).

### 3. Storage — `RiftboundDbContext` → `riftbound.db`
- Tables `Cards`, `HashCorrections`.
- `PRAGMA user_version` migration pattern copied from `OptcgDbContext`
  (`GetSchemaVersion`/`ApplySchemaUpgrades`/`MarkMigrationComplete`, wipe-on-bump).
- `Domain` list via `HasJsonConversion`.
- Web app opens read-only (`OmniCard.Web/Program.cs`), consistent with the others.

### 4. Download — `RiftboundService.DownloadBulkDataAsync`
- `GET /sets` → iterate 8 sets; for each, page `GET /cards?set_id={id}&size=100&page=n`
  until exhausted.
- Throttle ≈4 parallel + small delay (no documented limit → be polite).
- Map DTO → `RiftboundCard`, dedupe on `Id`, upsert in batches of 500.
- Auto-invoke `ComputeImageHashesAsync` for newly inserted rows (as OptcgService does).
- **`UpdatePricesAsync`:** logged no-op.

### 5. Images & hashing — `ComputeImageHashesAsync`
- Query cards missing `ImageHash`/`EdgeHash`; download `media.image_url` →
  cache `riftbound-art/{Id}.png` under `IDataPathService.DataDirectory`.
- Compute `ImageHash` (luminance pHash) + `EdgeHash` (foil-robust), 8-way parallel.
- Persist via batched `ExecuteUpdateAsync`; invalidate in-memory caches.
- Skip cards with a null/empty image URL.

### 6. OCR — orientation-aware collector detection
In `OmniCard.Imaging/OcrMatchingService.cs` + `IOcrMatchingService`:
- New `DetectRiftboundCollectorNumberAsync(...)`.
- Two crop-region constants:
  - `RiftboundPortraitRegion` — lower-left (~`X≈0.03, Y≈0.95, W≈0.30, H≈0.05`).
  - `RiftboundLandscapeRegion` — lower-left of a wide card.
  - Region chosen by scanned-card aspect ratio (w/h > ~1 → landscape).
- Char whitelist `A–Z0–9•·./ -`.
- Regex captures **set code + collector number**, ignoring the printed `/total`:
  roughly `([A-Za-z]{2,4})\s*[•·.]\s*(\d{1,3})\s*/\s*\d{1,3}`.
- On a structured match, floor confidence to 0.9 (clears the downstream gate),
  mirroring the OPTCG path.
- Crop geometry stays a compiled-in per-game constant (existing convention; no
  config file introduced).

### 7. Matching — `RiftboundService.FindClosestMatch` (phased, per OptcgService)
- **Phase 0 — OCR direct.** With `(set_id, collector_number)` from OCR
  (confidence ≥ 0.5): query `WHERE SetId=? AND CollectorNumber=?`.
  - Exactly one row → return, phase `"OcrCollectorNumber"`.
  - Multiple rows (alt arts) → pHash **only among those candidates** to pick.
  - Zero → fall through.
- **Phase 1/2 — corrections** table (exact then fuzzy, `CorrectionTrustBonus`).
- **Phase 3 — EdgeHash** for foil scans.
- **Phase 4 — global luminance pHash** scan, `maxDistance` default 14, confidence
  `(1 - distance/maxDistance) * 100`.

### 8. Scan orchestrator — `CardService.AddFromStream`
- Add a `Riftbound` arm to the extras block (compute EdgeHash, as the One Piece
  foil path does).
- Add a `Riftbound` arm to the async OCR-override block: call
  `DetectRiftboundCollectorNumberAsync`, and on a hit re-run `FindBestMatch` with
  the `OcrMatchResult`.
- Portrait 180° rotate-retry retained; landscape handled by orientation detection.

### 9. DI registration
- `OmniCard/App.xaml.cs`: `AddDbContextFactory<RiftboundDbContext>` (path
  `riftbound.db`) + `AddSingleton<ICardGameService, RiftboundService>`.
- `OmniCard.Web/Program.cs`: same, with the read-only connection variant.

### 10. Remaining `CardGame` touchpoints (audit + add arm, often passthrough)
- `OmniCard/Views/Root/RootViewModel.cs` — download triggers + scan/query branches.
- `OmniCard/Views/Root/ScannerTabView.xaml.cs`.
- `OmniCard/Views/Inventory/ProductEditorViewModel.cs`.
- `OmniCard.Web/Pages/Index.cshtml.cs` — game-code map.
- `OmniCard.Collection/DecklistService.cs`.
- `OmniCard.Collection/CsvExportImportService.cs`.
- `OmniCard/Views/Dashboard/DashboardView.xaml`.

## Testing Strategy (TDD)

Mirror `OptcgServiceTests` / `FallbackMatchingTests` / `EdgeHashTests` /
`ScanMatchingIntegrationTests`:
- **OCR regex parsing** — portrait and landscape samples; the `150/219`-vs-280
  total mismatch must not affect the parsed `(set, collector)`.
- **Candidate disambiguation** — set+collector maps to multiple rows; pHash
  selects the right printing.
- **DTO → entity mapping** — including `alternate_art`/`overnumbered`/`signature`
  and the `ogn-310*-298` alt-art form.
- **Download paging** — mocked `HttpClient` across multiple pages and sets.
- **Migration** — `user_version` bump path.

## Error Handling
- OCR unavailable (missing tessdata) → degrade to pHash only (existing flag).
- Network failures during download → surface/log; resumable on re-run
  (idempotent upsert on `Id`).
- Cards with null image URL → skipped during hashing.

## Out of Scope (YAGNI)
- Pricing (deferred; `UpdatePricesAsync` is a no-op).
- Deck/CSV special-casing beyond what's needed to compile.
- Any shared-base-class refactor across game services.
