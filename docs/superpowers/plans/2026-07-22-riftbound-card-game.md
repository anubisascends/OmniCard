# Riftbound Card Game Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Riftbound TCG to OmniCard — download the full catalog from the Riftcodex API, cache images, compute perceptual hashes, and match scanned cards OCR-first with pHash fallback — mirroring the existing One Piece (`OptcgService`) slice.

**Architecture:** One self-contained vertical slice dispatched by the `CardGame` enum + DI, exactly like One Piece: a `RiftboundCard` entity, a `RiftboundDbContext` → `riftbound.db`, a `RiftboundService : ICardGameService`, plus orientation-aware OCR additions in `OcrMatchingService`. No changes to the MTG or One Piece services. No shared-base-class refactor.

**Tech Stack:** C# / .NET, EF Core + SQLite, System.Text.Json (snake_case), Tesseract OCR, xUnit + Moq (+ Xunit.StaFact for WPF), the shared `IPerceptualHashService`.

## Global Constraints

- **Never modify** `ScryfallService`, `OptcgService`, `Card`, or `OptcgCard` — Riftbound is additive.
- **API base URL:** `https://api.riftcodex.com`, no authentication. Throttle conservatively (≈4 parallel set fetches, 8 parallel image downloads + 50 ms delay) — no documented rate limit.
- **JSON:** deserialize with `JsonNamingPolicy.SnakeCaseLower` + `JsonNumberHandling.AllowReadingFromString`. Do **not** map the API's `new` field (C# keyword) or `cardmarket_id` (heterogeneous string|array) — leaving them unmapped makes System.Text.Json ignore them.
- **Primary key:** the Riftcodex `id` (hex string) — unique per printing; alternate arts are distinct rows.
- **OCR ignores the printed `/total`** — it parses only set code + collector number (printed total 219 ≠ catalog count 280).
- **Pricing deferred:** `UpdatePricesAsync` is a logged no-op; price getters return null/empty.
- **Test/build commands** (PowerShell, from repo root `d:\source\repos\OmniCard`):
  - Build: `dotnet build OmniCard.slnx`
  - Run one test: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~<ClassName>.<TestName>"`
- **Commit** after every task's tests pass.

---

### Task 1: `CardGame.Riftbound` enum + `RiftboundCard` entity + `RiftboundDbContext`

**Files:**
- Modify: `OmniCard.Shared/Models/CardGame.cs`
- Create: `OmniCard.Shared/Models/RiftboundCard.cs`
- Create: `OmniCard.Data/RiftboundDbContext.cs`
- Test: `OmniCard.Tests/Services/RiftboundDbContextTests.cs`

