# Pokémon, Yu-Gi-Oh!, and Final Fantasy TCG (TCGCSV-backed) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three new card games — Pokémon, Yu-Gi-Oh!, and Final Fantasy TCG — as full vertical slices sourced entirely from the TCGCSV API, sharing one entity and one abstract service base, leaving Magic/One Piece/Riftbound untouched.

**Architecture:** A shared `TcgCsvCard` entity and an abstract `TcgCsvGameService<TContext>` implement the common TCGCSV catalog-download / image-hashing / price-fetch / matching / query logic once. Each game is a thin subclass supplying a category ID, extended-data mapping, sub-type→price mapping, and an OCR spec. Persistence uses one shared abstract `TcgCsvDbContext` with three concrete per-game subclasses writing separate `.db` files. OCR collector-number detection is config-driven via a single `DetectCollectorNumberAsync(bytes, OcrCollectorSpec)` method.

**Tech Stack:** .NET (C# 12), WPF (desktop), ASP.NET Core Razor Pages + SignalR (web, read-only), EF Core + SQLite, System.Text.Json, Tesseract (OCR), xUnit + Moq (tests).

## Global Constraints

- **Do not modify** `ScryfallService`, `OptcgService`, `RiftboundService`, `RiftboundCard`, `RiftboundDbContext`, or `RiftboundApiModels` — the existing games stay as-is.
- **TCGCSV JSON is camelCase** — always deserialize with `TcgCsvJsonOptions` (`PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, `NumberHandling = JsonNumberHandling.AllowReadingFromString`). Never reuse a snake_case options object.
- **TCGCSV base URL:** `https://tcgcsv.com`. Category IDs: Pokémon `3`, Yu-Gi-Oh! `2`, Final Fantasy TCG `24`.
- **HTTP client** always comes from injected `IHttpClientFactory`; set UA `"OmniCard/1.0"`.
- **Primary key / GameCardId** is the TCGCSV `productId`; `ICardGameService` string ids are `productId.ToString()`.
- **Persist all card data:** promote `CollectorNumber`/`Rarity`/`CardType` to columns AND store the full `extendedData` array as `ExtendedDataJson`.
- **Every desktop UI change has a matching Web change.**
- **SQLite type mapping:** `decimal?`→`TEXT`, `DateTime?`→`TEXT`, `ulong?`→`INTEGER`; additive columns must match `EnsureCreated` exactly.
- **Enum name → display name:** `Pokemon`→"Pokémon", `YuGiOh`→"Yu-Gi-Oh!", `FinalFantasy`→"Final Fantasy TCG". Web option codes: `pokemon`, `yugioh`, `fftcg`.
- **TDD:** write the failing test first, watch it fail, implement minimally, watch it pass, commit. Frequent commits.

---

## File Structure

**New files:**
- `OmniCard.Shared/Models/TcgCsvCard.cs` — shared persistence entity.
- `OmniCard.Shared/Models/TcgCsvApiModels.cs` — camelCase TCGCSV DTOs.
- `OmniCard.Shared/Models/OcrCollectorSpec.cs` — OCR crop/regex config.
- `OmniCard.Data/TcgCsvDbContext.cs` — abstract base context (schema mechanics).
- `OmniCard.Data/PokemonDbContext.cs`, `YugiohDbContext.cs`, `FinalFantasyDbContext.cs` — concrete contexts.
- `OmniCard.CardMatching/TcgCsvGameService.cs` — abstract base service.
- `OmniCard.CardMatching/PokemonService.cs`, `YugiohService.cs`, `FinalFantasyService.cs` — subclasses.
- `OmniCard.Controls/Controls/ExtendedDataView.xaml(.cs)` — desktop key/value card-detail panel.
- `OmniCard.Tests/Services/TcgCsvGameServiceTests.cs` — base behavior via a test subclass.
- `OmniCard.Tests/Services/PokemonServiceTests.cs`, `YugiohServiceTests.cs`, `FinalFantasyServiceTests.cs` — per-game.
- `OmniCard.Tests/Services/TcgCsvOcrTests.cs` — OCR spec/regex extraction.

**Modified files:**
- `OmniCard.Shared/Models/CardGame.cs` — three enum values.
- `OmniCard.Shared/Interfaces/IOcrMatchingService.cs` + `OmniCard.Imaging/OcrMatchingService.cs` — generic detector.
- `OmniCard.Collection/CardService.cs` — scan/rotate/foil arms.
- `OmniCard.CardMatching/CardAttributeExtractor.cs` — color/type arms.
- `OmniCard.Controls/Converters/RootConverters.cs` — two display converters.
- `OmniCard/App.xaml.cs`, `OmniCard.Web/Program.cs` — DI.
- `OmniCard/Views/Dashboard/DashboardView.xaml` — set-code triggers.
- `OmniCard/Views/Root/RootViewModel.cs` — SET-NUM query arm.
- `OmniCard.Web/Pages/Index.cshtml` + `Index.cshtml.cs` — options + filter parse + extended-data rendering.

---

## Task 1: Add enum values

**Files:**
- Modify: `OmniCard.Shared/Models/CardGame.cs`

**Interfaces:**
- Produces: `CardGame.Pokemon`, `CardGame.YuGiOh`, `CardGame.FinalFantasy`.

- [ ] **Step 1: Add the enum members**

Replace the enum body so it reads:

```csharp
namespace OmniCard.Models;

public enum CardGame
{
    Mtg,
    OnePiece,
    Riftbound,
    Pokemon,
    YuGiOh,
    FinalFantasy
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build OmniCard.Shared`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add OmniCard.Shared/Models/CardGame.cs
git commit -m "feat(tcgcsv): add Pokemon, YuGiOh, FinalFantasy enum values"
```

---

## Task 2: Shared entity + TCGCSV DTOs

**Files:**
- Create: `OmniCard.Shared/Models/TcgCsvCard.cs`
- Create: `OmniCard.Shared/Models/TcgCsvApiModels.cs`
- Test: `OmniCard.Tests/Services/TcgCsvGameServiceTests.cs` (DTO deserialization test only in this task)

**Interfaces:**
- Produces:
  - `TcgCsvCard` with props: `int ProductId`, `CardGame Game`, `string Name`, `string? CleanName`, `int GroupId`, `string SetCode`, `string SetName`, `string CollectorNumber`, `string Rarity`, `string CardType`, `string? ImageUrl`, `string? Url`, `string? ExtendedDataJson`, `ulong? ImageHash`, `ulong? EdgeHash`, `string? LocalImagePath`, `decimal? MarketPrice`, `decimal? FoilMarketPrice`, `DateTime? PriceUpdatedAt`.
  - `TcgCsvGroupsResponse { List<TcgCsvGroup> Results }`, `TcgCsvGroup { int GroupId; string Name; string? Abbreviation }`.
  - `TcgCsvProductsResponse { List<TcgCsvProduct> Results }`, `TcgCsvProduct { int ProductId; string Name; string? CleanName; string? ImageUrl; int GroupId; string? Url; List<TcgCsvExtendedData> ExtendedData }`, `TcgCsvExtendedData { string Name; string? DisplayName; string Value }`.
  - `TcgCsvPricesResponse { List<TcgCsvPrice> Results }`, `TcgCsvPrice { int ProductId; decimal? MarketPrice; string? SubTypeName }`.

- [ ] **Step 1: Write the entity**

Create `OmniCard.Shared/Models/TcgCsvCard.cs`:

```csharp
namespace OmniCard.Models;

// Shared persistence entity for all TCGCSV-backed games (Pokémon, Yu-Gi-Oh!, Final Fantasy TCG).
// One row per printing (per TCGplayer productId). Populated by TcgCsvGameService subclasses.
public class TcgCsvCard
{
    // TCGplayer productId — unique per printing. Primary key. Exposed as GameCardId (ToString()).
    public int ProductId { get; set; }

    public CardGame Game { get; set; }

    public string Name { get; set; } = "";
    public string? CleanName { get; set; }

    public int GroupId { get; set; }              // TCGCSV group (set) id, used for API fetches
    public string SetCode { get; set; } = "";      // group abbreviation, or GroupId as string when blank
    public string SetName { get; set; } = "";

    public string CollectorNumber { get; set; } = ""; // extendedData "Number" (e.g. "123/198", "1-001H")
    public string Rarity { get; set; } = "";
    public string CardType { get; set; } = "";

    public string? ImageUrl { get; set; }
    public string? Url { get; set; }

    // Full extendedData array serialized verbatim as JSON — retains every game-specific attribute.
    public string? ExtendedDataJson { get; set; }

    // Computed locally, not from API.
    public ulong? ImageHash { get; set; }
    public ulong? EdgeHash { get; set; }
    public string? LocalImagePath { get; set; }

    // Pricing — populated from TCGCSV prices, keyed by ProductId.
    public decimal? MarketPrice { get; set; }        // "Normal" subtype market price
    public decimal? FoilMarketPrice { get; set; }    // game's principal foil subtype market price
    public DateTime? PriceUpdatedAt { get; set; }     // UTC
}
```

- [ ] **Step 2: Write the DTOs**

Create `OmniCard.Shared/Models/TcgCsvApiModels.cs`:

```csharp
namespace OmniCard.Models;

// Response DTOs for tcgcsv.com. TCGCSV returns camelCase JSON — deserialize with a
// CamelCase JsonNamingPolicy. Envelope shape: { success, errors, results[] }.

public sealed class TcgCsvGroupsResponse
{
    public List<TcgCsvGroup> Results { get; set; } = [];
}

public sealed class TcgCsvGroup
{
    public int GroupId { get; set; }
    public string Name { get; set; } = "";
    public string? Abbreviation { get; set; }
}

public sealed class TcgCsvProductsResponse
{
    public List<TcgCsvProduct> Results { get; set; } = [];
}

public sealed class TcgCsvProduct
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string? CleanName { get; set; }
    public string? ImageUrl { get; set; }
    public int GroupId { get; set; }
    public string? Url { get; set; }
    public List<TcgCsvExtendedData> ExtendedData { get; set; } = [];
}

public sealed class TcgCsvExtendedData
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Value { get; set; } = "";
}

public sealed class TcgCsvPricesResponse
{
    public List<TcgCsvPrice> Results { get; set; } = [];
}

public sealed class TcgCsvPrice
{
    public int ProductId { get; set; }
    public decimal? MarketPrice { get; set; }
    public string? SubTypeName { get; set; }
}
```

- [ ] **Step 3: Write the failing DTO deserialization test**

Create `OmniCard.Tests/Services/TcgCsvGameServiceTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TcgCsvGameServiceTests
{
    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    [Fact]
    public void Product_Deserializes_WithExtendedData()
    {
        const string json = """
        {"results":[{"productId":132375,"name":"Auron (Hero)","cleanName":"Auron Hero",
          "imageUrl":"https://cdn/132375_200w.jpg","groupId":1939,
          "url":"https://tcgplayer.com/132375",
          "extendedData":[
            {"name":"Rarity","displayName":"Rarity","value":"Hero"},
            {"name":"Number","displayName":"Number","value":"1-001H"},
            {"name":"CardType","displayName":"Card Type","value":"Forward"}]}],
          "success":true,"errors":[]}
        """;

        var resp = JsonSerializer.Deserialize<TcgCsvProductsResponse>(json, Camel);

        Assert.NotNull(resp);
        var p = Assert.Single(resp!.Results);
        Assert.Equal(132375, p.ProductId);
        Assert.Equal("Auron (Hero)", p.Name);
        Assert.Equal(1939, p.GroupId);
        Assert.Equal(3, p.ExtendedData.Count);
        Assert.Equal("1-001H", p.ExtendedData.Single(e => e.Name == "Number").Value);
    }

    [Fact]
    public void Prices_Deserialize_WithSubTypeNames()
    {
        const string json = """
        {"results":[
          {"productId":1,"marketPrice":1.50,"subTypeName":"Normal"},
          {"productId":1,"marketPrice":3.00,"subTypeName":"Holofoil"}],
          "success":true,"errors":[]}
        """;

        var resp = JsonSerializer.Deserialize<TcgCsvPricesResponse>(json, Camel);

        Assert.Equal(2, resp!.Results.Count);
        Assert.Equal("Holofoil", resp.Results[1].SubTypeName);
        Assert.Equal(3.00m, resp.Results[1].MarketPrice);
    }
}
```

- [ ] **Step 4: Run the tests — expect failure first, then pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvGameServiceTests`
Expected: FAILS to compile until Steps 1-2 are saved; once saved, PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Shared/Models/TcgCsvCard.cs OmniCard.Shared/Models/TcgCsvApiModels.cs OmniCard.Tests/Services/TcgCsvGameServiceTests.cs
git commit -m "feat(tcgcsv): shared TcgCsvCard entity and camelCase DTOs"
```

---

## Task 3: DbContext base + concrete contexts

**Files:**
- Create: `OmniCard.Data/TcgCsvDbContext.cs`
- Create: `OmniCard.Data/PokemonDbContext.cs`
- Create: `OmniCard.Data/YugiohDbContext.cs`
- Create: `OmniCard.Data/FinalFantasyDbContext.cs`
- Test: `OmniCard.Tests/Services/TcgCsvSchemaTests.cs`

**Interfaces:**
- Consumes: `TcgCsvCard` (Task 2), `HashCorrection` (existing shared entity).
- Produces:
  - `abstract class TcgCsvDbContext : DbContext` with `DbSet<TcgCsvCard> Cards`, `DbSet<HashCorrection> HashCorrections`, `const int TcgCsvSchemaVersion = 1`, `int GetSchemaVersion()`, `void MarkMigrationComplete()`, `void ApplySchemaUpgrades()`; protected ctor `TcgCsvDbContext(DbContextOptions options)`.
  - `class PokemonDbContext : TcgCsvDbContext` (ctor takes `DbContextOptions<PokemonDbContext>`), and likewise `YugiohDbContext`, `FinalFantasyDbContext`.

- [ ] **Step 1: Write the abstract context**

Create `OmniCard.Data/TcgCsvDbContext.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

