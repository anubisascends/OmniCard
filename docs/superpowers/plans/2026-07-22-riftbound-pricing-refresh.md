# Riftbound Pricing Data Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Populate Riftbound card market prices from TCGCSV (keyed by `TcgplayerId`) so the existing "Refresh Prices" menu and `PriceUpdateService` orchestrator light up Riftbound.

**Architecture:** Add three nullable price fields to `RiftboundCard` and matching additive SQLite columns. Implement `RiftboundService.UpdatePricesAsync` to fetch bulk prices from TCGCSV (category 89, ~9 group requests), build a `productId → (normal, foil)` map, and write prices in batches — mirroring the existing `OptcgService.UpdatePricesAsync`. Implement the `GetCurrentPrice`/`GetCurrentPrices` read path to be foil-aware with fallback. No DI, orchestrator, cooldown, or UI changes.

**Tech Stack:** C# / .NET 10, WPF, EF Core + `Microsoft.Data.Sqlite`, `System.Text.Json`, `IHttpClientFactory`. Tests: xUnit + Moq.

## Global Constraints

- Target framework: .NET 10 (match existing projects; no new NuGet dependencies).
- Data source base URL: `https://tcgcsv.com`; Riftbound TCGPlayer category id: `89`.
- TCGCSV JSON is **camelCase** (`groupId`, `productId`, `marketPrice`, `subTypeName`) — the Riftcodex `JsonOptions` on `RiftboundService` uses `SnakeCaseLower` and MUST NOT be reused for TCGCSV.
- Decimal price columns are stored as SQLite `TEXT` (EF Core's default `decimal?` mapping; confirmed by `OptcgSchemaTests` where `InventoryPrice TEXT, MarketPrice TEXT`). `DateTime?` maps to `TEXT`.
- Do **not** bump `RiftboundDbContext.RiftboundSchemaVersion` (additive columns only; a bump triggers wipe-and-redownload).
- `PriceUpdateProgress` constructor: `new PriceUpdateProgress(CardGame game, string? setCode, int done, int total, string message)`.
- Follow existing patterns in `RiftboundService`/`OptcgService`; run all tests from the repo root with `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`.

---

## File Structure

- `OmniCard.Shared/Models/RiftboundCard.cs` — add `MarketPrice`, `FoilMarketPrice`, `PriceUpdatedAt`.
- `OmniCard.Data/RiftboundDbContext.cs` — add three `AddColumnIfMissing` calls.
- `OmniCard.CardMatching/RiftboundService.cs` — TCGCSV DTOs + options, price-map fetch, `UpdatePricesAsync`, `GetCurrentPrice`, `GetCurrentPrices`.
- `OmniCard.Tests/Data/RiftboundSchemaTests.cs` — **new**; verifies the additive columns.
- `OmniCard.Tests/Services/RiftboundDownloadTests.cs` — replace `UpdatePrices_IsNoOp` with a price-write test; add a read-path test.

---

## Task 1: Add pricing fields to the model and DB schema

**Files:**
- Modify: `OmniCard.Shared/Models/RiftboundCard.cs` (after line 36, alongside the "Computed locally" section)
- Modify: `OmniCard.Data/RiftboundDbContext.cs:37-44` (`ApplySchemaUpgrades`)
- Test: `OmniCard.Tests/Data/RiftboundSchemaTests.cs` (new)

**Interfaces:**
- Produces: `RiftboundCard.MarketPrice` (`decimal?`), `RiftboundCard.FoilMarketPrice` (`decimal?`), `RiftboundCard.PriceUpdatedAt` (`DateTime?`) — consumed by Tasks 2 and 3.

- [ ] **Step 1: Write the failing schema test**

Create `OmniCard.Tests/Data/RiftboundSchemaTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class RiftboundSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RiftboundSchemaTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private RiftboundDbContext NewContext() =>
        new(new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public void FreshDatabase_HasPriceColumns_AndRoundTrips()
    {
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new RiftboundCard
        {
            Id = "c1",
            Name = "Cull the Weak",
            SetId = "OGN",
            TcgplayerId = "653002",
            MarketPrice = 1.23m,
            FoilMarketPrice = 4.56m,
            PriceUpdatedAt = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
        });
        ctx.SaveChanges();

        var loaded = ctx.Cards.Single(c => c.Id == "c1");
        Assert.Equal(1.23m, loaded.MarketPrice);
        Assert.Equal(4.56m, loaded.FoilMarketPrice);
        Assert.Equal(new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc), loaded.PriceUpdatedAt);
    }

    [Fact]
    public void ApplySchemaUpgrades_AddsPriceColumns_ToLegacyTable()
    {
        // Simulate an old Cards table that lacks the price columns.
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE Cards (
                Id TEXT PRIMARY KEY, RiftboundId TEXT, TcgplayerId TEXT, CollectorNumber INTEGER,
                Name TEXT, CleanName TEXT, SetId TEXT, SetName TEXT, Rarity TEXT, CardType TEXT,
                Supertype TEXT, Domain TEXT, Energy INTEGER, Might INTEGER, Power INTEGER,
                CardText TEXT, Flavour TEXT, Artist TEXT, Orientation TEXT, AlternateArt INTEGER,
                Overnumbered INTEGER, Signature INTEGER, CardImageUri TEXT, DateScraped TEXT,
                ImageHash INTEGER, EdgeHash INTEGER, LocalImagePath TEXT);";
            cmd.ExecuteNonQuery();
        }

        using var ctx = NewContext();
        ctx.ApplySchemaUpgrades();

        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name IN ('MarketPrice','FoilMarketPrice','PriceUpdatedAt');";
        Assert.Equal(3L, (long)check.ExecuteScalar()!);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundSchemaTests"`
Expected: FAIL — `RiftboundCard` has no `MarketPrice`/`FoilMarketPrice`/`PriceUpdatedAt` (compile error), and once that compiles, `ApplySchemaUpgrades` does not add the columns.

- [ ] **Step 3: Add the fields to `RiftboundCard`**

In `OmniCard.Shared/Models/RiftboundCard.cs`, add after line 41 (`public string? LocalImagePath { get; set; }`):

```csharp

    // Pricing — populated by RiftboundService.UpdatePricesAsync from TCGCSV, keyed by TcgplayerId.
    public decimal? MarketPrice { get; set; }        // TCGCSV "Normal" subtype market price
    public decimal? FoilMarketPrice { get; set; }    // TCGCSV "Foil" subtype market price
    public DateTime? PriceUpdatedAt { get; set; }     // UTC timestamp of last successful price write
```

- [ ] **Step 4: Add the columns in `ApplySchemaUpgrades`**

In `OmniCard.Data/RiftboundDbContext.cs`, `ApplySchemaUpgrades` (after line 43, `AddColumnIfMissing(conn, "LocalImagePath TEXT");`):

```csharp
        AddColumnIfMissing(conn, "MarketPrice TEXT");
        AddColumnIfMissing(conn, "FoilMarketPrice TEXT");
        AddColumnIfMissing(conn, "PriceUpdatedAt TEXT");
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundSchemaTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/RiftboundCard.cs OmniCard.Data/RiftboundDbContext.cs OmniCard.Tests/Data/RiftboundSchemaTests.cs
git commit -m "feat(riftbound): add market-price fields and additive schema columns"
```

---

## Task 2: Implement `UpdatePricesAsync` via TCGCSV

**Files:**
- Modify: `OmniCard.CardMatching/RiftboundService.cs` — add consts, TCGCSV DTOs + JSON options, a fetch helper, and replace `UpdatePricesAsync` (lines 269-275).
- Test: `OmniCard.Tests/Services/RiftboundDownloadTests.cs` — replace `UpdatePrices_IsNoOp` (lines 161-170) and extend the routing.

**Interfaces:**
- Consumes: `RiftboundCard.MarketPrice`, `RiftboundCard.FoilMarketPrice`, `RiftboundCard.PriceUpdatedAt` (Task 1); existing `_httpClientFactory`, `_dbContextFactory`, `_logger`.
- Produces: `RiftboundService.UpdatePricesAsync` writes prices — behavior consumed by Task 3's read path and by valuation.

- [ ] **Step 1: Write the failing price-write test**

In `OmniCard.Tests/Services/RiftboundDownloadTests.cs`, add these TCGCSV fixtures next to the existing JSON constants (after line 71):

```csharp
    // TCGCSV: one group (24344). Prices cover:
    //   653002 -> Normal 1.50 + Foil 3.00 (both subtypes)
    //   685522 -> Foil 542.31 only (foil-only)
    //   999999 -> present but matches no seeded card
    private const string TcgCsvGroupsJson = """
    {"results":[{"groupId":24344,"name":"Origins","abbreviation":"OGN"}],"success":true,"errors":[]}
    """;

    private const string TcgCsvPricesJson = """
    {"results":[
      {"productId":653002,"lowPrice":1.0,"midPrice":1.4,"highPrice":2.0,"marketPrice":1.50,"directLowPrice":null,"subTypeName":"Normal"},
      {"productId":653002,"lowPrice":2.5,"midPrice":3.1,"highPrice":5.0,"marketPrice":3.00,"directLowPrice":null,"subTypeName":"Foil"},
      {"productId":685522,"lowPrice":600.0,"midPrice":625.0,"highPrice":1299.0,"marketPrice":542.31,"directLowPrice":null,"subTypeName":"Foil"},
      {"productId":999999,"lowPrice":1.0,"midPrice":1.0,"highPrice":1.0,"marketPrice":9.99,"directLowPrice":null,"subTypeName":"Normal"}
    ],"success":true,"errors":[]}
    """;
```

Add a service factory that routes TCGCSV as well (after `CreateService`, around line 93):

```csharp
    private RiftboundService CreateServiceWithPricing()
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.Contains("/sets")) return SetListJson;
            if (uri.Contains("/cards"))
                return CardsPage(uri.Contains("page=2") ? 2 : 1);
            if (uri.Contains("/prices")) return TcgCsvPricesJson;
            if (uri.Contains("/groups")) return TcgCsvGroupsJson;
            return null;
        });
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new RiftboundService(
            new FakeHttpClientFactory(handler),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<RiftboundService>.Instance);
    }
```

Replace `UpdatePrices_IsNoOp` (lines 161-170) with:

```csharp
    [Fact]
    public async Task UpdatePrices_WritesMarketPrices_FromTcgCsv()
    {
        // Seed three cards: both-subtypes, foil-only, and one with no matching price row.
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new RiftboundCard { Id = "c1", Name = "Cull the Weak", SetId = "OGN", TcgplayerId = "653002" },
                new RiftboundCard { Id = "c2", Name = "Vi", SetId = "UNL", TcgplayerId = "685522" },
                new RiftboundCard { Id = "c3", Name = "Orphan", SetId = "OGN", TcgplayerId = "111111" },
                new RiftboundCard { Id = "c4", Name = "NoTcg", SetId = "OGN", TcgplayerId = null });
            seed.SaveChanges();
        }

        var svc = CreateServiceWithPricing();
        await svc.UpdatePricesAsync();

        using var ctx = _factory.CreateDbContext();
        var c1 = ctx.Cards.Single(c => c.Id == "c1");
        Assert.Equal(1.50m, c1.MarketPrice);
        Assert.Equal(3.00m, c1.FoilMarketPrice);
        Assert.NotNull(c1.PriceUpdatedAt);

        var c2 = ctx.Cards.Single(c => c.Id == "c2");
        Assert.Null(c2.MarketPrice);
        Assert.Equal(542.31m, c2.FoilMarketPrice);

        var c3 = ctx.Cards.Single(c => c.Id == "c3");
        Assert.Null(c3.MarketPrice);
        Assert.Null(c3.FoilMarketPrice);
        Assert.Null(c3.PriceUpdatedAt);

        var c4 = ctx.Cards.Single(c => c.Id == "c4");
        Assert.Null(c4.MarketPrice);
        Assert.Null(c4.FoilMarketPrice);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~UpdatePrices_WritesMarketPrices_FromTcgCsv"`
Expected: FAIL — current `UpdatePricesAsync` is a no-op, so `c1.MarketPrice` is null.

- [ ] **Step 3: Add TCGCSV consts, JSON options, and DTOs**

In `OmniCard.CardMatching/RiftboundService.cs`, add after line 20 (`private const int CorrectionTrustBonus = 5;`):

```csharp
    private const string TcgCsvBaseUrl = "https://tcgcsv.com";
    private const int RiftboundCategoryId = 89;
```

Add after the existing `JsonOptions` block (after line 38). TCGCSV is camelCase, so this needs its own options:

```csharp
    // TCGCSV returns camelCase JSON — do NOT reuse JsonOptions (SnakeCaseLower).
    private static readonly JsonSerializerOptions TcgCsvJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
```

Add these private DTOs inside the class (place them near the bottom of the class, before `Dispose`):

```csharp
    private sealed class TcgCsvGroupsResponse
    {
        public List<TcgCsvGroup> Results { get; set; } = [];
    }

    private sealed class TcgCsvGroup
    {
        public int GroupId { get; set; }
    }

    private sealed class TcgCsvPricesResponse
    {
        public List<TcgCsvPrice> Results { get; set; } = [];
    }

    private sealed class TcgCsvPrice
    {
        public int ProductId { get; set; }
        public decimal? MarketPrice { get; set; }
        public string? SubTypeName { get; set; }
    }
```

- [ ] **Step 4: Add the price-map fetch helper**

Add this private method to `RiftboundService` (near `UpdatePricesAsync`):

```csharp
    // Fetches all Riftbound group prices from TCGCSV and folds them into a
    // productId -> (normal, foil) market-price map.
    private async Task<Dictionary<int, (decimal? Normal, decimal? Foil)>> FetchTcgCsvPriceMapAsync(
        HttpClient client, IProgress<PriceUpdateProgress>? progress, CancellationToken ct)
    {
        var groups = await client.GetFromJsonAsync<TcgCsvGroupsResponse>(
            $"{TcgCsvBaseUrl}/tcgplayer/{RiftboundCategoryId}/groups", TcgCsvJsonOptions, ct);

        var map = new Dictionary<int, (decimal? Normal, decimal? Foil)>();
        var groupList = groups?.Results ?? [];
        var done = 0;

        foreach (var group in groupList)
        {
            ct.ThrowIfCancellationRequested();
            var prices = await client.GetFromJsonAsync<TcgCsvPricesResponse>(
                $"{TcgCsvBaseUrl}/tcgplayer/{RiftboundCategoryId}/{group.GroupId}/prices", TcgCsvJsonOptions, ct);

            foreach (var row in prices?.Results ?? [])
            {
                map.TryGetValue(row.ProductId, out var entry);
                if (string.Equals(row.SubTypeName, "Foil", StringComparison.OrdinalIgnoreCase))
                    entry.Foil = row.MarketPrice;
                else if (string.Equals(row.SubTypeName, "Normal", StringComparison.OrdinalIgnoreCase))
                    entry.Normal = row.MarketPrice;
                map[row.ProductId] = entry;
            }

            done++;
            progress?.Report(new PriceUpdateProgress(CardGame.Riftbound, null, done, groupList.Count,
                $"Riftbound prices: {done}/{groupList.Count} groups"));
        }

        return map;
    }
```

- [ ] **Step 5: Replace `UpdatePricesAsync`**

Replace lines 269-275 (the no-op comment + method) with:

```csharp
    public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        await using (var ctx = _dbContextFactory.CreateDbContext())
        {
            if (!await ctx.Cards.AnyAsync(ct))
            {
                _logger.LogInformation("Skipping Riftbound price refresh: card database is empty (run a full data download first)");
                return;
            }
        }

        _logger.LogInformation("Starting Riftbound price-only refresh via TCGCSV");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        var priceMap = await FetchTcgCsvPriceMapAsync(client, progress, ct);

        await using var context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();

        var now = DateTime.UtcNow;
        var updated = 0;

        // Only touch cards whose TcgplayerId parses and exists in the price map.
        var allCards = await context.Cards
            .Where(c => c.TcgplayerId != null)
            .Select(c => new { c.Id, c.TcgplayerId })
            .ToListAsync(ct);

        var targets = allCards
            .Select(c => (c.Id, Parsed: int.TryParse(c.TcgplayerId, out var pid) ? pid : (int?)null))
            .Where(c => c.Parsed is int pid && priceMap.ContainsKey(pid))
            .ToList();

        foreach (var batch in targets.Chunk(500))
        {
            var ids = batch.Select(c => c.Id).ToList();
            var tracked = await context.Cards
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

            foreach (var (id, parsed) in batch)
            {
                if (parsed is int pid && tracked.TryGetValue(id, out var existing)
                    && priceMap.TryGetValue(pid, out var prices))
                {
                    existing.MarketPrice = prices.Normal;
                    existing.FoilMarketPrice = prices.Foil;
                    existing.PriceUpdatedAt = now;
                    updated++;
                }
            }

            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        _logger.LogInformation("Riftbound price refresh complete: {Updated} cards updated", updated);
        progress?.Report(new PriceUpdateProgress(CardGame.Riftbound, null, 0, 0,
            $"Riftbound prices updated ({updated} cards)"));
    }
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~UpdatePrices_WritesMarketPrices_FromTcgCsv"`
Expected: PASS.

- [ ] **Step 7: Run the full Riftbound test set (no regressions)**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~Riftbound"`
Expected: PASS (download tests, schema tests, api-model tests, new price test).

- [ ] **Step 8: Commit**

```bash
git add OmniCard.CardMatching/RiftboundService.cs OmniCard.Tests/Services/RiftboundDownloadTests.cs
git commit -m "feat(riftbound): fetch market prices from TCGCSV in UpdatePricesAsync"
```

---

## Task 3: Foil-aware read path (`GetCurrentPrice` / `GetCurrentPrices`)

**Files:**
- Modify: `OmniCard.CardMatching/RiftboundService.cs:694-696` (the two stub methods).
- Test: `OmniCard.Tests/Services/RiftboundDownloadTests.cs` — add read-path test.

**Interfaces:**
- Consumes: `RiftboundCard.MarketPrice`, `RiftboundCard.FoilMarketPrice` (Task 1); `_readContext` (existing field, refreshed after writes).
- Produces: `decimal? GetCurrentPrice(string gameCardId, bool isFoil)` and `Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)` — matches `ICardGameService`; keyed on `RiftboundCard.Id`.

- [ ] **Step 1: Write the failing read-path test**

In `OmniCard.Tests/Services/RiftboundDownloadTests.cs`, add:

```csharp
    [Fact]
    public void GetCurrentPrice_IsFoilAware_WithFallback()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new RiftboundCard { Id = "both", Name = "Both", SetId = "OGN", MarketPrice = 1.50m, FoilMarketPrice = 3.00m },
                new RiftboundCard { Id = "foilonly", Name = "FoilOnly", SetId = "OGN", MarketPrice = null, FoilMarketPrice = 9.00m },
                new RiftboundCard { Id = "normalonly", Name = "NormalOnly", SetId = "OGN", MarketPrice = 2.00m, FoilMarketPrice = null },
                new RiftboundCard { Id = "none", Name = "None", SetId = "OGN", MarketPrice = null, FoilMarketPrice = null });
            seed.SaveChanges();
        }

        var svc = CreateService(); // read path uses the DB, not HTTP

        // Foil requested
        Assert.Equal(3.00m, svc.GetCurrentPrice("both", isFoil: true));
        Assert.Equal(9.00m, svc.GetCurrentPrice("foilonly", isFoil: true));
        Assert.Equal(2.00m, svc.GetCurrentPrice("normalonly", isFoil: true));   // falls back to normal
        Assert.Null(svc.GetCurrentPrice("none", isFoil: true));

        // Non-foil requested
        Assert.Equal(1.50m, svc.GetCurrentPrice("both", isFoil: false));
        Assert.Equal(9.00m, svc.GetCurrentPrice("foilonly", isFoil: false));    // falls back to foil
        Assert.Equal(2.00m, svc.GetCurrentPrice("normalonly", isFoil: false));

        // Bulk
        var prices = svc.GetCurrentPrices(new[] { "both", "foilonly", "none" }, isFoil: true);
        Assert.Equal(3.00m, prices["both"]);
        Assert.Equal(9.00m, prices["foilonly"]);
        Assert.False(prices.ContainsKey("none"));   // no value for either subtype
    }
```

Note: `CreateService()` builds the service *before* the seed rows exist only if called first — but here the seed runs in the constructor-shared in-memory connection, and `CreateService` opens `_readContext` against that same connection. Because `_readContext` is created in the `RiftboundService` constructor, seed the rows **before** calling `CreateService()`. The test above already does this (seed block precedes `CreateService()`).

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~GetCurrentPrice_IsFoilAware_WithFallback"`
Expected: FAIL — current stubs return `null` / empty.

- [ ] **Step 3: Replace the read-path stubs**

In `OmniCard.CardMatching/RiftboundService.cs`, replace lines 694-696:

```csharp
    // Riftcodex provides no prices.
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => [];
```

with:

```csharp
    // Prices come from TCGCSV (see UpdatePricesAsync). Foil-aware with fallback to the
    // other subtype so a card resolves a price whenever any subtype has one.
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil)
    {
        var row = _readContext.Cards.AsNoTracking()
            .Where(c => c.Id == gameCardId)
            .Select(c => new { c.MarketPrice, c.FoilMarketPrice })
            .FirstOrDefault();
        if (row is null) return null;
        return isFoil
            ? row.FoilMarketPrice ?? row.MarketPrice
            : row.MarketPrice ?? row.FoilMarketPrice;
    }

    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)
    {
        var ids = gameCardIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var result = new Dictionary<string, decimal>(ids.Count);

        foreach (var chunk in ids.Chunk(500))
        {
            var rows = _readContext.Cards.AsNoTracking()
                .Where(c => chunk.Contains(c.Id))
                .Select(c => new { c.Id, c.MarketPrice, c.FoilMarketPrice })
                .ToList();

            foreach (var row in rows)
            {
                var price = isFoil
                    ? row.FoilMarketPrice ?? row.MarketPrice
                    : row.MarketPrice ?? row.FoilMarketPrice;
                if (price.HasValue)
                    result[row.Id] = price.Value;
            }
        }

        return result;
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~GetCurrentPrice_IsFoilAware_WithFallback"`
Expected: PASS.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS (no regressions across the suite).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.CardMatching/RiftboundService.cs OmniCard.Tests/Services/RiftboundDownloadTests.cs
git commit -m "feat(riftbound): foil-aware GetCurrentPrice/GetCurrentPrices read path"
```

---

## Self-Review Notes

- **Spec coverage:** Model fields (Task 1), additive schema no-bump (Task 1), TCGCSV bulk fetch keyed by TcgplayerId (Task 2), both-subtype storage (Task 2), read-path foil fallback (Task 3), tests incl. schema + both/foil-only/unmatched cards (Tasks 1-3). Download-preserves-prices is already guaranteed by the existing explicit-field upsert in `DownloadBulkDataAsync` (lines 153-181 never touch price columns) — no task needed; a reviewer should confirm no price field is added to that block.
- **camelCase gotcha:** Task 2 Step 3 introduces `TcgCsvJsonOptions` (CamelCase) precisely because the existing `JsonOptions` is SnakeCaseLower and would silently fail to bind TCGCSV fields.
- **Type consistency:** `FetchTcgCsvPriceMapAsync` returns `Dictionary<int, (decimal? Normal, decimal? Foil)>`, consumed only within `UpdatePricesAsync`. Read path keys on `RiftboundCard.Id` (consistent with `FindCardById` and `ToMatch`'s `GameSpecificId`).
- **Out of scope (unchanged):** DI, orchestrator, cooldown, UI menu — already fan out to Riftbound via `ICardGameService`.
