# Swap OPTCG API to api.poneglyph.one Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the OmniCard One Piece TCG data source (`optcgapi.com`) with `api.poneglyph.one`, storing one row per card variant (alternate art) and wiping the local OPTCG cache on first migration.

**Architecture:** The new API has no bulk endpoint, so `DownloadBulkDataAsync` iterates `GET /v1/sets` then `GET /v1/sets/{code}`, flattening each card's `variants[]` into `OptcgCard` rows. Card identity (`CardSetId`, the primary key) becomes a *variant uid*: the bare card number for the base printing (`OP01-001`) and a suffixed form for alternate arts (`OP01-001_p1`). A version marker (`PRAGMA user_version`) lets the OPTCG database detect it is still on old-API data and wipe itself (records, corrections, downloaded art) before repopulating. Only the OPTCG reference cache is wiped — never the user's collection database.

**Tech Stack:** C# / .NET, EF Core + SQLite, System.Text.Json, xUnit + Moq.

## Global Constraints

- Base API URL: `https://api.poneglyph.one` (no authentication). HTTP User-Agent: `OmniCard/1.0` (verbatim, matches existing code).
- English only — do not send or iterate `lang`; the API defaults to English.
- Only touch the OPTCG path: `OmniCard.CardMatching/OptcgService.cs`, `OmniCard.Shared/Models/OptcgCard.cs`, `OmniCard.Data/OptcgDbContext.cs`, new DTO file, and OPTCG tests. Do not modify the Scryfall/MTG path or other games.
- The wipe must target only `OptcgDbContext` (`Cards`, `HashCorrections`) and the `optcg-art/` folder under the data directory. Never touch `CollectionDbContext`.
- Base-variant uid MUST equal the bare card number so existing `CollectionCard.GameCardId` values keep resolving after re-download.
- Follow existing conventions: `[JsonPropertyName]`-free DTOs deserialized with `JsonNamingPolicy.SnakeCaseLower` (as in `CardDeserializationTests`); `NumberHandling = AllowReadingFromString` is already used and can be kept.
- TDD: write the failing test first, watch it fail, implement minimally, watch it pass, commit. Run tests from repo root with `dotnet test OmniCard.Tests`.

---

### Task 1: New-API response DTOs + deserialization test

**Files:**
- Create: `OmniCard.Shared/Models/OptcgApiModels.cs`
- Test: `OmniCard.Tests/Models/OptcgApiDeserializationTests.cs`

**Interfaces:**
- Produces: DTO records in namespace `OmniCard.Models`:
  - `OptcgSetListResponse { List<OptcgSetSummary> Data }`
  - `OptcgSetSummary { string Code; string Name; DateTimeOffset? ReleasedAt; int CardCount }`
  - `OptcgSetDetailResponse { OptcgSetDetail Data }`
  - `OptcgSetDetail { string Code; string Name; int CardCount; List<OptcgApiCard> Cards }`
  - `OptcgApiCard { string CardNumber; string Name; string Set; string SetName; string CardType; string? Rarity; List<string> Color; int? Cost; int? Power; int? Counter; int? Life; List<string>? Attribute; List<string> Types; string? Effect; List<OptcgApiVariant> Variants }`
  - `OptcgApiVariant { int Index; string? Label; string? Artist; OptcgApiImages Images; OptcgApiMarket Market }`
  - `OptcgApiImages { OptcgApiStockImages Stock; OptcgApiScanImages Scan }`
  - `OptcgApiStockImages { string? Full; string? Thumb }`
  - `OptcgApiScanImages { string? Display; string? Full; string? Thumb }`
  - `OptcgApiMarket { string? MarketPrice; string? LowPrice; string? MidPrice; string? HighPrice }`

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Models/OptcgApiDeserializationTests.cs`:

```csharp
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Tests.Models;