**Interfaces:**
- Produces: `CardGame.Riftbound`; `RiftboundCard` entity (PK `string Id`); `RiftboundDbContext` with `DbSet<RiftboundCard> Cards`, `DbSet<HashCorrection> HashCorrections`, `const int RiftboundSchemaVersion = 1`, and methods `GetSchemaVersion()`, `MarkMigrationComplete()`, `ApplySchemaUpgrades()`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/RiftboundDbContextTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RiftboundDbContext> _options;

    public RiftboundDbContextTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void EnsureCreated_RoundTripsCard_AndStampsVersion()
    {
        using var ctx = new RiftboundDbContext(_options);
        ctx.Database.EnsureCreated();
        ctx.ApplySchemaUpgrades();

        ctx.Cards.Add(new RiftboundCard
        {
            Id = "69bc5bd2d308c64675ca879d",
            RiftboundId = "ogn-209-298",
            CollectorNumber = 209,
            Name = "Cull the Weak",
            SetId = "OGN",
            SetName = "Origins",
            Rarity = "Common",
            CardType = "Spell",
            Domain = "Order",
            Orientation = "portrait",
        });
        ctx.SaveChanges();

        Assert.Equal(0, ctx.GetSchemaVersion());
        ctx.MarkMigrationComplete();
        Assert.Equal(RiftboundDbContext.RiftboundSchemaVersion, ctx.GetSchemaVersion());

        var row = ctx.Cards.Single();
        Assert.Equal("Cull the Weak", row.Name);
        Assert.Equal(209, row.CollectorNumber);
        Assert.Equal("OGN", row.SetId);
    }

    [Fact]
    public void CardGameEnum_ContainsRiftbound()
    {
        Assert.True(Enum.IsDefined(CardGame.Riftbound));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundDbContextTests"`
Expected: FAIL — `CardGame.Riftbound`, `RiftboundCard`, and `RiftboundDbContext` do not exist (compile error).

- [ ] **Step 3: Add the enum value**

In `OmniCard.Shared/Models/CardGame.cs`, add `Riftbound`:

```csharp
namespace OmniCard.Models;

public enum CardGame
{
    Mtg,
    OnePiece,
    Riftbound
}
```

- [ ] **Step 4: Create the entity**

Create `OmniCard.Shared/Models/RiftboundCard.cs`:

```csharp
namespace OmniCard.Models;

// Persistence entity for Riftbound cards. One row per printing.
// Populated by RiftboundService from api.riftcodex.com DTOs (see RiftboundApiModels).
public class RiftboundCard
{
    // Riftcodex hex id — unique per printing (alt arts are distinct rows). Primary key.
    public string Id { get; set; } = "";

    // Riftbound id, e.g. "ogn-209-298"; alt arts carry a '*' (e.g. "ogn-310*-298").
    public string RiftboundId { get; set; } = "";
    public string? TcgplayerId { get; set; }

    // Printed collector number, e.g. 150. NOT unique — shared across alt-art printings.
    public int CollectorNumber { get; set; }

    public string Name { get; set; } = "";
    public string? CleanName { get; set; }
    public string SetId { get; set; } = "";      // e.g. "OGN"
    public string SetName { get; set; } = "";     // e.g. "Origins"
    public string Rarity { get; set; } = "";
    public string CardType { get; set; } = "";    // Unit / Spell / Legend / Battlefield
    public string? Supertype { get; set; }
    public string Domain { get; set; } = "";       // domain[] joined with '/', e.g. "Body/Order"
    public int? Energy { get; set; }
    public int? Might { get; set; }
    public int? Power { get; set; }
    public string? CardText { get; set; }
    public string? Flavour { get; set; }
    public string? Artist { get; set; }
    public string Orientation { get; set; } = "portrait"; // "portrait" | "landscape"
    public bool AlternateArt { get; set; }
    public bool Overnumbered { get; set; }
    public bool Signature { get; set; }
    public string? CardImageUri { get; set; }
    public string? DateScraped { get; set; }

    // Computed locally, not from API
    public ulong? ImageHash { get; set; }
    public ulong? EdgeHash { get; set; }
    public string? LocalImagePath { get; set; }
}
```

- [ ] **Step 5: Create the DbContext**

Create `OmniCard.Data/RiftboundDbContext.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class RiftboundDbContext : DbContext
{
    public DbSet<RiftboundCard> Cards => Set<RiftboundCard>();
    public DbSet<HashCorrection> HashCorrections => Set<HashCorrection>();

    public RiftboundDbContext(DbContextOptions<RiftboundDbContext> options) : base(options) { }

    // Bump when the on-disk schema/data source changes incompatibly; a stored
    // user_version below this triggers a wipe-and-redownload in RiftboundService.
    public const int RiftboundSchemaVersion = 1;

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
        cmd.CommandText = $"PRAGMA user_version = {RiftboundSchemaVersion};";
        cmd.ExecuteNonQuery();
    }

    public void ApplySchemaUpgrades()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        // Reserved for future additive columns (see OptcgDbContext for the pattern).
        AddColumnIfMissing(conn, "EdgeHash INTEGER");
        AddColumnIfMissing(conn, "LocalImagePath TEXT");
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
        var card = modelBuilder.Entity<RiftboundCard>();
        card.HasKey(c => c.Id);
        card.HasIndex(c => c.Name);
        card.HasIndex(c => c.SetId);
        card.HasIndex(c => c.CollectorNumber);
        card.HasIndex(c => c.ImageHash);
        card.HasIndex(c => c.EdgeHash);

        modelBuilder.Entity<HashCorrection>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedOnAdd();
            e.HasIndex(h => h.ScanHash).IsUnique();
        });
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundDbContextTests"`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Models/CardGame.cs OmniCard.Shared/Models/RiftboundCard.cs OmniCard.Data/RiftboundDbContext.cs OmniCard.Tests/Services/RiftboundDbContextTests.cs
git commit -m "feat(riftbound): add CardGame.Riftbound, RiftboundCard entity, RiftboundDbContext"
```

---

### Task 2: Riftcodex API DTOs

**Files:**
- Create: `OmniCard.Shared/Models/RiftboundApiModels.cs`
- Test: `OmniCard.Tests/Services/RiftboundApiModelsTests.cs`

**Interfaces:**
- Produces: `RiftboundCardListResponse { List<RiftboundApiCard> Items; int Total, Page, Size, Pages; }`, `RiftboundApiCard`, `RiftboundApiAttributes`, `RiftboundApiClassification`, `RiftboundApiText`, `RiftboundApiCardSet`, `RiftboundApiMedia`, `RiftboundApiMetadata`, `RiftboundSetListResponse { List<RiftboundApiSetSummary> Items; }`, `RiftboundApiSetSummary`. All deserialized with `JsonNamingPolicy.SnakeCaseLower`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/RiftboundApiModelsTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundApiModelsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // Verbatim shape from api.riftcodex.com/cards (one base + one alt-art item).
    private const string CardListJson = """
    {"items":[
      {"id":"69bc5bd2d308c64675ca879d","name":"Cull the Weak","riftbound_id":"ogn-209-298",
       "tcgplayer_id":"653002","collector_number":209,
       "attributes":{"energy":2,"might":null,"power":1},
       "classification":{"type":"Spell","supertype":null,"rarity":"Common","domain":["Order"]},
       "text":{"rich":"<p>x</p>","plain":"Each player kills one of their units.","flavour":"Flav."},
       "set":{"set_id":"OGN","label":"Origins"},
       "media":{"image_url":"https://cdn/x.png","artist":"Kudos","accessibility_text":"alt"},
       "tags":[],"orientation":"portrait",
       "metadata":{"clean_name":"Cull the Weak","updated_on":"2026-07-10T22:44:35Z",
                   "alternate_art":false,"overnumbered":false,"signature":false},"new":false},
      {"id":"aaaa1111bbbb2222cccc3333","name":"Vex","riftbound_id":"ogn-310*-298",
       "tcgplayer_id":null,"collector_number":310,
       "attributes":{"energy":4,"might":null,"power":4},
       "classification":{"type":"Legend","supertype":null,"rarity":"Epic","domain":["Body","Order"]},
       "text":{"rich":null,"plain":null,"flavour":null},
       "set":{"set_id":"OGN","label":"Origins"},
       "media":{"image_url":"https://cdn/vex.png","artist":"Splash","accessibility_text":null},
       "tags":[],"orientation":"landscape",
       "metadata":{"clean_name":"Vex","updated_on":"2026-07-10T22:44:35Z",
                   "alternate_art":true,"overnumbered":true,"signature":false},"new":true}
    ],"total":352,"page":1,"size":50,"pages":8}
    """;

    private const string SetListJson = """
    {"items":[
      {"id":"69bc5bf6e195be3e561d1eae","name":"Unleashed","set_id":"UNL","card_count":280,
       "tcgplayer_id":"24560","cardmarket_id":null,"published_on":"2026-05-08T00:00:00"},
      {"id":"69bc5bf6e195be3e561d1eb1","name":"Origins","set_id":"OGN","card_count":352,
       "tcgplayer_id":"24344","cardmarket_id":"6286","published_on":"2025-10-31T00:00:00"},
      {"id":"69bc5bf6e195be3e561d1eb3","name":"OP Promos","set_id":"OPP","card_count":133,
       "tcgplayer_id":"24343","cardmarket_id":["6322","6483"],"published_on":"2025-10-31T00:00:00"}
    ],"total":3,"page":1,"size":100,"pages":1}
    """;

    [Fact]
    public void DeserializesCardList_IncludingNestedAndAltArt()
    {
        var resp = JsonSerializer.Deserialize<RiftboundCardListResponse>(CardListJson, Options)!;
        Assert.Equal(8, resp.Pages);
        Assert.Equal(2, resp.Items.Count);

        var cull = resp.Items[0];
        Assert.Equal("69bc5bd2d308c64675ca879d", cull.Id);
        Assert.Equal("ogn-209-298", cull.RiftboundId);
        Assert.Equal(209, cull.CollectorNumber);
        Assert.Equal(2, cull.Attributes.Energy);
        Assert.Null(cull.Attributes.Might);
        Assert.Equal("Spell", cull.Classification.Type);
        Assert.Equal(["Order"], cull.Classification.Domain);
        Assert.Equal("OGN", cull.Set.SetId);
        Assert.Equal("Origins", cull.Set.Label);
        Assert.Equal("https://cdn/x.png", cull.Media.ImageUrl);
        Assert.Equal("portrait", cull.Orientation);
        Assert.False(cull.Metadata.AlternateArt);

        var vex = resp.Items[1];
        Assert.Equal("ogn-310*-298", vex.RiftboundId);
        Assert.True(vex.Metadata.AlternateArt);
        Assert.True(vex.Metadata.Overnumbered);
        Assert.Equal("landscape", vex.Orientation);
        Assert.Equal(["Body", "Order"], vex.Classification.Domain);
    }

    [Fact]
    public void DeserializesSetList_IgnoringHeterogeneousCardmarketId()
    {
        var resp = JsonSerializer.Deserialize<RiftboundSetListResponse>(SetListJson, Options)!;
        Assert.Equal(3, resp.Items.Count);
        Assert.Equal("UNL", resp.Items[0].SetId);
        Assert.Equal(280, resp.Items[0].CardCount);
        Assert.Equal("Origins", resp.Items[1].Name);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundApiModelsTests"`
Expected: FAIL — DTO types do not exist (compile error).

- [ ] **Step 3: Create the DTOs**

Create `OmniCard.Shared/Models/RiftboundApiModels.cs`:

```csharp
namespace OmniCard.Models;

// Response DTOs for api.riftcodex.com.
// Deserialized with JsonNamingPolicy.SnakeCaseLower (no [JsonPropertyName] needed).
// The API's `new` (C# keyword) and `cardmarket_id` (string|array) fields are intentionally
// unmapped — System.Text.Json ignores unmapped JSON properties by default.

public sealed class RiftboundCardListResponse
{
    public List<RiftboundApiCard> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int Pages { get; set; }
}

public sealed class RiftboundApiCard
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string RiftboundId { get; set; } = "";
    public string? TcgplayerId { get; set; }
    public int CollectorNumber { get; set; }
    public RiftboundApiAttributes Attributes { get; set; } = new();
    public RiftboundApiClassification Classification { get; set; } = new();
    public RiftboundApiText Text { get; set; } = new();
    public RiftboundApiCardSet Set { get; set; } = new();
    public RiftboundApiMedia Media { get; set; } = new();
    public string Orientation { get; set; } = "portrait";
    public RiftboundApiMetadata Metadata { get; set; } = new();
}

public sealed class RiftboundApiAttributes
{
    public int? Energy { get; set; }
    public int? Might { get; set; }
    public int? Power { get; set; }
}

public sealed class RiftboundApiClassification
{
    public string Type { get; set; } = "";
    public string? Supertype { get; set; }
    public string? Rarity { get; set; }
    public List<string> Domain { get; set; } = [];
}

public sealed class RiftboundApiText
{
    public string? Rich { get; set; }
    public string? Plain { get; set; }
    public string? Flavour { get; set; }
}

public sealed class RiftboundApiCardSet
{
    public string SetId { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class RiftboundApiMedia
{
    public string? ImageUrl { get; set; }
    public string? Artist { get; set; }
    public string? AccessibilityText { get; set; }
}

public sealed class RiftboundApiMetadata
{
    public string? CleanName { get; set; }
    public string? UpdatedOn { get; set; }
    public bool AlternateArt { get; set; }
    public bool Overnumbered { get; set; }
    public bool Signature { get; set; }
}

public sealed class RiftboundSetListResponse
{
    public List<RiftboundApiSetSummary> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int Pages { get; set; }
}

public sealed class RiftboundApiSetSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SetId { get; set; } = "";
    public int CardCount { get; set; }
    public string? TcgplayerId { get; set; }
    public DateTimeOffset? PublishedOn { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundApiModelsTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Shared/Models/RiftboundApiModels.cs OmniCard.Tests/Services/RiftboundApiModelsTests.cs
git commit -m "feat(riftbound): add Riftcodex API DTOs"
```

---

### Task 3: Attribute extractor + display converter Riftbound arms

**Files:**
- Modify: `OmniCard.CardMatching/CardAttributeExtractor.cs:19-37`
- Modify: `OmniCard.Controls/Converters/RootConverters.cs` (every `CardGameDisplayConverter.Convert` switch — there are two, ~line 150 and ~line 300)
- Test: `OmniCard.Tests/Services/RiftboundAttributeExtractorTests.cs`

**Interfaces:**
- Consumes: `RiftboundCard` (Task 1), `CardGame.Riftbound` (Task 1).
- Produces: `CardAttributeExtractor.ExtractColor`/`ExtractCardType` handle `CardGame.Riftbound`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/RiftboundAttributeExtractorTests.cs`:

```csharp
using OmniCard.CardMatching;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundAttributeExtractorTests
{
    [Fact]
    public void ExtractColorAndType_ReadFromRiftboundSource()
    {
        var card = new RiftboundCard { Domain = "Body/Order", CardType = "Legend" };
        var match = new CardMatch { Name = "Vex", Source = card };

        Assert.Equal("Body/Order", CardAttributeExtractor.ExtractColor(match, CardGame.Riftbound));
        Assert.Equal("Legend", CardAttributeExtractor.ExtractCardType(match, CardGame.Riftbound));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundAttributeExtractorTests"`
Expected: FAIL — extractor returns `null` for `CardGame.Riftbound` (the `_ => null` arm), assertions fail.

- [ ] **Step 3: Add the extractor arms**

In `OmniCard.CardMatching/CardAttributeExtractor.cs`, add a `Riftbound` arm to both switches:

```csharp
    public static string? ExtractColor(CardMatch match, CardGame game)
    {
        return game switch
        {
            CardGame.Mtg => ExtractMtgColor(match.Source as Card),
            CardGame.OnePiece => (match.Source as OptcgCard)?.CardColor,
            CardGame.Riftbound => (match.Source as RiftboundCard)?.Domain,
            _ => null
        };
    }

    public static string? ExtractCardType(CardMatch match, CardGame game)
    {
        return game switch
        {
            CardGame.Mtg => ExtractMtgCardType(match.Source as Card),
            CardGame.OnePiece => (match.Source as OptcgCard)?.CardType,
            CardGame.Riftbound => (match.Source as RiftboundCard)?.CardType,
            _ => null
        };
    }
```

- [ ] **Step 4: Add the display-converter arm(s)**

In `OmniCard.Controls/Converters/RootConverters.cs`, add `CardGame.Riftbound => "Riftbound",` to **every** `CardGameDisplayConverter` switch. Find them with:

Run: `grep -n "One Piece TCG" OmniCard.Controls/Converters/RootConverters.cs`

For each match, add the arm above the `_ =>` default, e.g.:

```csharp
            CardGame.Mtg => "Magic: The Gathering",
            CardGame.OnePiece => "One Piece TCG",
            CardGame.Riftbound => "Riftbound",
            _ => value?.ToString() ?? ""
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundAttributeExtractorTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.CardMatching/CardAttributeExtractor.cs OmniCard.Controls/Converters/RootConverters.cs OmniCard.Tests/Services/RiftboundAttributeExtractorTests.cs
git commit -m "feat(riftbound): map domain/type attributes and display name"
```

---

### Task 4: `RiftboundService` — download, mapping, no-op pricing + DI registration

**Files:**
- Create: `OmniCard.CardMatching/RiftboundService.cs`
- Modify: `OmniCard/App.xaml.cs:106-110` (after the One Piece block)
- Modify: `OmniCard.Web/Program.cs:49-62`
- Test: `OmniCard.Tests/Services/RiftboundDownloadTests.cs`

**Interfaces:**
- Consumes: `RiftboundDbContext` (Task 1), `RiftboundCard` (Task 1), all DTOs (Task 2), shared `IHttpClientFactory`, `IDbContextFactory<RiftboundDbContext>`, `IPerceptualHashService`, `IDataPathService`, `ILogger<RiftboundService>`.
- Produces: `RiftboundService : ICardGameService, IDisposable` with `Game => CardGame.Riftbound`, `DownloadBulkDataAsync`, `UpdatePricesAsync` (no-op), and the full `ICardGameService` surface. Internal `static RiftboundCard MapCard(RiftboundApiCard)`. Art path scheme `riftbound-art/{Id}.png`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/RiftboundDownloadTests.cs`:

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

public class RiftboundDownloadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<RiftboundDbContext> _factory;
    private readonly string _dataDir;

    public RiftboundDownloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
        _factory = new TestFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();

        _dataDir = Path.Combine(Path.GetTempPath(), "rift-dl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private const string SetListJson = """
    {"items":[{"id":"s1","name":"Origins","set_id":"OGN","card_count":2,
      "tcgplayer_id":"24344","cardmarket_id":"6286","published_on":"2025-10-31T00:00:00"}],
      "total":1,"page":1,"size":100,"pages":1}
    """;

    // OGN has 2 cards spread over 2 pages (size=1) to exercise paging.
    private static string CardsPage(int page) => page switch
    {
        1 => """
        {"items":[{"id":"c1","name":"Cull the Weak","riftbound_id":"ogn-209-298","tcgplayer_id":"653002",
          "collector_number":209,"attributes":{"energy":2,"might":null,"power":1},
          "classification":{"type":"Spell","supertype":null,"rarity":"Common","domain":["Order"]},
          "text":{"rich":null,"plain":"kill","flavour":null},"set":{"set_id":"OGN","label":"Origins"},
          "media":{"image_url":"https://cdn/c1.png","artist":"A","accessibility_text":null},
          "tags":[],"orientation":"portrait",
          "metadata":{"clean_name":"Cull the Weak","updated_on":null,"alternate_art":false,
            "overnumbered":false,"signature":false},"new":false}],
          "total":2,"page":1,"size":1,"pages":2}
        """,
        _ => """
        {"items":[{"id":"c2","name":"Vex","riftbound_id":"ogn-310*-298","tcgplayer_id":null,
          "collector_number":310,"attributes":{"energy":4,"might":null,"power":4},
          "classification":{"type":"Legend","supertype":null,"rarity":"Epic","domain":["Body","Order"]},
          "text":{"rich":null,"plain":null,"flavour":null},"set":{"set_id":"OGN","label":"Origins"},
          "media":{"image_url":"https://cdn/c2.png","artist":"B","accessibility_text":null},
          "tags":[],"orientation":"landscape",
          "metadata":{"clean_name":"Vex","updated_on":null,"alternate_art":true,
            "overnumbered":true,"signature":false},"new":false}],
          "total":2,"page":2,"size":1,"pages":2}
        """
    };

    private RiftboundService CreateService()
    {
        var handler = new RoutingHandler(uri =>
        {
            if (uri.Contains("/sets")) return SetListJson;
            if (uri.Contains("/cards"))
            {
                var page = uri.Contains("page=2") ? 2 : 1;
                return CardsPage(page);
            }
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

    [Fact]
    public async Task DownloadBulkData_PagesThroughAllCards_AndMaps()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();

        using var ctx = _factory.CreateDbContext();
        var rows = ctx.Cards.OrderBy(c => c.CollectorNumber).ToList();
        Assert.Equal(2, rows.Count);

        var cull = rows.Single(c => c.Id == "c1");
        Assert.Equal(209, cull.CollectorNumber);
        Assert.Equal("Cull the Weak", cull.Name);
        Assert.Equal("OGN", cull.SetId);
        Assert.Equal("Origins", cull.SetName);
        Assert.Equal("Spell", cull.CardType);
        Assert.Equal("Order", cull.Domain);
        Assert.Equal("portrait", cull.Orientation);
        Assert.Equal("https://cdn/c1.png", cull.CardImageUri);
        Assert.False(cull.AlternateArt);

        var vex = rows.Single(c => c.Id == "c2");
        Assert.Equal("Body/Order", vex.Domain);
        Assert.Equal("landscape", vex.Orientation);
        Assert.True(vex.AlternateArt);
        Assert.True(vex.Overnumbered);

        Assert.Equal(RiftboundDbContext.RiftboundSchemaVersion, ctx.GetSchemaVersion());
    }

    [Fact]
    public async Task UpdatePrices_IsNoOp()
    {
        var svc = CreateService();
        await svc.DownloadBulkDataAsync();
        // Should not throw and should not alter rows.
        await svc.UpdatePricesAsync();
        using var ctx = _factory.CreateDbContext();
        Assert.Equal(2, ctx.Cards.Count());
    }

    private class RoutingHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = route(request.RequestUri!.ToString());
            var resp = body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(resp);
        }
    }

    private class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class TestFactory(DbContextOptions<RiftboundDbContext> options) : IDbContextFactory<RiftboundDbContext>
    {
        public RiftboundDbContext CreateDbContext() => new(options);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundDownloadTests"`
Expected: FAIL — `RiftboundService` does not exist (compile error).

- [ ] **Step 3: Create the service (download + mapping + no-op pricing + full interface)**

Create `OmniCard.CardMatching/RiftboundService.cs`. This mirrors `OptcgService` (paging replaces per-set detail; `Id` replaces `CardSetId`; no prices):

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

public sealed class RiftboundService : ICardGameService, IDisposable
{
    private const string ApiBaseUrl = "https://api.riftcodex.com";
    private const int CorrectionTrustBonus = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<RiftboundDbContext> _dbContextFactory;
    private readonly IPerceptualHashService _hashService;
    private readonly ILogger<RiftboundService> _logger;
    private readonly string _dataDirectory;
    private RiftboundDbContext _readContext;

    private List<(string Id, ulong Hash)>? _hashCache;
    private List<(string Id, ulong EdgeHash, string SetId)>? _edgeHashCache;
    private Dictionary<string, string>? _hashSetLookup;
    private List<(ulong ScanHash, string CorrectCardId)>? _correctionsCache;

    private static JsonSerializerOptions JsonOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public RiftboundService(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<RiftboundDbContext> dbContextFactory,
        IPerceptualHashService hashService,
        IDataPathService dataPathService,
        ILogger<RiftboundService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _hashService = hashService;
        _dataDirectory = dataPathService.DataDirectory;
        _logger = logger;

        _logger.LogInformation("Initializing Riftbound service");
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

        if (_readContext.GetSchemaVersion() < RiftboundDbContext.RiftboundSchemaVersion)
        {
            _logger.LogWarning("Riftbound database predates current schema; wiping for migration");
            WipeForMigration();
        }

        _logger.LogInformation("Riftbound database ready at {DbPath}", dbPath);
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
            _logger.LogWarning(ex, "Riftbound database is read-only; skipping migration wipe");
        }

        var artDir = Path.Combine(_dataDirectory, "riftbound-art");
        if (Directory.Exists(artDir))
        {
            try { Directory.Delete(artDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete Riftbound art directory during migration wipe");
            }
        }

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        _correctionsCache = null;
        oldContext.Dispose();
    }

    public CardGame Game => CardGame.Riftbound;
    public MatchDiagnostics? LastMatchDiagnostics { get; private set; }

    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Riftbound card data download from Riftcodex API");
        var sw = Stopwatch.StartNew();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        progress?.Report("Fetching Riftbound set list...");
        var allCards = await FetchAllCardsAsync(client,
            (done, total, code) => progress?.Report($"Fetched {done}/{total} sets ({code})..."), ct);

        var deduped = allCards
            .GroupBy(c => c.Id)
            .Select(g => g.Last())
            .ToList();
        _logger.LogInformation("Fetched {Total} card rows ({Unique} unique)", allCards.Count, deduped.Count);
        progress?.Report($"Fetched {deduped.Count} cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingIds = (await importContext.Cards.Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        var inserted = 0;
        var updated = 0;

        foreach (var batch in deduped.Chunk(500))
        {
            var newCards = new List<RiftboundCard>();
            var existingCardIds = new List<string>();

            foreach (var card in batch)
            {
                if (existingIds.Contains(card.Id)) existingCardIds.Add(card.Id);
                else newCards.Add(card);
            }

            if (existingCardIds.Count > 0)
            {
                var tracked = await importContext.Cards
                    .Where(c => existingCardIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, ct);

                foreach (var card in batch)
                {
                    if (tracked.TryGetValue(card.Id, out var existing))
                    {
                        // Refresh metadata; preserve computed ImageHash/EdgeHash/LocalImagePath.
                        existing.RiftboundId = card.RiftboundId;
                        existing.TcgplayerId = card.TcgplayerId;
                        existing.CollectorNumber = card.CollectorNumber;
                        existing.Name = card.Name;
                        existing.CleanName = card.CleanName;
                        existing.SetId = card.SetId;
                        existing.SetName = card.SetName;
                        existing.Rarity = card.Rarity;
                        existing.CardType = card.CardType;
                        existing.Supertype = card.Supertype;
                        existing.Domain = card.Domain;
                        existing.Energy = card.Energy;
                        existing.Might = card.Might;
                        existing.Power = card.Power;
                        existing.CardText = card.CardText;
                        existing.Flavour = card.Flavour;
                        existing.Artist = card.Artist;
                        existing.Orientation = card.Orientation;
                        existing.AlternateArt = card.AlternateArt;
                        existing.Overnumbered = card.Overnumbered;
                        existing.Signature = card.Signature;
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
                foreach (var card in newCards) existingIds.Add(card.Id);
                inserted += newCards.Count;
            }

            progress?.Report($"Processed {inserted + updated} cards ({inserted} new, {updated} updated)...");
        }

        if (deduped.Count > 0)
            importContext.MarkMigrationComplete();

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("Riftbound download complete: {Inserted} new, {Updated} updated in {Sec:F1}s", inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        if (inserted > 0)
            await ComputeImageHashesAsync(forceAll: false, progress, ct);
    }

    // Fetch the set list, then page every set's cards. onSetCompleted(done,total,setId) after each set.
    private async Task<List<RiftboundCard>> FetchAllCardsAsync(
        HttpClient client, Action<int, int, string>? onSetCompleted, CancellationToken ct)
    {
        var setList = await client.GetFromJsonAsync<RiftboundSetListResponse>(
            $"{ApiBaseUrl}/sets?size=100", JsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to fetch set list from Riftcodex API.");

        _logger.LogInformation("Discovered {Count} Riftbound sets", setList.Items.Count);

        var allCards = new List<RiftboundCard>();
        var cardsLock = new object();
        var fetchedSets = 0;

        await Parallel.ForEachAsync(setList.Items, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        }, async (set, token) =>
        {
            try
            {
                var page = 1;
                var pages = 1;
                do
                {
                    var resp = await client.GetFromJsonAsync<RiftboundCardListResponse>(
                        $"{ApiBaseUrl}/cards?set_id={set.SetId}&size=100&page={page}", JsonOptions, token);
                    if (resp is null) break;
                    pages = resp.Pages;

                    var rows = resp.Items.Select(MapCard).ToList();
                    lock (cardsLock) allCards.AddRange(rows);
                    page++;
                } while (page <= pages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch Riftbound set {SetId}; skipping", set.SetId);
            }
            finally
            {
                var done = Interlocked.Increment(ref fetchedSets);
                onSetCompleted?.Invoke(done, setList.Items.Count, set.SetId);
            }
        });

        return allCards;
    }

    // Riftcodex returns no prices; pricing is out of scope this pass.
    public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Riftbound price refresh skipped — pricing is not wired for Riftbound yet");
        progress?.Report(new PriceUpdateProgress(CardGame.Riftbound, null, 0, 0, "Riftbound pricing not available"));
        return Task.CompletedTask;
    }

    internal static RiftboundCard MapCard(RiftboundApiCard c) => new()
    {
        Id = c.Id,
        RiftboundId = c.RiftboundId,
        TcgplayerId = c.TcgplayerId,
        CollectorNumber = c.CollectorNumber,
        Name = c.Name,
        CleanName = c.Metadata.CleanName,
        SetId = c.Set.SetId,
        SetName = c.Set.Label,
        Rarity = c.Classification.Rarity ?? "",
        CardType = c.Classification.Type,
        Supertype = c.Classification.Supertype,
        Domain = string.Join("/", c.Classification.Domain),
        Energy = c.Attributes.Energy,
        Might = c.Attributes.Might,
        Power = c.Attributes.Power,
        CardText = c.Text.Plain,
        Flavour = c.Text.Flavour,
        Artist = c.Media.Artist,
        Orientation = c.Orientation,
        AlternateArt = c.Metadata.AlternateArt,
        Overnumbered = c.Metadata.Overnumbered,
        Signature = c.Metadata.Signature,
        CardImageUri = c.Media.ImageUrl,
        DateScraped = DateTime.UtcNow.ToString("o"),
    };

    // === Image hashing (Task 5 fills the body) ===
    public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default)
        => throw new NotImplementedException("Implemented in Task 5");

    // === Matching (Task 6 fills the body) ===
    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        => throw new NotImplementedException("Implemented in Task 6");

    // === Query surface ===
    public IReadOnlyList<SetInfo> GetAvailableSets()
        => _readContext.Cards.AsNoTracking()
            .Select(c => new { c.SetId, c.SetName }).Distinct()
            .OrderBy(s => s.SetName).AsEnumerable()
            .Select(s => new SetInfo(s.SetId, s.SetName)).ToList();

    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
    {
        var setTotals = _readContext.Cards.AsNoTracking()
            .Select(c => new { c.SetId, c.SetName, c.CollectorNumber }).Distinct()
            .AsEnumerable()
            .GroupBy(c => new { c.SetId, c.SetName })
            .Select(g => new { g.Key.SetId, g.Key.SetName, Total = g.Count() })
            .ToDictionary(s => s.SetId, s => (s.SetName, s.Total));

        var ownedPerSet = ownedCards
            .GroupBy(c => c.SetCode)
            .ToDictionary(g => g.Key, g => (Distinct: g.Select(c => c.Number).Distinct().Count(), Physical: g.Count()));

        var results = new List<SetCompletionSummary>();
        foreach (var (setId, (setName, total)) in setTotals)
        {
            ownedPerSet.TryGetValue(setId, out var owned);
            results.Add(new SetCompletionSummary
            {
                SetCode = setId,
                SetName = setName,
                OwnedCount = owned.Distinct,
                OwnedPhysicalCount = owned.Physical,
                TotalCount = total,
                Game = CardGame.Riftbound,
            });
        }
        return Task.FromResult(results);
    }

    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers)
    {
        var ownedSet = ownedCollectorNumbers.ToHashSet();
        return _readContext.Cards.AsNoTracking()
            .Where(c => c.SetId == setCode).AsEnumerable()
            .Where(c => !ownedSet.Contains(c.CollectorNumber.ToString()))
            .GroupBy(c => c.CollectorNumber)
            .Select(g => g.OrderBy(c => c.AlternateArt).First())
            .Select(c => new MissingCard
            {
                Name = c.Name,
                CollectorNumber = c.CollectorNumber.ToString(),
                SetCode = c.SetId,
                Rarity = c.Rarity,
                ImageUri = c.CardImageUri,
                TypeLine = c.CardType,
                OracleText = c.CardText,
                Power = c.Power?.ToString(),
                CardColor = c.Domain,
                CardCost = c.Energy?.ToString(),
            })
            .OrderBy(m => m.CollectorNumber).ToList();
    }

    public List<CardMatch> SearchCards(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        IQueryable<RiftboundCard> cards = _readContext.Cards.AsNoTracking();
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = term;
            if (t.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[4..];
                cards = cards.Where(c => EF.Functions.Like(c.SetId, $"%{val}%") || EF.Functions.Like(c.SetName, $"%{val}%"));
            }
            else if (t.StartsWith("type:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[(t.IndexOf(':') + 1)..];
                cards = cards.Where(c => EF.Functions.Like(c.CardType, $"%{val}%"));
            }
            else
            {
                cards = cards.Where(c => EF.Functions.Like(c.Name, $"%{t}%"));
            }
        }
        return cards.OrderBy(c => c.Name).Take(maxResults).AsEnumerable().Select(ToMatch).ToList();
    }

    public List<CardMatch> GetPrintings(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return [];
        return _readContext.Cards.AsNoTracking()
            .Where(c => c.Name == cardName)
            .OrderBy(c => c.SetName).ThenBy(c => c.CollectorNumber)
            .AsEnumerable().Select(ToMatch).ToList();
    }

    // Riftcodex provides no prices.
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => [];

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
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().FirstOrDefault(c => c.Id == gameCardId);
    }

    internal CardMatch ToMatch(RiftboundCard c, double? confidence = null) => new()
    {
        Name = c.Name,
        SetCode = c.SetId,
        SetName = c.SetName,
        CollectorNumber = c.CollectorNumber.ToString(),
        Rarity = c.Rarity,
        ImageUri = c.CardImageUri,
        GameSpecificId = c.Id,
        LocalImagePath = ResolveLocalArtPath(c.LocalImagePath),
        Confidence = confidence,
        Source = c
    };

    internal static string GetLocalArtRelativePath(string id) => $"riftbound-art/{id}.png";
    internal string GetLocalArtFullPath(string id) => Path.Combine(_dataDirectory, "riftbound-art", $"{id}.png");
    private string? ResolveLocalArtPath(string? relativePath)
    {
        if (relativePath is null) return null;
        var full = Path.Combine(_dataDirectory, relativePath);
        return File.Exists(full) ? full : null;
    }

    public void Dispose() => _readContext.Dispose();
}
```

> Note: `ComputeImageHashesAsync` and `FindClosestMatch` intentionally throw `NotImplementedException` here; the two download tests do not call them. Tasks 5 and 6 replace those bodies.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundDownloadTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Register in desktop DI**

In `OmniCard/App.xaml.cs`, after the One Piece block (line 109) and before `PriceUpdateService` (line 110):

```csharp
            // Riftbound (Riftcodex)
            services.AddDbContextFactory<RiftboundDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "riftbound.db")}"));
            services.AddSingleton<ICardGameService, RiftboundService>();
```

- [ ] **Step 6: Register in web DI**

In `OmniCard.Web/Program.cs`, add the context (after line 50) and the service (after line 62):

```csharp
builder.Services.AddDbContextFactory<RiftboundDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "riftbound.db")};Mode=ReadOnly"));
```

```csharp
builder.Services.AddSingleton<RiftboundService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<RiftboundService>());
```

Add `using OmniCard.Data;` / `using OmniCard.CardMatching;` at the top of `Program.cs` if not already present (they are — Scryfall/Optcg use them).

- [ ] **Step 7: Build to verify DI compiles**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeded (0 errors). Warnings about the two `NotImplementedException` bodies are acceptable at this stage.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.CardMatching/RiftboundService.cs OmniCard/App.xaml.cs OmniCard.Web/Program.cs OmniCard.Tests/Services/RiftboundDownloadTests.cs
git commit -m "feat(riftbound): RiftboundService download/mapping + no-op pricing + DI"
```

