# Sub-phase 2a — Unified Store + Core Facade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement task-by-task with review gates. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Move singles storage from the `CollectionCard` table to `Product`+`InventoryLot` in one unified database, behind a `CollectionCard` DTO facade, so the app + Web app behave identically and the `CollectionCard` table is dropped.

**Architecture:** Rename `InventoryDbContext`→`OmniCardDbContext` holding all app data (Product/Lot/Movement + StorageContainer/EbayListing/diagnostics). `CollectionCard` becomes a DTO; `CollectionCardMapper` projects `Lot`+`Product`→`CollectionCard` (reads) and translates writes. `CardService`/`CollectionQueryService`/Audit/CSV/Decklist/Web project/translate over the unified store. One-time migration imports `collection.db` into it.

**Tech Stack:** .NET 10 WPF + ASP.NET (Web), EF Core (SQLite, `IDbContextFactory`), CommunityToolkit.Mvvm, xUnit.

## Global Constraints
- **DO NOT touch the game reference DBs.** `ScryfallService`/`OptcgService` use `_readContext.Cards` = the *game* card tables (`scryfall.db`/`optcg.db`) — unrelated to `CollectionCard`. Leave all of `ScryfallService`/`OptcgService` alone except where they take `IEnumerable<CollectionCard>` params (those stay — DTO).
- Behavior must be identical to today: scanning/commit, search/sort/filter/stack, edit, bulk, move, delete, containers, eBay, pricing, set-completion, Web pages. This is a storage migration behind a stable DTO.
- Preserve `CollectionCard` public shape (it's the DTO); its `Id` = LotId in projections.
- `ICardGameService.GetSetCompletionAsync`/`GetCurrentPrices` keep `IEnumerable<CollectionCard>` signatures.
- One unified DB file: reuse `inventory.db`, rename context to `OmniCardDbContext` (chosen over a new `omnicard.db` to avoid moving Phase-1 data). Game DBs stay separate.
- Migration is idempotent, backs up first, and leaves `collection.db` on disk (rollback).
- Build: `dotnet build OmniCard.slnx`. Tests: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`. Also build `OmniCard.Web`.
- Land the tasks in order; the app/build stays green after each task (a temporary shim on `CollectionDbContext` is allowed until the final drop task).

## Direct `CollectionCard`-table consumers (2a surface)
Projection/translation needed: `CardService`, `CollectionQueryService`, `AuditService` (`collCtx.Cards`), `CsvExportImportService`, `DecklistService`, `App.xaml.cs` `BackfillColorCardType` (`ctx.Cards`), Web pages (`Index`/`Card`/`Location`). Context-type swap only (use other tables): `StorageContainerService`, `MismatchLogService`, `ScanDiagnosticService`, `EbayListingService`, `EbaySyncService`. FK remaps: `EbayListing.CollectionCardId`→`LotId`, `FlagResolution.CollectionCardId`→`LotId`.

---

## Task 1: Unified `OmniCardDbContext` (add collection tables; keep a compat shim)
**Files:** rename/modify `OmniCard.Data/InventoryDbContext.cs`→`OmniCardDbContext.cs`; modify `OmniCard.Data/CollectionDbContext.cs` (keep temporarily as a shim); `App.xaml.cs` DI.

- [ ] Rename `InventoryDbContext`→`OmniCardDbContext`; add DbSets + `OnModelCreating` config (copied from `CollectionDbContext`) for `StorageContainer`, `EbayListing`, `MismatchLog`, `FlagResolution`, `ScanDiagnosticEvent`. Add FKs `InventoryLot.LocationId→StorageContainer` and (temporarily nullable) prepare for `EbayListing.LotId`/`FlagResolution.LotId` (added in Task 5). No `CollectionCard` DbSet.
- [ ] Register `AddDbContextFactory<OmniCardDbContext>` against `inventory.db`; keep `CollectionDbContext` registered for now (shim). Update the Phase-1 `IInventoryService`/inventory UI references from `InventoryDbContext`→`OmniCardDbContext`.
- [ ] Build (`dotnet build OmniCard.slnx`) → 0 errors. Tests green. Commit: `refactor: rename InventoryDbContext to OmniCardDbContext and add collection tables`.

## Task 2: One-time data migration `collection.db` → unified store
**Files:** `OmniCard.Data/CollectionMigrationService.cs` (or a new `UnifiedInventoryMigration`); `App.xaml.cs` startup; tests `OmniCard.Tests/Services/UnifiedMigrationTests.cs`.

- [ ] Write a migration that, guarded by a "unified-migrated" flag, reads existing `CollectionCard` rows and writes: dedup `Product`(Category=Single) by (Game,GameCardId,Foil); one `InventoryLot` per card (Condition/PurchasePrice→UnitCost/DateAdded→AcquisitionDate/ContainerId→LocationId/ScanImagePath/Page/Slot/Section/IsMissing/FlagReason); copy `StorageContainer`/`MismatchLog`/`FlagResolution`/`ScanDiagnosticEvent`; copy `EbayListing` (FK remap done in Task 5 once LotId exists — for now stash the old CollectionCardId→new LotId map); seed an `Acquire` movement per lot. Back up `inventory.db`+`collection.db` first; idempotent.
- [ ] TDD: `UnifiedMigrationTests` — cards→products/lots (dedup, counts, field mapping), containers/diagnostics copied, movements seeded, idempotency. Run at startup after the existing migrations.
- [ ] Build + tests green. Commit: `feat: migrate collection.db singles into unified Product/Lot store`.

## Task 3: `CollectionCardMapper` + read facade + parity tests
**Files:** create `OmniCard.Collection/CollectionCardMapper.cs`; modify `OmniCard.Collection/CardService.cs` (read methods), `OmniCard.Collection/CollectionQueryService.cs`; make `CollectionCard` a non-entity DTO; tests `OmniCard.Tests/Services/FacadeParityTests.cs`.

- [ ] `CollectionCardMapper.ToDto(InventoryLot, Product, decimal marketPrice)`→`CollectionCard` (Id=LotId; identity fields from Product; copy attrs from Lot).
- [ ] Rewrite `CardService` reads over `OmniCardDbContext` Lots⋈Products projecting to `CollectionCard`: `SearchCollection` (all overloads), `GetSearchCount`, `GetMatchingContainerIds`, `BuildFilteredQuery`, `ApplySortPreset`, `GetDistinctFieldValues`, `GetCollectionCards`. Preserve stacking (group by Product.GameCardId, Product.Foil, Lot.Condition), sort/filter presets (map preset field names→Product/Lot columns — enumerate from existing presets). `CollectionQueryService.GetLocationOverviewsAsync` over Lots+Products.
- [ ] Remove `CollectionCard` from `CollectionDbContext` (it becomes a DTO); keep the shim context for the remaining tables until Task 6.
- [ ] TDD `FacadeParityTests`: seed Lots+Products, assert projected search/stack/sort/filter/count match expected (the safety net before dropping the table). Update existing collection read tests to the unified store.
- [ ] Build + tests green. Commit: `feat: project CollectionCard reads from Product/Lot (facade) with parity tests`.

## Task 4: Write facade (`CardService` writes) + `BulkUpdateField`/identity-edit translation
**Files:** `OmniCard.Collection/CardService.cs` (write methods); tests `OmniCard.Tests/Services/FacadeWriteTests.cs`.

- [ ] Translate writes to Product/Lot (+movements): `CommitScans`/`AddCardToCollection` (find-or-create Product + qty-1 Lot + Acquire), `UpdateCollectionCard` (update Lot by Id; identity-field change → move lot to matching Product), `DeleteCollectionCard` (delete Lot), `BulkUpdateField(ids, Action<CollectionCard>)` (project→apply action→diff back to Lot/Product), `MoveCardsToContainer` (Lot.LocationId + Move movement). Remove the raw-SQL EbayListings table creation in `CardService` (now modeled by the context).
- [ ] TDD `FacadeWriteTests`: each write path produces correct Lots/Products/movements; BulkUpdateField over condition/foil/price/location; identity-changing edit moves the lot.
- [ ] Build + tests green. Commit: `feat: translate CollectionCard writes to Product/Lot operations`.

## Task 5: eBay + FlagResolution FK remap (CollectionCardId → LotId)
**Files:** `OmniCard.Shared/Models/EbayListing.cs`, `FlagResolution.cs`; `OmniCard.Data/OmniCardDbContext.cs` (FK config); `OmniCard.eBay/EbayListingService.cs`, `EbaySyncService.cs`; migration Task 2's stashed map; tests update.

- [ ] Rename `EbayListing.CollectionCardId`→`LotId` (+ `FlagResolution.CollectionCardId`→`LotId`); update context FKs (→`InventoryLot`); update `EbayListingService`/`EbaySyncService` (SKU `omnicard-{LotId}`, lookups by LotId; sold→`Sell` movement). Apply the migration's CollectionCardId→LotId map to existing EbayListing/FlagResolution rows.
- [ ] Update eBay/flag tests to LotId. Build + tests green. Commit: `feat: repoint EbayListing/FlagResolution FK to InventoryLot`.

## Task 6: Peripheral direct consumers + context swap + drop the table
**Files:** `AuditService.cs`, `CsvExportImportService.cs`, `DecklistService.cs`, `App.xaml.cs` (`BackfillColorCardType` + DI), `StorageContainerService`/`MismatchLogService`/`ScanDiagnosticService` (context-type swap), Web pages (`Index`/`Card`/`Location`), `Program.cs`; delete `CollectionDbContext`.

- [ ] Repoint `AuditService`/`CsvExportImportService`/`DecklistService`/`App.BackfillColorCardType` card reads to the mapper/projection. Swap `StorageContainerService`/`MismatchLogService`/`ScanDiagnosticService` from `CollectionDbContext`→`OmniCardDbContext` (other tables; mechanical).
- [ ] Web app: `Program.cs`→`OmniCardDbContext` on `inventory.db`; `Index`/`Card`/`Location` pages project Lots+Products→`CollectionCard` via the shared mapper (add `OmniCard.Collection` reference or a shared read helper).
- [ ] Remove the remaining `CollectionDbContext` registration + delete `CollectionDbContext.cs` (the table is now unused). Confirm nothing references it.
- [ ] Build all (incl. Web) + full tests + Web tests green. Commit: `refactor: repoint remaining consumers to unified store; remove CollectionDbContext`.

## Task 7: Full verification
- [ ] `dotnet build OmniCard.slnx` (+ Web) 0 errors; `dotnet test` all pass.
- [ ] Grep: no `CollectionDbContext` or `CollectionCard`-table query remains (game `.Cards` excluded).
- [ ] **Human E2E (required, cannot be automated):** fresh migration from a real `collection.db` → collection intact; scan→commit; search/sort/filter/stack; edit; bulk; move; delete; eBay list/sync; Web pages; prices display. Confirm `collection.db` no longer opened by the app.

## Risks & mitigations
- Crown-jewel rewrite → per-task green checkpoints, parity + write + migration tests, migrate into `inventory.db` while retaining `collection.db` for rollback, mandatory human E2E before merge.
- Game-DB `.Cards` false-positive → explicitly out of scope (constraint above).
- If any task is still too large (esp. Task 3/4), split by method group.

## Self-Review Notes
- Covers spec pieces: unified context (T1), migration (T2), mapper+read facade+parity (T3), write facade (T4), eBay/flag FK (T5), peripheral consumers + Web + drop table (T6), verify (T7).
- Deferred to Phase 2b/2c per program design: none — 2a here absorbs Web + peripherals because they query the table directly and the table must drop atomically. (Program-design 2b/2c effectively fold into T6; 2d cleanup = T7. Flag to user.)
- Not unit-testable (human E2E): scanning, GUI, eBay live, Web rendering. Service/mapper/migration/parity are unit-tested.
