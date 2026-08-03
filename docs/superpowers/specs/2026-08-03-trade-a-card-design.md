# Trade a Card (web app → desktop) — Design

## Context

At in-person trading, the user's phone (the read-only web companion) is what's on hand, not the
desktop. Today there's no way to record a trade at all. This adds: marking a specific card as
traded from the phone, with a note and a photo of what was received in return; the desktop later
(next launch) picks up the record and updates the real collection — flags the traded card with a
badge, excludes it from collection value, and logs it in Movement History. Removing the traded card
and scanning in whatever was received afterward is unchanged, existing workflow.

The web app must stay offline-tolerant at the moment of trading (no live connection to the desktop
required) and must not write to the SQLite collection DBs directly (an existing, deliberate
invariant — see `OmniCard.Web`'s `Mode=ReadOnly` connection strings and `WebCardService`'s
`NotSupportedException` writes). So the handoff is a plain file drop, not a live relay.

## Flow

1. Web: Location page (tiles, unchanged) → tap a tile → existing `/card/{id}` detail page.
2. `/card/{id}` gets a new "Trade" button → `/Trade?lotId={id}` (same lot id the detail page
   already resolves and displays).
3. `/Trade`: shows the card read-only, a free-text note field, and a plain
   `<input type="file" accept="image/*" capture="environment">` (opens the phone's native camera
   directly, no custom capture UI needed — this photo is just a reference image, not something fed
   through matching/cropping). Submit writes a trade record to a new shared folder and redirects
   back with a confirmation. No DB write.
4. Desktop: on next launch, right after the existing `UnifiedMigrationService.MigrateDataIfNeeded`
   step, a new import step scans for unprocessed trade records and applies them.

## Storage

New `IDataPathService.TradesDirectory => Path.Combine(DataDirectory, "trades")`, implemented in
both `DataPathService` (desktop, `OmniCard.Data`) and `WebDataPathService` (web,
`OmniCard.Web/Services`) — same pattern as the existing `ScansDirectory`/`LogsDirectory`.

Each trade is `trades/<guid>/trade.json` + `trades/<guid>/photo.<ext>`. `trade.json`
(`OmniCard.Shared/Models/TradeRecord.cs`, new): `TradeId` (Guid), `LotId` (int), `Note` (string),
`PhotoFileName` (string), `CreatedAt` (DateTime, UTC), `ProcessedAt` (DateTime?, null = pending).
The folder is never deleted — `ProcessedAt` is the idempotency marker, and the folder stays around
for the user to browse later.

## Desktop schema + import

`InventoryLot` gains `bool IsTraded`, `string? TradeNote`, `string? TradePhotoPath`. This app uses
`EnsureCreated()`, not real EF migrations, so new columns on existing installs are patched via
`UnifiedMigrationService.EnsureUnifiedSchema`'s existing `AddColumnIfMissing(cmd, table, column,
definition)` helper — same mechanism already used for every prior column addition to this table.

`MovementType` gains `Trade`.

New `ITradeImportService`/`TradeImportService` (`OmniCard.Collection`, DI singleton, same shape as
`UnifiedMigrationService`'s usage): `ImportPendingTrades()` reads every `trades/*/trade.json` with
`ProcessedAt == null`, loads the `InventoryLot` by `LotId`, sets `IsTraded = true` +
`TradeNote`/`TradePhotoPath`, adds an `InventoryMovement { Type = Trade }`, saves, then rewrites
`trade.json` with `ProcessedAt` set. A lot that no longer exists (deleted since the trade was
recorded) is logged and still marked processed (with a note) so it doesn't retry forever.

Wired into `App.xaml.cs OnStartup`, immediately after the existing `MigrateDataIfNeeded` call,
following that same `splash.SetStatus(...)` + try/catch-log-and-continue pattern (an uncaught
exception here must not block startup or repeat-crash on relaunch).

## Valuation exclusion

`InventoryService.GetValuation()` and `AnalyticsService.GetHoldings()` add `.Where(l => !l.IsTraded)`
to their lot queries. `CollectionCardMapper` (used to build the on-screen `CollectionCard` DTOs)
zeroes `MarketPrice` for traded lots so `CollectionViewModel.FilteredMarketValue` (which just sums
whatever's currently on screen) naturally excludes them too, without needing its own filter.

## Badge

Clone the existing eBay-listing tile badge (`CardListView.xaml`, `ListingStatus` +
`ListingBadgeConverter`): `CollectionCard` gets a `[NotMapped] bool IsTraded`, populated at query
time in `CollectionViewModel` the same way `ListingStatus` already is. `CardListView.xaml`'s tile
template gets a second small corner `Border` (a different corner from the eBay badge) reading
"TRADED", visible via the existing `BoolToVisibilityConverter`.

## Out of scope

- No in-app UI to browse/review trade records (photo + note) — the user reviews the `trades/`
  folder directly on disk, as they asked for.
- No live desktop connection / SignalR relay (that's the separate, unmerged web-scanner feature —
  wrong fit here since trading needs to work with the desktop closed).
- No auto-adding the received card to the collection — scanned in manually afterward, unchanged
  workflow.
- No lot-picker UI for stacks — a stack's representative lot (already how `/card/{id}` resolves
  today) is the one traded.

## Verification

- New unit tests for `TradeImportService.ImportPendingTrades` (in-memory SQLite + a temp directory
  for `trades/`): seeds a lot, drops a `trade.json` + photo, asserts `IsTraded`/`TradeNote`/
  `TradePhotoPath` get set, an `InventoryMovement` row is added, and `ProcessedAt` is written back
  (and that a second run is a no-op).
- `dotnet build OmniCard.slnx` and `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`.
- Manual smoke: run the web app, trade a card from a phone/browser, confirm a `trades/<guid>/`
  folder appears with `trade.json` + photo; run the desktop app, confirm the badge appears, the
  collection total drops, and a Trade entry shows in Movement History.