public class OptcgApiDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void Deserialize_SetDetail_MapsCardsAndVariants()
    {
        var json = """
        {
          "data": {
            "code": "OP01",
            "name": "Romance Dawn",
            "released_at": "2022-12-02T00:00:00.000Z",
            "card_count": 121,
            "products": [],
            "cards": [
              {
                "card_number": "OP01-001",
                "name": "Roronoa Zoro",
                "language": "en",
                "set": "OP01",
                "set_name": "Romance Dawn",
                "released_at": "2022-12-02T00:00:00.000Z",
                "released": true,
                "card_type": "Leader",
                "rarity": "L",
                "color": ["Red"],
                "cost": null,
                "power": 5000,
                "counter": null,
                "life": 5,
                "attribute": ["Slash"],
                "types": ["Supernovas", "Straw Hat Crew"],
                "effect": "Your turn +1000 power.",
                "trigger": null,
                "block": null,
                "variants": [
                  {
                    "index": 0,
                    "name": null,
                    "label": "Standard",
                    "artist": null,
                    "crop_focus": {"x": null, "y": null},
                    "product": {"id": null, "slug": null, "name": null, "set_code": null, "released_at": null},
                    "images": {
                      "stock": {"full": "https://cdn.poneglyph.one/OP01-001/stock/full.png", "thumb": "https://cdn.poneglyph.one/OP01-001/stock/thumb.webp"},
                      "scan": {"display": null, "full": null, "thumb": null}
                    },
                    "errata": [],
                    "market": {"tcgplayer_url": "https://tcg/x", "market_price": "6.00", "low_price": "1.46", "mid_price": "6.80", "high_price": "34.99"}
                  },
                  {
                    "index": 1,
                    "name": null,
                    "label": "Alternate Art",
                    "artist": "Some Artist",
                    "crop_focus": {"x": 0.5, "y": 0.5},
                    "product": {"id": null, "slug": null, "name": null, "set_code": null, "released_at": null},
                    "images": {
                      "stock": {"full": "https://cdn.poneglyph.one/OP01-001/stock/full-1.png", "thumb": null},
                      "scan": {"display": "https://cdn.poneglyph.one/OP01-001/scan/display-1.png", "full": null, "thumb": null}
                    },
                    "errata": [],
                    "market": {"tcgplayer_url": null, "market_price": "40.00", "low_price": "25.00", "mid_price": "41.00", "high_price": "99.00"}
                  }
                ]
              }
            ]
          }
        }
        """;

        var resp = JsonSerializer.Deserialize<OptcgSetDetailResponse>(json, JsonOptions)!;

        Assert.Equal("OP01", resp.Data.Code);
        Assert.Single(resp.Data.Cards);
        var card = resp.Data.Cards[0];
        Assert.Equal("OP01-001", card.CardNumber);
        Assert.Equal("Roronoa Zoro", card.Name);
        Assert.Equal("Leader", card.CardType);
        Assert.Equal(["Red"], card.Color);
        Assert.Null(card.Cost);
        Assert.Equal(5000, card.Power);
        Assert.Equal(5, card.Life);
        Assert.Equal(["Slash"], card.Attribute);
        Assert.Equal(["Supernovas", "Straw Hat Crew"], card.Types);
        Assert.Equal("Your turn +1000 power.", card.Effect);

        Assert.Equal(2, card.Variants.Count);
        var v0 = card.Variants[0];
        Assert.Equal(0, v0.Index);
        Assert.Equal("Standard", v0.Label);
        Assert.Equal("https://cdn.poneglyph.one/OP01-001/stock/full.png", v0.Images.Stock.Full);
        Assert.Null(v0.Images.Scan.Display);
        Assert.Equal("6.00", v0.Market.MarketPrice);
        Assert.Equal("1.46", v0.Market.LowPrice);

        var v1 = card.Variants[1];
        Assert.Equal(1, v1.Index);
        Assert.Equal("Some Artist", v1.Artist);
        Assert.Equal("https://cdn.poneglyph.one/OP01-001/scan/display-1.png", v1.Images.Scan.Display);
    }

    [Fact]
    public void Deserialize_SetList_MapsSummaries()
    {
        var json = """
        {"data":[{"code":"OP01","name":"Romance Dawn","released_at":"2022-12-02T00:00:00.000Z","card_count":121}]}
        """;
        var resp = JsonSerializer.Deserialize<OptcgSetListResponse>(json, JsonOptions)!;
        Assert.Single(resp.Data);
        Assert.Equal("OP01", resp.Data[0].Code);
        Assert.Equal(121, resp.Data[0].CardCount);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgApiDeserializationTests`
Expected: FAIL — build error, `OptcgSetDetailResponse` / `OptcgSetListResponse` do not exist.

- [ ] **Step 3: Create the DTO file**

Create `OmniCard.Shared/Models/OptcgApiModels.cs`:

```csharp
namespace OmniCard.Models;

// Response DTOs for the api.poneglyph.one v1 endpoints.
// Deserialized with JsonNamingPolicy.SnakeCaseLower (no [JsonPropertyName] needed).

public sealed class OptcgSetListResponse
{
    public List<OptcgSetSummary> Data { get; set; } = [];
}

public sealed class OptcgSetSummary
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset? ReleasedAt { get; set; }
    public int CardCount { get; set; }
}

public sealed class OptcgSetDetailResponse
{
    public OptcgSetDetail Data { get; set; } = new();
}

public sealed class OptcgSetDetail
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int CardCount { get; set; }
    public List<OptcgApiCard> Cards { get; set; } = [];
}

public sealed class OptcgApiCard
{
    public string CardNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string Set { get; set; } = "";
    public string SetName { get; set; } = "";
    public string CardType { get; set; } = "";
    public string? Rarity { get; set; }
    public List<string> Color { get; set; } = [];
    public int? Cost { get; set; }
    public int? Power { get; set; }
    public int? Counter { get; set; }
    public int? Life { get; set; }
    public List<string>? Attribute { get; set; }
    public List<string> Types { get; set; } = [];
    public string? Effect { get; set; }
    public List<OptcgApiVariant> Variants { get; set; } = [];
}

public sealed class OptcgApiVariant
{
    public int Index { get; set; }
    public string? Label { get; set; }
    public string? Artist { get; set; }
    public OptcgApiImages Images { get; set; } = new();
    public OptcgApiMarket Market { get; set; } = new();
}

public sealed class OptcgApiImages
{
    public OptcgApiStockImages Stock { get; set; } = new();
    public OptcgApiScanImages Scan { get; set; } = new();
}

public sealed class OptcgApiStockImages
{
    public string? Full { get; set; }
    public string? Thumb { get; set; }
}

public sealed class OptcgApiScanImages
{
    public string? Display { get; set; }
    public string? Full { get; set; }
    public string? Thumb { get; set; }
}

public sealed class OptcgApiMarket
{
    public string? MarketPrice { get; set; }
    public string? LowPrice { get; set; }
    public string? MidPrice { get; set; }
    public string? HighPrice { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgApiDeserializationTests`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Shared/Models/OptcgApiModels.cs OmniCard.Tests/Models/OptcgApiDeserializationTests.cs
git commit -m "feat(optcg): add poneglyph API response DTOs"
```

---

### Task 2: OptcgCard entity — variant columns + schema upgrade

**Files:**
- Modify: `OmniCard.Shared/Models/OptcgCard.cs`
- Modify: `OmniCard.Data/OptcgDbContext.cs:14-49`
- Test: `OmniCard.Tests/Data/OptcgSchemaTests.cs` (create)

**Interfaces:**
- Produces: `OptcgCard` gains `string CardNumber`, `int VariantIndex`, `string? VariantLabel`, `string? Artist`. `OptcgDbContext` indexes `CardNumber` and its `ApplySchemaUpgrades()` adds the four columns to pre-existing tables.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Data/OptcgSchemaTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class OptcgSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public OptcgSchemaTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public void FreshDatabase_HasVariantColumns()
    {
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-001_p1",
            CardNumber = "OP01-001",
            VariantIndex = 1,
            VariantLabel = "Alternate Art",
            Artist = "Some Artist",
            CardName = "Zoro",
            SetId = "OP01",
        });
        ctx.SaveChanges();

