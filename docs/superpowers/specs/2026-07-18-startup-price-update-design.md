# Background Price Updates (Startup + Manual)

**Date:** 2026-07-18
**Status:** Approved design, ready for implementation plan

## Problem

Card prices are only refreshed today by the heavyweight full bulk re-download
(`ICardGameService.DownloadBulkDataAsync`, driven by `RootViewModel.RefreshCardData`), which
re-imports cards and recomputes image hashes. There is no lightweight, price-only refresh, and
nothing refreshes prices automatically. The user wants prices kept current without manual
full-data downloads.

## Goals

- On app startup, asynchronously refresh **price data for all sets, for every game**, without
  blocking app load — the refresh continues in the background while the app is used.
- The splash screen notes that a price update is starting.
- Once the app is open, a small status-bar indicator shows the ongoing refresh and a brief
  completion state.
- A manual "Refresh Prices" action re-runs the same all-sets refresh on demand.
- Automatic startup refresh is throttled ("only if stale") so it does not re-pull unchanged
  prices on every launch; the manual action bypasses the throttle.
- After a refresh completes, prices already displayed in the collection update without the user
  re-searching.

## Non-Goals

- No new pricing sources/APIs — reuse the ones each game service already uses (Scryfall for MTG,
  the OPTCG source for One Piece).
- Not changing the full bulk data download flow (`DownloadBulkDataAsync`) or card matching.
- No per-set scoping / owned-only scoping — every run covers all sets for all games (per user
  decision, for uniform behavior).
- No historical price tracking or charts — this only refreshes the current price fields.

## Approach

Add a lightweight, price-only refresh to each game service, orchestrate it across games in a
new background service, hook it into startup (non-blocking) and a manual command, surface
progress on the splash and an in-app status indicator, and refresh the visible collection
prices on completion.

Per-game reality drives the price-only implementation:
- **MTG (Scryfall):** prices come as one daily bulk file. Scryfall's guidance is to use that
  bulk file for prices rather than many per-set API calls. So the MTG price-only refresh streams
  the bulk file and applies **only** the `Prices` field to existing cards — no new inserts, no
  image-hash recompute. This inherently covers all sets.
- **One Piece (OPTCG):** the source API is per-set. The price-only refresh loops all sets and
  refetches each set's `MarketPrice`/`InventoryPrice` only. This gives natural per-set progress.

## Detailed Design

### 1. `ICardGameService.UpdatePricesAsync`
Add:
```csharp
Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default);
```
Always refreshes all sets for that game. Reports progress via `PriceUpdateProgress`.

`PriceUpdateProgress` (new, `OmniCard.Shared`): `{ CardGame Game, string? SetCode, int Completed, int Total, string Message }`.
`SetCode`/`Total` may be null/0 for MTG's bulk path (overall progress only); populated per set
for OPTCG.

Implementations:
- **ScryfallService.UpdatePricesAsync:** reuse the existing bulk download/stream scaffolding
  from `DownloadBulkDataAsync`, but in the per-batch upsert apply only `existing.Prices =
  card.Prices` for cards already present (skip inserts, skip `FlattenFrontFace`/hash work).
  Report overall progress ("MTG: updated N cards"). Swap the read context at the end as the
  existing method does so reads see new prices.
- **OptcgService.UpdatePricesAsync:** loop `GetAvailableSets()`; for each set, fetch its cards
  and update only `MarketPrice`/`InventoryPrice` on existing rows (mirror the per-set fetch
  already in its `DownloadBulkDataAsync`, price-only). Report per-set progress
  (`SetCode`, Completed/Total).

### 2. `PriceUpdateService` (singleton orchestrator)
New service in the app project (or `OmniCard.Collection`), registered as a singleton.
```csharp
Task RunAsync(bool force, CancellationToken ct = default);
```
- Iterates all registered `ICardGameService`.
- For each game, unless `force`, skip when a price refresh happened within the throttle window
  (see §5). On success, record the game's price-refresh timestamp.
- Calls each service's `UpdatePricesAsync`, forwarding a progress handler that updates the
  service's bindable state.
- **Single-run guard:** if a run is already in progress, a new call is a no-op (returns the
  in-flight task or returns immediately).
