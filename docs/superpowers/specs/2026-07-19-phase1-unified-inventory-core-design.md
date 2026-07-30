# Phase 1 — Unified Inventory Core (sealed-first) + Demolition

**Date:** 2026-07-19
**Status:** Approved design direction (from the all-phases program design); detailed spec for review.
**Parent:** `2026-07-19-tcg-erp-unified-inventory-design.md`

## Scope

Two things, in order:
1. **Demolition (Phase 0):** delete the MTG-specific sealed template/archetype/contents/crack system
   and its UI entirely.
2. **Unified core (Phase 1):** introduce the game-agnostic `Product` / `InventoryLot` /
   `InventoryMovement` model + services + a sealed-product experience built on it (add product,
   record owned lots with cost, open a lot, see inventory value).

**Out of scope:** anything touching singles / `CollectionCard` (that is Phase 2). No changes to
scanning, matching, pricing sources, containers, or eBay internals (the model reuses
`StorageContainer` but does not modify it).

## Goals

- Old sealed system gone; app builds and runs without it.
- A clean, game-agnostic inventory model that Phase 2 will migrate singles into unchanged.
- Sealed products fully usable: catalog a product for any game, own quantities with cost basis,
  open units, and see cost/market valuation.
- Service layer covered by tests mirroring the existing `SealedProductServiceTests` discipline.

## Demolition — delete list

Remove (and their DI registrations, `App.xaml.cs` init, and menu entry):
- Models: `SealedProductType`, `SealedProductArchetype` (+ `ArchetypeContent`/`ArchetypeTier`),
  `SealedProductTemplate`, `SealedProductContents`, `SealedProductInstance`.
- Services: `ISealedProductService`, `SealedProductService`, `SealedProductArchetypeRegistry`.
- Data: `SealedProductDbContext` (and its `sealed_products.db` registration).
- UI: `SealedProductListView(.xaml/.cs)`, `SealedProductViewModel`, `SealedProductTemplateEditor*`,
  `SealedProductEntry*`, `CrackProduct*`.
- Tests: `SealedProductDbContextTests`, `SealedProductArchetypeRegistryTests`,
  `SealedProductServiceTests` (replaced by new inventory tests).

**Data safety:** before deletion, provide a one-time CSV export of existing `sealed_products.db`
rows (templates + instances) so no owned-product data is silently lost. If the DB is empty/absent,
skip. The old `.db` file is left on disk (not deleted) but no longer opened.

## Unified domain model (new)

New project home: models in `OmniCard.Shared/Models`, context in `OmniCard.Data`, service in
`OmniCard.Collection` (alongside the existing collection services), matching current layering.

### Enums
```csharp
public enum ProductCategory { Single, Case, Box, Pack, Deck, Bundle, Other }
public enum MovementType { Acquire, Sell, Open, Adjust, Move }
```

### `Product` (catalog identity)
```csharp
public class Product
{
    public int Id { get; set; }
    public CardGame Game { get; set; }
    public ProductCategory Category { get; set; }
    public string Name { get; set; } = "";
    public string? SetCode { get; set; }
    public string? Upc { get; set; }          // sealed
    public string? GameCardId { get; set; }    // single (Phase 2 link; unused in Phase 1)
    public string? CollectorNumber { get; set; }
    public string? Rarity { get; set; }
    public bool Foil { get; set; }
    public string? ImageUri { get; set; }
    [NotMapped] public decimal MarketPrice { get; set; }  // cached, refreshed like today
}
```
Phase 1 only creates `Category != Single` products; the single-oriented fields exist so Phase 2's
migration needs no schema change.

### `InventoryLot` (owned holding)
```csharp
public class InventoryLot
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; } = 1;
    public decimal? UnitCost { get; set; }
    public DateTime AcquisitionDate { get; set; } = DateTime.UtcNow;
    public string? Source { get; set; }
    public int? LocationId { get; set; }       // existing StorageContainer.Id
    // Single copy attributes (unused in Phase 1; filled by Phase 2 migration):
    public string? Condition { get; set; }
    public string? ScanImagePath { get; set; }
    public int? Page { get; set; }
    public int? Slot { get; set; }
    public string? Section { get; set; }
}
```

### `InventoryMovement` (ledger)
```csharp
public class InventoryMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public MovementType Type { get; set; }
    public int Quantity { get; set; }
    public decimal? UnitValue { get; set; }   // cost for Acquire, price for Sell
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
    public int? RelatedMovementId { get; set; }
}
```

