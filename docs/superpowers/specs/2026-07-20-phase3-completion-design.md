# Phase 3 Completion — Sealed Pricing, Movement History, Realized Period Filter, Charts

**Date:** 2026-07-20
**Status:** Detailed spec for review. Builds on the merged/in-flight Dashboard (`AnalyticsService`).
**Parent:** `2026-07-19-tcg-erp-unified-inventory-design.md` (Phase 3).

## Scope
Complete Phase 3 with four features on top of the valuation dashboard:
1. **Automated sealed pricing** (persisted market price for sealed products, sourced from eBay).
2. **Movement history browser** (browse the inventory ledger with filters).
3. **Realized period filter** (dashboard: this month / this year / all time).
4. **Charts** (simple bar charts on the dashboard — no new dependency).

## Feature 1 — Automated sealed pricing

### Source & persistence
- Sealed products have no live price service (singles use the game price services; sealed = $0
  today). The one automated source already in the app is **eBay**:
  `IEbayCatalogService.GetMarketPriceAsync(searchQuery, condition, isFoil)` → `EbayMarketPrice`
  (Median/Low/High/SampleCount). Use **MedianPrice** as the sealed market value.
- `Product.MarketPrice` is `[NotMapped]`, so add a **persisted** field: `decimal? LastMarketPrice`
  and `DateTime? PriceUpdatedAt` on `Product` (+ context config + schema-ensure raw SQL for existing
  `inventory.db`). Sealed valuation reads `LastMarketPrice`.

### Refresh flow
- New `SealedPriceUpdateService` (or extend `PriceUpdateService`): for each sealed Product
  (Category != Single), build a query (Name + SetCode, or UPC if present), call
  `GetMarketPriceAsync`, persist `LastMarketPrice = Median` + `PriceUpdatedAt`. Throttled via a
  cooldown (reuse `PriceRefreshCooldownHelper` pattern with a sealed key); runnable in the
  background (opt-in) **and** via a manual "Refresh Sealed Prices" action (menu / Inventory view).
- **Requires eBay connected** (OAuth). If not connected, the refresh is a no-op with a clear
  status message; manual price entry (Inventory product editor) remains the fallback. Rate-limited
  / best-effort per product (skip failures, log, continue).

### Consumers
- `AnalyticsService.GetHoldings` and `InventoryService.GetValuation`: for non-Single lots use
  `Product.LastMarketPrice ?? 0` instead of the `[NotMapped]` `MarketPrice`. Inventory list shows it.

### Risks
- eBay dependency + auth; median-of-search is approximate (note `SampleCount`, skip if 0); rate
  limits → throttle + background. Sealed with no eBay match → stays null/0 (labeled).

## Feature 2 — Movement history browser
- `IAnalyticsService` (or a small `IMovementService`) gains
  `IReadOnlyList<MovementView> GetMovements(MovementFilter filter)` where `MovementView` is a
  display record (Timestamp, Type, ProductName+Game, Qty, UnitValue, Note) joined from
  `Movements` → `Products`; `MovementFilter` = optional type, product, date range, take/limit.
- UI: a **Movement History** view opened from a menu item (dialog window) — a `DataGrid`
  (MaterialDesign-themed, `BasedOn={x:Type DataGrid}`) with filters (type dropdown, date range,
  text search on product) + a running total. Read-only.
- Tests: `GetMovements` filtering (type/date/product), ordering (newest first), join correctness.

## Feature 3 — Realized period filter
- `AnalyticsService.GetRealized(DateTime? since = null)` filters `Sell` movements by `Timestamp >=
  since` (cost from the paired `Acquire` regardless of date). Backward-compatible default (all time).
- Dashboard: a small segmented control / combo — **All Time / This Year / This Month** — bound to a
  `DashboardViewModel.RealizedPeriod`; changing it re-runs realized (off-thread) and updates the
  Realized tile + Realized-by-game table. Holdings unaffected.
- Tests: `GetRealized(since)` includes/excludes by date correctly; totals recompute.

## Feature 4 — Charts (dashboard)
- Simple **bar charts**, no new NuGet: an `ItemsControl` of rows with a label + a proportional
  `Rectangle`/`Border` (like the existing share-bars) + value — packaged as a small reusable
  `BarChart` user control or inline. Charts: **Market value by game**, **Market value by category**
  (and optionally realized **profit by game**). Colors from a small theme-consistent categorical
  palette (apply dataviz best-practices: legible in dark/light, value labels, sorted desc,
  accessible contrast). Consult the `dataviz` skill when building.
- Placed on the Dashboard (a "Charts" section above/below the tables, or replacing the inline
  share-bar columns). Purely presentational over the same `HoldingsValuation`/`RealizedSummary`.
- No unit tests (visual); build + human check.

## Cross-cutting
- All new DataGrids use the MaterialDesign theme (`BasedOn="{StaticResource {x:Type DataGrid}}"`) —
  the fix just applied to the dashboard.
- Off-thread for any eBay/price fetch + analytics recompute; busy indicators; errors surfaced
  (StatusMessage pattern), not silent.
- Build `OmniCard.slnx`; tests `dotnet test`. Sealed-pricing + realized-filter + movement queries
  are unit-tested; UI (movement browser, charts, period control) is human-verified.

## Sub-feature order (each shippable; own tasks in the plan)
1. Sealed pricing (persisted field + refresh service + valuation consumers + tests) — biggest, and
   it makes the dashboard/charts meaningful for sealed.
2. Realized period filter (small service+VM change).
3. Movement history browser (service query + dialog UI).
4. Charts (dashboard visual).

## Open questions (resolve in the plan)
- Sealed refresh: background-on-startup (throttled) vs manual-only. Lean: manual "Refresh Sealed
  Prices" + reuse the existing price-update opt-in; confirm.
- Movement browser: standalone dialog vs a tab section. Lean: dialog from a menu item.
- Charts: dedicated section vs replacing the share-bar columns. Lean: a Charts section, keep tables.

## Next step
Review; then plan + execute subagent-driven, sub-feature by sub-feature.
