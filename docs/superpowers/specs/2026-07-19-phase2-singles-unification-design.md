# Phase 2 — Migrate Singles into the Unified Model (Program Design)

**Date:** 2026-07-19
**Status:** Program design for review. Sub-phases each get their own spec → plan → execution.
**Parents:** `2026-07-19-tcg-erp-unified-inventory-design.md`, `2026-07-19-phase1-unified-inventory-core-design.md`

## Decisions (from brainstorming)
- **Facade/DTO:** `CollectionCard` stops being a persisted entity and becomes a lightweight read/transfer **DTO** projected from `Product` + `InventoryLot`. Its ~187 references (incl. the Web app and the game-service signatures) keep compiling with minimal change; storage unifies underneath.
- **One database:** the collection tables merge with the Phase-1 inventory tables into a **single unified DbContext/database**, so foreign keys work (`Lot → StorageContainer`, `Lot ↔ EbayListing`) and there's one store.

## Goal
Singles become `Product` (Category=`Single`) + `InventoryLot`, in the same store as sealed products, with the whole app (and the Web companion) reading/writing through the unified model — while the collection UI, scanning, containers, eBay, pricing, and set-completion behave exactly as today.

## Non-goals
- No new user-facing features; this is a storage/architecture migration behind stable behavior.
- Not purifying `CollectionCard` out of existence (that's an optional later cleanup); it remains as a DTO.
- Phase 3 ERP dashboards/reporting are separate.

## Target architecture

### Unified DbContext / database
Merge into one context (extend the Phase-1 `InventoryDbContext` → rename to e.g. `OmniCardDbContext`, one `omnicard.db` — or migrate collection tables into `inventory.db`). It owns:
`Product`, `InventoryLot`, `InventoryMovement`, `StorageContainer`, `EbayListing`, `MismatchLog`, `FlagResolution`, `ScanDiagnosticEvent`. **The `CollectionCard` table is dropped** (its data → `Product`+`Lot`). Game DBs (`scryfall.db`, `optcg.db`) stay separate read-only reference data.

### `CollectionCard` as a DTO (facade)
`CollectionCard` becomes a plain (non-entity) class. A **mapping layer** projects between it and `Product`+`Lot`:
- **Read projection:** `InventoryLot` + `Product` → `CollectionCard` (Id = LotId; GameCardId/SetCode/Name/Rarity/Foil/Color/CardType/ImageUri from Product; Condition/ScanImagePath/Page/Slot/Section/PurchasePrice/DateAdded/ContainerId from Lot; MarketPrice from the price cache; Quantity/StackedIds computed by stacking).
- **Write translation:** operations expressed on `CollectionCard` (scan commit, edit, bulk-update, move, delete, CSV import) map to create/update/delete of `Product`+`Lot` (+ `Acquire`/`Adjust`/`Move` movements).

Because consumers keep using `CollectionCard`, the game services (`GetSetCompletionAsync`/`GetCurrentPrices` taking `IEnumerable<CollectionCard>`), Audit, CSV, eBay, and most ViewModels/XAML need **no signature changes** — only `CardService`/`CollectionQueryService` internals change.

### Read path (facade projection)
`CardService.SearchCollection`/`GetSearchCount`/`GetMatchingContainerIds` today query `CollectionDbContext.Cards` then stack (group by GameCardId/IsFoil/Condition), sort, paginate. In the facade they query `Lots` joined to `Products`, project to `CollectionCard`, and reuse the **same** stacking/sort/filter/pagination logic (grouping keys map to `Product.GameCardId`/`Product.Foil`/`Lot.Condition`). Filter/sort presets operate on the projected fields.

### Write path
- **Scan commit** (`CommitScans`): each scanned card → find/create `Product` (dedup by Game+GameCardId+Foil) + insert a qty-1 `Lot` (condition/cost/location/scan/slot) + `Acquire` movement.
- **Edit / bulk / move / delete:** update/delete the `Lot` (and `Product` where identity fields change), writing `Adjust`/`Move` movements where meaningful.
- **CSV import:** rows → Products+Lots.
- **eBay:** `EbayListing.CollectionCardId` → **`LotId`**; a completed sale → `Sell` movement. `EbayListingService` updated to the new FK.

### Web app (`OmniCard.Web`)
Repoint its `IDbContextFactory<CollectionDbContext>` to the unified context; its direct `db.Cards` queries (Index/Card/Location pages) become projections over `Lots`+`Products` (share the mapping layer from `OmniCard.Shared`/`OmniCard.Collection`). Program.cs points at the unified db.

## Data migration (one-time)
From `collection.db` into the unified store:
- **Products:** dedupe existing `CollectionCard`s by (Game, GameCardId, Foil) → one `Product` (Category=Single) each, carrying Name/Set/Number/Rarity/Color/CardType/ImageUri.
- **Lots:** one `InventoryLot` per `CollectionCard` row (Quantity=1) carrying Condition/PurchasePrice(→UnitCost)/DateAdded(→AcquisitionDate)/ContainerId(→LocationId)/ScanImagePath/Page/Slot/Section/IsMissing/FlagReason.
- **StorageContainer / diagnostics tables:** copied as-is into the unified db.
- **EbayListing:** copied; `CollectionCardId` → the new `LotId`; seed a `Sell` movement for sold listings.
- **Movements:** seed an `Acquire` per lot from DateAdded/PurchasePrice.
- **Safety:** migrate into a fresh unified db; leave `collection.db` untouched on disk (rollback = point back at it). Idempotent/guarded (skip if already migrated). Backup before migrating.

## Sub-phase decomposition (each shippable, app green, its own spec/plan)
- **2a — Unified store + core facade:** unified DbContext + one-time migration + `CollectionCard` as DTO + `CardService`/`CollectionQueryService` read-projection & write-translation (incl. eBay `LotId`). Internally sequenced with tests: (i) unified context + migration, (ii) read projection, (iii) write translation, (iv) drop `CollectionCard` table. The large, core sub-phase; guarded by the existing service-test suite (extended). May be further split in its own spec.
- **2b — Peripheral consumers:** CSV import/export, Audit, cover-art, set-completion, and any remaining `CollectionCard`-writing paths verified/adjusted against the facade.
- **2c — Web app:** repoint to the unified db + project `db.Cards` reads from `Product`+`Lot`.
- **2d — Cleanup & verification:** remove dead collection-storage code, confirm `collection.db` no longer opened by the app, full regression + human E2E.

## Risks & mitigations
- **App-core rewrite risk:** the read/write facade is the crown-jewel path. Mitigations: keep `CollectionCard` as the stable DTO so consumers don't churn; land 2a's internal steps behind the (extended) service-test suite; migrate into a new db with `collection.db` retained for rollback; heavy human E2E (scan → commit → search/sort/filter/stack → edit → move → eBay).
- **Stacking/sort/filter parity:** projection must reproduce today's grouping/sort/filter exactly — add parity tests comparing projected results to expectations before dropping the old table.
- **Two consumers of the DB (app + Web):** unify the db and share the mapping layer so both project identically; migrate/repoint both.
- **eBay FK change (CollectionCardId→LotId):** migrate existing listings' FK during the data migration; verify sync/list/end flows.
- **Cross-process file locking** (app + Web on one SQLite file): already the case today with `collection.db`; unchanged risk.

## Open questions (resolved in sub-phase specs)
- 2a: rename `InventoryDbContext`→`OmniCardDbContext` + reuse `inventory.db`, vs a new `omnicard.db`. Whether to keep a thin `CollectionCard`-shaped EF query type or project purely in-memory.
- 2a: exact write-translation for identity-changing edits (e.g., changing a card's foil = move to a different Product).
- 2c: whether the Web app shares `OmniCard.Collection`'s projection or has a read-only copy.

## Next step
Review this design; then spec **sub-phase 2a** (unified store + core facade) in detail — the foundation everything else builds on.
