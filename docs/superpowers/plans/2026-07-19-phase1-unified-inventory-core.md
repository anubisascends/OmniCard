# Phase 1 — Unified Inventory Core (sealed-first) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the MTG-specific sealed template engine and replace it with a game-agnostic `Product`/`InventoryLot`/`InventoryMovement` model + `InventoryService` + a sealed-product Inventory UI (add product, own lots with cost, open units, see valuation). Singles are untouched.

**Architecture:** New `inventory.db` (`InventoryDbContext`) holding `Product` (catalog identity), `InventoryLot` (owned holdings w/ cost), `InventoryMovement` (ledger). `InventoryService` wraps CRUD + movements + valuation. The existing Collection-tab "Sealed Products" toggle mode is transformed into an "Inventory" mode backed by a new `InventoryViewModel`/`InventoryListView`; the recipe/crack/template editors and archetype registry are deleted.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, EF Core (SQLite, `IDbContextFactory`), xUnit.

## Global Constraints

- Do not touch singles / `CollectionCard`, scanning, matching, pricing sources, containers, or eBay internals. `InventoryLot.LocationId` reuses `StorageContainer.Id` (read-only use of the existing container list).
- Category set is fixed: `Single, Case, Box, Pack, Deck, Bundle, Other`. Phase 1 creates only non-`Single` products.
- Enums persisted as strings (matches existing convention). New context: `inventory.db`, `EnsureCreated()` at startup.
- Product single-oriented fields (`GameCardId`, `CollectorNumber`, `Rarity`, `Foil`) and lot copy-attributes (`Condition`, `ScanImagePath`, `Page`, `Slot`, `Section`) exist now but are unused in Phase 1 — they exist so Phase 2 needs no schema change. Do not wire them into the Phase 1 UI.
- Build: `dotnet build OmniCard/OmniCard.csproj`. Tests: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj` (currently 531 pass; sealed tests are removed and replaced by inventory tests, so the count changes — that's expected).
- The old `sealed_products.db` file is left on disk (data-safety net); it is simply no longer opened. No CSV export code (preserving the file is the safeguard; a CSV can be produced on request).

## File Structure

- **Delete:** `OmniCard.Shared/Models/SealedProduct{Type,Archetype,Template,Contents,Instance}.cs`; `OmniCard.Shared/Interfaces/ISealedProductService.cs`; `OmniCard.Collection/SealedProduct{Service,ArchetypeRegistry}.cs`; `OmniCard.Data/SealedProductDbContext.cs`; `OmniCard/Views/SealedProductEditor/*` (Template editor, Entry, CrackProduct); sealed test files.
- **Create:** `OmniCard.Shared/Models/{Product,InventoryLot,InventoryMovement,ProductCategory,MovementType,InventoryValuation}.cs`; `OmniCard.Data/InventoryDbContext.cs`; `OmniCard.Shared/Interfaces/IInventoryService.cs`; `OmniCard.Collection/InventoryService.cs`; `OmniCard/Views/Inventory/InventoryViewModel.cs`, `InventoryListView.xaml(.cs)`, `ProductEditorView.xaml(.cs)`+VM, `AddLotView.xaml(.cs)`+VM; tests `OmniCard.Tests/Services/InventoryServiceTests.cs`.
- **Transform:** rename `RootViewModel.Sealed`→`Inventory`; `CollectionTabView.WireUpSealed`→`WireUpInventory` + swap `SealedProductListView`→`InventoryListView` and `Sealed.ShowSealed`→`Inventory.ShowInventory` bindings; `RootView.xaml.cs` wiring; `DialogService`/`IDialogService` sealed methods → inventory equivalents; `App.xaml.cs` DI + startup init.

---

## Task 1: Demolition — remove the sealed template system

**Files (delete):**
- `OmniCard.Shared/Models/SealedProductType.cs`, `SealedProductArchetype.cs`, `SealedProductTemplate.cs`, `SealedProductContents.cs`, `SealedProductInstance.cs`
- `OmniCard.Shared/Interfaces/ISealedProductService.cs`
- `OmniCard.Collection/SealedProductService.cs`, `SealedProductArchetypeRegistry.cs`
- `OmniCard.Data/SealedProductDbContext.cs`
- `OmniCard/Views/SealedProductEditor/` (entire folder: `SealedProductTemplateEditorView.xaml(.cs)`+VM, `SealedProductEntryView.xaml(.cs)`+VM, `CrackProductView.xaml(.cs)`+VM)
- `OmniCard/Views/Root/SealedProductListView.xaml(.cs)`, `OmniCard/Views/Root/SealedProductViewModel.cs`
- `OmniCard.Tests/Data/SealedProductDbContextTests.cs`, `OmniCard.Tests/Services/SealedProductArchetypeRegistryTests.cs`, `OmniCard.Tests/Services/SealedProductServiceTests.cs`

**Files (edit — remove references):**
- `OmniCard/App.xaml.cs`: delete `using OmniCard.Views.SealedProductEditor;` (line ~35); `AddSingleton<SealedProductViewModel>()` (~76); the "Sealed products" DI block `AddDbContextFactory<SealedProductDbContext>` + `AddSingleton<ISealedProductService, SealedProductService>()` (~109-112); the 4 `AddTransient<SealedProduct*Editor*/Entry*>()` lines (~169-172); the sealed DB init block (~263-267); and the `MigrateSealedProductEnumValues` method (~616).
- `OmniCard/Services/DialogService.cs`: delete `using OmniCard.Views.SealedProductEditor;` and the 3 methods `EditSealedProductTemplate`, `OpenSealedProductEntry`, `CrackSealedProduct` (~149-175).
- `OmniCard.Shared/Interfaces/IDialogService.cs`: delete the 3 sealed method declarations (~20-22).
- `OmniCard/Views/Root/RootViewModel.cs`: delete the `sealedVm` ctor param (~38), the `Sealed` property (~164-165), and the sealed wiring in `Initialize()` (~1171-1173).
- `OmniCard/Views/Root/RootView.xaml.cs`: delete `CollectionTab.WireUpSealed(...)` (~25) and the `viewModel.Sealed.LaunchScanner` delegate (~40-43).
- `OmniCard/Views/Root/CollectionTabView.xaml.cs`: delete `WireUpSealed` (~26-29).
- `OmniCard/Views/Root/CollectionTabView.xaml`: delete the "Cards / Sealed" toggle radio buttons (~15-30), the `SealedProductListView` element (~300-303), and every `Sealed.ShowSealed` `DataTrigger`/`Condition` (the `~8` visibility triggers). Restore each hidden panel (toolbar, stats, overview, card list, no-cards placeholder) to its non-sealed visibility (i.e. drop the sealed-mode hiding). *(Task 4 re-introduces an Inventory toggle; this task just removes sealed cleanly so the Collection tab shows only cards/overview.)*

**Interfaces:**
- Produces: a solution with no sealed-template code and a clean Collection tab (cards/overview only).

- [ ] **Step 1: Delete the model/service/data/UI/test files listed above**

Use `git rm` for each path in "Files (delete)".

- [ ] **Step 2: Remove all references (edits above)**

Work project-by-project. After edits, grep must return nothing:
Run: `grep -rniE "sealed|archetype|CrackProduct|SealedProduct" --include=*.cs --include=*.xaml OmniCard OmniCard.Shared OmniCard.Collection OmniCard.Data | grep -viE "/obj/|/bin/"`
Expected: no matches (a lingering comment mentioning "sealed" is acceptable only if clearly unrelated; otherwise remove).

- [ ] **Step 3: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: 0 errors. Fix any dangling reference the grep missed.

- [ ] **Step 4: Test**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: all pass (sealed test classes are gone; remaining suite green).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: remove MTG-specific sealed product template/archetype/crack system"
```

