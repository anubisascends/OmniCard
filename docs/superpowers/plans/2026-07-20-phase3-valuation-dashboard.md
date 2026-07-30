# Phase 3 (Feature 1) — Valuation + Margin Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A Dashboard tab showing holdings value (cost vs live market, unrealized gain) and realized profit (Sell vs Acquire), broken down by game/category/location — backed by a new `AnalyticsService` — plus making an eBay sale auto-remove the sold lot so figures don't double-count.

**Architecture:** `AnalyticsService` computes holdings (live prices via game services for singles; `Product.MarketPrice` for sealed) and realized P&L (from the movement ledger) over `OmniCardDbContext`. `EbaySyncService` seeds the `Sell` movement then removes the sold lot. A `DashboardView`/`DashboardViewModel` on a new tab renders tiles + breakdown tables, lazy-loaded with an off-thread Refresh.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, EF Core (SQLite, `IDbContextFactory<OmniCardDbContext>`), xUnit.

## Global Constraints
- Read money/prices via the existing game price services (`ICardGameService.GetCurrentPrices(gameCardIds, isFoil)`); do NOT use `Product.MarketPrice` for singles (it's `[NotMapped]`≈0). Sealed (non-Single) uses `Product.MarketPrice` (manual/0 for now).
- Realized P&L is ALWAYS sourced from the movement ledger (`Sell` proceeds paired with `Acquire` cost by `LotId`), never from live holdings — movements persist after a lot is deleted.
- Price fetching + analytics run OFF the UI thread; the Dashboard lazy-loads on first tab activation + explicit Refresh (no auto-polling).
- No new NuGet dependency (tiles + tables/bars, no charts).
- Do NOT change collection/scanning/game DBs. Build: `dotnet build OmniCard.slnx`. Tests: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`.

## File Structure
- Create: `OmniCard.Shared/Models/{ValuationLine,HoldingsValuation,RealizedLine,RealizedSummary}.cs`; `OmniCard.Shared/Interfaces/IAnalyticsService.cs`; `OmniCard.Collection/AnalyticsService.cs`; `OmniCard/Views/Dashboard/DashboardView.xaml(.cs)` + `DashboardViewModel.cs`; tests `OmniCard.Tests/Services/AnalyticsServiceTests.cs`.
- Modify: `OmniCard.eBay/EbaySyncService.cs` (+ tests); `OmniCard/Views/Root/RootView.xaml` (+ `.xaml.cs`); `OmniCard/App.xaml.cs` (DI).

---

## Task 1: `AnalyticsService` + models + tests
**Files:** create the models, `IAnalyticsService`, `AnalyticsService`; test `AnalyticsServiceTests.cs`; modify `App.xaml.cs` (DI).

**Interfaces:**
```csharp
public record ValuationLine(string Key, int Units, decimal Cost, decimal Market);
public record HoldingsValuation(int TotalUnits, decimal TotalCost, decimal TotalMarket,
    IReadOnlyList<ValuationLine> ByGame, IReadOnlyList<ValuationLine> ByCategory, IReadOnlyList<ValuationLine> ByLocation);
public record RealizedLine(string Key, int Count, decimal Proceeds, decimal Cost);
public record RealizedSummary(int TotalSold, decimal TotalProceeds, decimal TotalCost, IReadOnlyList<RealizedLine> ByGame);

public interface IAnalyticsService
{
    HoldingsValuation GetHoldings();
    RealizedSummary GetRealized();
}
```

- [ ] **Step 1: Write failing tests** `AnalyticsServiceTests` (in-memory SQLite `IDbContextFactory<OmniCardDbContext>` per existing test pattern; fake `ICardGameService` list returning known prices by GameCardId/foil). Cover:
  - Holdings totals: cost = Σ Qty·UnitCost; market = Σ Qty·(single→fake price; sealed→Product.MarketPrice); unrealized derivable (Market−Cost per line).
  - Breakdowns ByGame/ByCategory/ByLocation (StorageContainer name; null LocationId → "Unassigned"), sums reconcile to totals.
  - Realized: seed Acquire+Sell movements for a lot (incl. a lot that was then deleted) → TotalSold/Proceeds/Cost, ByGame; profit = Proceeds−Cost; a lot with only Acquire (unsold) excluded from realized.
- [ ] **Step 2: Run — verify fail** (`--filter AnalyticsServiceTests`) → compile fail (types absent).
- [ ] **Step 3: Implement models + `IAnalyticsService` + `AnalyticsService`.** Inject `IDbContextFactory<OmniCardDbContext>` + `IEnumerable<ICardGameService>` (map by Game). `GetHoldings`: load lots+Product; batch single prices by (Game,IsFoil) via `GetCurrentPrices(gameCardIds, foil)` keyed by GameCardId; sealed market from `Product.MarketPrice`; aggregate totals + 3 breakdowns. `GetRealized`: query `Movements`; group by LotId; for lots with a `Sell`, proceeds = Σ Qty·UnitValue, cost = matching `Acquire` Σ Qty·UnitValue; ByGame via the movement's ProductId→Product.Game.
- [ ] **Step 4: Register DI** in `App.xaml.cs`: `services.AddSingleton<IAnalyticsService, AnalyticsService>();`
- [ ] **Step 5: Run tests — pass; then full suite.** `dotnet test` all green.
- [ ] **Step 6: Commit** `feat: add AnalyticsService (holdings valuation + realized P&L) with tests`.

---

## Task 2: eBay sale auto-removes the sold lot
**Files:** `OmniCard.eBay/EbaySyncService.cs`; tests `OmniCard.Tests/Services/EbaySyncServiceTests.cs`.

- [ ] **Step 1: Update the sold path.** In the sold block (where `SeedSellMovementAsync` is called), pass the eBay item id; in `SeedSellMovementAsync` set `Note = ebayItemId` on the `Sell` movement, then after adding the movement REMOVE the sold lot (`ctx.Lots.Remove(lot)`), so holdings exclude it. Ordering: add the `Sell` movement BEFORE removing the lot (movement persists; the sold `EbayListing` cascade-deletes with the lot — its P&L is captured in the movement Note/UnitValue). Keep the "lot already gone → no-op" guard.
- [ ] **Step 2: Update eBay tests** to the new behavior: after a synced sale, the lot is gone, a `Sell` movement exists (with item-id note + sold price), and the listing row is removed; realized P&L (via `AnalyticsService.GetRealized` or direct movement query) still computes. Verify no double-Sell on re-sync (the lot is gone → guard).
- [ ] **Step 3: Build + full tests** green.
- [ ] **Step 4: Commit** `feat: eBay sale removes the sold lot from holdings (P&L retained in ledger)`.

---

## Task 3: Dashboard tab UI
**Files:** create `Views/Dashboard/DashboardViewModel.cs`, `DashboardView.xaml(.cs)`; modify `RootView.xaml`, `RootView.xaml.cs`, `App.xaml.cs`.

- [ ] **Step 1: `DashboardViewModel`** (CommunityToolkit): observable `HoldingsValuation? Holdings`, `RealizedSummary? Realized`, `bool IsBusy`, derived tile props (cost/market/unrealized/realized profit); `[RelayCommand] async Task Refresh()` → `await Task.Run(() => (analytics.GetHoldings(), analytics.GetRealized()))` then assign on the UI thread; a `Load()` that runs Refresh once (lazy).
- [ ] **Step 2: `DashboardView`** — summary tiles (Cost Basis · Market Value · Unrealized Gain [green/red] · Realized Profit) + three breakdown `DataGrid`/`ItemsControl` tables (By Game/Category/Location: Units, Cost, Market, Unrealized + inline proportion bar) + a Realized-by-game table (Sold, Proceeds, Cost, Profit) + a Refresh button + busy indicator. Reuse existing money/color converters + Material styles. `WireUp(DashboardViewModel)` sets DataContext.
- [ ] **Step 3: Add the tab.** `RootView.xaml`: a 4th `<TabItem Header="Dashboard" x:Name="tabItemDashboard">` hosting `DashboardView` after Scanner. `RootView.xaml.cs`: `DashboardTab.WireUp(viewModel.Dashboard)`; trigger `viewModel.Dashboard.Load()` on first activation of that tab (e.g. `MainTabControl.SelectionChanged` → if Dashboard selected and not yet loaded). `RootViewModel`: add `DashboardViewModel` ctor param + `public DashboardViewModel Dashboard { get; }`. `App.xaml.cs`: `services.AddSingleton<DashboardViewModel>();`
- [ ] **Step 4: Build + full tests** green.
- [ ] **Step 5: Manual verification (human, PENDING):** Dashboard tab renders; tiles + tables populate on first open; Refresh recomputes; numbers reconcile on a small dataset; sealed shows $0 market (expected). Do NOT launch GUI from an agent.
- [ ] **Step 6: Commit** `feat: Dashboard tab (valuation + margin) with breakdowns`.

---

## Task 4: Full verification
- [ ] `dotnet build OmniCard.slnx` → 0 errors; `dotnet test` → all pass.
- [ ] Human E2E: open Dashboard, verify holdings vs a manual spot-check, sell an item on eBay → it leaves holdings and appears in realized profit.

## Self-Review Notes
- Spec coverage: AnalyticsService+models+realized → T1; eBay auto-remove → T2; Dashboard tab → T3; verify → T4.
- Not unit-testable (manual): the WPF Dashboard UI (T3). AnalyticsService + eBay removal are unit-tested.
- Deferred (per spec): automated sealed pricing, charts, realized period filter, movement-history browser.
- Type consistency: `IAnalyticsService`/records identical across T1 (def) and T3 (consumer).