---

### Task 5: `ComputeImageHashesAsync`

**Files:**
- Modify: `OmniCard.CardMatching/RiftboundService.cs` (replace the `ComputeImageHashesAsync` stub; add `SaveHashBatchAsync`)
- Test: `OmniCard.Tests/Services/RiftboundHashingTests.cs`

**Interfaces:**
- Consumes: `RiftboundService` (Task 4), `IPerceptualHashService.ComputeHash`/`ComputeEdgeHash`.
- Produces: `ComputeImageHashesAsync` populates `ImageHash`, `EdgeHash`, `LocalImagePath` and caches the downloaded image at `riftbound-art/{Id}.png`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/RiftboundHashingTests.cs`:

```csharp
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;
using SkiaSharp;

namespace OmniCard.Tests.Services;

public class RiftboundHashingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<RiftboundDbContext> _factory;
    private readonly string _dataDir;

    public RiftboundHashingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
        _factory = new Factory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();
        ctx.Cards.Add(new RiftboundCard
        {
            Id = "c1", RiftboundId = "ogn-1-298", CollectorNumber = 1, Name = "Test",
            SetId = "OGN", SetName = "Origins", CardImageUri = "https://cdn/c1.png",
        });
        ctx.SaveChanges();

        _dataDir = Path.Combine(Path.GetTempPath(), "rift-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private static byte[] PngBytes()
    {
        using var bmp = new SKBitmap(64, 90);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(10, 10, 30, 40, paint);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    [Fact]
    public async Task ComputeImageHashes_PopulatesHashesAndPath()
    {
        var png = PngBytes();
        var handler = new StubHandler(png);
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        var svc = new RiftboundService(
            new FakeFactory(handler), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object, NullLogger<RiftboundService>.Instance);

        await svc.ComputeImageHashesAsync(forceAll: true);

        using var ctx = _factory.CreateDbContext();
        var row = ctx.Cards.Single();
        Assert.NotNull(row.ImageHash);
        Assert.NotNull(row.EdgeHash);
        Assert.Equal("riftbound-art/c1.png", row.LocalImagePath);
        Assert.True(File.Exists(Path.Combine(_dataDir, "riftbound-art", "c1.png")));
    }

    private class StubHandler(byte[] png) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(png) });
    }
    private class FakeFactory(HttpMessageHandler h) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(h); }
    private class Factory(DbContextOptions<RiftboundDbContext> o) : IDbContextFactory<RiftboundDbContext>
    { public RiftboundDbContext CreateDbContext() => new(o); }
}
```

> If `SkiaSharp` is not already referenced by the test project, generate the PNG another way, or copy the helper from `EdgeHashTests.cs`. Check first: `grep -rn "SkiaSharp\|SKBitmap" OmniCard.Tests`. `PerceptualHashService` uses SkiaSharp, so it is available transitively; add a direct `<PackageReference Include="SkiaSharp" />` to `OmniCard.Tests.csproj` only if the test fails to compile.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundHashingTests"`
Expected: FAIL — `ComputeImageHashesAsync` throws `NotImplementedException`.

