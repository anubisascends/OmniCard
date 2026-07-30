# Sub-phase 2a — Unified Store + Core Facade (Detailed Spec)

**Date:** 2026-07-19
**Status:** Detailed spec for review (highest-risk sub-phase).
**Parent:** `2026-07-19-phase2-singles-unification-design.md`

## Scope
Flip singles storage from the `CollectionCard` table to `Product`+`InventoryLot` in ONE unified
database, behind a `CollectionCard` **DTO facade**, coordinating every *direct* consumer of the
`CollectionCard` table so the table can be dropped and the app + Web app stay green. Includes the
one-time data migration and the eBay FK change.

Out of scope (later sub-phases): consumers that use `CollectionCard` only *through* `CardService`
(CSV, Audit, cover-art, set-completion) need no change here — that's the facade's payoff, verified
in 2b; deep cleanup is 2d.

## Why a coordinated flip (not a strangler with dual-write)
The `CollectionCard` table can only be dropped when nothing queries it directly. The facade shrinks
the set of *direct* table consumers to a manageable few, so we migrate them together in 2a rather
than maintaining a fragile dual-write sync between two stores. **First planning step: enumerate the
exact direct consumers** — `grep -rn "CollectionDbContext" + ".Cards"` across app, services, and
`OmniCard.Web` — and treat that list as 2a's surface. Known direct consumers: `CardService`,
`CollectionQueryService`, the Web app pages (`Index`/`Card`/`Location`), and the migration service.
Everything else goes through `CardService` and rides the facade unchanged.

## Target pieces

### 1. Unified DbContext
Rename `InventoryDbContext` → `OmniCardDbContext` (keep the `inventory.db` file to avoid moving
Phase-1 data — final file name is an open question, see below). Add DbSets and model config for the
migrated-in tables: `StorageContainer`, `EbayListing`, `MismatchLog`, `FlagResolution`,
`ScanDiagnosticEvent` (config copied from `CollectionDbContext`). Add FKs now possible in one store:
`InventoryLot.LocationId → StorageContainer.Id`, `EbayListing.LotId → InventoryLot.Id`. **No
`CollectionCard` DbSet.**

### 2. `CollectionCard` as DTO
Remove `CollectionCard` from any DbContext; it stays a plain class (already has `INotifyPropertyChanged`
on `MarketPrice`). In projections its `Id` carries the **LotId**. `ContainerId` maps to `Lot.LocationId`.

### 3. Mapping layer (`CollectionCardMapper`, in `OmniCard.Collection`)
- `ToDto(InventoryLot lot, Product product, decimal marketPrice)` → `CollectionCard`.
- Write helpers to create/update `Product`+`Lot` from a `CollectionCard`-shaped operation, and to
  apply a mutation (see `BulkUpdateField` below). Shared by app and Web app.

### 4. Read facade (`CardService` / `CollectionQueryService`)
Rewrite over `OmniCardDbContext` `Lots` joined to `Products`, projecting to `CollectionCard`:
- `SearchCollection` (all overloads), `GetSearchCount`, `GetMatchingContainerIds`,
  `BuildFilteredQuery`, `ApplySortPreset`, `GetDistinctFieldValues`, `GetCollectionCards`.
- **Stacking parity:** today groups by (`GameCardId`,`IsFoil`,`Condition`) → now
  (`Product.GameCardId`,`Product.Foil`,`Lot.Condition`); `Id`=representative LotId, `StackedIds`=lot
  ids in group. Sort/filter presets operate on projected fields (map preset field names →
  Product/Lot columns). `CollectionQueryService.GetLocationOverviewsAsync` over Lots+Products.

### 5. Write facade (`CardService`)
Translate to `Product`+`Lot` (+ movements):
- `CommitScans` / `AddCardToCollection`: find-or-create `Product` (dedup Game+GameCardId+Foil) +
  insert qty-1 `Lot` (condition/cost/location/scan/slot) + `Acquire` movement.
- `UpdateCollectionCard`: update the `Lot` by its Id (=LotId); if an identity field changes
  (foil/game/card), move the lot to the matching `Product` (find-or-create) — see open question.