// Shared abstract context for all TCGCSV-backed games. Concrete per-game subclasses
// (PokemonDbContext etc.) exist only to give EF distinct types → distinct .db files.
public abstract class TcgCsvDbContext : DbContext
{
    public DbSet<TcgCsvCard> Cards => Set<TcgCsvCard>();
    public DbSet<HashCorrection> HashCorrections => Set<HashCorrection>();

    protected TcgCsvDbContext(DbContextOptions options) : base(options) { }

    // Bump when the on-disk schema/data source changes incompatibly; a stored user_version
    // below this triggers a wipe-and-redownload in TcgCsvGameService.
    public const int TcgCsvSchemaVersion = 1;

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
        cmd.CommandText = $"PRAGMA user_version = {TcgCsvSchemaVersion};";
        cmd.ExecuteNonQuery();
    }

    public void ApplySchemaUpgrades()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        // Additive columns for forward-compatibility (idempotent; safe on read-only DBs).
        AddColumnIfMissing(conn, "EdgeHash INTEGER");
        AddColumnIfMissing(conn, "LocalImagePath TEXT");
        AddColumnIfMissing(conn, "ExtendedDataJson TEXT");
        AddColumnIfMissing(conn, "MarketPrice TEXT");
        AddColumnIfMissing(conn, "FoilMarketPrice TEXT");
        AddColumnIfMissing(conn, "PriceUpdatedAt TEXT");
    }

    private static void AddColumnIfMissing(System.Data.Common.DbConnection conn, string columnDef)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE Cards ADD COLUMN {columnDef}";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name") || ex.Message.Contains("readonly"))
        {
            // Column already exists, or the DB is read-only (Web app hitting a not-yet-migrated DB).
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var card = modelBuilder.Entity<TcgCsvCard>();
        card.HasKey(c => c.ProductId);
        card.Property(c => c.ProductId).ValueGeneratedNever();
        card.HasIndex(c => c.Name);
        card.HasIndex(c => c.SetCode);
        card.HasIndex(c => c.CollectorNumber);
        card.HasIndex(c => c.ImageHash);
        card.HasIndex(c => c.EdgeHash);
        card.Property(c => c.PriceUpdatedAt)
            .HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        modelBuilder.Entity<HashCorrection>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedOnAdd();
            e.HasIndex(h => h.ScanHash).IsUnique();
        });
    }
}
```

- [ ] **Step 2: Write the three concrete contexts**

Create `OmniCard.Data/PokemonDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data;

public class PokemonDbContext : TcgCsvDbContext
{
    public PokemonDbContext(DbContextOptions<PokemonDbContext> options) : base(options) { }
}
```

Create `OmniCard.Data/YugiohDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data;

public class YugiohDbContext : TcgCsvDbContext
{
    public YugiohDbContext(DbContextOptions<YugiohDbContext> options) : base(options) { }
}
```

Create `OmniCard.Data/FinalFantasyDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data;

public class FinalFantasyDbContext : TcgCsvDbContext
{
    public FinalFantasyDbContext(DbContextOptions<FinalFantasyDbContext> options) : base(options) { }
}
```

- [ ] **Step 3: Write the failing schema test**

Create `OmniCard.Tests/Services/TcgCsvSchemaTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TcgCsvSchemaTests
{
    [Fact]
    public void EnsureCreated_ThenApplySchemaUpgrades_IsIdempotent()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(conn).Options;

        using var ctx = new PokemonDbContext(options);
        ctx.Database.EnsureCreated();
        ctx.ApplySchemaUpgrades();   // must not throw on already-present columns
        ctx.ApplySchemaUpgrades();   // second call also fine
        ctx.MarkMigrationComplete();

        Assert.Equal(TcgCsvDbContext.TcgCsvSchemaVersion, ctx.GetSchemaVersion());

        ctx.Cards.Add(new TcgCsvCard { ProductId = 1, Game = CardGame.Pokemon, Name = "Pikachu", ExtendedDataJson = "[]" });
        ctx.SaveChanges();
        Assert.Equal("Pikachu", ctx.Cards.Single(c => c.ProductId == 1).Name);
    }
}
```

- [ ] **Step 4: Run — expect fail then pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvSchemaTests`
Expected: PASS (1 test) once Steps 1-2 are saved.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Data/TcgCsvDbContext.cs OmniCard.Data/PokemonDbContext.cs OmniCard.Data/YugiohDbContext.cs OmniCard.Data/FinalFantasyDbContext.cs OmniCard.Tests/Services/TcgCsvSchemaTests.cs
git commit -m "feat(tcgcsv): shared DbContext base + Pokemon/Yugioh/FFTCG contexts"
```

---

## Task 4: Base service — download + catalog mapping

**Files:**
- Create: `OmniCard.CardMatching/TcgCsvGameService.cs` (base skeleton + download in this task; later tasks add methods)
- Test: `OmniCard.Tests/Services/TcgCsvGameServiceTests.cs` (append download tests + a `TestTcgCsvService` subclass)

**Interfaces:**
- Consumes: `TcgCsvCard`, `TcgCsvGroupsResponse`, `TcgCsvProductsResponse` (Task 2); `TcgCsvDbContext`, `PokemonDbContext` (Task 3); `IHttpClientFactory`, `IPerceptualHashService`, `IDataPathService`, `ILogger`.
- Produces: `abstract class TcgCsvGameService<TContext> : ICardGameService, IDisposable where TContext : TcgCsvDbContext`, with abstract members `int CategoryId`, `CardGame Game`, `string GameKey`, `void MapExtendedData(TcgCsvProduct, TcgCsvCard)`, `(decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice>)`; and `Task DownloadBulkDataAsync(...)`. Protected members `_readContext`, `_dbContextFactory`, `_httpClientFactory`, `_hashService`, `_dataDirectory`, `_logger`, and `TcgCsvBaseUrl`/`TcgCsvJsonOptions`.

- [ ] **Step 1: Write the failing download test + test subclass**

Append to `OmniCard.Tests/Services/TcgCsvGameServiceTests.cs` (add the `using`s at the top of the file: `System.Net`, `System.Text`, `Microsoft.Data.Sqlite`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Logging.Abstractions`, `OmniCard.CardMatching`, `OmniCard.Data`, `OmniCard.Imaging`, `OmniCard.Interfaces`):

```csharp
public class TcgCsvDownloadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<PokemonDbContext> _factory;
    private readonly string _dataDir;

    public TcgCsvDownloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(_connection).Options;
        _factory = new TestFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();
        _dataDir = Path.Combine(Path.GetTempPath(), "tcgcsv-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private const string GroupsJson = """
    {"results":[{"groupId":1939,"name":"Opus I","abbreviation":"OP1"}],"success":true,"errors":[]}
    """;

    private const string ProductsJson = """
    {"results":[
      {"productId":132375,"name":"Auron (Hero)","cleanName":"Auron Hero","imageUrl":"https://cdn/132375_200w.jpg","groupId":1939,"url":"https://tcg/132375",
        "extendedData":[{"name":"Number","displayName":"Number","value":"1-001H"},{"name":"Rarity","displayName":"Rarity","value":"Hero"},{"name":"CardType","displayName":"Card Type","value":"Forward"},{"name":"Element","displayName":"Element","value":"Fire"}]},
      {"productId":132376,"name":"Auron (Rare)","cleanName":"Auron Rare","imageUrl":"https://cdn/132376_200w.jpg","groupId":1939,"url":"https://tcg/132376",
        "extendedData":[{"name":"Number","displayName":"Number","value":"1-002R"},{"name":"Rarity","displayName":"Rarity","value":"Rare"},{"name":"CardType","displayName":"Card Type","value":"Forward"}]}
    ],"success":true,"errors":[]}
    """;

    private const string PricesJson = """
    {"results":[
      {"productId":132375,"marketPrice":1.50,"subTypeName":"Normal"},
      {"productId":132375,"marketPrice":3.00,"subTypeName":"Holofoil"},
      {"productId":132376,"marketPrice":0.12,"subTypeName":"Reverse Holofoil"}
    ],"success":true,"errors":[]}
    """;

    private TestTcgCsvService CreateService() => CreateService(uri =>
    {
        if (uri.Contains("/groups")) return GroupsJson;
        if (uri.Contains("/products")) return ProductsJson;
        if (uri.Contains("/prices")) return PricesJson;
        return null;
    });

    private TestTcgCsvService CreateService(Func<string, string?> route)
    {
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        return new TestTcgCsvService(
            new FakeHttpClientFactory(new RoutingHandler(route)),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object,
            NullLogger<TestTcgCsvService>.Instance);
    }

    [Fact]
    public async Task DownloadBulkData_MapsProducts_AndStoresExtendedDataJson()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var rows = ctx.Cards.OrderBy(c => c.ProductId).ToList();
        Assert.Equal(2, rows.Count);

        var hero = rows.Single(c => c.ProductId == 132375);
        Assert.Equal("Auron (Hero)", hero.Name);
        Assert.Equal(1939, hero.GroupId);
        Assert.Equal("OP1", hero.SetCode);
        Assert.Equal("Opus I", hero.SetName);
        Assert.Equal("1-001H", hero.CollectorNumber);
        Assert.Equal("Hero", hero.Rarity);
        Assert.Equal("Forward", hero.CardType);
        Assert.Equal(CardGame.Pokemon, hero.Game); // TestTcgCsvService reports Pokemon
        Assert.Contains("Element", hero.ExtendedDataJson);   // full extendedData retained
        Assert.Contains("Fire", hero.ExtendedDataJson);
    }

    [Fact]
    public async Task DownloadBulkData_ExistingRow_RefreshesMetadata_PreservesHashAndPrice()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.Add(new TcgCsvCard { ProductId = 132375, Game = CardGame.Pokemon, Name = "Stale",
                CollectorNumber = "999", ImageHash = 12345UL, LocalImagePath = "pokemon-art/132375.png", MarketPrice = 7.77m });
            seed.SaveChanges();
        }
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var row = ctx.Cards.Single(c => c.ProductId == 132375);
        Assert.Equal("Auron (Hero)", row.Name);         // refreshed
        Assert.Equal("1-001H", row.CollectorNumber);
        Assert.Equal(12345UL, row.ImageHash);           // preserved
        Assert.Equal("pokemon-art/132375.png", row.LocalImagePath);
        Assert.Equal(7.77m, row.MarketPrice);           // price not clobbered by catalog re-download
    }
}
```

Also append the shared harness + test subclass at the end of the file (inside the namespace):

```csharp
file class RoutingHandler(Func<string, string?> route) : System.Net.Http.HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = route(request.RequestUri!.ToString());
        var resp = body is null
            ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        return Task.FromResult(resp);
    }
}

file class FakeHttpClientFactory(System.Net.Http.HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}

file class TestFactory(Microsoft.EntityFrameworkCore.DbContextOptions<PokemonDbContext> options)
    : Microsoft.EntityFrameworkCore.IDbContextFactory<PokemonDbContext>
{
    public PokemonDbContext CreateDbContext() => new(options);
}

// Minimal concrete subclass exercising the base service against the Pokemon context.
file class TestTcgCsvService : TcgCsvGameService<PokemonDbContext>
{
    public TestTcgCsvService(IHttpClientFactory h, Microsoft.EntityFrameworkCore.IDbContextFactory<PokemonDbContext> f,
        IPerceptualHashService ph, IDataPathService dp, Microsoft.Extensions.Logging.ILogger l) : base(h, f, ph, dp, l) { }

    protected override int CategoryId => 3;
    public override CardGame Game => CardGame.Pokemon;
    protected override string GameKey => "pokemon";

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows)
    {
        decimal? Price(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        return (Price("Normal"), Price("Holofoil") ?? Price("Reverse Holofoil"));
    }
}
```

> Note: `file`-scoped helper classes keep this test file's harness from colliding with the identically-named ones in `RiftboundDownloadTests.cs`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvDownloadTests`
Expected: FAIL to compile — `TcgCsvGameService` does not exist.

- [ ] **Step 3: Write the base service (skeleton + download)**