        var loaded = ctx.Cards.Single(c => c.CardSetId == "OP01-001_p1");
        Assert.Equal("OP01-001", loaded.CardNumber);
        Assert.Equal(1, loaded.VariantIndex);
        Assert.Equal("Alternate Art", loaded.VariantLabel);
        Assert.Equal("Some Artist", loaded.Artist);
    }

    [Fact]
    public void ApplySchemaUpgrades_AddsColumns_ToLegacyTable()
    {
        // Simulate an old table that lacks the new columns.
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE Cards (
                CardSetId TEXT PRIMARY KEY, CardName TEXT, SetId TEXT, SetName TEXT,
                Rarity TEXT, CardColor TEXT, CardType TEXT, CardCost TEXT, CardPower TEXT,
                Life TEXT, CardText TEXT, SubTypes TEXT, Attribute TEXT, CounterAmount INTEGER,
                InventoryPrice TEXT, MarketPrice TEXT, CardImageId TEXT, CardImageUri TEXT,
                DateScraped TEXT, ImageHash INTEGER, LocalImagePath TEXT);";
            cmd.ExecuteNonQuery();
        }

        using var ctx = NewContext();
        ctx.ApplySchemaUpgrades(); // must not throw and must add CardNumber/VariantIndex/VariantLabel/Artist

        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name IN ('CardNumber','VariantIndex','VariantLabel','Artist');";
        Assert.Equal(4L, (long)check.ExecuteScalar()!);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgSchemaTests`
Expected: FAIL — build error, `OptcgCard.CardNumber` etc. do not exist.

- [ ] **Step 3: Add columns to the entity**

In `OmniCard.Shared/Models/OptcgCard.cs`, remove the now-unused `[JsonPropertyName]` attributes (the entity is no longer deserialized directly from the API) and add the four variant columns. Replace the file body with:

```csharp
namespace OmniCard.Models;

// Persistence entity for OPTCG cards. One row per card variant (printing).
// Populated by OptcgService from api.poneglyph.one DTOs (see OptcgApiModels).
public class OptcgCard
{
    // Variant uid: bare card number for the base printing (index 0),
    // "{CardNumber}_p{index}" for alternate arts.
    public string CardSetId { get; set; } = "";

    // Printed collector number, e.g. "OP01-001" (shared across variants).
    public string CardNumber { get; set; } = "";

    public int VariantIndex { get; set; }
    public string? VariantLabel { get; set; }
    public string? Artist { get; set; }

    public string CardName { get; set; } = "";
    public string SetId { get; set; } = "";
    public string SetName { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string CardColor { get; set; } = "";
    public string CardType { get; set; } = "";
    public string? CardCost { get; set; }
    public string? CardPower { get; set; }
    public string? Life { get; set; }
    public string? CardText { get; set; }
    public string? SubTypes { get; set; }
    public string? Attribute { get; set; }
    public int? CounterAmount { get; set; }
    public decimal? InventoryPrice { get; set; }
    public decimal? MarketPrice { get; set; }
    public string? CardImageId { get; set; }
    public string? CardImageUri { get; set; }
    public string? DateScraped { get; set; }

    // Computed locally, not from API
    public ulong? ImageHash { get; set; }
    public string? LocalImagePath { get; set; }
}
```

Remove the now-unused `using System.Text.Json.Serialization;` line.

- [ ] **Step 4: Index CardNumber and add the schema upgrade**

In `OmniCard.Data/OptcgDbContext.cs`, add a `CardNumber` index in `OnModelCreating` (after the existing `SetId` index at line 38):

```csharp
        card.HasIndex(c => c.CardNumber);
```

Replace the body of `ApplySchemaUpgrades()` with a helper-driven version that adds each new column idempotently:

```csharp
    public void ApplySchemaUpgrades()
    {
        var conn = Database.GetDbConnection();
        conn.Open();

        AddColumnIfMissing(conn, "LocalImagePath TEXT");
        AddColumnIfMissing(conn, "CardNumber TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(conn, "VariantIndex INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "VariantLabel TEXT");
        AddColumnIfMissing(conn, "Artist TEXT");
    }