### `InventoryDbContext` (new `inventory.db`)
`DbSet<Product> Products`, `DbSet<InventoryLot> Lots`, `DbSet<InventoryMovement> Movements`.
Indices: `Product` by (`Game`,`Category`), by `Upc`, by (`Game`,`GameCardId`,`Foil`); `Lot` by
`ProductId`; `Movement` by `ProductId`,`Timestamp`. Enums stored as strings (matches current
convention). `EnsureCreated()` at startup (replacing the sealed DB init).

## Service — `IInventoryService` / `InventoryService`

```csharp
public interface IInventoryService
{
    // Catalog
    List<Product> GetProducts(CardGame? game = null, ProductCategory? category = null);
    Product? FindProductByUpc(string upc);
    Product CreateProduct(Product product);
    void UpdateProduct(Product product);
    void DeleteProduct(int productId);            // also removes its lots/movements

    // Holdings
    List<InventoryLot> GetLots(int productId);
    InventoryLot AddLot(int productId, int quantity, decimal? unitCost, int? locationId, string? source);
    void UpdateLot(InventoryLot lot);
    void DeleteLot(int lotId);

    // Operations
    void OpenUnits(int lotId, int quantity, string? note);   // Phase 1: consume + Open movement
    IReadOnlyList<InventoryMovement> GetMovements(int productId);

    // Valuation
    InventoryValuation GetValuation(CardGame? game = null, ProductCategory? category = null);
}

public record InventoryValuation(int TotalUnits, decimal TotalCost, decimal TotalMarket);
```
- `AddLot` also writes an `Acquire` movement. `OpenUnits` decrements the lot's quantity (deleting
  the lot at 0), writes an `Open` movement, and records `note` (what came out). Full open-into-
  singles is deferred to Phase 2 when singles are inventory; Phase 1 keeps opening as the movement
  the design specified, with an optional hand-off to the **existing** "add cards to collection"
  flow so pulled singles can still be logged into today's collection (wiring detail decided in the
  plan; the service itself only records the movement).
- Valuation sums `Quantity * UnitCost` (cost) and `Quantity * Product.MarketPrice` (market) across
  lots; `MarketPrice` is refreshed via the existing per-game price services for sealed where
  available, else left 0 (sealed pricing source is a Phase 3 concern — Phase 1 allows manual entry).

## UI

Replace the sealed menu entry with an **Inventory** entry (menu item, opening a list view — same
surfacing pattern the sealed module used, not a new tab):
- **Inventory list:** sealed products grouped/filterable by game and category, showing owned
  quantity, unit/total cost, and total market value; a header total (units, cost, market).
- **Add/Edit product:** game, category, name, set code, UPC, market price, image URI.
- **Add lot:** quantity, unit cost, location (existing container picker), source, date.
- **Open:** pick a lot, quantity to open, optional note; records the movement and (optionally)
  offers the existing add-cards flow.
- Reuses existing converters, container picker, and Material styles. New VMs use
  CommunityToolkit.Mvvm like the rest of the app.

## Testing

Service-layer xUnit tests (new `InventoryServiceTests`, `InventoryDbContextTests`) using the
in-temp-dir `IDbContextFactory` pattern already used by `SealedProductServiceTests`:
- Create/find/update/delete product (UPC uniqueness, cascade delete removes lots+movements).
- AddLot writes an Acquire movement with correct qty/cost; GetLots/GetValuation math.
- OpenUnits decrements quantity, deletes lot at 0, writes an Open movement with the note.
- Valuation totals across multiple products/lots/games/categories, with and without market prices.
UI is verified manually (human), consistent with the rest of the app.

## Risks & mitigations

- **Removing a whole module + DI wiring** can leave dangling references (menu, App init,
  DialogService). Mitigation: demolition is its own set of tasks; build must be clean before
  building the new module.
- **`sealed_products.db` data loss** — mitigated by the pre-deletion CSV export.
- **Opening semantics feel half-done in Phase 1** (records a movement but singles aren't inventory
  yet) — acceptable and documented; completed in Phase 2. The optional hand-off to the existing
  add-cards flow keeps it useful meanwhile.
- **Two inventory stores until Phase 2** (new `inventory.db` for sealed, old `collection.db` for
  singles) — intentional and temporary; Phase 2 migrates singles in and can consolidate.

## Open questions (resolve in the plan)
- Exact Inventory menu placement and whether the list is a dialog window or a hosted view.
- Whether Phase 1 opening wires to the existing add-cards flow now or just records the movement +
  note (leaning: movement + note now, wire-through in Phase 2).
- Sealed market-price source: manual entry in Phase 1 (no automated sealed pricing yet).