Create `OmniCard.CardMatching/TcgCsvGameService.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.CardMatching;

// Abstract base for all TCGCSV-backed games. Concrete games subclass this, supplying a
// category id, extended-data mapping, and sub-type→price mapping. Catalog download, image
// hashing, price refresh, matching, and queries live here — implemented once.
public abstract class TcgCsvGameService<TContext> : ICardGameService, IDisposable
    where TContext : TcgCsvDbContext
{
    protected const string TcgCsvBaseUrl = "https://tcgcsv.com";
    private const int CorrectionTrustBonus = 5;

    protected readonly IHttpClientFactory _httpClientFactory;
    protected readonly IDbContextFactory<TContext> _dbContextFactory;
    protected readonly IPerceptualHashService _hashService;
    protected readonly ILogger _logger;
    protected readonly string _dataDirectory;
    protected TContext _readContext;

    private List<(int Id, ulong Hash)>? _hashCache;
    private List<(int Id, ulong EdgeHash, string SetCode)>? _edgeHashCache;
    private Dictionary<int, string>? _hashSetLookup;
    private List<(ulong ScanHash, string CorrectCardId)>? _correctionsCache;

    // TCGCSV returns camelCase JSON.
    protected static readonly JsonSerializerOptions TcgCsvJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // === Per-game hooks ===
    protected abstract int CategoryId { get; }
    public abstract CardGame Game { get; }
    protected abstract string GameKey { get; }   // art-dir prefix, e.g. "pokemon"

    // Fold a product's price rows into (normal, foil). Games differ in sub-type naming.
    protected abstract (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> productPriceRows);

    // Promote game-specific extendedData into columns. Default reads "Number"/"Rarity"/"CardType";
    // override when a game uses different keys.
    protected virtual void MapExtendedData(TcgCsvProduct product, TcgCsvCard card)
    {
        string? Val(string name) => product.ExtendedData
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
        card.CollectorNumber = Val("Number") ?? "";
        card.Rarity = Val("Rarity") ?? "";
        card.CardType = Val("CardType") ?? Val("Card Type") ?? "";
    }

    // TCGCSV product images default to _200w; upgrade for usable perceptual hashing.
    protected virtual string? UpgradeImageUrl(string? url)
        => url is null ? null : url.Replace("_200w.", "_400w.");

    protected TcgCsvGameService(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<TContext> dbContextFactory,
        IPerceptualHashService hashService,
        IDataPathService dataPathService,
        ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _hashService = hashService;
        _dataDirectory = dataPathService.DataDirectory;
        _logger = logger;

        _readContext = _dbContextFactory.CreateDbContext();
        var dbPath = _readContext.Database.GetConnectionString();
        if (dbPath is not null)
        {
            var dataSource = dbPath.Replace("Data Source=", "");
            var dir = Path.GetDirectoryName(dataSource.Replace(";Mode=ReadOnly", ""));
            if (dir is not null && dir.Length > 0)
                Directory.CreateDirectory(dir);
        }
        _readContext.Database.EnsureCreated();
        _readContext.ApplySchemaUpgrades();

        if (_readContext.GetSchemaVersion() < TcgCsvDbContext.TcgCsvSchemaVersion)
        {
            _logger.LogWarning("{Game} database predates current schema; wiping for migration", Game);
            WipeForMigration();
        }
        _logger.LogInformation("{Game} database ready at {DbPath}", Game, dbPath);
    }

    private void WipeForMigration()
    {
        try
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            ctx.Database.ExecuteSqlRaw("DELETE FROM Cards");
            ctx.Database.ExecuteSqlRaw("DELETE FROM HashCorrections");
        }
        catch (SqliteException ex) when (ex.Message.Contains("readonly"))
        {
            _logger.LogWarning(ex, "{Game} database is read-only; skipping migration wipe", Game);
        }

        var artDir = Path.Combine(_dataDirectory, $"{GameKey}-art");
        if (Directory.Exists(artDir))
        {
            try { Directory.Delete(artDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete {Game} art directory during migration wipe", Game);
            }
        }

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null; _edgeHashCache = null; _hashSetLookup = null; _correctionsCache = null;
        oldContext.Dispose();
    }

    public MatchDiagnostics? LastMatchDiagnostics { get; private set; }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");
        return client;
    }

    // === Download ===
    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting {Game} card data download from TCGCSV", Game);
        var sw = Stopwatch.StartNew();
        var client = CreateClient();

        progress?.Report($"Fetching {Game} set list...");
        var groups = await client.GetFromJsonAsync<TcgCsvGroupsResponse>(
            $"{TcgCsvBaseUrl}/tcgplayer/{CategoryId}/groups", TcgCsvJsonOptions, ct);
        var groupList = groups?.Results ?? [];
        _logger.LogInformation("Discovered {Count} {Game} groups", groupList.Count, Game);

        var allCards = new List<TcgCsvCard>();
        var cardsLock = new object();
        var done = 0;

        await Parallel.ForEachAsync(groupList, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (group, token) =>
            {
                try
                {
                    var products = await client.GetFromJsonAsync<TcgCsvProductsResponse>(
                        $"{TcgCsvBaseUrl}/tcgplayer/{CategoryId}/{group.GroupId}/products", TcgCsvJsonOptions, token);
                    var setCode = string.IsNullOrWhiteSpace(group.Abbreviation) ? group.GroupId.ToString() : group.Abbreviation!;
                    var rows = (products?.Results ?? []).Select(p => MapProduct(p, setCode, group.Name)).ToList();
                    lock (cardsLock) allCards.AddRange(rows);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to fetch {Game} group {GroupId}; skipping", Game, group.GroupId);
                }
                finally
                {
                    var d = Interlocked.Increment(ref done);
                    progress?.Report($"Fetched {d}/{groupList.Count} sets...");
                }
            });

        var deduped = allCards.GroupBy(c => c.ProductId).Select(g => g.Last()).ToList();
        progress?.Report($"Fetched {deduped.Count} cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingIds = (await importContext.Cards.Select(c => c.ProductId).ToListAsync(ct)).ToHashSet();
        var inserted = 0; var updated = 0;

        foreach (var batch in deduped.Chunk(500))
        {
            var newCards = new List<TcgCsvCard>();
            var existingCardIds = batch.Where(c => existingIds.Contains(c.ProductId)).Select(c => c.ProductId).ToList();
            foreach (var c in batch) if (!existingIds.Contains(c.ProductId)) newCards.Add(c);

            if (existingCardIds.Count > 0)
            {
                var tracked = await importContext.Cards.Where(c => existingCardIds.Contains(c.ProductId))
                    .ToDictionaryAsync(c => c.ProductId, ct);
                foreach (var c in batch)
                {
                    if (tracked.TryGetValue(c.ProductId, out var existing))
                    {
                        // Refresh catalog fields; preserve computed hashes/paths and prices.
                        existing.Game = c.Game;
                        existing.Name = c.Name;
                        existing.CleanName = c.CleanName;
                        existing.GroupId = c.GroupId;
                        existing.SetCode = c.SetCode;
                        existing.SetName = c.SetName;
                        existing.CollectorNumber = c.CollectorNumber;
                        existing.Rarity = c.Rarity;
                        existing.CardType = c.CardType;
                        existing.ImageUrl = c.ImageUrl;
                        existing.Url = c.Url;
                        existing.ExtendedDataJson = c.ExtendedDataJson;
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
                foreach (var c in newCards) existingIds.Add(c.ProductId);
                inserted += newCards.Count;
            }
            progress?.Report($"Processed {inserted + updated} cards ({inserted} new, {updated} updated)...");
        }

        if (deduped.Count > 0) importContext.MarkMigrationComplete();

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null; _edgeHashCache = null; _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("{Game} download complete: {Ins} new, {Upd} updated in {Sec:F1}s", Game, inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        if (inserted > 0) await ComputeImageHashesAsync(forceAll: false, progress, ct);
    }

    protected TcgCsvCard MapProduct(TcgCsvProduct p, string setCode, string setName)
    {
        var card = new TcgCsvCard
        {
            ProductId = p.ProductId,
            Game = Game,
            Name = p.Name,
            CleanName = p.CleanName,
            GroupId = p.GroupId,
            SetCode = setCode,
            SetName = setName,
            ImageUrl = p.ImageUrl,
            Url = p.Url,
            ExtendedDataJson = JsonSerializer.Serialize(p.ExtendedData, TcgCsvJsonOptions),
        };
        MapExtendedData(p, card);
        return card;
    }

    public void Dispose() => _readContext.Dispose();

    // Methods added in Tasks 5-8:
    public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => throw new NotImplementedException();
    public List<CardMatch> SearchCards(string query, int maxResults = 20) => throw new NotImplementedException();
    public List<CardMatch> GetPrintings(string cardName) => throw new NotImplementedException();
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => throw new NotImplementedException();
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => throw new NotImplementedException();
    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) => throw new NotImplementedException();
    public IReadOnlyList<SetInfo> GetAvailableSets() => throw new NotImplementedException();
    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => throw new NotImplementedException();
    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => throw new NotImplementedException();
    public object? FindCardById(string gameCardId) => throw new NotImplementedException();
}
```

> The `NotImplementedException` stubs let the base compile and satisfy `ICardGameService` now; Tasks 5-8 replace each in place. `CorrectionTrustBonus` is referenced in Task 7.