- [ ] **Step 3: Replace the `ComputeImageHashesAsync` stub**

In `RiftboundService.cs`, replace the stub with (mirrors `OptcgService.ComputeImageHashesAsync`):

```csharp
    public async Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Riftbound image hash computation (forceAll: {ForceAll})", forceAll);
        var sw = Stopwatch.StartNew();

        await using var context = _dbContextFactory.CreateDbContext();
        var query = context.Cards.Where(c => c.CardImageUri != null);
        if (!forceAll)
            query = query.Where(c => c.ImageHash == null || c.EdgeHash == null);

        var cards = await query.Select(c => new { c.Id, c.CardImageUri }).ToListAsync(ct);
        _logger.LogInformation("Found {Count} Riftbound cards requiring hash computation", cards.Count);
        progress?.Report($"Computing hashes for {cards.Count} cards...");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        using var throttle = new SemaphoreSlim(8);
        var completed = 0;
        var failed = 0;
        var results = new List<(string Id, ulong Hash, ulong EdgeHash)>();
        var saveLock = new object();

        await Parallel.ForEachAsync(cards, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct
        }, async (card, token) =>
        {
            if (card.CardImageUri is null) { Interlocked.Increment(ref failed); return; }
            try
            {
                await throttle.WaitAsync(token);
                try
                {
                    var artFullPath = GetLocalArtFullPath(card.Id);
                    byte[] imageBytes;
                    if (File.Exists(artFullPath))
                    {
                        imageBytes = await File.ReadAllBytesAsync(artFullPath, token);
                    }
                    else
                    {
                        using var response = await client.GetAsync(card.CardImageUri, token);
                        response.EnsureSuccessStatusCode();
                        imageBytes = await response.Content.ReadAsByteArrayAsync(token);
                        Directory.CreateDirectory(Path.GetDirectoryName(artFullPath)!);
                        await File.WriteAllBytesAsync(artFullPath, imageBytes, token);
                    }

                    using var buffer = new MemoryStream(imageBytes);
                    var hash = _hashService.ComputeHash(buffer);
                    buffer.Position = 0;
                    var edgeHash = _hashService.ComputeEdgeHash(buffer);
                    lock (saveLock) results.Add((card.Id, hash, edgeHash));
                }
                finally { throttle.Release(); await Task.Delay(50, token); }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to compute hash for Riftbound card {Id}", card.Id);
                Interlocked.Increment(ref failed);
            }

            var done = Interlocked.Increment(ref completed);
            if (done % 100 == 0)
                progress?.Report($"Hashed {done}/{cards.Count} cards ({failed} failed)...");

            List<(string Id, ulong Hash, ulong EdgeHash)>? toSave = null;
            lock (saveLock)
            {
                if (results.Count >= 200) { toSave = [.. results]; results.Clear(); }
            }
            if (toSave is not null) await SaveHashBatchAsync(toSave, ct);
        });

        if (results.Count > 0) await SaveHashBatchAsync(results, ct);

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("Riftbound hash computation complete: {Hashed} hashed, {Failed} failed in {Sec:F1}s", completed - failed, failed, sw.Elapsed.TotalSeconds);
        progress?.Report($"Done — hashed {completed - failed} cards ({failed} failed).");
    }

    private async Task SaveHashBatchAsync(List<(string Id, ulong Hash, ulong EdgeHash)> batch, CancellationToken ct)
    {
        await using var context = _dbContextFactory.CreateDbContext();
        foreach (var (id, hash, edgeHash) in batch)
        {
            var rel = GetLocalArtRelativePath(id);
            await context.Cards.Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ImageHash, hash)
                    .SetProperty(c => c.EdgeHash, edgeHash)
                    .SetProperty(c => c.LocalImagePath, rel), ct);
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundHashingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/RiftboundService.cs OmniCard.Tests/Services/RiftboundHashingTests.cs
git commit -m "feat(riftbound): compute image + edge hashes and cache art"
```