- `DeleteCollectionCard`: delete the `Lot`; leave `Product` (catalog) in place.
- `BulkUpdateField(ids, Action<CollectionCard>)`: for each lot id, project to a `CollectionCard`,
  apply the `Action`, then diff back onto the `Lot`/`Product`. (The delegate signature is preserved;
  the diff-back is the translation.)
- `MoveCardsToContainer`: update `Lot.LocationId` (+ optional `Move` movement).

### 6. eBay FK
`EbayListing.CollectionCardId` → `LotId`; update `EbayListingService`/`EbaySyncService` references
and the model + context config. Sold listings seed a `Sell` movement.

### 7. Web app (`OmniCard.Web`)
Repoint `Program.cs` to the unified db + register `OmniCardDbContext`; `Index`/`Card`/`Location`
pages project `Lots`+`Products` → `CollectionCard` via the shared mapper (add a project reference to
`OmniCard.Collection` if not present, or a small shared read helper in `OmniCard.Shared`).

### 8. One-time data migration
Extend the migration service: on first run against a `collection.db` with a `Cards` table not yet
migrated, into the unified store — dedup Products (Game,GameCardId,Foil); one Lot per CollectionCard
(condition/cost/date/location/scan/slot/missing/flag); copy StorageContainer + diagnostics tables;
copy EbayListing remapping `CollectionCardId`→new `LotId`; seed an `Acquire` per lot (+ `Sell` for
sold listings). Idempotent (guard flag); back up first; **leave `collection.db` on disk** for
rollback. Runs at startup like the existing migration steps.

### 9. Game services / set-completion / pricing
`ICardGameService.GetSetCompletionAsync`/`GetCurrentPrices` keep taking `IEnumerable<CollectionCard>`
(DTOs) — **no signature changes**; callers pass projected DTOs.

## Internal sequencing (green checkpoints within 2a)
Land in order, building/testing at each: (i) unified `OmniCardDbContext` + migration (old code still
compiles against it or a shim); (ii) mapping layer + read facade with **parity tests** (projected
search/stack/sort/filter == expected) while writes still work; (iii) write facade + eBay LotId;
(iv) Web app repoint; (v) drop `CollectionCard` table + `CollectionDbContext` and remove the shim.
Full suite + human E2E green before declaring 2a done.

## Testing
- **Parity tests** (new): projected `SearchCollection`/stack/sort/filter/count over seeded
  Lots+Products match expected results (the safety net before dropping the table).
- **Migration tests:** CollectionCard rows → correct Products/Lots/movements; EbayListing FK remap;
  containers/diagnostics copied; idempotent.
- **Write tests:** CommitScans/Update/Delete/Bulk/Move produce correct Lots/Products/movements.
- Update existing collection/CardService tests to the unified store (many `CollectionDbContext`-based
  tests will move to `OmniCardDbContext`).
- Human E2E (pending): scan→commit, search/sort/filter/stack, edit, move, bulk, delete, eBay
  list/sync, Web app pages — all behave as before; prices display; collection intact after migration.

## Risks & mitigations
- **Crown-jewel rewrite:** biggest risk in the program. Mitigations: coordinated flip limited to the
  enumerated direct-table consumers; parity tests before dropping the table; migrate into the unified
  db while retaining `collection.db` (rollback = revert code + repoint); staged internal checkpoints;
  mandatory human E2E before merge.
- **BulkUpdateField `Action<CollectionCard>`:** the project-apply-diff translation is subtle; cover
  the common fields (condition/foil/price/location) with tests.
- **Identity-changing edits** (foil/card): treated as lot-move-to-Product; test explicitly.
- **App + Web on one SQLite file:** unchanged from today's `collection.db` sharing.
- **Large sub-phase:** if 2a proves too big during planning, split at the internal checkpoints (each
  is a green boundary) into 2a-1..2a-5.

## Open questions (resolve in the plan)
- Final unified DB file name (`inventory.db` reuse vs rename to `omnicard.db` with a Phase-1 data
  copy-in).
- Whether the Web app references `OmniCard.Collection` for the mapper or gets a minimal shared read
  helper in `OmniCard.Shared`.
- Exact preset-field → Product/Lot column mapping table (enumerate from existing sort/filter presets).

## Next step
Review this spec; then write the 2a implementation plan (following the internal green-checkpoint
sequence), and execute subagent-driven with review gates.