- [ ] **Step 4: Run to verify the download tests pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvDownloadTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/TcgCsvGameService.cs OmniCard.Tests/Services/TcgCsvGameServiceTests.cs
git commit -m "feat(tcgcsv): base service skeleton + catalog download/mapping"
```

---

## Task 5: Base service — price refresh

**Files:**
- Modify: `OmniCard.CardMatching/TcgCsvGameService.cs` (replace `UpdatePricesAsync` stub; add `FetchTcgCsvPriceMapAsync`)
- Test: `OmniCard.Tests/Services/TcgCsvGameServiceTests.cs` (append to `TcgCsvDownloadTests`)

**Interfaces:**
- Consumes: `MapSubtypePrices` hook, `TcgCsvPrice`, `PriceUpdateProgress`.
- Produces: working `UpdatePricesAsync`; private `Task<Dictionary<int,(decimal? Normal, decimal? Foil)>> FetchTcgCsvPriceMapAsync(HttpClient, IProgress<PriceUpdateProgress>?, CancellationToken)`.

- [ ] **Step 1: Write the failing price tests**

Append to `TcgCsvDownloadTests`:

```csharp
    [Fact]
    public async Task UpdatePrices_WritesMarketPrices_FoilFromHolofoil()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new TcgCsvCard { ProductId = 132375, Game = CardGame.Pokemon, Name = "Auron (Hero)" },
                new TcgCsvCard { ProductId = 132376, Game = CardGame.Pokemon, Name = "Auron (Rare)" });
            seed.SaveChanges();
        }
        var svc = CreateService();
        await svc.UpdatePricesAsync();

        using var ctx = _factory.CreateDbContext();
        var hero = ctx.Cards.Single(c => c.ProductId == 132375);
        Assert.Equal(1.50m, hero.MarketPrice);           // Normal
        Assert.Equal(3.00m, hero.FoilMarketPrice);       // Holofoil
        Assert.NotNull(hero.PriceUpdatedAt);

        var rare = ctx.Cards.Single(c => c.ProductId == 132376);
        Assert.Null(rare.MarketPrice);                   // no Normal row
        Assert.Equal(0.12m, rare.FoilMarketPrice);       // Reverse Holofoil fallback
    }

    [Fact]
    public async Task UpdatePrices_OneGroupFails_StillAppliesOtherGroupPrices()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.Add(new TcgCsvCard { ProductId = 700001, Game = CardGame.Pokemon, Name = "GoodCard", SetCode = "GOOD" });
            seed.SaveChanges();
        }
        var svc = CreateService(uri =>
        {
            if (uri.Contains("/groups")) return """{"results":[{"groupId":1,"name":"Bad"},{"groupId":2,"name":"Good"}],"success":true,"errors":[]}""";
            if (uri.Contains("/3/1/prices")) return null;  // group 1 fails
            if (uri.Contains("/3/2/prices")) return """{"results":[{"productId":700001,"marketPrice":5.25,"subTypeName":"Normal"}],"success":true,"errors":[]}""";
            return null;
        });
        await svc.UpdatePricesAsync();

        using var ctx = _factory.CreateDbContext();
        Assert.Equal(5.25m, ctx.Cards.Single(c => c.ProductId == 700001).MarketPrice);
    }

    [Fact]
    public async Task UpdatePrices_EmptyDb_NoThrow()
    {
        var svc = CreateService();
        await svc.UpdatePricesAsync();   // no cards seeded; should bail quietly
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~UpdatePrices`
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Replace the `UpdatePricesAsync` stub and add the fetch helper**

In `TcgCsvGameService.cs`, replace the `UpdatePricesAsync` stub line with:

```csharp
    public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        await using (var ctx = _dbContextFactory.CreateDbContext())
        {
            if (!await ctx.Cards.AnyAsync(ct))
            {
                _logger.LogInformation("Skipping {Game} price refresh: card database is empty", Game);
                return;
            }
        }

        _logger.LogInformation("Starting {Game} price refresh via TCGCSV", Game);
        var client = CreateClient();
        var priceMap = await FetchTcgCsvPriceMapAsync(client, progress, ct);

        await using var context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();
        var now = DateTime.UtcNow;
        var updated = 0;

        var targetIds = (await context.Cards.Select(c => c.ProductId).ToListAsync(ct))
            .Where(priceMap.ContainsKey).ToList();

        foreach (var batch in targetIds.Chunk(500))
        {
            var tracked = await context.Cards.Where(c => batch.Contains(c.ProductId))
                .ToDictionaryAsync(c => c.ProductId, ct);
            foreach (var pid in batch)
            {
                if (tracked.TryGetValue(pid, out var existing) && priceMap.TryGetValue(pid, out var prices))
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

        _logger.LogInformation("{Game} price refresh complete: {Updated} cards updated", Game, updated);
        progress?.Report(new PriceUpdateProgress(Game, null, 0, 0, $"{Game} prices updated ({updated} cards)"));
    }

    private async Task<Dictionary<int, (decimal? Normal, decimal? Foil)>> FetchTcgCsvPriceMapAsync(
        HttpClient client, IProgress<PriceUpdateProgress>? progress, CancellationToken ct)
    {
        var groups = await client.GetFromJsonAsync<TcgCsvGroupsResponse>(
            $"{TcgCsvBaseUrl}/tcgplayer/{CategoryId}/groups", TcgCsvJsonOptions, ct);
        var groupList = groups?.Results ?? [];
        var rowsByProduct = new Dictionary<int, List<TcgCsvPrice>>();
        var done = 0;

        foreach (var group in groupList)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var prices = await client.GetFromJsonAsync<TcgCsvPricesResponse>(
                    $"{TcgCsvBaseUrl}/tcgplayer/{CategoryId}/{group.GroupId}/prices", TcgCsvJsonOptions, ct);
                foreach (var row in prices?.Results ?? [])
                {
                    if (!rowsByProduct.TryGetValue(row.ProductId, out var list))
                        rowsByProduct[row.ProductId] = list = [];
                    list.Add(row);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch {Game} prices for group {GroupId}; skipping", Game, group.GroupId);
            }
            done++;
            progress?.Report(new PriceUpdateProgress(Game, null, done, groupList.Count, $"{Game} prices: {done}/{groupList.Count} groups"));
        }

        var map = new Dictionary<int, (decimal? Normal, decimal? Foil)>(rowsByProduct.Count);
        foreach (var (pid, rows) in rowsByProduct) map[pid] = MapSubtypePrices(rows);
        return map;
    }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~UpdatePrices`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/TcgCsvGameService.cs OmniCard.Tests/Services/TcgCsvGameServiceTests.cs
git commit -m "feat(tcgcsv): base price refresh with per-group resilience"
```

---

## Task 6: Base service — image hashing

**Files:**
- Modify: `OmniCard.CardMatching/TcgCsvGameService.cs` (replace `ComputeImageHashesAsync` stub; add art-path + save-batch helpers)
- Test: `OmniCard.Tests/Services/TcgCsvGameServiceTests.cs` (append)

**Interfaces:**
- Produces: working `ComputeImageHashesAsync`; helpers `string GetLocalArtRelativePath(int)`, `string GetLocalArtFullPath(int)`, `string? ResolveLocalArtPath(string?)`, `Task SaveHashBatchAsync(List<(int,ulong,ulong)>, CancellationToken)`.

- [ ] **Step 1: Write the failing hashing test**

Append to `TcgCsvDownloadTests`. This serves a tiny real PNG so `PerceptualHashService` produces a hash:

```csharp
    private static string TinyPngDataRoute() => "PNG"; // placeholder; replaced below

    [Fact]
    public async Task ComputeImageHashes_DownloadsAndHashes()
    {
        // 2x2 PNG bytes (base64) served for any image URL.
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD8GO2jAAAAE0lEQVR4nGP8z8Dwn4EIwDiqEAD6/AeR6qKYFwAAAABJRU5ErkJggg==");

        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.Add(new TcgCsvCard { ProductId = 42, Game = CardGame.Pokemon, Name = "Pic", ImageUrl = "https://cdn/42_200w.jpg" });
            seed.SaveChanges();
        }

        var svc = CreateService(uri =>
        {
            if (uri.Contains("42_400w") || uri.Contains("42_200w")) return null; // handled by binary route below
            return null;
        });
        // Route binary image responses through a dedicated handler:
        svc = new TestTcgCsvService(
            new FakeHttpClientFactory(new BinaryRoutingHandler(pngBytes)),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            MockDataPath(), NullLogger<TestTcgCsvService>.Instance);

        await svc.ComputeImageHashesAsync(forceAll: true);

        using var ctx = _factory.CreateDbContext();
        var row = ctx.Cards.Single(c => c.ProductId == 42);
        Assert.NotNull(row.ImageHash);
        Assert.NotNull(row.EdgeHash);
        Assert.Equal("pokemon-art/42.png", row.LocalImagePath);
    }

    private IDataPathService MockDataPath()
    {
        var m = new Moq.Mock<IDataPathService>();
        m.Setup(d => d.DataDirectory).Returns(_dataDir);
        return m.Object;
    }
```

Add this binary handler among the `file`-scoped helpers at the bottom of the file:

```csharp
file class BinaryRoutingHandler(byte[] payload) : System.Net.Http.HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new ByteArrayContent(payload) });
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~ComputeImageHashes`
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Replace the `ComputeImageHashesAsync` stub and add helpers**

Replace the stub with (adapted from RiftboundService.ComputeImageHashesAsync, using `int` ids and `UpgradeImageUrl`):

```csharp
    public async Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting {Game} image hash computation (forceAll: {ForceAll})", Game, forceAll);
        var sw = Stopwatch.StartNew();

        await using var context = _dbContextFactory.CreateDbContext();
        var query = context.Cards.Where(c => c.ImageUrl != null);
        if (!forceAll) query = query.Where(c => c.ImageHash == null || c.EdgeHash == null);
        var cards = await query.Select(c => new { c.ProductId, c.ImageUrl }).ToListAsync(ct);
        progress?.Report($"Computing hashes for {cards.Count} cards...");

        var client = CreateClient();
        using var throttle = new SemaphoreSlim(8);
        var completed = 0; var failed = 0;
        var results = new List<(int Id, ulong Hash, ulong EdgeHash)>();
        var saveLock = new object();

        await Parallel.ForEachAsync(cards, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (card, token) =>
            {
                var url = UpgradeImageUrl(card.ImageUrl);
                if (url is null) { Interlocked.Increment(ref failed); return; }
                try
                {
                    await throttle.WaitAsync(token);
                    try
                    {
                        var artFullPath = GetLocalArtFullPath(card.ProductId);
                        byte[] imageBytes;
                        if (File.Exists(artFullPath))
                        {
                            imageBytes = await File.ReadAllBytesAsync(artFullPath, token);
                        }
                        else
                        {
                            using var response = await client.GetAsync(url, token);
                            response.EnsureSuccessStatusCode();
                            imageBytes = await response.Content.ReadAsByteArrayAsync(token);
                            Directory.CreateDirectory(Path.GetDirectoryName(artFullPath)!);
                            await File.WriteAllBytesAsync(artFullPath, imageBytes, token);
                        }

                        using var buffer = new MemoryStream(imageBytes);
                        var hash = _hashService.ComputeHash(buffer);
                        buffer.Position = 0;
                        var edgeHash = _hashService.ComputeEdgeHash(buffer);
                        lock (saveLock) results.Add((card.ProductId, hash, edgeHash));
                    }
                    finally { throttle.Release(); await Task.Delay(50, token); }
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to compute hash for {Game} card {Id}", Game, card.ProductId);
                    Interlocked.Increment(ref failed);
                }

                var d = Interlocked.Increment(ref completed);
                if (d % 100 == 0) progress?.Report($"Hashed {d}/{cards.Count} cards ({failed} failed)...");

                List<(int, ulong, ulong)>? toSave = null;
                lock (saveLock) { if (results.Count >= 200) { toSave = [.. results]; results.Clear(); } }
                if (toSave is not null) await SaveHashBatchAsync(toSave, ct);
            });

        if (results.Count > 0) await SaveHashBatchAsync(results, ct);

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null; _edgeHashCache = null; _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("{Game} hash computation complete: {Hashed} hashed, {Failed} failed in {Sec:F1}s", Game, completed - failed, failed, sw.Elapsed.TotalSeconds);
        progress?.Report($"Done — hashed {completed - failed} cards ({failed} failed).");
    }

    private async Task SaveHashBatchAsync(List<(int Id, ulong Hash, ulong EdgeHash)> batch, CancellationToken ct)
    {
        await using var context = _dbContextFactory.CreateDbContext();
        foreach (var (id, hash, edgeHash) in batch)
        {
            var rel = GetLocalArtRelativePath(id);
            await context.Cards.Where(c => c.ProductId == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ImageHash, hash)
                    .SetProperty(c => c.EdgeHash, edgeHash)
                    .SetProperty(c => c.LocalImagePath, rel), ct);
        }
    }

    protected string GetLocalArtRelativePath(int id) => $"{GameKey}-art/{id}.png";
    protected string GetLocalArtFullPath(int id) => Path.Combine(_dataDirectory, $"{GameKey}-art", $"{id}.png");
    protected string? ResolveLocalArtPath(string? relativePath)
    {
        if (relativePath is null) return null;
        var full = Path.Combine(_dataDirectory, relativePath);
        return File.Exists(full) ? full : null;
    }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~ComputeImageHashes`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/TcgCsvGameService.cs OmniCard.Tests/Services/TcgCsvGameServiceTests.cs
git commit -m "feat(tcgcsv): base image hashing with art caching and url upgrade"
```

---

## Task 7: Base service — matching, corrections, and query surface

**Files:**
- Modify: `OmniCard.CardMatching/TcgCsvGameService.cs` (replace remaining stubs: `FindClosestMatch`, `SearchCards`, `GetPrintings`, `GetCurrentPrice`, `GetCurrentPrices`, `RecordCorrection`, `GetAvailableSets`, `GetSetCompletionAsync`, `GetMissingCards`, `FindCardById`; add `ToMatch`, `LookupById`, `NormalizeNumber`)
- Test: `OmniCard.Tests/Services/TcgCsvGameServiceTests.cs` (append matching + price-read + query tests)

**Interfaces:**
- Produces: working implementations of all remaining `ICardGameService` members; `internal CardMatch ToMatch(TcgCsvCard, double?)`; `TcgCsvCard? LookupById(int)`; `static string NormalizeNumber(string)`. `GameSpecificId`/`gameCardId` is `ProductId.ToString()`.

- [ ] **Step 1: Write the failing matching + query tests**

Append to `TcgCsvDownloadTests`:

```csharp
    [Fact]
    public void FindClosestMatch_OcrCollectorNumber_ReturnsExactCard()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new TcgCsvCard { ProductId = 1, Game = CardGame.Pokemon, Name = "A", SetCode = "OP1", CollectorNumber = "1-001H", ImageHash = 1UL },
                new TcgCsvCard { ProductId = 2, Game = CardGame.Pokemon, Name = "B", SetCode = "OP1", CollectorNumber = "1-002R", ImageHash = 2UL });
            seed.SaveChanges();
        }
        var svc = CreateService();
        var ocr = new OcrMatchResult { CollectorNumber = "1-002R", CollectorNumberConfidence = 0.95 };
        var match = svc.FindClosestMatch(imageHash: 999UL, ocrResult: ocr);
        Assert.NotNull(match);
        Assert.Equal("2", match!.GameSpecificId);
        Assert.Equal(100, match.Confidence);
    }

    [Fact]
    public void FindClosestMatch_PHash_ReturnsNearest()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new TcgCsvCard { ProductId = 10, Game = CardGame.Pokemon, Name = "Near", SetCode = "S", ImageHash = 0b1111UL },
                new TcgCsvCard { ProductId = 11, Game = CardGame.Pokemon, Name = "Far", SetCode = "S", ImageHash = 0xFFFFFFFFUL });
            seed.SaveChanges();
        }
        var svc = CreateService();
        var match = svc.FindClosestMatch(imageHash: 0b1110UL);
        Assert.Equal("10", match!.GameSpecificId);
    }

    [Fact]
    public void GetCurrentPrice_IsFoilAware_WithFallback()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new TcgCsvCard { ProductId = 1, Game = CardGame.Pokemon, Name = "Both", SetCode = "S", MarketPrice = 1.50m, FoilMarketPrice = 3.00m },
                new TcgCsvCard { ProductId = 2, Game = CardGame.Pokemon, Name = "FoilOnly", SetCode = "S", FoilMarketPrice = 9.00m },
                new TcgCsvCard { ProductId = 3, Game = CardGame.Pokemon, Name = "None", SetCode = "S" });
            seed.SaveChanges();
        }
        var svc = CreateService();
        Assert.Equal(3.00m, svc.GetCurrentPrice("1", isFoil: true));
        Assert.Equal(9.00m, svc.GetCurrentPrice("2", isFoil: false)); // falls back to foil
        Assert.Null(svc.GetCurrentPrice("3", isFoil: true));

        var bulk = svc.GetCurrentPrices(new[] { "1", "2", "3" }, isFoil: false);
        Assert.Equal(1.50m, bulk["1"]);
        Assert.False(bulk.ContainsKey("3"));
    }

    [Fact]
    public void SearchCards_And_GetAvailableSets_Work()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Cards.AddRange(
                new TcgCsvCard { ProductId = 1, Game = CardGame.Pokemon, Name = "Charizard", SetCode = "BS", SetName = "Base Set", CardType = "Fire" },
                new TcgCsvCard { ProductId = 2, Game = CardGame.Pokemon, Name = "Blastoise", SetCode = "BS", SetName = "Base Set", CardType = "Water" });
            seed.SaveChanges();
        }
        var svc = CreateService();
        Assert.Single(svc.SearchCards("Charizard"));
        Assert.Single(svc.SearchCards("type:Water"));
        Assert.Single(svc.GetAvailableSets());
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~FindClosestMatch`
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Replace the remaining stubs**

Replace the ten stub lines with these implementations (adapted from RiftboundService; keys are `int ProductId`, collector numbers are strings):

```csharp
    private TcgCsvCard? LookupById(int id) => _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.ProductId == id);

    internal static string NormalizeNumber(string s) => s.Trim().ToUpperInvariant();

    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
        IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
    {
        LastMatchDiagnostics = new MatchDiagnostics { SetFilterActive = setFilter is not null };

        // Phase 0: OCR collector-number lookup. Number is ground truth from extendedData.
        if (ocrResult?.CollectorNumber is not null && ocrResult.CollectorNumberConfidence >= 0.5)
        {
            var num = NormalizeNumber(ocrResult.CollectorNumber);
            var candidates = _readContext.Cards.AsNoTracking()
                .Where(c => c.CollectorNumber.ToUpper() == num).ToList();
            if (setFilter is not null) candidates = candidates.Where(c => setFilter.Contains(c.SetCode)).ToList();

            if (candidates.Count == 1)
            {
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ToMatch(candidates[0], 100);
            }
            if (candidates.Count > 1)
            {
                var hashed = candidates.Where(c => c.ImageHash != null).ToList();
                var best = hashed.Count > 0
                    ? hashed.OrderBy(c => PerceptualHashService.HammingDistance(imageHash, c.ImageHash!.Value)).First()
                    : candidates[0];
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ToMatch(best, 100);
            }
        }

        // Foil path: color-robust edge hash.
        if (scanEdgeHash is ulong scanEdge)
        {
            _edgeHashCache ??= _readContext.Cards.Where(c => c.EdgeHash != null)
                .Select(c => new { c.ProductId, Edge = c.EdgeHash!.Value, c.SetCode })
                .AsNoTracking().AsEnumerable().Select(c => (c.ProductId, c.Edge, c.SetCode)).ToList();

            int bestEdgeId = -1; int bestEdgeDist = int.MaxValue;
            foreach (var (id, edge, setCode) in _edgeHashCache)
            {
                if (setFilter is not null && !setFilter.Contains(setCode)) continue;
                var dist = PerceptualHashService.HammingDistance(scanEdge, edge);
                if (dist < bestEdgeDist) { bestEdgeDist = dist; bestEdgeId = id; }
            }
            if (bestEdgeId >= 0 && bestEdgeDist <= maxDistance)
            {
                LastMatchDiagnostics.DecisionPhase = "EdgeHashFoil";
                LastMatchDiagnostics.PHashDistance = bestEdgeDist;
                var conf = Math.Max(0, 1.0 - (double)bestEdgeDist / maxDistance) * 100;
                var card = LookupById(bestEdgeId);
                return card is null ? null : ToMatch(card, conf);
            }
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

        if (_hashCache is null)
        {
            var entries = _readContext.Cards.Where(c => c.ImageHash != null)
                .Select(c => new { c.ProductId, Hash = c.ImageHash!.Value, c.SetCode })
                .AsNoTracking().AsEnumerable().ToList();
            _hashCache = entries.Select(c => (c.ProductId, c.Hash)).ToList();
            _hashSetLookup = entries.ToDictionary(c => c.ProductId, c => c.SetCode);
        }
        if (_correctionsCache is null)
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            _correctionsCache = ctx.HashCorrections.AsNoTracking()
                .Select(h => new { h.ScanHash, h.CorrectCardId }).AsEnumerable()
                .Select(h => (h.ScanHash, h.CorrectCardId)).ToList();
        }

        // Phase 1: exact correction.
        var exact = _correctionsCache.FirstOrDefault(c => c.ScanHash == imageHash);
        if (exact.CorrectCardId is not null && int.TryParse(exact.CorrectCardId, out var exactId))
        {
            var corrected = LookupById(exactId);
            if (corrected is not null && (setFilter is null || setFilter.Contains(corrected.SetCode)))
            {
                LastMatchDiagnostics.DecisionPhase = "ExactCorrection";
                return ToMatch(corrected, 100);
            }
        }

        if (_hashCache.Count == 0) { LastMatchDiagnostics.DecisionPhase = "NoMatch"; return null; }

        // Phase 2: nearest pHash + fuzzy corrections.
        int bestId = -1; int bestDist = int.MaxValue;
        foreach (var (id, hash) in _hashCache)
        {
            if (setFilter is not null && !setFilter.Contains(_hashSetLookup![id])) continue;
            var dist = PerceptualHashService.HammingDistance(imageHash, hash);
            if (dist < bestDist) { bestDist = dist; bestId = id; }
        }

        int? bestCorrId = null; int bestCorrAdjusted = int.MaxValue;
        foreach (var (scanHash, correctCardId) in _correctionsCache)
        {
            if (!int.TryParse(correctCardId, out var cid)) continue;
            if (setFilter is not null)
            {
                var corrSet = _hashSetLookup!.GetValueOrDefault(cid);
                if (corrSet is null || !setFilter.Contains(corrSet)) continue;
            }
            var dist = PerceptualHashService.HammingDistance(imageHash, scanHash);
            if (dist <= maxDistance)
            {
                var adjusted = Math.Max(0, dist - CorrectionTrustBonus);
                if (adjusted < bestCorrAdjusted) { bestCorrAdjusted = adjusted; bestCorrId = cid; }
            }
        }

        if (bestCorrId is int corrId && bestCorrAdjusted <= bestDist)
        {
            var conf = Math.Max(0, 1.0 - (double)bestCorrAdjusted / maxDistance) * 100;
            var corrected = LookupById(corrId);
            if (corrected is not null)
            {
                LastMatchDiagnostics.DecisionPhase = "PHashConfident";
                LastMatchDiagnostics.PHashDistance = bestCorrAdjusted;
                return ToMatch(corrected, conf);
            }
        }

        if (bestDist > maxDistance) { LastMatchDiagnostics.DecisionPhase = "NoMatch"; return null; }

        var bestCard = bestId >= 0 ? LookupById(bestId) : null;
        if (bestCard is null) { LastMatchDiagnostics.DecisionPhase = "NoMatch"; return null; }
        LastMatchDiagnostics.DecisionPhase = "PHashConfident";
        LastMatchDiagnostics.PHashDistance = bestDist;
        var confidence = Math.Max(0, 1.0 - (double)bestDist / maxDistance) * 100;
        return ToMatch(bestCard, confidence);
    }

    public IReadOnlyList<SetInfo> GetAvailableSets()
        => _readContext.Cards.AsNoTracking()
            .Select(c => new { c.SetCode, c.SetName }).Distinct()
            .OrderBy(s => s.SetName).AsEnumerable()
            .Select(s => new SetInfo(s.SetCode, s.SetName)).ToList();

    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
    {
        var setTotals = _readContext.Cards.AsNoTracking()
            .Select(c => new { c.SetCode, c.SetName, c.CollectorNumber }).Distinct()
            .AsEnumerable()
            .GroupBy(c => new { c.SetCode, c.SetName })
            .Select(g => new { g.Key.SetCode, g.Key.SetName, Total = g.Count() })
            .ToDictionary(s => s.SetCode, s => (s.SetName, s.Total));

        var ownedPerSet = ownedCards.GroupBy(c => c.SetCode)
            .ToDictionary(g => g.Key, g => (Distinct: g.Select(c => c.Number).Distinct().Count(), Physical: g.Count()));

        var results = new List<SetCompletionSummary>();
        foreach (var (setCode, (setName, total)) in setTotals)
        {
            ownedPerSet.TryGetValue(setCode, out var owned);
            results.Add(new SetCompletionSummary
            {
                SetCode = setCode, SetName = setName,
                OwnedCount = owned.Distinct, OwnedPhysicalCount = owned.Physical,
                TotalCount = total, Game = Game,
            });
        }
        return Task.FromResult(results);
    }

    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers)
    {
        var ownedSet = ownedCollectorNumbers.ToHashSet();
        return _readContext.Cards.AsNoTracking()
            .Where(c => c.SetCode == setCode).AsEnumerable()
            .Where(c => !ownedSet.Contains(c.CollectorNumber))
            .GroupBy(c => c.CollectorNumber)
            .Select(g => g.First())
            .Select(c => new MissingCard
            {
                Name = c.Name, CollectorNumber = c.CollectorNumber, SetCode = c.SetCode,
                Rarity = c.Rarity, ImageUri = c.ImageUrl, TypeLine = c.CardType,
            })
            .OrderBy(m => m.CollectorNumber).ToList();
    }

    public List<CardMatch> SearchCards(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        IQueryable<TcgCsvCard> cards = _readContext.Cards.AsNoTracking();
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = term;
            if (t.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[4..];
                cards = cards.Where(c => EF.Functions.Like(c.SetCode, $"%{val}%") || EF.Functions.Like(c.SetName, $"%{val}%"));
            }
            else if (t.StartsWith("type:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[(t.IndexOf(':') + 1)..];
                cards = cards.Where(c => EF.Functions.Like(c.CardType, $"%{val}%"));
            }
            else if (t.StartsWith("cn:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[3..];
                cards = cards.Where(c => EF.Functions.Like(c.CollectorNumber, $"%{val}%"));
            }
            else
            {
                cards = cards.Where(c => EF.Functions.Like(c.Name, $"%{t}%"));
            }
        }
        return cards.OrderBy(c => c.Name).Take(maxResults).AsEnumerable().Select(c => ToMatch(c)).ToList();
    }

    public List<CardMatch> GetPrintings(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return [];
        return _readContext.Cards.AsNoTracking()
            .Where(c => c.Name == cardName)
            .OrderBy(c => c.SetName).ThenBy(c => c.CollectorNumber)
            .AsEnumerable().Select(c => ToMatch(c)).ToList();
    }

    public decimal? GetCurrentPrice(string gameCardId, bool isFoil)
    {
        if (!int.TryParse(gameCardId, out var id)) return null;
        var row = _readContext.Cards.AsNoTracking().Where(c => c.ProductId == id)
            .Select(c => new { c.MarketPrice, c.FoilMarketPrice }).FirstOrDefault();
        if (row is null) return null;
        return isFoil ? row.FoilMarketPrice ?? row.MarketPrice : row.MarketPrice ?? row.FoilMarketPrice;
    }

    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)
    {
        var ids = gameCardIds.Select(s => int.TryParse(s, out var i) ? i : (int?)null).OfType<int>().Distinct().ToList();
        if (ids.Count == 0) return [];
        var result = new Dictionary<string, decimal>(ids.Count);
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = _readContext.Cards.AsNoTracking().Where(c => chunk.Contains(c.ProductId))
                .Select(c => new { c.ProductId, c.MarketPrice, c.FoilMarketPrice }).ToList();
            foreach (var row in rows)
            {
                var price = isFoil ? row.FoilMarketPrice ?? row.MarketPrice : row.MarketPrice ?? row.FoilMarketPrice;
                if (price.HasValue) result[row.ProductId.ToString()] = price.Value;
            }
        }
        return result;
    }

    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        ctx.Database.ExecuteSqlRaw(
            "INSERT OR REPLACE INTO HashCorrections (ScanHash, CorrectCardId, CreatedAt) VALUES ({0}, {1}, {2})",
            (long)scanHash, correctCardId, DateTime.UtcNow.ToString("o"));
        _correctionsCache = null;
    }

    public object? FindCardById(string gameCardId)
    {
        if (!int.TryParse(gameCardId, out var id)) return null;
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().FirstOrDefault(c => c.ProductId == id);
    }

    internal CardMatch ToMatch(TcgCsvCard c, double? confidence = null) => new()
    {
        Name = c.Name,
        SetCode = c.SetCode,
        SetName = c.SetName,
        CollectorNumber = c.CollectorNumber,
        Rarity = c.Rarity,
        ImageUri = c.ImageUrl,
        GameSpecificId = c.ProductId.ToString(),
        LocalImagePath = ResolveLocalArtPath(c.LocalImagePath),
        Confidence = confidence,
        Source = c
    };
