# Swap OPTCG API → api.poneglyph.one

**Date:** 2026-07-14
**Status:** Approved

## Summary

Replace the current One Piece TCG data source (`https://www.optcgapi.com/api/allSetCards/`)
with the `https://api.poneglyph.one` REST API. The new API models multiple *variants*
(alternate arts) per card number, each with its own image and market price. We store
one row per variant so alternate-art scans can match their exact printing, while
preserving backward compatibility with existing user collections.

Only `OmniCard.CardMatching/OptcgService.cs` and `OmniCard.Shared/Models/OptcgCard.cs`
(plus the OPTCG DbContext schema upgrade and related tests) reference the old API. The
Scryfall/MTG path and all other games are untouched.

## Old vs. new API

| Concern | Old (`optcgapi.com`) | New (`api.poneglyph.one`) |
|---|---|---|
| Bulk fetch | one call: `GET /api/allSetCards/` → flat list | `GET /v1/sets` (60 sets) → per set `GET /v1/sets/{code}` |
| Card identity | `card_set_id` (e.g. `OP01-001`) | `card_number` + `variants[].index` |
| Alt arts | duplicate rows, "keep last" | explicit `variants[]` array |
| Image | one `card_image` URL | `variants[].images.scan.{display,full,thumb}` / `images.stock.{full,thumb}` |
| Price | `market_price`, `inventory_price` (numbers) | `variants[].market.{market_price,low_price,mid_price,high_price}` (strings) |
| Field shapes | scalar strings | `color[]`, `types[]`, `attribute[]` arrays; `cost`/`power`/`counter`/`life` integers; text in `effect` |
| Auth | none | none |

## Design

### 1. Data source and download flow

- Base URL as a `private const string` in `OptcgService` (matches the current hardcoded
  style). No auth. User-Agent stays `OmniCard/1.0`.
- `DownloadBulkDataAsync`:
  1. `GET /v1/sets` → list of set codes.
  2. For each set, `GET /v1/sets/{code}`, throttled to ~4 concurrent requests.
  3. Flatten `data.cards[] × variants[]` into `OptcgCard` rows.
  4. Reuse the existing batched upsert (insert new / update existing) and the
     post-download incremental auto-hash of newly inserted rows.
  5. On success, set `PRAGMA user_version = PoneglyphSchemaVersion` to mark migration
     complete (see §5).
- A per-set fetch failure is logged and skipped; the download proceeds with the sets
  that succeeded (consistent with the existing hash-failure tolerance). Progress
  reporting continues per set.

### 2. Variant identity (schema change)

Primary key stays `CardSetId`, but its value becomes a **variant uid**:

- `variants[].index == 0` → uid = bare `card_number` (e.g. `OP01-001`).
- `index > 0` → uid = `{card_number}_p{index}` (e.g. `OP01-001_p1`).

This keeps the base printing's identity equal to the bare card number, so existing
`CollectionCard.GameCardId` values (which today store the bare number) continue to
resolve after the swap. Alt-art variants become their own matchable rows.

New columns on `OptcgCard` (added via `OptcgDbContext.ApplySchemaUpgrades` using
`ALTER TABLE Cards ADD COLUMN`, following the existing `LocalImagePath` upgrade):

- `CardNumber` (printed collector number, e.g. `OP01-001`; indexed, non-unique)
- `VariantIndex` (int)
- `VariantLabel` (string?, e.g. "Standard", alt-art label)
- `Artist` (string?)

`CardMatch` mapping changes:

- `CollectorNumber` = `CardNumber` (printed number — used for OCR lookup and display)
- `GameSpecificId` = `CardSetId` (variant uid — used for price, local art, corrections)

Today these two are equal; under this design they diverge cleanly. All downstream
identity flows (`FindCardById`, `GetCurrentPrice(s)`, `RecordCorrection`) continue to
key on `CardSetId` / `GameSpecificId` unchanged.

### 3. Field mapping

| `OptcgCard` field | Source | Transform |
|---|---|---|
| `CardSetId` (PK) | `card_number` + `variant.index` | variant uid scheme above |
| `CardNumber` | `card_number` | as-is |
| `CardName` | `name` | as-is |
| `SetId` | `set` | as-is (e.g. `OP01`) |
| `SetName` | `set_name` | as-is |
| `Rarity` | `rarity` | `?? ""` |
| `CardColor` | `color[]` | join with `/` |
| `CardType` | `card_type` | as-is |
| `SubTypes` | `types[]` | join with `/` |
| `Attribute` | `attribute[]` | join with `/` |
| `CardText` | `effect` | as-is |
| `CardCost` | `cost` (int?) | `ToString()` |
| `CardPower` | `power` (int?) | `ToString()` |
| `Life` | `life` (int?) | `ToString()` |
| `CounterAmount` | `counter` (int?) | as-is |
| `VariantIndex` | `variant.index` | as-is |
| `VariantLabel` | `variant.label` | as-is |
| `Artist` | `variant.artist` | as-is |
| `CardImageUri` | `variant.images` | `scan.display ?? scan.full ?? stock.full ?? stock.thumb` |
| `MarketPrice` | `variant.market.market_price` | parse string → decimal? |
| `InventoryPrice` | `variant.market.low_price` | parse string → decimal? (closest analog to old inventory price) |
| `DateScraped` | — | download timestamp (ISO 8601) |

