# Riftbound Pricing Data Refresh — Design

**Date:** 2026-07-22
**Status:** Approved for planning

## Summary

Implement market-price refresh for the Riftbound card game. `RiftboundService.UpdatePricesAsync`
is currently a deliberate no-op; this feature fills it in so the existing "Refresh Prices" menu
item and `PriceUpdateService` orchestrator populate Riftbound market prices from **TCGCSV**
(`https://tcgcsv.com`), keyed by each card's existing `TcgplayerId`.

No DI, orchestrator, or UI wiring changes are required — `PriceUpdateService` and the
"Refresh Prices" menu already fan out to every `ICardGameService`, including Riftbound.

## Background

- `RiftboundCard` already persists `TcgplayerId` (nullable string), but has **no** market-price field.
- `PriceUpdateService` injects `IEnumerable<ICardGameService>` and already calls
  `UpdatePricesAsync` on Riftbound, guarded by a 24h per-game cooldown (`PriceRefreshCooldownHelper`).
- The One Piece (`OptcgService`) price refresh is the reference pattern: chunked load, write only
  price fields, `SaveChangesAsync` + `ChangeTracker.Clear()` per batch, progress via
  `IProgress<PriceUpdateProgress>`.
- `RiftboundDbContext.ApplySchemaUpgrades()` already supports additive columns via
  `AddColumnIfMissing` without a schema-version bump (no wipe/redownload).

## Data Source: TCGCSV

Chosen over the raw `mp-search-api.tcgplayer.com/v1/product/{id}/listings` endpoint.

**Why TCGCSV:**
- Purpose-built TCGPlayer price mirror; no auth, no browser-header spoofing.
- Bulk: ~9 requests total (one per Riftbound group) vs. one request per card (~1,300) for listings.
- Returns `marketPrice` directly; the listings endpoint returns raw seller listings that would
  need client-side aggregation.

**Verified facts (probed 2026-07-22):**
- Riftbound category id = **89** (`GET https://tcgcsv.com/tcgplayer/categories`).
- Category 89 has 9 groups (`GET https://tcgcsv.com/tcgplayer/89/groups`).
- `GET https://tcgcsv.com/tcgplayer/89/{groupId}/prices` returns rows shaped:
  ```json
  { "productId": 685522, "lowPrice": 623.69, "midPrice": 625.0, "highPrice": 1299.0,
    "marketPrice": 542.31, "directLowPrice": null, "subTypeName": "Foil" }
  ```
- Total across groups: 1,715 price rows / 1,305 unique `productId`s.
- `subTypeName` is `"Normal"` or `"Foil"` (600 Normal, 1,115 Foil).
- **410 productIds have both a Normal and a Foil row** — the reason we store two prices.
- `productId` (int) == our `TcgplayerId` (parsed from string).

Response envelope: `{ "results": [ ...rows... ], "success": true, "errors": [] }`.

## Design

### 1. Model — `RiftboundCard` (OmniCard.Shared/Models/RiftboundCard.cs)

Add three nullable fields (grouped with the other "computed locally, not from API" fields, or a
new "Pricing" section):

```csharp
public decimal? MarketPrice { get; set; }        // TCGCSV "Normal" subtype market price
public decimal? FoilMarketPrice { get; set; }    // TCGCSV "Foil" subtype market price
public DateTime? PriceUpdatedAt { get; set; }     // UTC timestamp of last successful price write
```

### 2. Schema — `RiftboundDbContext.ApplySchemaUpgrades()` (OmniCard.Data/RiftboundDbContext.cs)

Add additive columns; **do not** bump `RiftboundSchemaVersion` (avoids wipe-and-redownload):

```csharp
AddColumnIfMissing(conn, "MarketPrice TEXT");
AddColumnIfMissing(conn, "FoilMarketPrice TEXT");
AddColumnIfMissing(conn, "PriceUpdatedAt TEXT");
```

**Column types must match what EF `EnsureCreated` generates for these properties** so a migrated
DB and a fresh DB agree. EF Core maps `decimal?` to a `TEXT` column on SQLite by default (precision
preservation) and `DateTime?` to `TEXT`. SQLite type affinity is forgiving, but matching EF's
generated type avoids fresh-vs-migrated schema divergence. The `RiftboundSchemaTests` (below) must
assert the migrated column types match a fresh `EnsureCreated` schema. If the implementation
configures a different mapping (e.g. `HasColumnType("REAL")`/a value converter), the
`AddColumnIfMissing` types must be updated to match.

`DownloadBulkDataAsync`'s existing-row update block must **not** touch these columns, so a metadata
re-download never clobbers prices. New cards start with null prices until the next refresh.

