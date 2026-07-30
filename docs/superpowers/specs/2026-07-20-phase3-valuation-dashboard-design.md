# Phase 3 (Feature 1) — Valuation + Margin Dashboard (Detailed Spec)

**Date:** 2026-07-20
**Status:** Detailed spec for review.
**Parent:** `2026-07-19-tcg-erp-unified-inventory-design.md` (Phase 3).

## Scope
A new **Dashboard** tab presenting, over the unified `Product`/`InventoryLot`/`InventoryMovement`
store: current **holdings value** (cost basis vs live market, unrealized gain) and **realized
profit** (from `Sell` vs `Acquire` movements), with breakdowns by game / category / location. Plus
a supporting correctness change: an eBay sale **auto-removes the sold lot** from holdings so the two
figures don't double-count.

Out of scope (later Phase-3 features): automated sealed pricing (sealed market stays manual/0 here),
charts (tiles + tables/bars only), rich time-series, movement-history browser, cross-game views
beyond the breakdowns.

## Building blocks (already present)
- `OmniCardDbContext`: `Lots` (Quantity, UnitCost, LocationId, Product), `Products` (Game, Category,
  GameCardId, Foil, MarketPrice[NotMapped]), `Movements` (Type Acquire/Sell/Open/Move/Adjust,
  Quantity, UnitValue, LotId, ProductId, Timestamp).
- Live prices: `ICardGameService.GetCurrentPrices(gameCardIds, isFoil)` (singles). Sealed has no
  automated price yet.
- eBay sold → `EbaySyncService.SeedSellMovementAsync` already records a `Sell` movement.
- `StorageContainer` (locations) for the location breakdown.

Note: the existing `InventoryService.GetValuation` sums the un-persisted `Product.MarketPrice`
(≈0) — it is NOT the source of truth for market value. The dashboard fetches **live** prices.

## Design

### 1. `AnalyticsService` (new, `OmniCard.Collection`; `IAnalyticsService` in `OmniCard.Shared/Interfaces`)
Kept separate from `InventoryService` to stay focused; injected with `IDbContextFactory<OmniCardDbContext>`
and the game services (`IEnumerable<ICardGameService>`) for live pricing.

```csharp
public interface IAnalyticsService
{
    HoldingsValuation GetHoldings();          // live prices; totals + breakdowns
    RealizedSummary   GetRealized();          // from the movement ledger
}
```
Models (new, `OmniCard.Shared/Models`):
```csharp
public record ValuationLine(string Key, int Units, decimal Cost, decimal Market); // Market-Cost = unrealized
public record HoldingsValuation(
    int TotalUnits, decimal TotalCost, decimal TotalMarket,
    IReadOnlyList<ValuationLine> ByGame,
    IReadOnlyList<ValuationLine> ByCategory,
    IReadOnlyList<ValuationLine> ByLocation);
public record RealizedLine(string Key, int Count, decimal Proceeds, decimal Cost); // Proceeds-Cost = profit
public record RealizedSummary(
    int TotalSold, decimal TotalProceeds, decimal TotalCost,
    IReadOnlyList<RealizedLine> ByGame);
```

- **GetHoldings:** load current lots (+Product); cost = `Σ Qty·UnitCost`; market — for `Single`
  lots, fetch live prices via the game services grouped by (Game, IsFoil) keyed by GameCardId (same
  approach as the collection view's price fetch); for non-`Single` (sealed) lots use
  `Product.MarketPrice` (manual/0 for now). Aggregate totals + `ByGame`/`ByCategory`/`ByLocation`
  (location name from `StorageContainer`; null → "Unassigned"). Unrealized gain = Market − Cost
  (derived in the VM/line).
- **GetRealized:** from `Movements`, pair each `Sell` (proceeds = `Σ Qty·UnitValue`) with its lot's
  `Acquire` cost by `LotId` (movements persist after a lot is deleted, so sold-and-removed lots
  still compute). Totals + `ByGame` (via the movement's Product). Profit = Proceeds − Cost.

Price fetching runs off the UI thread; results feed the VM. (Reuse/extract the collection's batch
price helper rather than duplicating.)

### 2. eBay sold → auto-remove the sold lot (correctness)
In `EbaySyncService` sold-handling: keep seeding the `Sell` movement (capture proceeds; put the eBay
item id in `Movement.Note` so the sale is traceable), THEN remove the sold lot (qty-1 → delete).
Because `Movements` have no FK to the lot, the `Sell`/`Acquire` pair survives for realized P&L. The
sold `EbayListing` row (FK `LotId`, cascade) is removed with the lot — its P&L-relevant data
(sold price, item id) is already captured in the `Sell` movement, so no reporting data is lost.
(If we later want to retain full eBay sale metadata, change the FK to `SetNull`; noted, not done
now.) Order matters: seed the movement before deleting the lot.

### 3. Dashboard tab (`OmniCard/Views/Dashboard/`)
- New 4th `TabItem` "Dashboard" in `RootView.xaml` (after Scanner), `DashboardView` + `DashboardViewModel`
  wired like the others in `RootView.xaml.cs`; `DashboardViewModel` registered (singleton) in `App.xaml.cs`.
- **Summary tiles:** Cost Basis · Market Value · Unrealized Gain (green/red) · Realized Profit.
- **Breakdown sections:** three tables (By Game, By Category, By Location), each row = Units, Cost,
  Market, Unrealized (with a simple inline proportion bar); a Realized-by-game table (Sold, Proceeds,
  Cost, Profit).
- **Refresh** command (recompute + re-fetch live prices; show a busy indicator; runs off-thread).
  Load on first activation of the tab (lazy) to avoid slowing startup.
- Reuse existing money/color converters and Material styles. No charting dependency.

## Testing
- **`AnalyticsServiceTests`** (xUnit, in-memory SQLite + fake `ICardGameService` returning known
  prices): holdings totals + each breakdown with mixed games/categories/locations; unassigned
  location; sealed market from Product.MarketPrice; realized summary pairs Sell↔Acquire by lot
  (incl. a sold-and-deleted lot), profit math, by-game.
- **eBay sold-removal test** (extend `EbaySyncServiceTests`): a synced sale seeds the `Sell` movement
  (with item-id note) and removes the lot; realized P&L still computes from the ledger.
- UI verified manually (human): tab renders, tiles/tables populate, Refresh works, numbers match a
  hand-check on a small dataset.

## Risks & mitigations
- **Live-price fetch cost** on large collections: batch by game/foil (as the collection view does),
  run off the UI thread, lazy-load on tab activation + explicit Refresh (not auto-polling).
- **Sold-lot removal vs eBay metadata:** P&L preserved in the `Sell` movement; operational eBay
  metadata (buyer/item-id) is captured in the movement Note but the `EbayListing` row is removed —
  acceptable; `SetNull` FK is the fallback if fuller history is wanted later.
- **Sealed market = 0** until automated sealed pricing (next Phase-3 feature) — clearly labeled so
  it doesn't read as a bug.
- **Double-count avoided** by the auto-remove change; realized always sourced from the ledger.

## Open questions (resolve in the plan)
- Realized "by period" (this month / all-time) — include a simple all-time now, period filter later?
- Whether `AnalyticsService` reuses a shared price-batch helper extracted from `CardService`/
  `CollectionViewModel`, or calls the game services directly.

## Next step
Review this spec; then plan + execute subagent-driven (AnalyticsService + tests → eBay sold-removal →
Dashboard tab UI → verify).