The `OptcgCard` DTO is repurposed: it deserializes from the new nested shape (via new
DTO types for the set/card/variant response) and materializes into the existing entity
columns. JSON `[JsonPropertyName]` attributes on `OptcgCard` that mirrored the old flat
API are replaced by the new response DTOs; `OptcgCard` becomes primarily the persistence
entity.

### 4. Set-completion semantics

`GetSetCompletion` and `GetMissingCards` group by **`CardNumber`** (distinct), not by row
count. Alt-art variant rows therefore do not inflate set totals — "completion" remains
per printed card number, matching current UX. `GetMissingCards` returns one entry per
distinct missing `CardNumber` (the base variant's data).

### 5. Migration — detect API switch and wipe

The OPTCG reference database must recognize that its existing data came from the old
API and wipe itself before migrating to the new system. Old-API rows are structurally
incompatible (no variant identity, stale image URLs pointing at the retired host,
prices from a different feed), so a clean rebuild is required rather than an in-place
upsert.

**Detection** uses SQLite `PRAGMA user_version` — a built-in integer stored in the DB
file, no extra table needed. A constant `PoneglyphSchemaVersion` (e.g. `1`) identifies
new-API data. Any older/absent value (default `0`, which covers both pre-existing
old-API databases and brand-new installs) signals "not yet migrated."

**Wipe** runs in the `OptcgService` constructor, after `EnsureCreated()` +
`ApplySchemaUpgrades()` (so the new columns exist first). When `user_version <
PoneglyphSchemaVersion`:

1. Delete all rows from `Cards`.
2. Delete all rows from `HashCorrections` (old corrections reference the old data set).
3. Delete the `optcg-art/` directory under the data directory (downloaded art).
4. Reset the in-memory caches (`_hashCache`, `_hashSetLookup`, `_correctionsCache`).

`user_version` is set to `PoneglyphSchemaVersion` only on **successful completion** of
`DownloadBulkDataAsync`. This makes migration idempotent and crash-safe: if the app is
launched after the wipe but before a successful download, it re-wipes (a no-op on the
now-empty DB) and still knows data must be re-fetched. Once a full download succeeds,
the version marker flips and subsequent launches skip the wipe.

**Scope:** the wipe touches only the OPTCG reference cache (`OptcgDbContext`: `Cards`,
`HashCorrections`) and the `optcg-art/` folder. The user's actual collection
(`CollectionDbContext`) is **not** touched. This is precisely why the base-variant uid
must remain the bare card number (§2): after the wipe and re-download, existing
`CollectionCard.GameCardId` values (bare numbers) must still resolve to the rebuilt
base-variant rows.

**Post-migration:** once migrated, `DownloadBulkDataAsync` behaves as a normal refresh —
new rows inserted and auto-hashed, existing rows updated (all metadata columns, not just
price). No wipe recurs unless `PoneglyphSchemaVersion` is bumped in a future change.

### 6. Error handling

- Per-set request failure: log warning, skip, continue.
- Null/empty image on a variant: row is still stored (for search/price); hashing already
  filters `CardImageUri != null`, so imageless variants are simply not hashed.
- Price string parse failure: treat as null.

### 7. Testing

- Update `OmniCard.Tests/Models/CardDeserializationTests.cs` for the new nested
  set/card/variant JSON shape.
- Add a flattening test: a card with N variants produces N rows with correct uid scheme
  (base = bare number, alt = `_pN`).
- Add a set-completion test asserting totals count distinct `CardNumber`, not variant rows.
- Existing matching/collection tests that key on `CardSetId` should remain green because
  the base variant's uid equals the bare card number.
- Add a migration test: a DB seeded with old-API-style rows and `user_version = 0` is
  wiped (Cards + HashCorrections emptied, art dir removed) on construction, and
  `user_version` flips to `PoneglyphSchemaVersion` only after a successful download.

## Out of scope (YAGNI)

- Multi-language support — English only (`lang=en` default), matching current behavior.
- Endpoints not needed for the swap: `/v1/decks`, `/v1/meta`, `/v1/formats`, `/v1/don`,
  `/v1/sleeves`, `/v1/prices/{card_number}` history, `/v1/report`.