- Catches and logs per-game failures without aborting the other games; reflects failure in the
  status text.
- Exposes bindable state via `INotifyPropertyChanged`: `IsRunning`, `StatusText`, `Completed`,
  `Total`; and a `PricesUpdated` event raised once when a run finishes (with at least one game
  updated).

### 3. Startup hook (`App.OnStartup`)
After `Host.Start()` and the other non-blocking startup calls, and just before `splash.Close()`:
- `splash.SetStatus("Updating card prices in background...");`
- `_ = Host.Services.GetRequiredService<PriceUpdateService>().RunAsync(force: false);`
- `splash.Close();`

App loading is not blocked; the run continues after the splash closes.

### 4. In-app status indicator (`RootView.xaml`)
Add a small status element (near the existing `RootViewModel.Message` status line) bound to
`PriceUpdateService`:
- Visible only while `IsRunning` (or briefly after completion).
- Shows `StatusText`, e.g. "Updating prices… (set 12/28)" (OPTCG) or "Updating MTG prices…".
- Shows a brief "Prices updated" state on completion, then hides.

`RootViewModel` exposes the `PriceUpdateService` (or a thin wrapper of its bindable state) for
binding.

### 5. Throttle ("only if stale")
Mirror `RefreshCooldownHelper` but with a **separate** persistence file so price refreshes are
independent of the existing bulk-data cooldown. Add `PriceRefreshCooldownHelper` (or
parameterize `RefreshCooldownHelper` with a filename) using `price-refresh-timestamps.json`,
per-game `DateTime`, 24h window. Startup passes `force: false` (respects it); manual passes
`force: true` (bypasses and re-records).

### 6. Manual refresh command (`RootViewModel`)
`[RelayCommand] Task RefreshPrices()` → `await priceUpdateService.RunAsync(force: true)`.
Add a "Refresh Prices" menu item to the existing menu that hosts `RefreshCardData` and the other
maintenance actions.

### 7. Reflect new prices in the open collection view
On `PriceUpdateService.PricesUpdated`, the collection re-pulls prices for the currently displayed
cards: re-run the existing `CollectionViewModel.FetchBatchPrices` over the current
`CollectionSearchResults`, update `MarketPrices` and the derived totals — no full DB re-search.
Add `CollectionViewModel.RefreshVisiblePrices()` and wire `RootViewModel`/`CollectionViewModel`
to the event on the UI thread.

## Concurrency / Safety

- Price writes go through `IDbContextFactory` (new context per batch) — the existing write
  pattern; reads (`GetCurrentPrices`) use short-lived `AsNoTracking` contexts. SQLite handles the
  brief read/write overlap (same as the existing bulk download running while the app is open).
- All progress/state mutation marshaled to the UI thread (progress handler via `Progress<T>`
  captured on the UI thread, or explicit `Dispatcher`).
- `RunAsync` is guarded against concurrent/overlapping runs.
- Startup fire-and-forget: exceptions are caught inside `RunAsync` and logged (no unobserved
  task crash).

## Testing / Verification

- **Unit-testable:** `PriceRefreshCooldownHelper` (stale/fresh/force logic); `PriceUpdateService`
  orchestration with fake `ICardGameService` implementations (all games invoked, per-game
  failure isolated, single-run guard, timestamp recorded only on success, `PricesUpdated` raised).
- **Manual/GUI (human):** startup shows the splash note and does not block app load; status bar
  shows progress then completion; manual "Refresh Prices" runs and bypasses the throttle; a
  second startup within 24h skips the network refresh; open collection tiles show updated prices
  after completion.

## Risks

- **MTG bulk bandwidth:** the Scryfall bulk file is large (~150 MB+); throttling to ~once daily
  mitigates frequency, but each MTG refresh is a sizable background download. Accepted per the
  "all sets" decision.
- **Read/write contention on SQLite** during a refresh while the user browses — brief, handled
  by WAL/short contexts, but worth watching under large refreshes.
- **Progress heterogeneity:** MTG reports overall progress while OPTCG reports per-set; the
  status text must read sensibly for both.
- **Stale displayed prices** if the `PricesUpdated` refresh is missed — mitigated by re-pulling
  on the event; a manual re-search remains the fallback.