---

## Task 2: Unified model + `InventoryDbContext` + DI/startup

**Files:**
- Create: `OmniCard.Shared/Models/ProductCategory.cs`, `MovementType.cs`, `Product.cs`, `InventoryLot.cs`, `InventoryMovement.cs`, `InventoryValuation.cs`
- Create: `OmniCard.Data/InventoryDbContext.cs`
- Modify: `OmniCard/App.xaml.cs` (DI + startup init)

**Interfaces:**
- Produces: the model types + `InventoryDbContext` (`DbSet<Product> Products`, `DbSet<InventoryLot> Lots`, `DbSet<InventoryMovement> Movements`), registered via `AddDbContextFactory<InventoryDbContext>` against `inventory.db`, `EnsureCreated()` at startup. Consumed by Task 3.

- [ ] **Step 1: Create the enums and models**

`OmniCard.Shared/Models/ProductCategory.cs`:
```csharp
namespace OmniCard.Models;

public enum ProductCategory { Single, Case, Box, Pack, Deck, Bundle, Other }
```
`OmniCard.Shared/Models/MovementType.cs`:
```csharp
namespace OmniCard.Models;

public enum MovementType { Acquire, Sell, Open, Adjust, Move }
```
`OmniCard.Shared/Models/Product.cs`:
```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace OmniCard.Models;

public class Product
{
    public int Id { get; set; }
    public CardGame Game { get; set; }
    public ProductCategory Category { get; set; }
    public string Name { get; set; } = "";
    public string? SetCode { get; set; }
    public string? Upc { get; set; }
    // Single-oriented fields (unused in Phase 1; present so Phase 2 needs no schema change).
    public string? GameCardId { get; set; }
    public string? CollectorNumber { get; set; }
    public string? Rarity { get; set; }
    public bool Foil { get; set; }
    public string? ImageUri { get; set; }

    /// <summary>Cached market price for display/valuation. Not persisted.</summary>
    [NotMapped] public decimal MarketPrice { get; set; }
}
```
`OmniCard.Shared/Models/InventoryLot.cs`:
```csharp
namespace OmniCard.Models;

public class InventoryLot
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; } = 1;
    public decimal? UnitCost { get; set; }
    public DateTime AcquisitionDate { get; set; } = DateTime.UtcNow;
    public string? Source { get; set; }
    public int? LocationId { get; set; }   // existing StorageContainer.Id
    // Single copy attributes (unused in Phase 1; filled by Phase 2 migration).
    public string? Condition { get; set; }
    public string? ScanImagePath { get; set; }
    public int? Page { get; set; }
    public int? Slot { get; set; }
    public string? Section { get; set; }
}
```
`OmniCard.Shared/Models/InventoryMovement.cs`:
```csharp
namespace OmniCard.Models;

public class InventoryMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public MovementType Type { get; set; }
    public int Quantity { get; set; }
    public decimal? UnitValue { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
    public int? RelatedMovementId { get; set; }
}
```
`OmniCard.Shared/Models/InventoryValuation.cs`:
```csharp
namespace OmniCard.Models;

public record InventoryValuation(int TotalUnits, decimal TotalCost, decimal TotalMarket);
```