```

> If `MissingCard` lacks any property used above (`TypeLine`, etc.), match Riftbound's usage in `RiftboundService.GetMissingCards` — set only the properties that exist. Verify against `OmniCard.Shared/Models` at implementation time and drop any absent property.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvDownloadTests`
Expected: PASS (all TcgCsvDownloadTests).

- [ ] **Step 5: Full base build + commit**

Run: `dotnet build OmniCard.CardMatching`
Expected: Build succeeded (no more `NotImplementedException` stubs).

```bash
git add OmniCard.CardMatching/TcgCsvGameService.cs OmniCard.Tests/Services/TcgCsvGameServiceTests.cs
git commit -m "feat(tcgcsv): base matching, corrections, and query surface"
```

---

## Task 8: PokemonService + DI (first concrete game end-to-end)

**Files:**
- Create: `OmniCard.CardMatching/PokemonService.cs`
- Modify: `OmniCard/App.xaml.cs` (DI), `OmniCard.Web/Program.cs` (DI)
- Test: `OmniCard.Tests/Services/PokemonServiceTests.cs`

**Interfaces:**
- Consumes: `TcgCsvGameService<PokemonDbContext>`, `PokemonDbContext`, `OcrCollectorSpec` (Task 9 — reference it only in Step for OcrSpec; if doing this task first, define OcrSpec placeholder or reorder — see note).
- Produces: `sealed class PokemonService : TcgCsvGameService<PokemonDbContext>` with `public static readonly OcrCollectorSpec OcrSpec`.

> **Ordering note:** `OcrCollectorSpec` is created in Task 9. If executing strictly in order, do Task 9 Step 1 (create the `OcrCollectorSpec` type) before this task, or temporarily omit the `OcrSpec` field and add it in Task 9. The plan assumes `OcrCollectorSpec` exists.

- [ ] **Step 1: Write the failing subtype-mapping test**

Create `OmniCard.Tests/Services/PokemonServiceTests.cs`:

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

public class PokemonServiceTests
{
    [Fact]
    public void Game_And_Category_AreCorrect()
    {
        var svc = Create();
        Assert.Equal(CardGame.Pokemon, svc.Game);
    }