    private static void AddColumnIfMissing(System.Data.Common.DbConnection conn, string columnDef)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE Cards ADD COLUMN {columnDef}";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
        {
            // Column already exists
        }
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgSchemaTests`
Expected: PASS (both tests).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/OptcgCard.cs OmniCard.Data/OptcgDbContext.cs OmniCard.Tests/Data/OptcgSchemaTests.cs
git commit -m "feat(optcg): add variant columns to OptcgCard schema"
```

---

### Task 3: Schema-version helpers on OptcgDbContext

**Files:**
- Modify: `OmniCard.Data/OptcgDbContext.cs`
- Test: `OmniCard.Tests/Data/OptcgSchemaTests.cs` (add cases)

**Interfaces:**
- Produces: `OptcgDbContext.PoneglyphSchemaVersion` (const int = 1), `int GetSchemaVersion()`, `void MarkMigrationComplete()`. Version is stored via SQLite `PRAGMA user_version`.

- [ ] **Step 1: Write the failing test**

Add to `OmniCard.Tests/Data/OptcgSchemaTests.cs`:

```csharp
    [Fact]
    public void SchemaVersion_DefaultsToZero_ThenMarksComplete()
    {
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();

        Assert.Equal(0, ctx.GetSchemaVersion());

        ctx.MarkMigrationComplete();

        Assert.Equal(OptcgDbContext.PoneglyphSchemaVersion, ctx.GetSchemaVersion());
        Assert.True(OptcgDbContext.PoneglyphSchemaVersion >= 1);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~SchemaVersion_DefaultsToZero`
Expected: FAIL — build error, `GetSchemaVersion` / `MarkMigrationComplete` / `PoneglyphSchemaVersion` do not exist.

- [ ] **Step 3: Implement the helpers**

In `OmniCard.Data/OptcgDbContext.cs`, add near the top of the class (after the constructor):

```csharp
    // Identifies data sourced from api.poneglyph.one. A stored user_version below
    // this value means the DB still holds old-API data and must be wiped.
    public const int PoneglyphSchemaVersion = 1;

    public int GetSchemaVersion()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void MarkMigrationComplete()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        // PRAGMA does not accept parameters; value is a compile-time constant.
        cmd.CommandText = $"PRAGMA user_version = {PoneglyphSchemaVersion};";
        cmd.ExecuteNonQuery();
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~SchemaVersion_DefaultsToZero`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Data/OptcgDbContext.cs OmniCard.Tests/Data/OptcgSchemaTests.cs
git commit -m "feat(optcg): add schema-version helpers via PRAGMA user_version"
```

---

### Task 4: Constructor wipe-on-stale + keep existing fixtures green

**Files:**
- Modify: `OmniCard.CardMatching/OptcgService.cs:25-51` (constructor) and add a private `WipeForMigration` method
- Modify (stamp version so they are not wiped): `OmniCard.Tests/Services/OptcgServiceTests.cs`, `OptcgCorrectionTests.cs`, `PriceResolutionTests.cs`, `SetCompletionTests.cs`, `SetFilterTests.cs`
- Test: `OmniCard.Tests/Services/OptcgMigrationTests.cs` (create)

**Interfaces:**
- Consumes: `OptcgDbContext.GetSchemaVersion()`, `PoneglyphSchemaVersion` (Task 3); `IDataPathService.DataDirectory`.
- Produces: `OptcgService` constructor wipes `Cards`, `HashCorrections`, and the `optcg-art/` folder when `GetSchemaVersion() < PoneglyphSchemaVersion`. It does NOT set the version (Task 5's download does).

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/OptcgMigrationTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OptcgMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;
    private readonly string _dataDir;

    public OptcgMigrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);

        _dataDir = Path.Combine(Path.GetTempPath(), "optcg-migration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dataDir, "optcg-art"));
        File.WriteAllText(Path.Combine(_dataDir, "optcg-art", "OP01-001.jpg"), "stale");
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private void SeedLegacy(int userVersion)
    {
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.Cards.Add(new OptcgCard { CardSetId = "OP01-001", CardNumber = "OP01-001", CardName = "Zoro", SetId = "OP01" });
        ctx.HashCorrections.Add(new HashCorrection { ScanHash = 123, CorrectCardId = "OP01-001", CreatedAt = "2024-01-01T00:00:00Z" });
        ctx.SaveChanges();
        if (userVersion > 0) ctx.MarkMigrationComplete();
    }

    private OptcgService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new OptcgService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public void Constructor_StaleVersion_WipesCardsCorrectionsAndArt()
    {
        SeedLegacy(userVersion: 0);

        _ = CreateService();

        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.Cards);
        Assert.Empty(ctx.HashCorrections);
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "optcg-art")));
    }

    [Fact]
    public void Constructor_CurrentVersion_PreservesData()
    {
        SeedLegacy(userVersion: OptcgDbContext.PoneglyphSchemaVersion);

        _ = CreateService();

        using var ctx = _factory.CreateDbContext();
        Assert.Single(ctx.Cards);
        Assert.Single(ctx.HashCorrections);
        Assert.True(File.Exists(Path.Combine(_dataDir, "optcg-art", "OP01-001.jpg")));
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgMigrationTests`
Expected: FAIL — `Constructor_StaleVersion_WipesCardsCorrectionsAndArt` fails (data still present; wipe not implemented).

- [ ] **Step 3: Implement the wipe in the constructor**

In `OmniCard.CardMatching/OptcgService.cs`, inside the constructor, after `_readContext.ApplySchemaUpgrades();` (line 49) and before the final log line, add:

```csharp
        if (_readContext.GetSchemaVersion() < OptcgDbContext.PoneglyphSchemaVersion)
        {
            _logger.LogWarning("OPTCG database predates api.poneglyph.one; wiping for migration");
            WipeForMigration();
        }
```

Add this method to the class (e.g. just below the constructor):

```csharp
    private void WipeForMigration()
    {
        using (var ctx = _dbContextFactory.CreateDbContext())
        {
            ctx.Database.ExecuteSqlRaw("DELETE FROM Cards");
            ctx.Database.ExecuteSqlRaw("DELETE FROM HashCorrections");
        }

        var artDir = Path.Combine(_dataDirectory, "optcg-art");
        if (Directory.Exists(artDir))
        {
            try
            {
                Directory.Delete(artDir, recursive: true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete OPTCG art directory during migration wipe");
            }
        }

        // Refresh the read context and drop in-memory caches so nothing stale survives.
        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _hashSetLookup = null;
        _correctionsCache = null;
        oldContext.Dispose();

        _logger.LogInformation("OPTCG migration wipe complete");
    }
```

- [ ] **Step 4: Stamp the version in existing OPTCG fixtures**

Every existing test that seeds an OPTCG database and then constructs `OptcgService` will now be wiped unless it marks the DB as migrated. In each fixture below, immediately after the final `ctx.SaveChanges();` of the seed step (while the seeding context is still in scope), add:

```csharp
        ctx.MarkMigrationComplete();
```

Apply to the seeding context in:
- `OmniCard.Tests/Services/OptcgServiceTests.cs` (constructor, after line 64 `ctx.SaveChanges();`)
- `OmniCard.Tests/Services/OptcgCorrectionTests.cs` (after its seed `SaveChanges`)
- `OmniCard.Tests/Services/PriceResolutionTests.cs` (after the OPTCG seed `SaveChanges` on `optcgCtx`; call `optcgCtx.MarkMigrationComplete();`)
- `OmniCard.Tests/Services/SetCompletionTests.cs` (in `OptcgSetCompletionTests` fixture, after its seed `SaveChanges`)
- `OmniCard.Tests/Services/SetFilterTests.cs` (in the OPTCG fixture that constructs `OptcgService`, after its seed `SaveChanges`)

For fixtures where the seeding `ctx` is inside a `using` block that closes before the service is created, add the call inside that same block. If a fixture disposes the seeding context before constructing the service, ensure the `MarkMigrationComplete()` call happens on the same open connection (the shared `:memory:` connection persists `user_version`), so placing it right after `SaveChanges` is correct.

- [ ] **Step 5: Run the full OPTCG test suite to verify green**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~Optcg`
Expected: PASS — `OptcgMigrationTests` (both cases) plus all pre-existing OPTCG tests remain green.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.CardMatching/OptcgService.cs OmniCard.Tests/Services/OptcgMigrationTests.cs OmniCard.Tests/Services/OptcgServiceTests.cs OmniCard.Tests/Services/OptcgCorrectionTests.cs OmniCard.Tests/Services/PriceResolutionTests.cs OmniCard.Tests/Services/SetCompletionTests.cs OmniCard.Tests/Services/SetFilterTests.cs
git commit -m "feat(optcg): wipe stale cache on migration to poneglyph"
```

---

### Task 5: Rewrite DownloadBulkDataAsync for the new API

**Files:**
- Modify: `OmniCard.CardMatching/OptcgService.cs:62-171` (`DownloadBulkDataAsync`) and add mapping + fetch helpers
- Test: `OmniCard.Tests/Services/OptcgDownloadTests.cs` (create)

**Interfaces:**
- Consumes: DTOs from Task 1; `OptcgDbContext.MarkMigrationComplete()` from Task 3.
- Produces: `DownloadBulkDataAsync` populates `Cards` from `api.poneglyph.one`, one row per variant, using uid scheme (base = bare number, alt = `{number}_p{index}`), and calls `MarkMigrationComplete()` on success. Adds `private static OptcgCard MapVariant(OptcgApiCard card, OptcgApiVariant variant)`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/OptcgDownloadTests.cs`. This uses a fake `HttpMessageHandler` that routes by URL:

```csharp
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OptcgDownloadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;
    private readonly string _dataDir;

