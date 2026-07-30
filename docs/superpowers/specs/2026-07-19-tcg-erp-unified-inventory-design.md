# TCG ERP — Unified Inventory (All-Phases Design)

**Date:** 2026-07-19
**Status:** Program-level design for review. Each phase gets its own spec → plan → implementation cycle.

## Vision

Replace the MTG-specific sealed-product template engine with a **game-agnostic inventory ERP core**
in which *everything you own* — single cards and sealed products alike — is modeled as a
**Product** (catalog identity) held as **Inventory Lots** (owned quantity with cost basis),
with an **Inventory Movement** ledger and a **valuation/reporting** layer on top. It must work
for any TCG, not just Magic.

## Goals

- Trash the recursive template/archetype/contents system entirely (it over-modeled the domain and
  hardcoded Magic's product taxonomy).
- One unified product/inventory model for singles **and** sealed, across all games.
- Cost basis + market valuation (unrealized gain) and an auditable movement history.
- Preserve every existing singles capability: scanning/matching, storage containers, eBay
  listing/sync, market pricing, the virtualized tile UI, sort/filter/stack.
- Product categories are a small fixed generic set that fits every TCG.
- Keep "opening" a sealed product, but as a simple inventory movement (no recipe engine).

## Non-Goals (this program)

- Full business ERP — vendors/purchase orders, customers/sales orders, accounting/P&L. That is a
  later, separate program ("Approach C"); the model here is designed not to preclude it.
- New pricing sources, new scanning pipeline, or changes to card matching.

## Category set (fixed, game-agnostic)

`Single, Case, Box, Pack, Deck, Bundle, Other`. Applies to every game. (Magic's ~30-value
`SealedProductType` enum and the archetype registry are deleted.)

## Unified domain model

Three core entities replace `SealedProductType` / `SealedProductArchetype` / `SealedProductTemplate`
/ `SealedProductContents` / `SealedProductInstance`:

### `Product` — catalog identity ("what it is")
Shared across all physical copies; one row per distinct SKU.
- `Id`, `Game` (CardGame), `Category` (the fixed set), `Name`, `MarketPrice` (cached, NotMapped-style
  or refreshed), `ImageUri`
- **Single** fills: `GameCardId` (link to the game DB card), `SetCode`, `CollectorNumber`, `Rarity`,
  `Foil`, `Color`, `CardType`. Foil is part of SKU identity (price differs by foil).
- **Sealed** fills: `Upc`, `SetCode`.
- Uniqueness: singles keyed by (`Game`, `GameCardId`, `Foil`); sealed by (`Game`, `Upc`) or a
  generated key when no UPC.

### `InventoryLot` — an owned holding ("what you have")
- `Id`, `ProductId`, `Quantity`, `UnitCost` (cost basis), `AcquisitionDate`, `Source` (free text /
  future vendor link), `LocationId` (existing `StorageContainer`)
- **Single** copy attributes (per physical card): `Condition`, `ScanImagePath`, `Page`, `Slot`,
  `Section`, `IsMissing`, `FlagReason`
- Singles are typically **quantity-1 lots** (one lot per physical card, carrying its own scan /
  condition / slot / cost — exactly today's `CollectionCard` row). Sealed products use
  quantity-N lots freely. UI "stacking" becomes a display grouping over lots of the same
  Product (+ condition/foil), matching current behavior.

### `InventoryMovement` — the ledger
- `Id`, `ProductId`, `LotId?`, `Type` (`Acquire`, `Sell`, `Open`, `Adjust`, `Move`), `Quantity`,
  `UnitValue` (cost or sale price), `Timestamp`, `Note`, `RelatedMovementId?`
- **Open** = consume 1 sealed lot (a `Sell`/`Open`-out movement) and add N single lots (linked
  `Acquire` movements at 0 or user-entered cost). No recipe; you record what actually came out.
- Enables valuation deltas, history, and later margin reporting against eBay `Sell` movements.

### Existing entities, re-pointed
- **`StorageContainer`** (locations) — unchanged; `InventoryLot.LocationId` replaces
  `CollectionCard.ContainerId`.
- **`EbayListing`** — attaches to an `InventoryLot` (a listing sells specific copies) instead of a
  `CollectionCardId`. A completed sale becomes a `Sell` movement.
- **Game databases** (Scryfall/OPTCG) — unchanged; `Product.GameCardId` links to them for singles.

### Capability mapping (nothing lost)
| Today (CollectionCard) | Unified model |
|---|---|
| One row per card | `InventoryLot` (qty 1) of a Single `Product` |
| Condition / foil | Condition on Lot; Foil on Product (SKU variant) |
| Container/Page/Slot/Section | Lot location fields |
| ScanImagePath | Lot |
| PurchasePrice / DateAdded | Lot `UnitCost` / `AcquisitionDate` |
| MarketPrice / Quantity(stack) | Product `MarketPrice`; stack = display grouping of Lots |
| EbayListing | Lot ↔ EbayListing; sale → `Sell` movement |
| Sealed template/instance | `Product` (Category≠Single) + `InventoryLot` |

## Phased delivery

Each phase is independently shippable and gets its own spec/plan.

### Phase 0 — Demolition
Delete the sealed template system in full: `SealedProductType`, `SealedProductArchetype` +
Registry, `SealedProductTemplate`, `SealedProductContents`, `SealedProductInstance`,
`ISealedProductService`/`SealedProductService`, the `SealedProductDbContext`, and all sealed
editor UI (`SealedProductTemplateEditor*`, `SealedProductEntry*`, `CrackProduct*`,
`SealedProductListView`, `SealedProductViewModel`) plus DI registrations and the menu entry
(~2,224 LOC). Any existing rows in `sealed_products.db` are discarded (offer a one-time CSV export
first if the user has data worth keeping). Ships as a clean removal; the app builds without the
module.

### Phase 1 — Unified Product + Inventory core, sealed-first
Introduce `Product`, `InventoryLot`, `InventoryMovement`, the fixed category set, a
`ProductService`/`InventoryService`, and a valuation helper — in a new `InventoryDbContext` (or a
renamed, reshaped former sealed DB). Build the sealed-product experience on it: add a sealed
product (game, category, name, set, UPC, market price), record owned lots (qty, cost, location),
"open" a lot into singles (movement), and a basic inventory value view. Singles are **not** touched
yet. This alone replaces the trashed module with a lean, game-agnostic sealed inventory + cost/
valuation/opening. De-risks the model before Phase 2.

### Phase 2 — Migrate singles into the model (full data migration + service refactor)
The hard phase. Migrate every `CollectionCard` into `Product` + `InventoryLot`:
- **Data migration:** dedupe Products by (Game, GameCardId, Foil); create one Lot per existing
  `CollectionCard` row carrying its condition/cost/location/scan/slot; re-point `EbayListing` to
  the new Lot; seed `Acquire` movements from `DateAdded`/`PurchasePrice`.
- **Service refactor:** rework `CardService`/`CollectionQueryService` and the collection
  ViewModels so search/sort/filter/stack, scanning `CommitScans`, pricing, containers, bulk ops,
  set completion, and eBay all operate over `Product`/`InventoryLot`. `CollectionCard` is retired
  (or kept only as a thin read DTO the query layer projects to, to limit UI churn — decided in the
  Phase 2 spec).
- **Preserve behavior:** the tile UI, scanning flow, and eBay flow must be functionally unchanged;
  service-layer tests are the safety net and are updated alongside.
This is large and touches the app's core; it will likely be sub-decomposed further in its own spec.

### Phase 3 — ERP features on the unified base
Valuation dashboard (cost vs market, unrealized gain, by game/category/location), cross-game
inventory views, cost-basis and margin reporting (pairing `Acquire` cost with eBay `Sell`
proceeds), and movement history. Optional on-ramp toward Approach C (vendors/orders) later.

## Risks & mitigations

- **Phase 2 is high-risk** (rebuilding the app's core storage). Mitigations: land Phase 1 first to
  prove the model; keep the migration reversible (back up DBs, migrate into new tables, keep the old
  `collection.db` untouched until verified); rely on and extend the service-layer test suite;
  consider the `CollectionCard`-as-projection option to cap UI churn.
- **Foil-as-SKU vs foil-as-lot-attribute** — chosen as SKU identity because pricing differs by
  foil; verify no single-copy foil edge cases break during migration.
- **Lot quantity vs per-copy attributes** — singles stay quantity-1 lots so no per-card scan/slot
  data is lost; only sealed uses qty>1. Enforced in the migration and scanning commit.
- **Data loss on demolition** — offer a CSV export of any existing sealed data before Phase 0
  deletes it.
- **Scope creep toward full ERP** — vendors/customers/orders are explicitly out; the model leaves
  room (Source field, movement ledger) without building them now.

## Open questions (resolved per-phase spec)
- Phase 1: new `InventoryDbContext` vs reshaping `sealed_products.db`? Exact `ProductService`/
  `InventoryService` API.
- Phase 2: retire `CollectionCard` entirely vs keep it as a projection DTO. Migration rollback UX.
- Phase 3: which valuation/reporting views matter most first.

## Next step
Review this program design; then brainstorm and spec **Phase 1** in detail (self-contained,
demolishes the old system, proves the unified model without touching singles).