    private static PokemonService Create()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(conn).Options;
        var factory = new PkFactory(options);
        using (var ctx = factory.CreateDbContext()) { ctx.Database.EnsureCreated(); ctx.MarkMigrationComplete(); }
        var dp = new Moq.Mock<IDataPathService>();
        dp.Setup(d => d.DataDirectory).Returns(Path.Combine(Path.GetTempPath(), "pk-" + Guid.NewGuid().ToString("N")));
        return new PokemonService(new NoHttp(), factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance), dp.Object,
            NullLogger<PokemonService>.Instance);
    }

    private class PkFactory(DbContextOptions<PokemonDbContext> o) : IDbContextFactory<PokemonDbContext>
    { public PokemonDbContext CreateDbContext() => new(o); }
    private class NoHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~PokemonServiceTests`
Expected: FAIL — `PokemonService` does not exist.

- [ ] **Step 3: Write `PokemonService`**

Create `OmniCard.CardMatching/PokemonService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.CardMatching;

public sealed class PokemonService : TcgCsvGameService<PokemonDbContext>
{
    public PokemonService(IHttpClientFactory httpClientFactory, IDbContextFactory<PokemonDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<PokemonService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 3;
    public override CardGame Game => CardGame.Pokemon;
    protected override string GameKey => "pokemon";

    // Pokémon prices: Normal + (Holofoil preferred over Reverse Holofoil) as the single foil slot.
    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows)
    {
        decimal? P(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        return (P("Normal"), P("Holofoil") ?? P("Reverse Holofoil"));
    }

    // Pokémon collector numbers look like "123/198"; number sits bottom-left.
    public static readonly OcrCollectorSpec OcrSpec = new()
    {
        PortraitRegion = (0.03, 0.90, 0.35, 0.07),
        LandscapeRegion = (0.03, 0.88, 0.30, 0.09),
        Whitelist = "0123456789/",
        RegexPattern = @"(\d+\s*/\s*\d+)"
    };
}
```

- [ ] **Step 4: Register in desktop DI**

In `OmniCard/App.xaml.cs`, immediately after the Riftbound registration (after `services.AddSingleton<ICardGameService, RiftboundService>();`, before `services.AddSingleton<Services.PriceUpdateService>();`), add:

```csharp
            // Pokémon (TCGCSV)
            services.AddDbContextFactory<PokemonDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "pokemon.db")}"));
            services.AddSingleton<ICardGameService, PokemonService>();
```

- [ ] **Step 5: Register in web DI**

In `OmniCard.Web/Program.cs`, after the Riftbound `AddDbContextFactory<RiftboundDbContext>` block add:

```csharp
builder.Services.AddDbContextFactory<PokemonDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "pokemon.db")};Mode=ReadOnly"));
```

and after the Riftbound concrete-then-aliased pair add:

```csharp
builder.Services.AddSingleton<PokemonService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<PokemonService>());
```

- [ ] **Step 6: Run tests + build both hosts**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~PokemonServiceTests`
Expected: PASS.
Run: `dotnet build OmniCard` and `dotnet build OmniCard.Web`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add OmniCard.CardMatching/PokemonService.cs OmniCard/App.xaml.cs OmniCard.Web/Program.cs OmniCard.Tests/Services/PokemonServiceTests.cs
git commit -m "feat(tcgcsv): PokemonService + desktop/web DI registration"
```

---

## Task 9: Config-driven OCR collector-number detection

**Files:**
- Create: `OmniCard.Shared/Models/OcrCollectorSpec.cs`
- Modify: `OmniCard.Shared/Interfaces/IOcrMatchingService.cs`
- Modify: `OmniCard.Imaging/OcrMatchingService.cs`
- Test: `OmniCard.Tests/Services/TcgCsvOcrTests.cs`

**Interfaces:**
- Produces:
  - `sealed class OcrCollectorSpec { (double X,double Y,double W,double H) PortraitRegion; (…) LandscapeRegion; string Whitelist; string RegexPattern }`.
  - `Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] imageData, OcrCollectorSpec spec)` on `IOcrMatchingService`.

- [ ] **Step 1: Write the OCR spec type**

Create `OmniCard.Shared/Models/OcrCollectorSpec.cs`:

```csharp
namespace OmniCard.Models;

// Per-game configuration for collector-number OCR. Regions are fractions of the card image
// (X, Y, Width, Height). RegexPattern's first capture group is the normalized collector number.
public sealed class OcrCollectorSpec
{
    public (double X, double Y, double W, double H) PortraitRegion { get; init; }
    public (double X, double Y, double W, double H) LandscapeRegion { get; init; }
    public string Whitelist { get; init; } = "";
    public string RegexPattern { get; init; } = "";
}
```

- [ ] **Step 2: Extend the interface**

In `OmniCard.Shared/Interfaces/IOcrMatchingService.cs`, add after the `DetectRiftboundCollectorNumberAsync` line:

```csharp
    /// <summary>OCR a collector number using a per-game crop/regex spec (Pokémon, Yu-Gi-Oh!, FFTCG).</summary>
    Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] imageData, OcrCollectorSpec spec);
```

- [ ] **Step 3: Write the failing OCR test (regex-extraction focus)**

Create `OmniCard.Tests/Services/TcgCsvOcrTests.cs`. OCR of real pixels is environment-dependent, so this test targets the deterministic regex/normalization contract via a public static helper we add in Step 4:

```csharp
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class TcgCsvOcrTests
{
    [Theory]
    [InlineData(@"(\d+\s*/\s*\d+)", "abc 123 / 198 xy", "123/198")]
    [InlineData(@"(\d+-\d+[A-Z]?)", "PR 1-001H", "1-001H")]
    [InlineData(@"([A-Z0-9]+-[A-Z]{0,2}\d+)", "noise LOB-EN001 noise", "LOB-EN001")]
    public void ExtractCollectorNumber_NormalizesAndMatches(string pattern, string ocrText, string expected)
    {
        var ok = OcrMatchingService.TryExtractCollectorNumber(ocrText, pattern, out var result);
        Assert.True(ok);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractCollectorNumber_NoMatch_ReturnsFalse()
    {
        Assert.False(OcrMatchingService.TryExtractCollectorNumber("nothing here", @"(\d+/\d+)", out _));
    }
}
```

- [ ] **Step 4: Implement the detector + extraction helper**

In `OmniCard.Imaging/OcrMatchingService.cs`, add a compiled-regex cache field near the other static regex fields:

```csharp
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.RegularExpressions.Regex> _specRegexCache = new();
```

Add the extraction helper (static, testable) near `TryExtractRiftboundNumber`:

```csharp
    // Applies a spec's regex to OCR text; returns the first capture group, whitespace-stripped and upper-cased.
    internal static bool TryExtractCollectorNumber(string ocrText, string pattern, out string? formatted)
    {
        formatted = null;
        if (string.IsNullOrWhiteSpace(ocrText)) return false;
        var rx = _specRegexCache.GetOrAdd(pattern, p =>
            new System.Text.RegularExpressions.Regex(p,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled));
        var m = rx.Match(ocrText);
        if (!m.Success) return false;
        var raw = (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value);
        formatted = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", "").ToUpperInvariant();
        return formatted.Length > 0;
    }
```

Add the detector (mirrors `DetectRiftboundCollectorNumber`, but region/whitelist/regex come from the spec):

```csharp
    public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] imageData, OcrCollectorSpec spec)
        => Task.Run(() => DetectCollectorNumber(imageData, spec));

    private (string? CollectorNumber, double Confidence) DetectCollectorNumber(byte[] imageData, OcrCollectorSpec spec)
    {
        if (!_ocrAvailable) return (null, 0);
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            var region = bitmap.Width > bitmap.Height ? spec.LandscapeRegion : spec.PortraitRegion;
            var rect = ToPixelRect(region, bitmap.Width, bitmap.Height);
            if (rect.Width < 10 || rect.Height < 5) return (null, 0);

            var (text, confidence) = OcrCroppedRegion(bitmap, rect, PageSegMode.SingleLine, spec.Whitelist);
            if (string.IsNullOrWhiteSpace(text)) return (null, 0);

            if (TryExtractCollectorNumber(text, spec.RegexPattern, out var formatted))
            {
                var reported = Math.Max(0.9, confidence);
                _logger.LogInformation("Collector number detected: {Number} (raw: {Raw}, ocrConf: {Conf:F2})", formatted, text, confidence);
                return (formatted, reported);
            }
            _logger.LogDebug("Collector OCR text did not match spec pattern: {Text}", text);
            return (null, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Collector number detection failed");
            return (null, 0);
        }
    }
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvOcrTests`
Expected: PASS (4 cases).
Run: `dotnet build OmniCard.Imaging`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/OcrCollectorSpec.cs OmniCard.Shared/Interfaces/IOcrMatchingService.cs OmniCard.Imaging/OcrMatchingService.cs OmniCard.Tests/Services/TcgCsvOcrTests.cs
git commit -m "feat(tcgcsv): config-driven collector-number OCR detector"
```

---

## Task 10: Wire scanning into CardService

**Files:**
- Modify: `OmniCard.Collection/CardService.cs`
- Test: `OmniCard.Tests/Services/TcgCsvScanRoutingTests.cs`

**Interfaces:**
- Consumes: `PokemonService.OcrSpec` (and later `YugiohService.OcrSpec`, `FinalFantasyService.OcrSpec`); `_ocrService.DetectCollectorNumberAsync`.
- Produces: scan/rotate/foil handling for the three games in `AddFromStream`.

- [ ] **Step 1: Write the failing routing test**

Create `OmniCard.Tests/Services/TcgCsvScanRoutingTests.cs`. It verifies `CardService.GetGameService(CardGame.Pokemon)` resolves the Pokémon service and that `FindBestMatch` under `SelectedGame = Pokemon` routes there:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TcgCsvScanRoutingTests
{
    [Fact]
    public void CardService_ResolvesPokemonService_AndRoutesMatch()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(conn).Options;
        var factory = new PkFactory(options);
        using (var ctx = factory.CreateDbContext())
        {
            ctx.Database.EnsureCreated(); ctx.MarkMigrationComplete();
            ctx.Cards.Add(new TcgCsvCard { ProductId = 5, Game = CardGame.Pokemon, Name = "Pikachu", SetCode = "BS", ImageHash = 0b1UL });
            ctx.SaveChanges();
        }
        var dp = new Moq.Mock<IDataPathService>();
        dp.Setup(d => d.DataDirectory).Returns(Path.Combine(Path.GetTempPath(), "route-" + Guid.NewGuid().ToString("N")));
        var pokemon = new PokemonService(new NoHttp(), factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance), dp.Object, NullLogger<PokemonService>.Instance);

        var svc = pokemon as ICardGameService;
        var match = svc.FindClosestMatch(0b1UL);
        Assert.Equal("5", match!.GameSpecificId);
        Assert.Equal(CardGame.Pokemon, svc.Game);
    }

    private class PkFactory(DbContextOptions<PokemonDbContext> o) : IDbContextFactory<PokemonDbContext>
    { public PokemonDbContext CreateDbContext() => new(o); }
    private class NoHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
```

- [ ] **Step 2: Run to verify pass (this asserts existing behavior)**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvScanRoutingTests`
Expected: PASS — confirms the service satisfies routing before we touch `CardService`. (This test guards regressions while we edit `AddFromStream`.)

- [ ] **Step 3: Add the foil edge-hash arm**

In `OmniCard.Collection/CardService.cs`, `AddFromStream`, change the foil edge-hash condition:

```csharp
        // BEFORE:
        if (DefaultIsFoil && (SelectedGame == CardGame.OnePiece || SelectedGame == CardGame.Riftbound))
        // AFTER:
        if (DefaultIsFoil && (SelectedGame == CardGame.OnePiece || SelectedGame == CardGame.Riftbound
            || SelectedGame == CardGame.Pokemon || SelectedGame == CardGame.YuGiOh || SelectedGame == CardGame.FinalFantasy))
```

- [ ] **Step 4: Add the async OCR arm**

In the async OCR block, after the `else if (game == CardGame.Riftbound) { … }` arm and before the `else` (MTG) arm, insert:

```csharp
                      else if (game == CardGame.Pokemon || game == CardGame.YuGiOh || game == CardGame.FinalFantasy)
                      {
                          var spec = game switch
                          {
                              CardGame.Pokemon => PokemonService.OcrSpec,
                              CardGame.YuGiOh => YugiohService.OcrSpec,
                              _ => FinalFantasyService.OcrSpec
                          };
                          var (collectorNumber, conf) = await _ocrService.DetectCollectorNumberAsync(rawBytes, spec);
                          if (collectorNumber is not null && conf >= 0.5)
                          {
                              ocrResult = new OcrMatchResult { CollectorNumber = collectorNumber, CollectorNumberConfidence = conf };
                              _logger.LogInformation("{Game} collector detected: {Number} (confidence {Conf:F2})", game, collectorNumber, conf);
                              var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, null, scannedCard.ScanEdgeHash);
                              if (ocrMatch is not null && (scannedCard.Match is null || ocrMatch.GameSpecificId != scannedCard.Match?.GameSpecificId))
                              {
                                  scannedCard.Match = ocrMatch;
                                  scannedCard.Game = ocrGame;
                                  scannedCard.FlagReason = FlagReason.None;
                                  _logger.LogInformation("OCR matched to \"{CardName}\" ({SetCode} #{Number})", ocrMatch.Name, ocrMatch.SetCode, ocrMatch.CollectorNumber);
                              }
                          }
                      }
```