---

### Task 6: `FindClosestMatch` — OCR-first with candidate disambiguation, then pHash

**Files:**
- Modify: `OmniCard.CardMatching/RiftboundService.cs` (replace the `FindClosestMatch` stub; add helpers)
- Test: `OmniCard.Tests/Services/RiftboundMatchingTests.cs`

**Interfaces:**
- Consumes: `RiftboundService` (Task 4/5), `OcrMatchResult.CollectorNumber` formatted as `"{SET}-{number}"` (e.g. `"OGN-310"`), `PerceptualHashService.HammingDistance`.
- Produces: `FindClosestMatch` with phases — Phase 0 OCR `(set, collector)` → candidate query → pHash disambiguation among candidates; edge-hash foil path; exact/fuzzy corrections; global pHash. Adds `internal static bool TryParseOcrCollectorNumber(string? ocr, out string setId, out int number)` and `private RiftboundCard? LookupById(string id)`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/RiftboundMatchingTests.cs`:

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

public class RiftboundMatchingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<RiftboundDbContext> _factory;
    private readonly RiftboundService _svc;

    public RiftboundMatchingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
        _factory = new Factory(options);
        using (var ctx = _factory.CreateDbContext())
        {
            ctx.Database.EnsureCreated();
            ctx.MarkMigrationComplete();
            // Two printings of collector 310 (base + alt art) — OCR gives (OGN,310); pHash must disambiguate.
            ctx.Cards.Add(new RiftboundCard { Id = "base", CollectorNumber = 310, SetId = "OGN", SetName = "Origins",
                Name = "Vex", Rarity = "Epic", CardType = "Legend", ImageHash = 0x0UL, AlternateArt = false, CardImageUri="u" });
            ctx.Cards.Add(new RiftboundCard { Id = "alt", CollectorNumber = 310, SetId = "OGN", SetName = "Origins",
                Name = "Vex", Rarity = "Epic", CardType = "Legend", ImageHash = 0xFFFFFFFFFFFFFFFFUL, AlternateArt = true, CardImageUri="u" });
            // A different card for pure pHash fallback.
            ctx.Cards.Add(new RiftboundCard { Id = "solo", CollectorNumber = 5, SetId = "OGN", SetName = "Origins",
                Name = "Solo", Rarity = "Common", CardType = "Unit", ImageHash = 0x00FF00FF00FF00FFUL, CardImageUri="u" });
            ctx.SaveChanges();
        }
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(Path.GetTempPath());
        _svc = new RiftboundService(new NullFactory(), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object, NullLogger<RiftboundService>.Instance);
    }

    public void Dispose() { _svc.Dispose(); _connection.Dispose(); }

    [Fact]
    public void ParsesOcrCollectorNumber_IgnoringTotal()
    {
        Assert.True(RiftboundService.TryParseOcrCollectorNumber("OGN-310", out var set, out var num));
        Assert.Equal("OGN", set);
        Assert.Equal(310, num);
    }

    [Fact]
    public void Ocr_MultipleCandidates_DisambiguatesByPHash()
    {
        var ocr = new OcrMatchResult { CollectorNumber = "OGN-310", CollectorNumberConfidence = 0.95 };
        // Scan hash all-zero → nearest is the base printing (ImageHash 0x0).
        var match = _svc.FindClosestMatch(0x0UL, ocrResult: ocr);
        Assert.NotNull(match);
        Assert.Equal("base", match!.GameSpecificId);

        // Scan hash all-ones → nearest is the alt art.
        var match2 = _svc.FindClosestMatch(0xFFFFFFFFFFFFFFFFUL, ocrResult: ocr);
        Assert.Equal("alt", match2!.GameSpecificId);
    }

    [Fact]
    public void NoOcr_FallsBackToPHash()
    {
        var match = _svc.FindClosestMatch(0x00FF00FF00FF00FFUL);
        Assert.NotNull(match);
        Assert.Equal("solo", match!.GameSpecificId);
    }

    [Fact]
    public void PHash_BeyondThreshold_ReturnsNull()
    {
        // Far from every stored hash; maxDistance small.
        var match = _svc.FindClosestMatch(0x0123456789ABCDEFUL, maxDistance: 1);
        Assert.Null(match);
    }

    private class NullFactory : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(); }
    private class Factory(DbContextOptions<RiftboundDbContext> o) : IDbContextFactory<RiftboundDbContext>
    { public RiftboundDbContext CreateDbContext() => new(o); }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundMatchingTests"`