- [ ] **Step 2: Create `InventoryDbContext`**

`OmniCard.Data/InventoryDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class InventoryDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryLot> Lots => Set<InventoryLot>();
    public DbSet<InventoryMovement> Movements => Set<InventoryMovement>();

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedOnAdd();
            e.Property(p => p.Game).HasConversion<string>();
            e.Property(p => p.Category).HasConversion<string>();
            e.Ignore(p => p.MarketPrice);
            e.HasIndex(p => new { p.Game, p.Category });
            e.HasIndex(p => p.Upc);
            e.HasIndex(p => new { p.Game, p.GameCardId, p.Foil });
        });

        modelBuilder.Entity<InventoryLot>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.ProductId);
        });

        modelBuilder.Entity<InventoryMovement>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedOnAdd();
            e.Property(m => m.Type).HasConversion<string>();
            e.HasIndex(m => new { m.ProductId, m.Timestamp });
        });
    }
}
```

- [ ] **Step 3: Register DI + startup init in `App.xaml.cs`**

In the main `ConfigureServices` block (where the sealed block was removed), add:
```csharp
            // Inventory (unified product model)
            services.AddDbContextFactory<InventoryDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "inventory.db")}"));
            services.AddSingleton<IInventoryService, InventoryService>();
```
In `OnStartup` (where the sealed DB init was removed), add:
```csharp
            using (var invCtx = Host.Services.GetRequiredService<IDbContextFactory<InventoryDbContext>>().CreateDbContext())
                invCtx.Database.EnsureCreated();
```
(`IInventoryService`/`InventoryService` are created in Task 3; if building this task alone, temporarily comment the `AddSingleton<IInventoryService,...>` line, or land Tasks 2–3 together. Prefer landing 2 then 3 without an intermediate build of the service line — build Step 4 below excludes the service registration until Task 3.)