- [ ] **Step 5: Add the rotate-retry arm**

In the rotate-retry block, after the `else if (game == CardGame.Riftbound) { … }` arm, insert:

```csharp
                          else if (game == CardGame.Pokemon || game == CardGame.YuGiOh || game == CardGame.FinalFantasy)
                          {
                              var spec = game switch
                              {
                                  CardGame.Pokemon => PokemonService.OcrSpec,
                                  CardGame.YuGiOh => YugiohService.OcrSpec,
                                  _ => FinalFantasyService.OcrSpec
                              };
                              var (cn, cnConf) = await _ocrService.DetectCollectorNumberAsync(rotatedBytes, spec);
                              if (cn is not null && cnConf >= 0.5)
                                  rotatedOcr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = cnConf };
                          }
```

And extend the rotated foil edge-hash condition:

```csharp
                          // BEFORE:
                          if (scannedCard.IsFoil && (game == CardGame.OnePiece || game == CardGame.Riftbound))
                          // AFTER:
                          if (scannedCard.IsFoil && (game == CardGame.OnePiece || game == CardGame.Riftbound
                              || game == CardGame.Pokemon || game == CardGame.YuGiOh || game == CardGame.FinalFantasy))
```

> `YugiohService` and `FinalFantasyService` are created in Tasks 11-12. If executing strictly in order, this task will not compile until those exist. Either (a) do Tasks 11-12 before building this task, or (b) temporarily reference only `PokemonService.OcrSpec` here and broaden after. Recommended: implement Tasks 11-12 first, then this task — but its test (Step 1-2) can run now.

- [ ] **Step 6: Build + test**

