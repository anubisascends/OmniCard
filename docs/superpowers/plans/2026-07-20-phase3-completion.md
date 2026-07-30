# Phase 3 Completion Implementation Plan (Sealed Pricing · Period Filter · Movement History · Charts)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Finish Phase 3 — automated sealed pricing (eBay), a realized-P&L period filter, a movement-history browser, and dashboard charts — on top of the valuation dashboard.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, EF Core (SQLite, `IDbContextFactory<OmniCardDbContext>`), MaterialDesignThemes, xUnit.

## Global Constraints
- New DataGrids use the MaterialDesign theme: `BasedOn="{StaticResource {x:Type DataGrid}}"`.
- Any eBay/price fetch or analytics recompute runs OFF the UI thread; busy indicators; errors surfaced via a StatusMessage (never silent).
- Sealed pricing source is eBay `IEbayCatalogService.GetMarketPriceAsync` (Median); it requires `IEbayAuthService.IsConnected` — if not connected, no-op with a status message; manual price entry stays the fallback.
- Do NOT change singles pricing (game price services), scanning, or game DBs. Build `OmniCard.slnx`; tests `dotnet test`.
- Baseline: branch off master (Phase 3 dashboard merged) or the dashboard branch if not yet merged — controller sets base.

## File Structure
- Modify: `OmniCard.Shared/Models/Product.cs` (+`LastMarketPrice`,`PriceUpdatedAt`), `OmniCard.Data/OmniCardDbContext.cs` (map them + schema-ensure), `OmniCard.Collection/AnalyticsService.cs` & `InventoryService.cs` (sealed market → `LastMarketPrice`), `OmniCard/Views/Dashboard/DashboardViewModel.cs` & `DashboardView.xaml` (period filter + charts), `OmniCard/Views/Root/RootView.xaml`(+menu) / `App.xaml.cs` (DI, movement dialog).
- Create: `OmniCard.Collection/SealedPriceUpdateService.cs` (+`ISealedPriceUpdateService`); `MovementView`/`MovementFilter` models + `GetMovements` on `IAnalyticsService`; `OmniCard/Views/MovementHistory/*`; tests.

---

## Task 1: Automated sealed pricing
**Files:** `Product.cs`, `OmniCardDbContext.cs` (+ schema-ensure in `UnifiedMigrationService`), `ISealedPriceUpdateService`+`SealedPriceUpdateService` (OmniCard.Collection or eBay-adjacent), `AnalyticsService.cs`, `InventoryService.cs`, DI + a manual command; tests.

- [ ] **Persist price:** add `public decimal? LastMarketPrice { get; set; }` + `public DateTime? PriceUpdatedAt { get; set; }` to `Product`. Map in `OmniCardDbContext` (keep `Ignore(MarketPrice)`). Add idempotent schema-ensure (ALTER TABLE ADD COLUMN if missing) in the existing `EnsureUnifiedSchema` raw-SQL so pre-existing `inventory.db` gets the columns.
- [ ] **Service:** `ISealedPriceUpdateService.RefreshSealedPricesAsync(IProgress<PriceUpdateProgress>?, CancellationToken)`. Impl: if `!IEbayAuthService.IsConnected` → report a "connect eBay to price sealed products" message and return. Else load sealed products (Category != Single); for each, `await ebayCatalog.GetMarketPriceAsync(query, condition:"", isFoil:false)` where query = `$"{Name} {SetCode}".Trim()` (or `Upc` if present); if result non-null and `SampleCount > 0`, set `LastMarketPrice = Median`, `PriceUpdatedAt = UtcNow`; SaveChanges (batch). Throttle via a sealed-key cooldown (reuse `PriceRefreshCooldownHelper` pattern); best-effort per product (catch+log+continue). Register DI.
- [ ] **Manual trigger:** a "Refresh Sealed Prices" command (Inventory view menu/button or a top-level menu) calling the service (force). Optionally hook the existing background price-update opt-in.
- [ ] **Consumers:** in `AnalyticsService.GetHoldings` (~line 62) and `InventoryService.GetValuation` (~line 155), for non-Single lots use `(l.Product.LastMarketPrice ?? 0m) * l.Quantity` instead of `Product.MarketPrice`.
- [ ] **Tests:** `SealedPriceUpdateServiceTests` (fake `IEbayCatalogService` returning known Median + a `SampleCount=0` skip case; fake `IEbayAuthService` connected/disconnected): persists LastMarketPrice for sealed only, disconnected → no-op, SampleCount 0 → skip. Extend `AnalyticsServiceTests`/valuation to assert sealed market uses `LastMarketPrice`.
- [ ] Build + tests green. Commit `feat: automated sealed pricing via eBay median (persisted LastMarketPrice)`.