- [ ] **Step 4: Build (models + context only)**

Temporarily omit the `AddSingleton<IInventoryService, InventoryService>()` line until Task 3.
Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add unified Product/InventoryLot/InventoryMovement model + InventoryDbContext"
```

---

## Task 3: `IInventoryService` / `InventoryService` + tests (TDD)

**Files:**
- Create: `OmniCard.Shared/Interfaces/IInventoryService.cs`, `OmniCard.Collection/InventoryService.cs`
- Modify: `OmniCard/App.xaml.cs` (uncomment/add the `AddSingleton<IInventoryService, InventoryService>()`)
- Test: `OmniCard.Tests/Services/InventoryServiceTests.cs`

**Interfaces:**
- Consumes: models + `InventoryDbContext` (Task 2).
- Produces: `IInventoryService` (below), used by Task 4.
```csharp
public interface IInventoryService
{
    List<Product> GetProducts(CardGame? game = null, ProductCategory? category = null);
    Product? FindProductByUpc(string upc);
    Product CreateProduct(Product product);
    void UpdateProduct(Product product);
    void DeleteProduct(int productId);
    List<InventoryLot> GetLots(int productId);
    InventoryLot AddLot(int productId, int quantity, decimal? unitCost, int? locationId, string? source);
    void UpdateLot(InventoryLot lot);
    void DeleteLot(int lotId);
    void OpenUnits(int lotId, int quantity, string? note);
    IReadOnlyList<InventoryMovement> GetMovements(int productId);
    InventoryValuation GetValuation(CardGame? game = null, ProductCategory? category = null);
}
```

- [ ] **Step 1: Write failing tests**

`OmniCard.Tests/Services/InventoryServiceTests.cs` (mirror the in-memory SQLite pattern from the old `SealedProductServiceTests` — a shared open `SqliteConnection`, `DbContextOptions<InventoryDbContext>`, and a `MockFactory` implementing `IDbContextFactory<InventoryDbContext>`; reuse or copy that test's `MockFactory` shape). Cover:
```csharp
[Fact] public void CreateProduct_RoundTrips() { /* create Box product, GetProducts returns it */ }
[Fact] public void FindProductByUpc_ReturnsMatch() { /* create with Upc, find by it */ }
[Fact] public void GetProducts_FiltersByGameAndCategory() { /* two products, filter each dimension */ }
[Fact] public void AddLot_WritesAcquireMovement_AndComputesTotals() {
    // create product; AddLot(qty 3, cost 10, loc null); GetLots -> 1 lot qty3;
    // GetMovements -> 1 Acquire qty3 unitValue10
}
[Fact] public void OpenUnits_DecrementsLot_WritesOpenMovement() {
    // AddLot qty2; OpenUnits(lotId,1,"pulled a foil"); GetLots -> qty1; GetMovements has Open qty1 note
}
[Fact] public void OpenUnits_DeletesLot_AtZero() { /* AddLot qty1; OpenUnits 1; GetLots empty */ }
[Fact] public void DeleteProduct_CascadesLots() { /* AddLot; DeleteProduct; GetLots empty */ }
[Fact] public void GetValuation_SumsCostAndMarket_AcrossLots() {
    // product A market 20, lots qty2@cost5 + qty1@cost8; product B ...
    // set Product.MarketPrice in the test via UpdateProduct/CreateProduct then assert TotalUnits/TotalCost/TotalMarket
}
```
Note: `MarketPrice` is `[NotMapped]`; valuation reads it from the in-memory Product. In tests, set it on the product object the service returns, or have `GetValuation` accept the market price via the product — since it's not persisted, Phase 1 valuation uses `Product.MarketPrice` as currently loaded (0 unless set). Test `TotalMarket` with products whose `MarketPrice` you set through an `UpdateProduct` that stores it in a `[NotMapped]`-aware path — OR assert `TotalMarket == 0` when unset and cover the cost math precisely. (Keep the market assertion simple: unset → 0.)

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests"`
Expected: FAIL to compile (`IInventoryService`/`InventoryService` absent).