### 3. TCGCSV fetch — `RiftboundService`

New consts:
```csharp
private const string TcgCsvBaseUrl = "https://tcgcsv.com";
private const int RiftboundCategoryId = 89;
```

New DTOs (System.Text.Json, snake/camel maps automatically; existing `JsonOptions` reused):
- `TcgCsvGroupsResponse { List<TcgCsvGroup> Results }`, `TcgCsvGroup { int GroupId }`
- `TcgCsvPricesResponse { List<TcgCsvPrice> Results }`,
  `TcgCsvPrice { int ProductId; decimal? MarketPrice; string? SubTypeName }`

Fetch helper builds a price map:
```csharp
// Dictionary<int productId, (decimal? normal, decimal? foil)>
```
- Fetch groups for category 89.
- For each group, `GetFromJsonAsync` its prices (parallelizable with `Parallel.ForEachAsync`,
  `MaxDegreeOfParallelism = 4`, mirroring the existing card-fetch pattern; 9 groups makes this
  optional — sequential is acceptable).
- Fold rows into the map: `"Normal"` → normal slot, `"Foil"` → foil slot. Ignore unknown/other
  subtype names.

### 4. `UpdatePricesAsync` — mirrors `OptcgService.UpdatePricesAsync`

1. Bail early if the Riftbound card DB is empty (report a message, return).
2. Fetch the TCGCSV price map.
3. Load tracked `RiftboundCard`s in chunks of 500.
4. For each card: skip if `TcgplayerId` is null/unparseable; `int.TryParse` it; look up the map;
   set `MarketPrice` and/or `FoilMarketPrice` (whichever subtype the map has) and
   `PriceUpdatedAt = DateTime.UtcNow`.
5. `SaveChangesAsync()` + `ChangeTracker.Clear()` per batch.
6. Report progress via `IProgress<PriceUpdateProgress>` (game = `CardGame.Riftbound`).

The 24h cooldown and "force" override are handled upstream by `PriceUpdateService` — not
re-implemented here.

### 5. Read path — `GetCurrentPrice` / `GetCurrentPrices`

Replace the current `=> null` / `=> []` stubs. Foil-aware with fallback so a card resolves a price
whenever *any* subtype has one:

```csharp
// isFoil == true  -> FoilMarketPrice ?? MarketPrice
// isFoil == false -> MarketPrice ?? FoilMarketPrice
```

`GetCurrentPrices` reads both columns for the requested ids (mirror OPTCG lines ~960–983) and
applies the same fallback, keyed by the card id the valuation layer uses.

## Testing

Test project: `OmniCard.Tests` (xUnit + Moq). Use the existing in-memory SQLite +
`RoutingHandler` / `FakeHttpClientFactory` / `TestFactory<RiftboundDbContext>` harness from
`RiftboundDownloadTests`.

1. **Rewrite `RiftboundDownloadTests.UpdatePrices_IsNoOp`** → `UpdatePrices_WritesMarketPrices`:
   seed cards, serve canned TCGCSV `groups` + `prices` JSON, run `UpdatePricesAsync`, assert:
   - a both-subtypes card gets both `MarketPrice` and `FoilMarketPrice`,
   - a foil-only card gets `FoilMarketPrice` with `MarketPrice` null,
   - a card whose `TcgplayerId` has no price row is left null,
   - `PriceUpdatedAt` is set.
2. **`RiftboundSchemaTests`** (mirror `OptcgSchemaTests`): open an old-shape DB, run
   `ApplySchemaUpgrades`, assert the three new columns exist and existing rows survive.
3. **`GetCurrentPrice` unit coverage**: foil→foil, foil→fallback-to-normal, normal→normal,
   normal→fallback-to-foil, and null when neither present.

## Out of Scope (YAGNI)

- Storing inventory / low / mid / high prices (only `marketPrice` is needed).
- Historical price tracking.
- Inferring a per-card finish (foil vs. normal) — resolved at read time via the `isFoil` argument.
- Any DI, orchestrator, cooldown, or UI/menu changes (already wired).

## Files Touched

- `OmniCard.Shared/Models/RiftboundCard.cs` — three new fields.
- `OmniCard.Data/RiftboundDbContext.cs` — three `AddColumnIfMissing` calls.
- `OmniCard.CardMatching/RiftboundService.cs` — TCGCSV DTOs + fetch, `UpdatePricesAsync`,
  `GetCurrentPrice`, `GetCurrentPrices`.
- `OmniCard.Tests/Services/RiftboundDownloadTests.cs` — replace no-op test.
- `OmniCard.Tests/Data/RiftboundSchemaTests.cs` — new.