---

## Task 2: Realized period filter
**Files:** `IAnalyticsService`/`AnalyticsService.cs`, `DashboardViewModel.cs`, `DashboardView.xaml`; tests.

- [ ] `GetRealized(DateTime? since = null)` — filter `Sell` movements by `Timestamp >= since` (cost from the paired `Acquire` regardless of date). Keep the no-arg behavior (all time) via default.
- [ ] `DashboardViewModel`: add `RealizedPeriod` (enum All/Year/Month) `[ObservableProperty]`; `OnRealizedPeriodChanged` recomputes realized off-thread (`GetRealized(since)`), updates `Realized` + tiles. Compute `since` from period (month/year start, UtcNow-based passed in; do NOT call DateTime.Now inside a testable service — the VM computes the date and passes it).
- [ ] `DashboardView`: a small ComboBox/segmented control (All Time / This Year / This Month) bound to `RealizedPeriod`, near the Realized tile.
- [ ] Tests: `GetRealized(since)` includes/excludes by Sell Timestamp; all-time default unchanged.
- [ ] Build + tests green. Commit `feat: realized P&L period filter (all/year/month) on dashboard`.

---

## Task 3: Movement history browser
**Files:** `MovementView`/`MovementFilter` models; `GetMovements` on `IAnalyticsService`; `OmniCard/Views/MovementHistory/MovementHistoryView.xaml(.cs)`+VM; `IDialogService`/`DialogService` + menu item; tests.

- [ ] Models: `MovementView(DateTime Timestamp, MovementType Type, string ProductName, CardGame Game, int Quantity, decimal? UnitValue, string? Note)`; `MovementFilter(MovementType? Type=null, DateTime? Since=null, string? ProductQuery=null, int Take=500)`.
- [ ] `GetMovements(MovementFilter)` — join `Movements`→`Products`, apply filters, order by Timestamp desc, Take; project to `MovementView`.
- [ ] UI: `MovementHistoryViewModel` (load via `GetMovements`, filter props, Refresh) + `MovementHistoryView` (MaterialDesign-themed DataGrid: Date/Type/Product/Game/Qty/Value/Note + type dropdown + date + product search). Opened as a dialog from a "Movement History" menu item via `IDialogService.OpenMovementHistory()` (follow the existing dialog pattern); register views in `App.xaml.cs`.
- [ ] Tests: `GetMovements` filter by type/date/product, ordering, projection/join correctness.
- [ ] Build + tests green. Commit `feat: movement history browser (ledger view with filters)`.

---

## Task 4: Dashboard charts
**Files:** `DashboardView.xaml`(+ a small `BarChart` control or inline `ItemsControl`), maybe `DashboardViewModel` (chart series), a categorical color converter/resource; consult the `dataviz` skill for palette/labels.

- [ ] Add a **Charts** section to the dashboard: horizontal bar charts for **Market value by game**, **Market value by category**, and **Realized profit by game** — each an `ItemsControl` over the existing breakdown lists (`Holdings.ByGame`/`ByCategory`, `Realized.ByGame`) with a label + proportional bar (width via a share converter vs the section max) + value label; sorted desc. No new NuGet.
- [ ] Apply dataviz basics: a small theme-consistent categorical palette (legible dark/light, sufficient contrast), value labels on bars, clear titles; reuse `BreakdownKeyDisplayConverter` for names.
- [ ] Build green (no unit tests — visual). Commit `feat: dashboard bar charts (value/profit breakdowns)`.

---

## Task 5: Full verification
- [ ] `dotnet build OmniCard.slnx` → 0 errors; `dotnet test` → all pass.
- [ ] Human E2E: Refresh Sealed Prices (with eBay connected) populates sealed market + dashboard/charts; period filter changes realized; movement history opens + filters; charts render legibly in the theme.

## Self-Review Notes
- Coverage: sealed pricing → T1; period filter → T2; movement browser → T3; charts → T4; verify → T5.
- Testable (unit): sealed price service (fake eBay), GetRealized(since), GetMovements(filter), valuation-uses-LastMarketPrice. Manual: refresh action, period control, movement dialog, charts.
- Deferred/known: sealed pricing needs eBay connected (manual fallback); eBay median is approximate (SampleCount guard).
- Dates: services take an explicit `since`/UtcNow-derived value from the caller (VM), keeping them deterministic/testable.