- [ ] **Step 3: Implement `IInventoryService` + `InventoryService`**

Create the interface (as above) and `InventoryService` using `IDbContextFactory<InventoryDbContext>` (new context per operation, `AsNoTracking` for reads), matching the old `SealedProductService` structure:
- `CreateProduct`/`UpdateProduct`/`DeleteProduct` (delete cascades lots via FK; also delete the product's movements explicitly).
- `AddLot`: insert lot, then insert an `Acquire` movement (`ProductId`, `LotId`, qty, `UnitValue = unitCost`), SaveChanges.
- `OpenUnits`: load lot; decrement `Quantity` by `quantity` (guard ≤ available); if reaches 0 delete the lot; insert an `Open` movement with `note`; SaveChanges.
- `GetValuation`: query lots (optionally filtered by joined product game/category), sum `Quantity*UnitCost` (cost) and `Quantity*Product.MarketPrice` (market), count units.

- [ ] **Step 4: Add DI registration**

In `App.xaml.cs`, ensure `services.AddSingleton<IInventoryService, InventoryService>();` is present (added/uncommented from Task 2).

- [ ] **Step 5: Run tests — verify pass + full suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: all pass (new inventory tests + existing).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add InventoryService (products, lots, movements, valuation) with tests"
```

---

## Task 4: Inventory UI (Collection-tab "Inventory" mode)

**Files:**
- Create: `OmniCard/Views/Inventory/InventoryViewModel.cs`, `InventoryListView.xaml(.cs)`, `ProductEditorView.xaml(.cs)` + `ProductEditorViewModel.cs`, `AddLotView.xaml(.cs)` + `AddLotViewModel.cs`
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (add `Inventory` VM + ctor param + Initialize wiring), `RootView.xaml.cs` (`WireUpInventory`), `CollectionTabView.xaml(.cs)` (Inventory toggle + host the list), `App.xaml.cs` (DI for the VMs/views), `DialogService`/`IDialogService` (product/lot editor dialogs)

**Interfaces:**
- Consumes: `IInventoryService` (Task 3).
- Produces: an "Inventory" mode in the Collection tab showing sealed products with owned qty/cost/market, and add/edit/add-lot/open actions.

- [ ] **Step 1: `InventoryViewModel`** — `[ObservableProperty] ShowInventory`, an `ObservableCollection` of a small `InventoryRow` view record (product + owned qty + total cost + total market), `LoadInventory()`, and `[RelayCommand]`s: `AddProduct`, `EditProduct`, `AddLot`, `OpenUnits`, plus header totals from `GetValuation`. Follows the deleted `SealedProductViewModel`'s shape/role but backed by `IInventoryService`. `ReportMessage`/`LaunchScanner` delegates kept only if still used.

- [ ] **Step 2: Editor dialogs** — `ProductEditorView` (game, category dropdown, name, set code, UPC, market price, image URI) returning a `Product`; `AddLotView` (quantity, unit cost, location via existing `StorageContainer` picker, source, date) returning lot params. Add `IDialogService`/`DialogService` methods `EditProduct(Product?)` and `AddLot(int productId)` (replacing the deleted sealed dialog methods), and register the views in `App.xaml.cs` (transient), following the deleted sealed editors' registration pattern.

- [ ] **Step 3: `InventoryListView`** — a list/grid of `InventoryRow` (name, game, category, owned qty, unit/total cost, market value) + a header total (units / cost / market), with buttons/context menu for Add Product, Edit, Add Lot, Open. Reuse existing converters/Material styles.

- [ ] **Step 4: Collection-tab wiring** — reintroduce the toggle from Task 1's removal, as "Cards / Inventory": in `CollectionTabView.xaml` add the radio pair bound to `DataContext.ViewModel.Inventory.ShowInventory`, host `<local:InventoryListView x:Name="InventoryList" .../>` (visible when `ShowInventory`), and re-add the `ShowInventory` visibility triggers to hide the card-list/toolbar/stats/overview panels in inventory mode (mirror the old sealed triggers, renamed). Add `CollectionTabView.WireUpInventory(InventoryViewModel)` calling `InventoryList.WireUp(vm)`. In `RootView.xaml.cs` call `CollectionTab.WireUpInventory(viewModel.Inventory)`. In `RootViewModel`, add the `InventoryViewModel` ctor param + `public InventoryViewModel Inventory { get; }` + wire `Inventory.LoadInventory()` in `Initialize()`. Register `InventoryViewModel` as a singleton in `App.xaml.cs`.

- [ ] **Step 5: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: 0 errors.

- [ ] **Step 6: Full test suite (regression)**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: all pass.

- [ ] **Step 7: Manual verification (human)**

Do NOT launch the GUI from an automated agent. List these as PENDING human checks: Collection tab shows a Cards/Inventory toggle; Inventory mode lists products with totals; Add Product (any game/category) → appears; Add Lot (qty/cost/location) → owned qty + value update; Open units → qty decrements, valuation updates; switching back to Cards shows the collection unchanged.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: Inventory UI (product catalog, lots, open, valuation) in Collection tab"
```

---

## Task 5: Full verification pass

- [ ] **Step 1:** `dotnet build OmniCard/OmniCard.csproj` → 0 errors.
- [ ] **Step 2:** `dotnet test OmniCard.Tests/OmniCard.Tests.csproj` → all pass.
- [ ] **Step 3:** Grep confirms no sealed remnants: `grep -rniE "SealedProduct|Archetype|CrackProduct" --include=*.cs --include=*.xaml OmniCard OmniCard.Shared OmniCard.Collection OmniCard.Data | grep -viE "/obj/|/bin/"` → empty.
- [ ] **Step 4 (human):** End-to-end: add products across two games, own lots, open units, verify valuation; confirm singles/collection, scanning, and eBay are unaffected.

---

## Self-Review Notes
- **Spec coverage:** demolition → Task 1; model/context → Task 2; service+tests → Task 3; UI → Task 4; valuation is in the service (Task 3) and surfaced in Task 4.
- **Deviation from spec:** the spec assumed a "menu item" surface; the sealed feature was actually a Collection-tab toggle mode, so Phase 1 transforms that toggle into an "Inventory" mode (cleaner, reuses working plumbing, forward-compatible with Phase 2). Flagged for the user.
- **Deviation:** CSV export downgraded to "preserve the old `sealed_products.db` on disk" (no throwaway export code for a trashed feature).
- **Not unit-testable (manual, by design):** demolition wiring, all WPF UI (Task 4). Model/context/service are unit-tested.
- **Type consistency:** `IInventoryService` signature identical across interface (Task 3) and consumer (Task 4); model field names consistent across Tasks 2–4.