Expected: FAIL — `TryParseOcrCollectorNumber` missing + `FindClosestMatch` throws `NotImplementedException`.

- [ ] **Step 3: Replace the `FindClosestMatch` stub + add helpers**

In `RiftboundService.cs`, replace the `FindClosestMatch` stub with the following and add the two helpers:

```csharp
    // Parses OCR collector text of the form "{SET}-{number}" (e.g. "OGN-310"). The printed
    // "/total" has already been stripped by the OCR detector.
    internal static bool TryParseOcrCollectorNumber(string? ocr, out string setId, out int number)
    {
        setId = ""; number = 0;
        if (string.IsNullOrWhiteSpace(ocr)) return false;
        var dash = ocr.LastIndexOf('-');
        if (dash <= 0 || dash == ocr.Length - 1) return false;
        setId = ocr[..dash].ToUpperInvariant();
        return int.TryParse(ocr[(dash + 1)..], out number);
    }

    private RiftboundCard? LookupById(string id)
        => _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.Id == id);

    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
    {
        LastMatchDiagnostics = new MatchDiagnostics { SetFilterActive = setFilter is not null };

        // Phase 0: OCR collector-number lookup. Set+number can map to multiple printings
        // (alt arts share a collector number) — disambiguate the candidate set by pHash.
        if (ocrResult?.CollectorNumber is not null && ocrResult.CollectorNumberConfidence >= 0.5
            && TryParseOcrCollectorNumber(ocrResult.CollectorNumber, out var ocrSet, out var ocrNum))
        {
            var candidates = _readContext.Cards.AsNoTracking()
                .Where(c => c.SetId == ocrSet && c.CollectorNumber == ocrNum)
                .ToList();

            if (setFilter is not null)
                candidates = candidates.Where(c => setFilter.Contains(c.SetId)).ToList();

            if (candidates.Count == 1)
            {
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ToMatch(candidates[0], confidence: 100);
            }
            if (candidates.Count > 1)
            {
                var hashed = candidates.Where(c => c.ImageHash != null).ToList();
                RiftboundCard best;
                if (hashed.Count > 0)
                {
                    best = hashed
                        .OrderBy(c => PerceptualHashService.HammingDistance(imageHash, c.ImageHash!.Value))
                        .First();
                }
                else
                {
                    best = candidates[0];
                }
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ToMatch(best, confidence: 100);
            }
        }

        // Foil path: match on the color-robust edge hash instead of the luminance pHash.
        if (scanEdgeHash is ulong scanEdge)
        {
            _edgeHashCache ??= _readContext.Cards
                .Where(c => c.EdgeHash != null)
                .Select(c => new { c.Id, Edge = c.EdgeHash!.Value, c.SetId })
                .AsNoTracking().AsEnumerable()
                .Select(c => (c.Id, c.Edge, c.SetId)).ToList();

            string bestEdgeId = "";
            int bestEdgeDist = int.MaxValue;
            foreach (var (id, edge, setId) in _edgeHashCache)
            {
                if (setFilter is not null && !setFilter.Contains(setId)) continue;
                var dist = PerceptualHashService.HammingDistance(scanEdge, edge);
                if (dist < bestEdgeDist) { bestEdgeDist = dist; bestEdgeId = id; }
            }
            if (bestEdgeId.Length > 0 && bestEdgeDist <= maxDistance)
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

        // Build caches.
        if (_hashCache is null)
        {
            var entries = _readContext.Cards
                .Where(c => c.ImageHash != null)
                .Select(c => new { c.Id, Hash = c.ImageHash!.Value, c.SetId })
                .AsNoTracking().AsEnumerable().ToList();
            _hashCache = entries.Select(c => (c.Id, c.Hash)).ToList();
            _hashSetLookup = entries.ToDictionary(c => c.Id, c => c.SetId);
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
        if (exact.CorrectCardId is not null)
        {
            var corrected = LookupById(exact.CorrectCardId);
            if (corrected is not null && (setFilter is null || setFilter.Contains(corrected.SetId)))
            {
                LastMatchDiagnostics.DecisionPhase = "ExactCorrection";
                return ToMatch(corrected, confidence: 100);
            }
        }

        if (_hashCache.Count == 0)
        {
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

        // Phase 2: nearest pHash + fuzzy corrections.
        string bestId = "";
        int bestDist = int.MaxValue;
        foreach (var (id, hash) in _hashCache)
        {
            if (setFilter is not null && !setFilter.Contains(_hashSetLookup![id])) continue;
            var dist = PerceptualHashService.HammingDistance(imageHash, hash);
            if (dist < bestDist) { bestDist = dist; bestId = id; }
        }

        string? bestCorrId = null;
        int bestCorrAdjusted = int.MaxValue;
        foreach (var (scanHash, correctCardId) in _correctionsCache)
        {
            if (setFilter is not null)
            {
                var corrSet = _hashSetLookup!.GetValueOrDefault(correctCardId);
                if (corrSet is null || !setFilter.Contains(corrSet)) continue;
            }
            var dist = PerceptualHashService.HammingDistance(imageHash, scanHash);
            if (dist <= maxDistance)
            {
                var adjusted = Math.Max(0, dist - CorrectionTrustBonus);
                if (adjusted < bestCorrAdjusted) { bestCorrAdjusted = adjusted; bestCorrId = correctCardId; }
            }
        }

        if (bestCorrId is not null && bestCorrAdjusted <= bestDist)
        {
            var conf = Math.Max(0, 1.0 - (double)bestCorrAdjusted / maxDistance) * 100;
            var corrected = LookupById(bestCorrId);
            if (corrected is not null)
            {
                LastMatchDiagnostics.DecisionPhase = "PHashConfident";
                LastMatchDiagnostics.PHashDistance = bestCorrAdjusted;
                return ToMatch(corrected, conf);
            }
        }

        if (bestDist > maxDistance)
        {
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

        if (setFilter is not null)
        {
            var bestSet = _hashSetLookup!.GetValueOrDefault(bestId);
            if (bestSet is null || !setFilter.Contains(bestSet))
            {
                LastMatchDiagnostics.DecisionPhase = "NoMatch";
                return null;
            }
        }

        var bestCard = LookupById(bestId);
        if (bestCard is null)
        {
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }
        LastMatchDiagnostics.DecisionPhase = "PHashConfident";
        LastMatchDiagnostics.PHashDistance = bestDist;
        var confidence = Math.Max(0, 1.0 - (double)bestDist / maxDistance) * 100;
        return ToMatch(bestCard, confidence);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundMatchingTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/RiftboundService.cs OmniCard.Tests/Services/RiftboundMatchingTests.cs
git commit -m "feat(riftbound): OCR-first matching with alt-art pHash disambiguation"
```