Run: `dotnet build OmniCard.Collection` (after Tasks 11-12 exist) and `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvScanRoutingTests`
Expected: Build succeeded; PASS.

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Collection/CardService.cs OmniCard.Tests/Services/TcgCsvScanRoutingTests.cs
git commit -m "feat(tcgcsv): route scanning/OCR/foil for Pokemon, Yugioh, FFTCG"
```

---

## Task 11: YugiohService + DI

**Files:**
- Create: `OmniCard.CardMatching/YugiohService.cs`
- Modify: `OmniCard/App.xaml.cs`, `OmniCard.Web/Program.cs`
- Test: `OmniCard.Tests/Services/YugiohServiceTests.cs`

**Interfaces:**
- Produces: `sealed class YugiohService : TcgCsvGameService<YugiohDbContext>` with `public static readonly OcrCollectorSpec OcrSpec`.

- [ ] **Step 1: Write the failing subtype test**

Create `OmniCard.Tests/Services/YugiohServiceTests.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class YugiohServiceTests
{
    [Fact]
    public void SubtypePrices_PrefersUnlimited_ForNormal_NoFoilSlot()
    {
        var rows = new List<TcgCsvPrice>
        {
            new() { ProductId = 1, SubTypeName = "1st Edition", MarketPrice = 5.00m },
            new() { ProductId = 1, SubTypeName = "Unlimited", MarketPrice = 2.00m },
            new() { ProductId = 1, SubTypeName = "Limited", MarketPrice = 8.00m },
        };
        var (normal, foil) = YugiohService.MapSubtypePricesForTest(rows);
        Assert.Equal(2.00m, normal);   // Unlimited preferred
        Assert.Null(foil);             // Yu-Gi-Oh has no foil-vs-nonfoil split
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~YugiohServiceTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Write `YugiohService`**

Create `OmniCard.CardMatching/YugiohService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.CardMatching;

public sealed class YugiohService : TcgCsvGameService<YugiohDbContext>
{
    public YugiohService(IHttpClientFactory httpClientFactory, IDbContextFactory<YugiohDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<YugiohService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 2;
    public override CardGame Game => CardGame.YuGiOh;
    protected override string GameKey => "yugioh";

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => MapSubtypePricesForTest(rows);

    // Yu-Gi-Oh! sub-types are editions, not foils. Use Unlimited as the reference "normal" price
    // (fallback to Limited, then 1st Edition, then any). No distinct foil price.
    internal static (decimal? Normal, decimal? Foil) MapSubtypePricesForTest(List<TcgCsvPrice> rows)
    {
        decimal? P(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        var normal = P("Unlimited") ?? P("Limited") ?? P("1st Edition") ?? rows.FirstOrDefault()?.MarketPrice;
        return (normal, null);
    }

    // Yu-Gi-Oh! set codes look like "LOB-EN001"; printed lower-left/right.
    public static readonly OcrCollectorSpec OcrSpec = new()
    {
        PortraitRegion = (0.55, 0.88, 0.42, 0.06),
        LandscapeRegion = (0.55, 0.86, 0.42, 0.08),
        Whitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-",
        RegexPattern = @"([A-Z0-9]+-[A-Z]{0,2}\d+)"
    };
}
```

- [ ] **Step 4: DI (desktop + web)**

In `OmniCard/App.xaml.cs`, after the Pokémon registration add:

```csharp
            // Yu-Gi-Oh! (TCGCSV)
            services.AddDbContextFactory<YugiohDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "yugioh.db")}"));
            services.AddSingleton<ICardGameService, YugiohService>();
```

In `OmniCard.Web/Program.cs`, add the factory (after Pokémon's) and the concrete-then-aliased pair:

```csharp
builder.Services.AddDbContextFactory<YugiohDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "yugioh.db")};Mode=ReadOnly"));
```
```csharp
builder.Services.AddSingleton<YugiohService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<YugiohService>());
```

- [ ] **Step 5: Run + build + commit**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~YugiohServiceTests`
Expected: PASS.

```bash
git add OmniCard.CardMatching/YugiohService.cs OmniCard/App.xaml.cs OmniCard.Web/Program.cs OmniCard.Tests/Services/YugiohServiceTests.cs
git commit -m "feat(tcgcsv): YugiohService + DI"
```

---

## Task 12: FinalFantasyService + DI

**Files:**
- Create: `OmniCard.CardMatching/FinalFantasyService.cs`
- Modify: `OmniCard/App.xaml.cs`, `OmniCard.Web/Program.cs`
- Test: `OmniCard.Tests/Services/FinalFantasyServiceTests.cs`

**Interfaces:**
- Produces: `sealed class FinalFantasyService : TcgCsvGameService<FinalFantasyDbContext>` with `public static readonly OcrCollectorSpec OcrSpec`.

- [ ] **Step 1: Write the failing subtype test**

Create `OmniCard.Tests/Services/FinalFantasyServiceTests.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class FinalFantasyServiceTests
{
    [Fact]
    public void SubtypePrices_MapsNormalAndFoil()
    {
        var rows = new List<TcgCsvPrice>
        {
            new() { ProductId = 1, SubTypeName = "Normal", MarketPrice = 1.25m },
            new() { ProductId = 1, SubTypeName = "Foil", MarketPrice = 4.75m },
        };
        var (normal, foil) = FinalFantasyService.MapSubtypePricesForTest(rows);
        Assert.Equal(1.25m, normal);
        Assert.Equal(4.75m, foil);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~FinalFantasyServiceTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Write `FinalFantasyService`**

Create `OmniCard.CardMatching/FinalFantasyService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.CardMatching;

public sealed class FinalFantasyService : TcgCsvGameService<FinalFantasyDbContext>
{
    public FinalFantasyService(IHttpClientFactory httpClientFactory, IDbContextFactory<FinalFantasyDbContext> dbContextFactory,
        IPerceptualHashService hashService, IDataPathService dataPathService, ILogger<FinalFantasyService> logger)
        : base(httpClientFactory, dbContextFactory, hashService, dataPathService, logger) { }

    protected override int CategoryId => 24;
    public override CardGame Game => CardGame.FinalFantasy;
    protected override string GameKey => "fftcg";

    protected override (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> rows) => MapSubtypePricesForTest(rows);

    internal static (decimal? Normal, decimal? Foil) MapSubtypePricesForTest(List<TcgCsvPrice> rows)
    {
        decimal? P(string name) => rows.FirstOrDefault(r =>
            string.Equals(r.SubTypeName, name, StringComparison.OrdinalIgnoreCase))?.MarketPrice;
        return (P("Normal"), P("Foil"));
    }

    // FFTCG collector numbers look like "1-001H"; printed bottom-left.
    public static readonly OcrCollectorSpec OcrSpec = new()
    {
        PortraitRegion = (0.03, 0.92, 0.30, 0.06),
        LandscapeRegion = (0.03, 0.90, 0.28, 0.08),
        Whitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-",
        RegexPattern = @"(\d+-\d+[A-Z]?)"
    };
}
```

- [ ] **Step 4: DI (desktop + web)**

In `OmniCard/App.xaml.cs`, after the Yu-Gi-Oh! registration add:

```csharp
            // Final Fantasy TCG (TCGCSV)
            services.AddDbContextFactory<FinalFantasyDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "fftcg.db")}"));
            services.AddSingleton<ICardGameService, FinalFantasyService>();
```

In `OmniCard.Web/Program.cs`, add:

```csharp
builder.Services.AddDbContextFactory<FinalFantasyDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "fftcg.db")};Mode=ReadOnly"));
```
```csharp
builder.Services.AddSingleton<FinalFantasyService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<FinalFantasyService>());
```

- [ ] **Step 5: Run + build both hosts + commit**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~FinalFantasyServiceTests`
Expected: PASS.
Run: `dotnet build OmniCard` and `dotnet build OmniCard.Web` and `dotnet build OmniCard.Collection`
Expected: Build succeeded (Task 10's `CardService` arms now compile).

```bash
git add OmniCard.CardMatching/FinalFantasyService.cs OmniCard/App.xaml.cs OmniCard.Web/Program.cs OmniCard.Tests/Services/FinalFantasyServiceTests.cs
git commit -m "feat(tcgcsv): FinalFantasyService + DI"
```

---

## Task 13: Display converters + attribute extractor arms

**Files:**
- Modify: `OmniCard.Controls/Converters/RootConverters.cs`
- Modify: `OmniCard.CardMatching/CardAttributeExtractor.cs`
- Test: `OmniCard.Tests/Services/TcgCsvAttributeTests.cs`

**Interfaces:**
- Produces: display-name arms for the three games in both converters; `ExtractColor`/`ExtractCardType` arms reading `TcgCsvCard`.

- [ ] **Step 1: Add the display-converter arms**

In `OmniCard.Controls/Converters/RootConverters.cs`, `CardGameDisplayConverter.Convert`, add after the `CardGame.Riftbound => "Riftbound",` line:

```csharp
            CardGame.Pokemon => "Pokémon",
            CardGame.YuGiOh => "Yu-Gi-Oh!",
            CardGame.FinalFantasy => "Final Fantasy TCG",
```

In `BreakdownKeyDisplayConverter.Convert`, inside the inner `game switch`, add after the `CardGame.Riftbound => "Riftbound",` line:

```csharp
                CardGame.Pokemon => "Pokémon",
                CardGame.YuGiOh => "Yu-Gi-Oh!",
                CardGame.FinalFantasy => "Final Fantasy TCG",
```

- [ ] **Step 2: Write the failing attribute-extractor test**

Create `OmniCard.Tests/Services/TcgCsvAttributeTests.cs`:

```csharp
using OmniCard.CardMatching;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TcgCsvAttributeTests
{
    [Fact]
    public void ExtractCardType_FromTcgCsvCard()
    {
        var card = new TcgCsvCard { ProductId = 1, Game = CardGame.Pokemon, CardType = "Fire" };
        var match = new CardMatch { Name = "X", Source = card };
        Assert.Equal("Fire", CardAttributeExtractor.ExtractCardType(match, CardGame.Pokemon));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvAttributeTests`
Expected: FAIL — extractor returns null for Pokémon.

- [ ] **Step 4: Add the extractor arms**

In `OmniCard.CardMatching/CardAttributeExtractor.cs`, `ExtractColor`, add after the `CardGame.Riftbound => …` arm:

```csharp
            CardGame.Pokemon => (match.Source as TcgCsvCard)?.CardType,
            CardGame.YuGiOh => (match.Source as TcgCsvCard)?.CardType,
            CardGame.FinalFantasy => (match.Source as TcgCsvCard)?.CardType,
```

In `ExtractCardType`, add after the `CardGame.Riftbound => …` arm:

```csharp
            CardGame.Pokemon => (match.Source as TcgCsvCard)?.CardType,
            CardGame.YuGiOh => (match.Source as TcgCsvCard)?.CardType,
            CardGame.FinalFantasy => (match.Source as TcgCsvCard)?.CardType,
```

> Color is not modeled distinctly for TCGCSV games; returning `CardType` keeps breakdowns populated. Adjust later if a color field is promoted from `ExtendedDataJson`.

- [ ] **Step 5: Run + build + commit**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~TcgCsvAttributeTests`
Expected: PASS.
Run: `dotnet build OmniCard.Controls` and `dotnet build OmniCard.CardMatching`
Expected: Build succeeded.

```bash
git add OmniCard.Controls/Converters/RootConverters.cs OmniCard.CardMatching/CardAttributeExtractor.cs OmniCard.Tests/Services/TcgCsvAttributeTests.cs
git commit -m "feat(tcgcsv): display names and attribute extraction for new games"
```

---

## Task 14: Dashboard set-code triggers + SET-NUM query arm

**Files:**
- Modify: `OmniCard/Views/Dashboard/DashboardView.xaml`
- Modify: `OmniCard/Views/Root/RootViewModel.cs`

**Interfaces:** none (XAML/VM display only).

- [ ] **Step 1: Add set-code DataTriggers**

In `OmniCard/Views/Dashboard/DashboardView.xaml`, inside the set-code `TextBlock`'s `Style.Triggers` (alongside the existing OnePiece + Riftbound triggers), add:

```xml
                                                        <DataTrigger Binding="{Binding Game}" Value="{x:Static models:CardGame.Pokemon}">
                                                            <Setter Property="Visibility" Value="Visible"/>
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding Game}" Value="{x:Static models:CardGame.YuGiOh}">
                                                            <Setter Property="Visibility" Value="Visible"/>
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding Game}" Value="{x:Static models:CardGame.FinalFantasy}">
                                                            <Setter Property="Visibility" Value="Visible"/>
                                                        </DataTrigger>
```

- [ ] **Step 2: Broaden the SET-NUM query rewrite**

In `OmniCard/Views/Root/RootViewModel.cs`, `ManualSearch`, the SET-NUM rewrite currently is `SelectedGame == CardGame.OnePiece ? $"cn:{set}-{num}" : $"set:{set} cn:{num}"`. Change to treat the TCGCSV games like the MTG separate-token form (their `SearchCards` supports `set:`/`cn:`):

```csharp
            query = SelectedGame == CardGame.OnePiece
                ? $"cn:{set}-{num}"       // OPTCG CardSetId is the full code
                : $"set:{set} cn:{num}";  // MTG + TCGCSV games use separate set + collector number
```

(No code change may be needed if the existing `else` branch already covers them — verify the ternary and leave as-is if so.)

- [ ] **Step 3: Build the desktop app**

Run: `dotnet build OmniCard`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Views/Dashboard/DashboardView.xaml OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat(tcgcsv): dashboard set-code display for new games"
```

---

## Task 15: Card-detail ExtendedData panel (desktop)

**Files:**
- Create: `OmniCard.Controls/Controls/ExtendedDataView.xaml`
- Create: `OmniCard.Controls/Controls/ExtendedDataView.xaml.cs`
- Test: `OmniCard.Tests/Services/ExtendedDataParseTests.cs`

**Interfaces:**
- Produces: `ExtendedDataView` (a `UserControl` with a `Json` dependency property that renders `{name → value}` rows); `static List<KeyValuePair<string,string>> ExtendedDataView.Parse(string? json)`.

- [ ] **Step 1: Write the failing parse test**

Create `OmniCard.Tests/Services/ExtendedDataParseTests.cs`:

```csharp
using OmniCard.Controls.Controls;

namespace OmniCard.Tests.Services;

public class ExtendedDataParseTests
{
    [Fact]
    public void Parse_ReturnsDisplayNameValuePairs()
    {
        const string json = """
        [{"name":"Number","displayName":"Number","value":"1-001H"},
         {"name":"Element","displayName":"Element","value":"Fire"}]
        """;
        var pairs = ExtendedDataView.Parse(json);
        Assert.Equal(2, pairs.Count);
        Assert.Equal("Number", pairs[0].Key);
        Assert.Equal("1-001H", pairs[0].Value);
        Assert.Equal("Fire", pairs[1].Value);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(ExtendedDataView.Parse(null));
        Assert.Empty(ExtendedDataView.Parse(""));
    }
}
```

> `OmniCard.Tests` must reference `OmniCard.Controls`. If it does not already, add `<ProjectReference Include="..\OmniCard.Controls\OmniCard.Controls.csproj" />` to `OmniCard.Tests.csproj` (WPF control libs are testable headlessly for pure static methods; if the test host cannot load the WPF assembly, move `Parse` to a plain non-UI helper class `ExtendedDataParser` in `OmniCard.Shared` and reference that instead).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~ExtendedDataParseTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Write the control**

Create `OmniCard.Controls/Controls/ExtendedDataView.xaml`:

```xml
<UserControl x:Class="OmniCard.Controls.Controls.ExtendedDataView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ItemsControl x:Name="Items">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Grid Margin="0,1">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="120"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="{Binding Key}" FontWeight="SemiBold" Opacity="0.7" TextWrapping="Wrap"/>
                    <TextBlock Grid.Column="1" Text="{Binding Value}" TextWrapping="Wrap"/>
                </Grid>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</UserControl>
```

Create `OmniCard.Controls/Controls/ExtendedDataView.xaml.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace OmniCard.Controls.Controls;

public partial class ExtendedDataView : UserControl
{
    public ExtendedDataView() => InitializeComponent();

    public static readonly DependencyProperty JsonProperty =
        DependencyProperty.Register(nameof(Json), typeof(string), typeof(ExtendedDataView),
            new PropertyMetadata(null, OnJsonChanged));

    public string? Json
    {
        get => (string?)GetValue(JsonProperty);
        set => SetValue(JsonProperty, value);
    }

    private static void OnJsonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ExtendedDataView)d).Items.ItemsSource = Parse(e.NewValue as string);

    public static List<KeyValuePair<string, string>> Parse(string? json)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name = el.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String
                    ? dn.GetString()
                    : el.TryGetProperty("name", out var n) ? n.GetString() : null;
                var value = el.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (!string.IsNullOrEmpty(name))
                    result.Add(new KeyValuePair<string, string>(name!, value ?? ""));
            }
        }
        catch (JsonException) { /* malformed — show nothing */ }
        return result;
    }
}
```

- [ ] **Step 4: Host the panel in the card-detail view**

Locate the desktop card-detail/inspector view that shows a matched/selected card's fields (search for where `CardMatch.Rarity` or `SetName` is displayed — e.g. the scanner detail pane or `ProductEditor`). Add, bound to the selected card's `ExtendedDataJson` (available when `Source is TcgCsvCard`):

```xml
<controls:ExtendedDataView Json="{Binding SelectedCard.ExtendedDataJson}"/>
```

(Add `xmlns:controls="clr-namespace:OmniCard.Controls.Controls;assembly=OmniCard.Controls"` to the view if not present. Expose an `ExtendedDataJson` string on the detail VM: `(_selectedMatch?.Source as TcgCsvCard)?.ExtendedDataJson`.)

- [ ] **Step 5: Run + build + commit**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~ExtendedDataParseTests`
Expected: PASS.
Run: `dotnet build OmniCard`
Expected: Build succeeded.

```bash
git add OmniCard.Controls/Controls/ExtendedDataView.xaml OmniCard.Controls/Controls/ExtendedDataView.xaml.cs OmniCard.Tests/Services/ExtendedDataParseTests.cs OmniCard.Tests/OmniCard.Tests.csproj
git commit -m "feat(tcgcsv): desktop ExtendedData card-detail panel"
```

---

## Task 16: Web wiring — options, filter, and ExtendedData rendering

**Files:**
- Modify: `OmniCard.Web/Pages/Index.cshtml`
- Modify: `OmniCard.Web/Pages/Index.cshtml.cs`
- Test: `OmniCard.Tests/Web/WebPageTests.cs` (append a ParseGameFilter-style test if one exists; otherwise a small unit test of the mapping)

**Interfaces:**
- Produces: three `<option>`s, three `ParseGameFilter` arms (`pokemon`→Pokemon, `yugioh`→YuGiOh, `fftcg`→FinalFantasy), and web-side ExtendedData rendering.

- [ ] **Step 1: Add the `<option>`s**

In `OmniCard.Web/Pages/Index.cshtml`, after the Riftbound option add:

```html
                <option value="pokemon" selected="@(Model.Game == "pokemon" ? "selected" : null)">Pokémon</option>
                <option value="yugioh" selected="@(Model.Game == "yugioh" ? "selected" : null)">Yu-Gi-Oh!</option>
                <option value="fftcg" selected="@(Model.Game == "fftcg" ? "selected" : null)">Final Fantasy TCG</option>
```

- [ ] **Step 2: Add the `ParseGameFilter` arms**

In `OmniCard.Web/Pages/Index.cshtml.cs`, `ParseGameFilter`, add after the `"riftbound" => CardGame.Riftbound,` arm:

```csharp
            "pokemon" => CardGame.Pokemon,
            "yugioh" => CardGame.YuGiOh,
            "fftcg" => CardGame.FinalFantasy,
```

- [ ] **Step 3: Render ExtendedData in the web card view**

In the web card-detail markup (where a card's set/rarity render), add a definition list bound to the card's `ExtendedDataJson`. Because the web card model surfaces cards via `CardMatch`, add a Razor helper on the page model:

```csharp
    public static IEnumerable<(string Name, string Value)> ParseExtendedData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var name = el.TryGetProperty("displayName", out var dn) && dn.ValueKind == System.Text.Json.JsonValueKind.String
                ? dn.GetString() : (el.TryGetProperty("name", out var n) ? n.GetString() : null);
            var value = el.TryGetProperty("value", out var v) ? v.GetString() : null;
            if (!string.IsNullOrEmpty(name)) yield return (name!, value ?? "");
        }
    }
```

and in the card-detail `.cshtml`:

```html
@foreach (var (name, value) in Model.ParseExtendedDataFor(card))
{
    <div class="ext-row"><span class="ext-key">@name</span><span class="ext-val">@value</span></div>
}
```

(Expose `ExtendedDataJson` on whatever web card DTO the page uses — populate it from `(match.Source as TcgCsvCard)?.ExtendedDataJson` when the web service projects cards. If the web layer only carries `CardMatch`, add an `ExtendedDataJson` string to that projection.)

- [ ] **Step 4: Write/append a filter test**

If `OmniCard.Tests/Web/WebPageTests.cs` exists, append a test asserting the new codes parse; otherwise create a focused test mirroring existing web tests. Example (adapt to the actual test harness in that file):

```csharp
[Theory]
[InlineData("pokemon", CardGame.Pokemon)]
[InlineData("yugioh", CardGame.YuGiOh)]
[InlineData("fftcg", CardGame.FinalFantasy)]
public void ParseGameFilter_MapsNewGames(string code, CardGame expected)
{
    // Use the same invocation pattern existing WebPageTests use to reach ParseGameFilter.
    Assert.Equal(expected, WebPageTestHelpers.ParseGameFilter(code));
}
```

> If `ParseGameFilter` is private with no existing test hook, assert instead via the page's public behavior the existing tests use (e.g. the filtered result set for a given `game` query value). Match the existing pattern in `WebPageTests.cs` rather than adding new visibility.

- [ ] **Step 5: Build + test + commit**

Run: `dotnet build OmniCard.Web` and `dotnet test OmniCard.Tests --filter FullyQualifiedName~WebPageTests`
Expected: Build succeeded; PASS.

```bash
git add OmniCard.Web/Pages/Index.cshtml OmniCard.Web/Pages/Index.cshtml.cs OmniCard.Tests/Web/WebPageTests.cs
git commit -m "feat(tcgcsv): web game options, filter parsing, extended-data rendering"
```

---

## Task 17: Full verification & manual smoke

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Full test run**

Run: `dotnet test OmniCard.Tests`
Expected: All tests PASS. Note the total count and confirm no Riftbound/OPTCG/MTG tests regressed.

- [ ] **Step 3: Manual smoke — real download (small game first)**

Launch the desktop app. Select **Final Fantasy TCG** (smallest, ~38 sets). Trigger a data download. Confirm:
- Cards populate; set-code shows on the dashboard tile.
- A card's detail panel shows the full ExtendedData (Element, Cost, Power, Job, etc.).
- Trigger a price refresh; confirm `MarketPrice`/`FoilMarketPrice` populate (spot-check a known card on TCGplayer).

- [ ] **Step 4: Manual smoke — web**

Run `OmniCard.Web`. Confirm the three new games appear in the `<select>`, filtering works, and a card detail renders its ExtendedData.

- [ ] **Step 5: Manual smoke — scan (if a scanner/sample image is available)**

Scan or feed a sample Pokémon/FFTCG card image; confirm OCR collector-number detection fires (check logs for "Collector number detected") and the match resolves. If crop regions miss, note the offsets — OCR region tuning is expected follow-up (see spec Risks).

- [ ] **Step 6: Final commit (if any verification fixes were made)**

```bash
git add -A
git commit -m "chore(tcgcsv): verification fixes for Pokemon/Yugioh/FFTCG"
```

---

## Self-Review Notes (author)

- **Spec coverage:** enum (T1); shared entity + all-data ExtendedDataJson (T2); per-game DbContext/.db (T3); base download/pricing/hashing/matching/queries (T4-T7); three services + DI desktop+web (T8, T11, T12); config-driven OCR (T9); CardService scan/rotate/foil (T10); display names + attribute extractor (T13); dashboard + query (T14); card-detail panel desktop (T15) + web (T16); verification (T17). Sub-type→foil mapping per game covered in T8/T11/T12 tests.
- **Ordering caveat (documented in-task):** `OcrCollectorSpec` (T9) is referenced by services (T8/T11/T12); `CardService` arms (T10) reference all three services. Recommended execution order if strict: T1→T2→T3→T4→T5→T6→T7→T9→T8→T11→T12→T10→T13→T14→T15→T16→T17. The task text flags this explicitly.
- **Verify-at-implementation items:** exact `MissingCard`/`SetCompletionSummary`/`CardMatch` property names (match Riftbound usage); Pokémon/Yu-Gi-Oh `extendedData` key names for `MapExtendedData` (override if not "Number"/"Rarity"/"CardType"); whether `_400w` image variant exists on the CDN; whether `OmniCard.Tests` already references `OmniCard.Controls`.