    public OptcgDownloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete(); // already migrated so the ctor does not wipe

        _dataDir = Path.Combine(Path.GetTempPath(), "optcg-dl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private const string SetListJson = """{"data":[{"code":"OP01","name":"Romance Dawn","released_at":null,"card_count":1}]}""";

    private const string SetDetailJson = """
    {"data":{"code":"OP01","name":"Romance Dawn","card_count":1,"products":[],"cards":[
      {"card_number":"OP01-001","name":"Zoro","language":"en","set":"OP01","set_name":"Romance Dawn",
       "released_at":null,"released":true,"card_type":"Leader","rarity":"L","color":["Red","Green"],
       "cost":null,"power":5000,"counter":null,"life":5,"attribute":["Slash"],"types":["Straw Hat Crew"],
       "effect":"Text.","trigger":null,"block":null,"variants":[
         {"index":0,"name":null,"label":"Standard","artist":null,"crop_focus":{"x":null,"y":null},
          "product":{"id":null,"slug":null,"name":null,"set_code":null,"released_at":null},
          "images":{"stock":{"full":"https://cdn/stock0.png","thumb":null},"scan":{"display":null,"full":null,"thumb":null}},
          "errata":[],"market":{"tcgplayer_url":null,"market_price":"6.00","low_price":"1.46","mid_price":"6.80","high_price":"34.99"}},
         {"index":1,"name":null,"label":"Alt","artist":"Artist X","crop_focus":{"x":null,"y":null},
          "product":{"id":null,"slug":null,"name":null,"set_code":null,"released_at":null},
          "images":{"stock":{"full":"https://cdn/stock1.png","thumb":null},"scan":{"display":"https://cdn/scan1.png","full":null,"thumb":null}},
          "errata":[],"market":{"tcgplayer_url":null,"market_price":"40.00","low_price":"25.00","mid_price":"41.00","high_price":"99.00"}}
       ]}
    ]}}
    """;

    private OptcgService CreateService()
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.EndsWith("/v1/sets")) return SetListJson;
            if (uri.EndsWith("/v1/sets/OP01")) return SetDetailJson;
            return null;
        });
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new OptcgService(
            new FakeHttpClientFactory(handler),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public async Task DownloadBulkData_FlattensVariants_WithUidScheme()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var rows = ctx.Cards.OrderBy(c => c.CardSetId).ToList();
        Assert.Equal(2, rows.Count);

        var baseRow = rows.Single(c => c.CardSetId == "OP01-001");
        Assert.Equal("OP01-001", baseRow.CardNumber);
        Assert.Equal(0, baseRow.VariantIndex);
        Assert.Equal("Zoro", baseRow.CardName);
        Assert.Equal("OP01", baseRow.SetId);
        Assert.Equal("Red/Green", baseRow.CardColor);
        Assert.Equal("Straw Hat Crew", baseRow.SubTypes);
        Assert.Equal("Slash", baseRow.Attribute);
        Assert.Equal("5000", baseRow.CardPower);
        Assert.Equal("5", baseRow.Life);
        Assert.Null(baseRow.CardCost);
        Assert.Equal("Text.", baseRow.CardText);
        Assert.Equal("https://cdn/stock0.png", baseRow.CardImageUri); // scan null -> stock.full
        Assert.Equal(6.00m, baseRow.MarketPrice);
        Assert.Equal(1.46m, baseRow.InventoryPrice);

        var altRow = rows.Single(c => c.CardSetId == "OP01-001_p1");
        Assert.Equal("OP01-001", altRow.CardNumber);
        Assert.Equal(1, altRow.VariantIndex);
        Assert.Equal("Artist X", altRow.Artist);
        Assert.Equal("https://cdn/scan1.png", altRow.CardImageUri); // scan.display preferred
        Assert.Equal(40.00m, altRow.MarketPrice);

        // Version marker flips to migrated after a successful download.
        Assert.Equal(OptcgDbContext.PoneglyphSchemaVersion, ctx.GetSchemaVersion());
    }

    private class RoutingHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = route(request.RequestUri!.ToString());
            var resp = body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            return Task.FromResult(resp);
        }
    }

    private class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgDownloadTests`
Expected: FAIL — the old download hits `optcgapi.com` (or the routing handler returns 404 for `allSetCards`), so no rows are inserted / assertions fail.

- [ ] **Step 3: Replace DownloadBulkDataAsync**

In `OmniCard.CardMatching/OptcgService.cs`, add a base-URL constant near the other private fields (after line 22):

```csharp
    private const string ApiBaseUrl = "https://api.poneglyph.one";
```

Replace the entire `DownloadBulkDataAsync` method (lines 62-171) with:

```csharp
    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting OPTCG card data download from poneglyph API");
        var sw = Stopwatch.StartNew();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        progress?.Report("Fetching OPTCG set list...");
        var setList = await client.GetFromJsonAsync<OptcgSetListResponse>(
            $"{ApiBaseUrl}/v1/sets", jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to fetch set list from poneglyph API.");

        _logger.LogInformation("Discovered {Count} OPTCG sets", setList.Data.Count);

        // Fetch each set's detail (cards + variants), throttled.
        using var throttle = new SemaphoreSlim(4);
        var allCards = new List<OptcgCard>();
        var cardsLock = new object();
        var fetchedSets = 0;

        await Parallel.ForEachAsync(setList.Data, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        }, async (set, token) =>
        {
            await throttle.WaitAsync(token);
            try
            {
                var detail = await client.GetFromJsonAsync<OptcgSetDetailResponse>(
                    $"{ApiBaseUrl}/v1/sets/{set.Code}", jsonOptions, token);
                if (detail is null)
                {
                    _logger.LogWarning("Set {SetCode} returned no detail; skipping", set.Code);
                    return;
                }

                var rows = detail.Data.Cards
                    .SelectMany(card => card.Variants.Select(v => MapVariant(card, v)))
                    .ToList();

                lock (cardsLock)
                    allCards.AddRange(rows);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch OPTCG set {SetCode}; skipping", set.Code);
            }
            finally
            {
                throttle.Release();
                var done = Interlocked.Increment(ref fetchedSets);
                progress?.Report($"Fetched {done}/{setList.Data.Count} sets...");
            }
        });

        // Dedupe defensively on the variant uid (primary key).
        var deduped = allCards
            .GroupBy(c => c.CardSetId)
            .Select(g => g.Last())
            .ToList();
        _logger.LogInformation("Fetched {Total} variant rows ({Unique} unique)", allCards.Count, deduped.Count);
        progress?.Report($"Fetched {deduped.Count} cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingIds = (await importContext.Cards
            .Select(c => c.CardSetId)
            .ToListAsync(ct))
            .ToHashSet();

        var inserted = 0;
        var updated = 0;

        foreach (var batch in deduped.Chunk(500))
        {
            var newCards = new List<OptcgCard>();
            var existingCardIds = new List<string>();

            foreach (var card in batch)
            {
                if (existingIds.Contains(card.CardSetId))
                    existingCardIds.Add(card.CardSetId);
                else
                    newCards.Add(card);
            }

            // Update all metadata (not just price) for existing rows, preserving
            // computed ImageHash / LocalImagePath.
            if (existingCardIds.Count > 0)
            {
                var tracked = await importContext.Cards
                    .Where(c => existingCardIds.Contains(c.CardSetId))
                    .ToDictionaryAsync(c => c.CardSetId, ct);

                foreach (var card in batch)
                {
                    if (tracked.TryGetValue(card.CardSetId, out var existing))
                    {
                        existing.CardNumber = card.CardNumber;
                        existing.VariantIndex = card.VariantIndex;
                        existing.VariantLabel = card.VariantLabel;
                        existing.Artist = card.Artist;
                        existing.CardName = card.CardName;
                        existing.SetId = card.SetId;
                        existing.SetName = card.SetName;
                        existing.Rarity = card.Rarity;
                        existing.CardColor = card.CardColor;
                        existing.CardType = card.CardType;
                        existing.CardCost = card.CardCost;
                        existing.CardPower = card.CardPower;
                        existing.Life = card.Life;
                        existing.CardText = card.CardText;
                        existing.SubTypes = card.SubTypes;
                        existing.Attribute = card.Attribute;
                        existing.CounterAmount = card.CounterAmount;
                        existing.InventoryPrice = card.InventoryPrice;
                        existing.MarketPrice = card.MarketPrice;
                        existing.CardImageUri = card.CardImageUri;
                        existing.DateScraped = card.DateScraped;
                    }
                }

                await importContext.SaveChangesAsync(ct);
                importContext.ChangeTracker.Clear();
                updated += existingCardIds.Count;
            }

            if (newCards.Count > 0)
            {
                importContext.Cards.AddRange(newCards);
                await importContext.SaveChangesAsync(ct);
                importContext.ChangeTracker.Clear();

                foreach (var card in newCards)
                    existingIds.Add(card.CardSetId);

                inserted += newCards.Count;
            }

            progress?.Report($"Processed {inserted + updated} cards ({inserted} new, {updated} updated)...");
        }

        // Migration complete: stamp the version so future launches skip the wipe.
        importContext.MarkMigrationComplete();

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("OPTCG download complete: {Inserted} new, {Updated} updated in {ElapsedSec:F1}s", inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        if (inserted > 0)
        {
            _logger.LogInformation("Auto-computing hashes for {Count} newly added cards", inserted);
            await ComputeImageHashesAsync(forceAll: false, progress, ct);
        }
    }

    private static OptcgCard MapVariant(OptcgApiCard card, OptcgApiVariant variant)
    {
        var uid = variant.Index == 0 ? card.CardNumber : $"{card.CardNumber}_p{variant.Index}";

        var imageUri = variant.Images.Scan.Display
            ?? variant.Images.Scan.Full
            ?? variant.Images.Stock.Full
            ?? variant.Images.Stock.Thumb;

        return new OptcgCard
        {
            CardSetId = uid,
            CardNumber = card.CardNumber,
            VariantIndex = variant.Index,
            VariantLabel = variant.Label,
            Artist = variant.Artist,
            CardName = card.Name,
            SetId = card.Set,
            SetName = card.SetName,
            Rarity = card.Rarity ?? "",
            CardColor = string.Join("/", card.Color),
            CardType = card.CardType,
            CardCost = card.Cost?.ToString(),
            CardPower = card.Power?.ToString(),
            Life = card.Life?.ToString(),
            CardText = card.Effect,
            SubTypes = card.Types.Count > 0 ? string.Join("/", card.Types) : null,
            Attribute = card.Attribute is { Count: > 0 } ? string.Join("/", card.Attribute) : null,
            CounterAmount = card.Counter,
            MarketPrice = ParsePrice(variant.Market.MarketPrice),
            InventoryPrice = ParsePrice(variant.Market.LowPrice),
            CardImageUri = imageUri,
            DateScraped = DateTime.UtcNow.ToString("o"),
        };
    }

    private static decimal? ParsePrice(string? raw) =>
        decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
```

Add the required using at the top of the file if not present: `using System.Net.Http.Json;` (already present) — no new namespace import needed beyond what exists.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgDownloadTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/OptcgService.cs OmniCard.Tests/Services/OptcgDownloadTests.cs
git commit -m "feat(optcg): download from poneglyph, one row per variant"
```

---

### Task 6: CardMatch — CollectorNumber = printed number

**Files:**
- Modify: `OmniCard.CardMatching/OptcgService.cs` — every `CardMatch { ... }` initializer and the `MissingCard` initializer
- Test: `OmniCard.Tests/Services/OptcgVariantMatchTests.cs` (create)

**Interfaces:**
- Produces: In all `CardMatch` results from `OptcgService`, `CollectorNumber = card.CardNumber` (printed number) while `GameSpecificId = card.CardSetId` (variant uid). `MissingCard.CollectorNumber = card.CardNumber`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/OptcgVariantMatchTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OptcgVariantMatchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgVariantMatchTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-001_p1", CardNumber = "OP01-001", VariantIndex = 1,
            CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn", Rarity = "SEC",
            ImageHash = 0x0UL, MarketPrice = 40m,
        });
        ctx.SaveChanges();
        ctx.MarkMigrationComplete();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        return new OptcgService(new StubHttpClientFactory(), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object, NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public void FindClosestMatch_AltArt_UsesPrintedNumberAndVariantUid()
    {
        var svc = CreateService();
        var match = svc.FindClosestMatch(0x0UL);

        Assert.NotNull(match);
        Assert.Equal("OP01-001", match.CollectorNumber);   // printed number
        Assert.Equal("OP01-001_p1", match.GameSpecificId);  // variant uid
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }
    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgVariantMatchTests`
Expected: FAIL — `CollectorNumber` is currently `card.CardSetId` (`"OP01-001_p1"`), so the printed-number assertion fails.

- [ ] **Step 3: Update CollectorNumber in every result mapping**

In `OmniCard.CardMatching/OptcgService.cs`, in each `CardMatch { ... }` initializer (currently in `FindClosestMatch` final return, `LookupOptcgCard`, `SearchCards`, `GetPrintings`), change:

```csharp
            CollectorNumber = card.CardSetId,
```
to:
```csharp
            CollectorNumber = card.CardNumber,
```

(In `SearchCards`/`GetPrintings` the loop variable is `c`, so use `CollectorNumber = c.CardNumber,`.) Leave `GameSpecificId = card.CardSetId` / `c.CardSetId` unchanged.

In `GetMissingCards`, change the `MissingCard` initializer:
```csharp
                CollectorNumber = c.CardSetId,
```
to:
```csharp
                CollectorNumber = c.CardNumber,
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgVariantMatchTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/OptcgService.cs OmniCard.Tests/Services/OptcgVariantMatchTests.cs
git commit -m "feat(optcg): CardMatch uses printed number, variant uid as id"
```

---

### Task 7: Set completion & missing cards count by printed number

**Files:**
- Modify: `OmniCard.CardMatching/OptcgService.cs` — `GetSetCompletionAsync` (lines ~491-529) and `GetMissingCards` (lines ~531-555)
- Test: `OmniCard.Tests/Services/OptcgVariantCompletionTests.cs` (create)

**Interfaces:**
- Produces: `GetSetCompletionAsync` totals count **distinct `CardNumber`** per set (alt-art rows do not inflate totals). `GetMissingCards` returns one entry per distinct missing `CardNumber`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/OptcgVariantCompletionTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OptcgVariantCompletionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgVariantCompletionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        // Set OP01: two printed numbers, one with an extra alt-art variant (3 rows total).
        ctx.Cards.AddRange(
            new OptcgCard { CardSetId = "OP01-001", CardNumber = "OP01-001", VariantIndex = 0, CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn" },
            new OptcgCard { CardSetId = "OP01-001_p1", CardNumber = "OP01-001", VariantIndex = 1, CardName = "Zoro", SetId = "OP01", SetName = "Romance Dawn" },
            new OptcgCard { CardSetId = "OP01-002", CardNumber = "OP01-002", VariantIndex = 0, CardName = "Nami", SetId = "OP01", SetName = "Romance Dawn" });
        ctx.SaveChanges();
        ctx.MarkMigrationComplete();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        return new OptcgService(new StubHttpClientFactory(), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object, NullLogger<OptcgService>.Instance);
    }

    [Fact]
    public async Task GetSetCompletion_CountsDistinctCardNumbers_NotVariants()
    {
        var svc = CreateService();
        var results = await svc.GetSetCompletionAsync([]);

        var op01 = results.Single(r => r.SetCode == "OP01");
        Assert.Equal(2, op01.TotalCount);  // two printed numbers, not three rows
        Assert.Equal(0, op01.OwnedCount);
    }

    [Fact]
    public void GetMissingCards_ReturnsOneEntryPerPrintedNumber()
    {
        var svc = CreateService();
        var missing = svc.GetMissingCards("OP01", []);

        Assert.Equal(2, missing.Count);
        Assert.Contains(missing, m => m.CollectorNumber == "OP01-001");
        Assert.Contains(missing, m => m.CollectorNumber == "OP01-002");
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options) : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }
    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgVariantCompletionTests`
Expected: FAIL — `TotalCount` is 3 (row count) and `GetMissingCards` returns 3 entries.

- [ ] **Step 3: Count by CardNumber in GetSetCompletionAsync**

In `GetSetCompletionAsync`, replace the `setTotals` query (the `GroupBy(c => new { c.SetId, c.SetName })` block) with one that counts distinct printed numbers:

```csharp
        var setTotals = _readContext.Cards
            .AsNoTracking()
            .Select(c => new { c.SetId, c.SetName, c.CardNumber })
            .Distinct()
            .AsEnumerable()
            .GroupBy(c => new { c.SetId, c.SetName })
            .Select(g => new { g.Key.SetId, g.Key.SetName, Total = g.Count() })
            .ToDictionary(s => s.SetId, s => (s.SetName, s.Total));
```

The `ownedPerSet` calculation is unchanged (it already counts distinct `CollectionCard.Number`, which stores the printed number).

- [ ] **Step 4: Dedupe by CardNumber in GetMissingCards**

In `GetMissingCards`, dedupe rows to one per printed number before projecting. Replace the method body's LINQ chain so it groups by `CardNumber` and takes the base (lowest `VariantIndex`) row:

```csharp
    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers)
    {
        var ownedSet = ownedCollectorNumbers.ToHashSet();

        return _readContext.Cards
            .AsNoTracking()
            .Where(c => c.SetId == setCode)
            .AsEnumerable()
            .Where(c => !ownedSet.Contains(c.CardNumber))
            .GroupBy(c => c.CardNumber)
            .Select(g => g.OrderBy(c => c.VariantIndex).First())
            .Select(c => new MissingCard
            {
                Name = c.CardName,
                CollectorNumber = c.CardNumber,
                SetCode = c.SetId,
                Rarity = c.Rarity,
                ImageUri = c.CardImageUri,
                TypeLine = c.CardType,
                OracleText = c.CardText,
                Power = c.CardPower,
                CardColor = c.CardColor,
                CardCost = c.CardCost,
            })
            .OrderBy(m => m.CollectorNumber)
            .ToList();
    }
```

Note: `ownedCollectorNumbers` now compares against `CardNumber` (the printed number stored in `CollectionCard.Number`).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgVariantCompletionTests`
Expected: PASS (both cases).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.CardMatching/OptcgService.cs OmniCard.Tests/Services/OptcgVariantCompletionTests.cs
git commit -m "feat(optcg): set completion counts distinct printed numbers"
```

---

### Task 8: Full-suite verification

**Files:** none (verification only)

- [ ] **Step 1: Build the solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run the entire test suite**

Run: `dotnet test`
Expected: All tests pass. Pay attention to the pre-existing OPTCG suites (`OptcgServiceTests`, `OptcgCorrectionTests`, `PriceResolutionTests`, `SetCompletionTests`, `SetFilterTests`) — all must remain green, confirming the version stamp from Task 4 prevents the migration wipe from deleting seeded data.

- [ ] **Step 3: Confirm no lingering references to the old API**

Run: `git grep -n "optcgapi.com\|allSetCards"`
Expected: No matches (all references removed).

- [ ] **Step 4: Commit any final cleanup**

If steps 1-3 surface nothing to fix, no commit is needed. Otherwise fix inline and commit:

```bash
git add -A
git commit -m "chore(optcg): finalize poneglyph API swap"
```

---

## Self-Review

**Spec coverage:**
- §1 Data source & flow → Task 5 (set iteration, throttling, UA, version stamp on success).
- §2 Variant identity (uid scheme, new columns, CardMatch mapping) → Tasks 2 (columns), 5 (uid in MapVariant), 6 (CardMatch mapping).
- §3 Field mapping (arrays/ints → strings, image fallback, price parse, InventoryPrice=low_price) → Task 5 (`MapVariant`, `ParsePrice`), verified by Task 5 test assertions.
- §4 Set-completion by CardNumber → Task 7.
- §5 Migration wipe (PRAGMA user_version, constructor wipe, scoped, crash-safe via stamp-on-success) → Tasks 3 (helpers), 4 (constructor wipe), 5 (stamp on success).
- §6 Error handling (per-set skip, null image stored, price parse null) → Task 5 (try/catch per set, image fallback chain, `ParsePrice` returns null).
- §7 Testing (deserialization, flattening, set-completion, migration) → Tasks 1, 5, 7, 4 respectively.
- Out-of-scope endpoints and multi-language correctly omitted.

**Placeholder scan:** No TBD/TODO; every code step contains complete code. Error handling is concrete (specific catch clauses, fallback chains), not "handle errors."

**Type consistency:** DTO names (`OptcgSetDetailResponse`, `OptcgApiCard`, `OptcgApiVariant`, `OptcgApiMarket`) are consistent across Tasks 1 and 5. `MapVariant(OptcgApiCard, OptcgApiVariant)` signature matches its call site. `PoneglyphSchemaVersion`, `GetSchemaVersion()`, `MarkMigrationComplete()` are defined in Task 3 and consumed in Tasks 4 and 5. `CardNumber`/`VariantIndex`/`VariantLabel`/`Artist` defined in Task 2 and used everywhere after. `CollectorNumber = CardNumber` / `GameSpecificId = CardSetId` consistent between Tasks 6 and 7.
