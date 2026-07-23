# Pokémon, Yu-Gi-Oh!, and Final Fantasy TCG Support (TCGCSV-backed) — Design

**Date:** 2026-07-23
**Status:** Approved (pending spec review)

## Goal

Add three new trading card games to OmniCard — **Pokémon**, **Yu-Gi-Oh!**, and
**Final Fantasy TCG** — implemented the same way as the existing games (full
vertical slice: catalog, images, search/printings, set completion, collection
tracking, camera/scanner matching, and pricing), **but sourcing all data from the
[TCGCSV](https://tcgcsv.com) API** (catalog + images + prices), rather than a
game-specific catalog API as Riftbound did.

Because all three games draw from the *same* TCGCSV API with a uniform shape, they
share one entity and one abstract service base. **Riftbound, One Piece, and Magic
are left untouched.**

## Data Source

**API:** TCGCSV — base `https://tcgcsv.com`. Mirrors TCGplayer data. No auth. JSON
is **camelCase** (unlike riftcodex's snake_case) — use `JsonNamingPolicy.CamelCase`
+ `NumberHandling.AllowReadingFromString`. UA `"OmniCard/1.0"` via
`IHttpClientFactory`.

### Endpoints used (all under `/tcgplayer`)
- `GET /categories` — all games (used once to confirm category IDs; not called at runtime).
- `GET /{categoryId}/groups` — sets for a game.
- `GET /{categoryId}/{groupId}/products` — cards in a set.
- `GET /{categoryId}/{groupId}/prices` — price rows for a set.

Response envelope: `{ "success": bool, "errors": [], "results": [ ... ] }`.

### Category IDs (verified against live API 2026-07-23)
| Game | `categoryId` | Enum value | DB file | GameKey |
|------|-------------|-----------|---------|---------|
| Pokémon | 3 | `Pokemon` | `pokemon.db` | `pokemon` |
| Yu-Gi-Oh! | 2 | `YuGiOh` | `yugioh.db` | `yugioh` |
| Final Fantasy TCG | 24 | `FinalFantasy` | `fftcg.db` | `fftcg` |

(For reference: Magic=1, One Piece=68, Riftbound=89.)

### Group object
```
groupId         int
name            string
abbreviation    string
isSupplemental  bool
publishedOn     datetime
modifiedOn      datetime
categoryId      int
```
Pokémon has ~217 groups; Yu-Gi-Oh many; FFTCG ~38. Full download/hash is long —
reuses existing progress reporting and the 24h price cooldown.

### Product object
```
productId       int      // UNIQUE per printing → primary key / GameCardId
name            string
cleanName       string
imageUrl        string   // .../product/{productId}_200w.jpg — upgrade resolution for pHash
categoryId      int
groupId         int
url             string
modifiedOn      datetime
imageCount      int
presaleInfo     { isPresale, releasedOn, note }
extendedData    [ { name, displayName, value }, ... ]
```
`extendedData` is where per-card attributes live and **varies by game**:
- FFTCG: `Rarity`, `Number` (e.g. `1-001H`), `Description`, `CardType`, `Element`, `Cost`, `Power`, `Job`, `Category`.
- Pokémon: `Number` (e.g. `123/198`), `Rarity`, HP/types, etc.
- Yu-Gi-Oh!: `Number` (set code, e.g. `LOB-EN001`), `Rarity`, `Attribute`, `ATK`/`DEF`, `Level`, etc.

### Price object
```
productId       int
lowPrice        decimal?
midPrice        decimal?
highPrice       decimal?
marketPrice     decimal?
directLowPrice  decimal?
subTypeName     string
```
`subTypeName` **varies by game** and does not map 1:1 to the app's single foil bool:
- FFTCG: `Normal`, `Foil`.
- Pokémon: `Normal`, `Holofoil`, `Reverse Holofoil`.
- Yu-Gi-Oh!: edition-based (`1st Edition`, `Limited`, `Unlimited`).

## Architecture Decisions (from brainstorming)

1. **Match scope:** Full parity — pHash + edge hash + per-game OCR collector-number detection.
2. **Code sharing:** A shared abstract `TcgCsvGameService` implements common TCGCSV
   logic once; three thin subclasses. Riftbound stays as-is.
3. **Persistence:** One shared `TcgCsvCard` entity, but **per-game `DbContext` + `.db`
   file** to preserve per-game isolation (a schema-version wipe/redownload of one
   game does not touch the others).
4. **Persist all data:** promoted/indexed columns for match/UI-critical fields, plus
   a full `ExtendedDataJson` blob so every game-specific attribute is retained and viewable.
5. **Web parity:** every desktop change has a matching change in `OmniCard.Web`.

## Components

### 1. Enum touchpoint
- `OmniCard.Shared/Models/CardGame.cs` — add `Pokemon`, `YuGiOh`, `FinalFantasy`.

### 2. Shared entity — `OmniCard.Shared/Models/TcgCsvCard.cs`
One row per printing (per `productId`).
- **Identity / catalog:** `ProductId` (PK), `Game`, `Name`, `CleanName`, `SetId`
  (groupId), `SetName`, `CollectorNumber` (from `extendedData.Number`), `Rarity`,
  `CardType`, `ImageUrl`, `Url`.
- **All attributes:** `ExtendedDataJson` (string) — the full `extendedData` array serialized verbatim.
- **Locally computed:** `ImageHash`, `EdgeHash`, `ArtHashes` (serialized), `LocalImagePath`.
- **Pricing (parity with Riftbound):** `MarketPrice`, `FoilMarketPrice`, `PriceUpdatedAt` (UTC).
- `GameCardId` = `ProductId.ToString()` (feeds inventory layer's `(Game, GameCardId, Foil)` key).

### 3. DTOs — `OmniCard.Shared/Models/TcgCsvApiModels.cs`
camelCase: `TcgCsvGroup`, `TcgCsvProduct` (+ `TcgCsvExtendedData`), `TcgCsvPrice`, and
their `{ success, errors, results[] }` envelopes. (Riftbound already has private
nested TCGCSV DTOs; these are promoted to shared public models for reuse.)

### 4. Abstract base — `OmniCard.CardMatching/TcgCsvGameService.cs`
Implements `ICardGameService` once against `TcgCsvCard`:
- `DownloadBulkDataAsync` — groups → per-group products; map product → entity
  (promote `Number`/`Rarity`/`CardType`, store full extendedData JSON); upsert
  **without clobbering** price/hash columns on re-download.
- `UpdatePricesAsync` — `FetchTcgCsvPriceMapAsync` (groups → per-group `/prices`,
  per-group try/catch resilience), join by `productId`, write price columns in
  500-row batches with `ChangeTracker.Clear()`.
- `ComputeImageHashesAsync` — download `imageUrl` (upgraded above `_200w`), compute
  pHash + art hashes + edge hash; cache under `{DataDir}/{GameKey}-art/{productId}.png`.
- `FindClosestMatch`, `SearchCards`, `GetPrintings`, `GetCurrentPrice(s)`,
  `RecordCorrection`, `GetAvailableSets`, `GetSetCompletionAsync`, `GetMissingCards`,
  `FindCardById` — generic against `TcgCsvCard`.

**Abstract hooks each subclass supplies:**
| Hook | Purpose |
|------|---------|
| `CategoryId` | 3 / 2 / 24 |
| `Game`, `GameKey` | enum value + folder/db name |
| DbContext factory | per-game `IDbContextFactory<...>` |
| `MapExtendedData(product, card)` | promote game-specific Number/Rarity/CardType |
| `SubTypePriceMap` | map sub-type names → (normal, foil) |
| `OcrConfig` | crop regions (portrait/landscape) + collector-number regex + whitelist |

### 5. Per-game subclasses — `OmniCard.CardMatching/`
`PokemonService`, `YugiohService`, `FinalFantasyService` — each supplies the six
hooks above and nothing more.

**Sub-type → foil mapping:**
- FFTCG: `Normal`→normal, `Foil`→foil.
- Pokémon: `Normal`→normal, `Holofoil`→foil (fallback `Reverse Holofoil`).
  *Known limitation:* the app models a single foil bool, so reverse-holo vs holo collapses.
- Yu-Gi-Oh!: `Unlimited`/`1st Edition`→normal (prefer `Unlimited`), no distinct foil.

### 6. Persistence — `OmniCard.Data/`
`PokemonDbContext`, `YugiohDbContext`, `FinalFantasyDbContext` — thin subclasses over
the shared `TcgCsvCard` model. Each copies Riftbound's schema mechanics: `PRAGMA
user_version` version gate (`WipeForMigration` on bump), additive
`ApplySchemaUpgrades`/`AddColumnIfMissing` (idempotent `ALTER TABLE`, swallowing
duplicate-column and readonly errors), shared `HashCorrections` table, UTC-kind
converter on `PriceUpdatedAt`, indexes on Name/SetId/CollectorNumber/ImageHash/EdgeHash.
Separate `.db` files. Manage `HashCorrections` via `EnsureCreated`/`OnModelCreating`
(matching Riftbound; not added to `EnsureHashCorrectionsInGameDbs`).

### 7. OCR & matching
Add **one config-driven** detector to `IOcrMatchingService` /`OcrMatchingService`:
`DetectCollectorNumberAsync(image, OcrCropSpec, regex, whitelist)`. Each subclass's
`OcrConfig` supplies portrait/landscape crop regions + regex + whitelist:
- Pokémon: `\d+/\d+`
- FFTCG: `\d+-\d+[A-Z]?`
- Yu-Gi-Oh!: `[A-Z0-9]+-[A-Z]{0,2}\d+`

OCR output is matched against the catalog's ground-truth `CollectorNumber`.
`CardService` scan-orchestration arms (OCR override, edge-hash-for-foils,
rotate-retry) get arms for the three games, reusing the generic detector.
`CardAttributeExtractor` gets `ExtractColor`/`ExtractCardType` arms off the promoted
columns / extendedData. Crop regions ship as sensible defaults flagged for
real-scan tuning (consistent with OPTCG/Riftbound OCR tuning history).

### 8. DI registration
- **Desktop `OmniCard/App.xaml.cs`:** three `AddDbContextFactory<...>` + three
  `AddSingleton<ICardGameService, ...>`. `PriceUpdateService` and the game dropdown
  auto-pick-up.
- **Web `OmniCard.Web/Program.cs`:** three `AddDbContextFactory<...>` with
  `Mode=ReadOnly`, concrete-then-aliased registration.

### 9. UI wiring (desktop **and** web)
- **Desktop:** `CardGameDisplayConverter` + `BreakdownKeyDisplayConverter` arms
  (`"Pokémon"`, `"Yu-Gi-Oh!"`, `"Final Fantasy TCG"`); `DashboardView.xaml` set-code
  trigger arms; scanner/query/copy branches in `RootViewModel`/`ScannerTabView`. Game
  dropdown auto-populates from DI.
- **Card detail (both platforms):** a panel rendering the full `ExtendedDataJson` as a
  labeled key→value list, so all persisted attributes are visible.
- **Web:** `Index.cshtml` three `<option>`s; `Index.cshtml.cs → ParseGameFilter` three
  arms; card-detail extendedData rendering mirroring desktop.

## Error Handling
- Per-group price fetch wrapped in try/catch — one failing group does not abort refresh.
- `UpdatePricesAsync` bails early if the card DB is empty.
- Re-download upserts never overwrite locally computed hash or price columns.
- Missing/failed image downloads skip hashing for that card, logged, non-fatal.
- Web opens DBs read-only; additive `ALTER TABLE` swallows readonly errors.

## Testing
Mirror the `Riftbound*` suite, but most coverage sits on the **base** (tested via a
test subclass) with thin per-game tests. HTTP mocked (`RoutingHandler`/
`FakeHttpClientFactory`), in-memory SQLite (`TestFactory`).
- **Base:** download paging/mapping, `ExtendedDataJson` round-trip, price writes +
  per-group failure resilience, foil-aware `GetCurrentPrice(s)` with fallback, schema
  additive-column parity vs `EnsureCreated`, `FindClosestMatch` phases.
- **Per game:** category-ID wiring, sub-type price mapping (esp. Pokémon 3-subtype →
  normal/foil), OCR regex/crop parsing against sample numbers, `CardService` scan routing.
- **Web:** `WebPageTests` arms for the three filters + extendedData rendering.

## Risks / Open Items
- **OCR crop regions** are the main unknown; defaults need real-scan tuning (flagged, non-blocking).
- **Image resolution:** confirm the CDN serves a larger variant than `_200w` for usable pHash.
- **Pokémon sub-types:** `Reverse Holofoil` vs `Holofoil` collapses to one foil bool (documented).
- **Volume:** Pokémon ~217 groups — long initial download/hash; reuses progress + cooldown.

## Out of Scope
- Refactoring Riftbound/One Piece/Magic onto the shared base.
- Modeling multiple foil/edition sub-types beyond the single foil bool.
- eBay/audit/decklist game-specific behavior beyond the MTG-default passthrough the
  existing games use.