---

### Task 7: Orientation-aware OCR collector-number detection

**Files:**
- Modify: `OmniCard.Imaging/OcrMatchingService.cs` (add regions, regex, parse helper, detect method)
- Modify: `OmniCard.Shared/Interfaces/IOcrMatchingService.cs` (add method)
- Test: `OmniCard.Tests/Services/RiftboundOcrParseTests.cs`

**Interfaces:**
- Produces: `IOcrMatchingService.DetectRiftboundCollectorNumberAsync(byte[])` returning `(string? CollectorNumber, double Confidence)` where `CollectorNumber` is `"{SET}-{number}"`. Internal static `OcrMatchingService.TryExtractRiftboundNumber(string ocrText, out string? formatted)` for unit testing the regex without Tesseract. Internal static regions `RiftboundPortraitRegion`, `RiftboundLandscapeRegion`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/RiftboundOcrParseTests.cs`:

```csharp
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class RiftboundOcrParseTests
{
    [Theory]
    [InlineData("UNL • 150/219", "UNL-150")]
    [InlineData("UNL 150/219", "UNL-150")]     // bullet dropped by OCR
    [InlineData("OGN · 209/298", "OGN-209")]   // middle-dot separator
    [InlineData("SFD•96/221", "SFD-96")]        // no spaces
    public void ExtractsSetAndCollector_IgnoringTotal(string ocr, string expected)
    {
        Assert.True(OcrMatchingService.TryExtractRiftboundNumber(ocr, out var formatted));
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("League Splash Team")]  // flavour/credit line, no number pattern
    [InlineData("")]
    public void RejectsNonCollectorText(string ocr)
    {
        Assert.False(OcrMatchingService.TryExtractRiftboundNumber(ocr, out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundOcrParseTests"`
Expected: FAIL — `TryExtractRiftboundNumber` does not exist (compile error).

- [ ] **Step 3: Add regions, regex, parse helper, and detect method**

In `OmniCard.Imaging/OcrMatchingService.cs`, add near the other region constants (after `OptcgCollectorNumberRegion`, ~line 52):

```csharp
    // Riftbound collector line — lower-LEFT: "{SET} • {n}/{total}" (e.g. "UNL • 150/219").
    // Portrait cards (Units/Spells/Legends) vs landscape cards (Battlefields) place it
    // differently, so we pick a region by the scanned card's aspect ratio.
    internal static readonly (double X, double Y, double W, double H) RiftboundPortraitRegion =
        (0.02, 0.945, 0.40, 0.05);
    internal static readonly (double X, double Y, double W, double H) RiftboundLandscapeRegion =
        (0.02, 0.93, 0.30, 0.06);

    // Restrict OCR to characters that appear in a Riftbound collector line.
    private const string RiftboundWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789•·./- ";

    // "{SET} [sep] {collector}/{total}". Captures set code + collector number; the /total is
    // matched only to anchor the pattern and is discarded.
    private static readonly System.Text.RegularExpressions.Regex RiftboundPattern =
        new(@"([A-Za-z]{2,4})\s*[•·.\-]{0,2}\s*(\d{1,3})\s*/\s*\d{1,3}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
```

Add the testable parse helper and the detect method (near `DetectOptcgCollectorNumber`, ~line 274):

```csharp
    // Extracts "{SET}-{collector}" from an OCR'd Riftbound collector line, or false if no match.
    internal static bool TryExtractRiftboundNumber(string ocrText, out string? formatted)
    {
        formatted = null;
        if (string.IsNullOrWhiteSpace(ocrText)) return false;
        var m = RiftboundPattern.Match(ocrText);
        if (!m.Success) return false;
        formatted = $"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}";
        return true;
    }

    public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] imageData)
        => Task.Run(() => DetectRiftboundCollectorNumber(imageData));

    private (string? CollectorNumber, double Confidence) DetectRiftboundCollectorNumber(byte[] imageData)
    {
        if (!_ocrAvailable) return (null, 0);
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            // Landscape cards (Battlefields) are wider than tall; portrait cards ~0.72 ratio.
            var region = bitmap.Width > bitmap.Height ? RiftboundLandscapeRegion : RiftboundPortraitRegion;
            var rect = ToPixelRect(region, bitmap.Width, bitmap.Height);
            if (rect.Width < 10 || rect.Height < 5) return (null, 0);

            var (text, confidence) = OcrCroppedRegion(bitmap, rect, PageSegMode.SingleLine, RiftboundWhitelist);
            if (string.IsNullOrWhiteSpace(text)) return (null, 0);

            if (TryExtractRiftboundNumber(text, out var formatted))
            {
                var reported = Math.Max(0.9, confidence);
                _logger.LogInformation("Riftbound collector detected: {Number} (raw: {Raw}, ocrConf: {Conf:F2})",
                    formatted, text, confidence);
                return (formatted, reported);
            }
            _logger.LogDebug("Riftbound collector OCR text did not match pattern: {Text}", text);
            return (null, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Riftbound collector number detection failed");
            return (null, 0);
        }
    }
```

In `OmniCard.Shared/Interfaces/IOcrMatchingService.cs`, add:

```csharp
    /// <summary>OCR the collector line from a Riftbound card, returning "{SET}-{number}" (e.g. "UNL-150").</summary>
    Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] imageData);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundOcrParseTests"`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Imaging/OcrMatchingService.cs OmniCard.Shared/Interfaces/IOcrMatchingService.cs OmniCard.Tests/Services/RiftboundOcrParseTests.cs
git commit -m "feat(riftbound): orientation-aware OCR collector-number detection"
```

---

### Task 8: Wire Riftbound into the scan orchestrator

**Files:**
- Modify: `OmniCard.Collection/CardService.cs:183-186` (foil edge branch), `:288-342` (async OCR branch), `:367-377` (rotate-retry branch)
- Test: `OmniCard.Tests/Services/RiftboundScanRoutingTests.cs`

**Interfaces:**
- Consumes: `IOcrMatchingService.DetectRiftboundCollectorNumberAsync` (Task 7), `RiftboundService.FindClosestMatch` (Task 6), `CardService.FindBestMatch` (existing dispatcher).
- Produces: scan pipeline computes an edge hash for Riftbound foils and runs Riftbound OCR → re-match, including on the 180° rotate retry.

- [ ] **Step 1: Write the failing test**

`CardService.AddFromStream`'s async section is bound to `Application.Current.Dispatcher` (WPF), so the synchronous match dispatch is what we test directly. This verifies `FindBestMatch` routes `CardGame.Riftbound` to the Riftbound service (the branch code paths are exercised at runtime and in Task 10's live verification).

Create `OmniCard.Tests/Services/RiftboundScanRoutingTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundScanRoutingTests
{
    [Fact]
    public void FindBestMatch_RoutesRiftboundGame_ToRiftboundService()
    {
        var riftMatch = new CardMatch { Name = "Vex", SetCode = "OGN", CollectorNumber = "310", GameSpecificId = "base" };
        var rift = new Mock<ICardGameService>();
        rift.SetupGet(s => s.Game).Returns(CardGame.Riftbound);
        rift.Setup(s => s.FindClosestMatch(It.IsAny<ulong>(), It.IsAny<ulong[]>(), It.IsAny<OcrMatchResult>(),
                It.IsAny<IReadOnlySet<string>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<int>(), It.IsAny<ulong?>()))
            .Returns(riftMatch);

        // Build a CardService with only the Riftbound stub registered and SelectedGame = Riftbound,
        // then assert FindBestMatch returns the Riftbound match. See CardServiceTestHarness usage in
        // ScanMatchingIntegrationTests.cs for the exact constructor wiring to mirror.
        var svc = CardServiceTestFactory.Create(new[] { rift.Object }, CardGame.Riftbound);
        var (match, game) = svc.FindBestMatch(123UL, null, null, null, null, null);

        Assert.Equal(CardGame.Riftbound, game);
        Assert.Equal("base", match!.GameSpecificId);
    }
}
```

> **Before writing this test**, open `OmniCard.Tests/Services/ScanMatchingIntegrationTests.cs` and copy its exact pattern for constructing a `CardService` with mock dependencies (logger, hash service, OCR service, diagnostic/audit/mismatch services, `IEnumerable<ICardGameService>`). If that file already exposes a factory/harness, reuse it and delete the `CardServiceTestFactory` reference above; otherwise add a small private factory in this test file that constructs `CardService` the same way and sets `SelectedGame`. `FindBestMatch` is the public method used at `CardService.cs:235`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundScanRoutingTests"`
Expected: FAIL — compile error until the harness is wired; then it should pass once `FindBestMatch` dispatch works (dispatch already exists, so failure is only the missing harness). If dispatch already routes correctly, this test may pass immediately after wiring the harness — that is acceptable (it guards against regressions when the orchestrator branches are edited in Step 3).

- [ ] **Step 3: Add the Riftbound orchestrator branches**

In `OmniCard.Collection/CardService.cs`:

**(a)** Foil edge hash (currently line 182-186) — include Riftbound:

```csharp
        ulong? scanEdgeHash = null;
        if (DefaultIsFoil && (SelectedGame == CardGame.OnePiece || SelectedGame == CardGame.Riftbound))
        {
            scanEdgeHash = _hashService.ComputeEdgeHash(new MemoryStream(rawBytes));
        }
```

**(b)** Async OCR branch (currently the `if (game == CardGame.OnePiece) { ... } else { MTG }` at lines 288-342) — insert a Riftbound branch between them:

```csharp
                    if (game == CardGame.OnePiece)
                    {
                        // OPTCG: detect collector number for direct lookup
                        var (collectorNumber, conf) = await _ocrService.DetectOptcgCollectorNumberAsync(rawBytes);
                        if (collectorNumber is not null && conf >= 0.5)
                        {
                            ocrResult = new OcrMatchResult { CollectorNumber = collectorNumber, CollectorNumberConfidence = conf };
                            var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, null, scannedCard.ScanEdgeHash);
                            if (ocrMatch is not null && (scannedCard.Match is null || ocrMatch.GameSpecificId != scannedCard.Match?.GameSpecificId))
                            {
                                scannedCard.Match = ocrMatch;
                                scannedCard.Game = ocrGame;
                                scannedCard.FlagReason = FlagReason.None;
                            }
                        }
                    }
                    else if (game == CardGame.Riftbound)
                    {
                        // Riftbound: detect "{SET}-{number}" for candidate lookup + pHash disambiguation
                        var (collectorNumber, conf) = await _ocrService.DetectRiftboundCollectorNumberAsync(rawBytes);
                        if (collectorNumber is not null && conf >= 0.5)
                        {
                            ocrResult = new OcrMatchResult { CollectorNumber = collectorNumber, CollectorNumberConfidence = conf };
                            _logger.LogInformation("Riftbound collector detected: {Number} (confidence {Conf:F2})", collectorNumber, conf);
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
                    else
                    {
                        // MTG: name recognition + symbol detection (unchanged)
                        // ... existing MTG block stays exactly as-is ...
```

> Keep the entire existing MTG block inside the final `else`. Only the `if`/`else if` heads above are new; do not alter the MTG body.

**(c)** Rotate-retry OCR + edge (currently lines 367-377) — add Riftbound:

```csharp
                        // Try OCR on rotated image
                        OcrMatchResult? rotatedOcr = null;
                        if (game == CardGame.OnePiece)
                        {
                            var (cn, cnConf) = await _ocrService.DetectOptcgCollectorNumberAsync(rotatedBytes);
                            if (cn is not null && cnConf >= 0.5)
                                rotatedOcr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = cnConf };
                        }
                        else if (game == CardGame.Riftbound)
                        {
                            var (cn, cnConf) = await _ocrService.DetectRiftboundCollectorNumberAsync(rotatedBytes);
                            if (cn is not null && cnConf >= 0.5)
                                rotatedOcr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = cnConf };
                        }

                        ulong? rotatedEdgeHash = null;
                        if (scannedCard.IsFoil && (game == CardGame.OnePiece || game == CardGame.Riftbound))
                            rotatedEdgeHash = _hashService.ComputeEdgeHash(new MemoryStream(rotatedBytes));
```

- [ ] **Step 4: Run test + full suite to verify**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~RiftboundScanRoutingTests"`
Expected: PASS.

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/CardService.cs OmniCard.Tests/Services/RiftboundScanRoutingTests.cs
git commit -m "feat(riftbound): wire OCR + foil edge hash into scan pipeline"
```

---

### Task 9: Remaining `CardGame` touchpoints (web + UI) + green build

**Files (audit each; add a `CardGame.Riftbound` arm only where a switch would otherwise drop it or throw):**
- Modify: `OmniCard.Web/Pages/Index.cshtml.cs:45-46` (game-code map)
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (~1003, 1012, 1382, 1465)
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml.cs` (~172)
- Modify: `OmniCard/Views/Inventory/ProductEditorViewModel.cs` (~51)
- Modify: `OmniCard.Collection/DecklistService.cs`, `OmniCard.Collection/CsvExportImportService.cs`
- Modify: `OmniCard/Views/Dashboard/DashboardView.xaml`
- Test: build + existing suite (no new unit test — these are passthrough/UI wiring)

**Interfaces:**
- Consumes: everything from Tasks 1-8.
- Produces: the app enumerates Riftbound wherever it enumerates games; no switch throws on `CardGame.Riftbound`.

- [ ] **Step 1: Find every remaining reference**

Run: `grep -rn "CardGame.OnePiece" OmniCard OmniCard.Web OmniCard.Collection --include=*.cs --include=*.xaml --include=*.cshtml`

For each hit **not** already handled in Tasks 1-8, decide:
- If it is a `switch` producing a value per game (display, game-code map, icon), add a `CardGame.Riftbound => ...` arm.
- If it is an `if (game == CardGame.OnePiece)` guard for behavior that Riftbound should share (e.g. collector-number games vs MTG), extend the condition to include `CardGame.Riftbound`.
- If it is MTG-specific (art hashes, set symbols), leave it alone.

- [ ] **Step 2: Add the web game-code map arm**

In `OmniCard.Web/Pages/Index.cshtml.cs` (read lines around 45 first), add the Riftbound mapping mirroring One Piece, e.g. if it maps a URL/query code to `CardGame`:

```csharp
        "riftbound" => CardGame.Riftbound,
```

and the reverse mapping (CardGame → code string) if one exists:

```csharp
        CardGame.Riftbound => "riftbound",
```

- [ ] **Step 3: Handle the desktop UI switches**

Read each `RootViewModel.cs`, `ScannerTabView.xaml.cs`, `ProductEditorViewModel.cs`, `DecklistService.cs`, `CsvExportImportService.cs` reference from Step 1. Apply the Step-1 decision rule. Example — a game-picker/label switch gets a `CardGame.Riftbound` arm; a "which games download" loop that iterates `AvailableGames` needs no change (Riftbound is already registered via DI in Task 4).

For `DashboardView.xaml`, if it has per-game elements bound to specific enum values, add the Riftbound equivalent; otherwise no change.

- [ ] **Step 4: Build and run the full test suite**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeded, 0 errors.

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: All tests pass (existing + all new Riftbound tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(riftbound): enumerate Riftbound across web + desktop UI touchpoints"
```

---

### Task 10: Live catalog download + scan verification

**Files:** none (verification task).

**Interfaces:** Consumes the fully wired app from Tasks 1-9.

- [ ] **Step 1: Invoke the `verify` skill**

Use the `verify` skill to drive the real app end-to-end (not just tests).

- [ ] **Step 2: Download the catalog**

Launch the desktop app, select **Riftbound** as the game, and trigger the catalog download (the same UI action used for One Piece). Confirm from logs / UI that ~8 sets and the expected card counts import, then that image-hash computation runs to completion.

Sanity-check the DB (PowerShell):

```powershell
$db = "$env:LOCALAPPDATA\OmniCard\riftbound.db"
# Requires sqlite3 on PATH; otherwise inspect via the app's set-completion view.
sqlite3 $db "SELECT SetId, COUNT(*) FROM Cards GROUP BY SetId;"
sqlite3 $db "SELECT COUNT(*) FROM Cards WHERE ImageHash IS NOT NULL;"
```

Expected: rows for UNL/OGN/SFD/VEN/OGS/OPP/JDG/PR; `ImageHash` populated for (nearly) all rows with an image URL.

- [ ] **Step 3: Scan the sample card**

Scan (or import) the Vex card shown in the design (`UNL • 150/219`). Confirm:
- The log shows `Riftbound collector detected: UNL-150`.
- The match resolves to the correct Vex printing (OCR phase `OcrCollectorNumber`), and if multiple printings share 150, pHash picks the visually-correct one.
- Scan a Battlefield (landscape) card and confirm OCR still reads its collector line (landscape region).

- [ ] **Step 4: Record the result**

Note pass/fail and any crop-region tuning needed (the portrait/landscape region constants in Task 7 are first estimates; adjust `RiftboundPortraitRegion`/`RiftboundLandscapeRegion` if OCR misses, then re-run this task). If regions are tuned, commit:

```bash
git add OmniCard.Imaging/OcrMatchingService.cs
git commit -m "fix(riftbound): tune OCR crop regions from live scans"
```

- [ ] **Step 5: Finish the branch**

Invoke the `superpowers:finishing-a-development-branch` skill to decide merge/PR.

---

## Notes on Deferred / Out-of-Scope Work
- **Pricing:** `UpdatePricesAsync` is a no-op and price getters return null/empty. A future task can wire TCGPlayer pricing via `RiftboundCard.TcgplayerId`.
- **`PriceUpdateService`** iterates registered `ICardGameService`s and will call Riftbound's no-op `UpdatePricesAsync` harmlessly (it logs and returns). No change needed there.
