# Baseline Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill all untested service/class gaps with 7 new test files (~44 test cases) to establish baseline code coverage.

**Architecture:** Per-service test classes using Moq for new tests, SQLite in-memory for DB-backed tests, direct instantiation for Web tests. Existing hand-rolled fakes untouched.

**Tech Stack:** xUnit 2.9.3, Moq, Xunit.StaFact, Microsoft.Data.Sqlite, Microsoft.EntityFrameworkCore (SQLite + InMemory), Microsoft.AspNetCore.Http (for FormFile)

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0` with `UseWPF=true`
- Test runner: xUnit with `[Fact]` for normal tests, `[StaFact]` for WPF-dependent tests
- DB pattern: SQLite in-memory with shared open connection per test class, `IDisposable` cleanup
- Mocking: Moq for NEW tests only. Existing hand-rolled fakes are not modified.
- All test classes live under `OmniCard.Tests/` in `Services/` or `Web/` subdirectories

---

### Task 1: Infrastructure Setup

**Files:**
- Modify: `OmniCard.Tests/OmniCard.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: Moq, ASP.NET Core types, and OmniCard.Web types available to all subsequent tasks

- [ ] **Step 1: Add Moq package**

```bash
cd d:/source/repos/OmniCard/OmniCard.Tests
dotnet add package Moq
```

- [ ] **Step 2: Add ASP.NET Core framework reference and Web project reference**

Add these to `OmniCard.Tests/OmniCard.Tests.csproj` inside the existing `<ItemGroup>` blocks:

In the `<ItemGroup>` with PackageReferences, add:
```xml
<FrameworkReference Include="Microsoft.AspNetCore.App" />
```

In the `<ItemGroup>` with ProjectReferences, add:
```xml
<ProjectReference Include="..\OmniCard.Web\OmniCard.Web.csproj" />
```

The final csproj should look like:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.9" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Moq" Version="4.*" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Xunit.StaFact" Version="1.1.11" />
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="System.IO" />
    <Using Include="System.Net.Http" />
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\OmniCard\OmniCard.csproj" />
    <ProjectReference Include="..\OmniCard.Audit\OmniCard.Audit.csproj" />
    <ProjectReference Include="..\OmniCard.Controls\OmniCard.Controls.csproj" />
    <ProjectReference Include="..\OmniCard.Web\OmniCard.Web.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Verify build succeeds**

```bash
cd d:/source/repos/OmniCard
dotnet build OmniCard.Tests/OmniCard.Tests.csproj
```

Expected: Build succeeds with no errors.

- [ ] **Step 4: Run existing tests to confirm no regressions**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --no-build
```

Expected: All existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Tests/OmniCard.Tests.csproj
git commit -m "test: add Moq, ASP.NET Core framework ref, and Web project ref to test project"
```

---

### Task 2: MismatchLogServiceTests

**Files:**
- Create: `OmniCard.Tests/Services/MismatchLogServiceTests.cs`

**Interfaces:**
- Consumes: `MismatchLogService` from `OmniCard.Collection`, `CollectionDbContext` from `OmniCard.Data`
- Produces: 5 test cases validating `LogMismatchAsync` conditional logging behavior

- [ ] **Step 1: Create the test file**

Create `OmniCard.Tests/Services/MismatchLogServiceTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class MismatchLogServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _factory;

    public MismatchLogServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection).Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MismatchLogService CreateService() => new(_factory);

    private static CardMatch MakeMatch(string id, string name, string setCode,
        string number, double? confidence) => new()
    {
        GameSpecificId = id,
        Name = name,
        SetCode = setCode,
        CollectorNumber = number,
        Confidence = confidence,
        Source = new object(),
    };

    private static ScannedCard MakeScannedCard(ulong hash = 0x1234UL) => new()
    {
        TempImagePath = "/tmp/scan.jpg",
        Hash = hash,
    };

    [Fact]
    public async Task LogMismatch_HighConfidenceDifferentIds_PersistsLog()
    {
        var svc = CreateService();
        var old = MakeMatch("old-id", "Old Card", "SET", "1", 85);
        var corrected = MakeMatch("new-id", "New Card", "SET", "2", null);

        await svc.LogMismatchAsync(old, corrected, MakeScannedCard());

        using var ctx = _factory.CreateDbContext();
        var log = Assert.Single(ctx.MismatchLogs.ToList());
        Assert.Equal("old-id", log.OriginalCardId);
        Assert.Equal("new-id", log.CorrectedCardId);
    }

    [Fact]
    public async Task LogMismatch_LowConfidence_DoesNotLog()
    {
        var svc = CreateService();
        await svc.LogMismatchAsync(
            MakeMatch("a", "A", "S", "1", 79),
            MakeMatch("b", "B", "S", "2", null),
            MakeScannedCard());

        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.MismatchLogs.ToList());
    }

    [Fact]
    public async Task LogMismatch_NullConfidence_DoesNotLog()
    {
        var svc = CreateService();
        await svc.LogMismatchAsync(
            MakeMatch("a", "A", "S", "1", null),
            MakeMatch("b", "B", "S", "2", null),
            MakeScannedCard());

        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.MismatchLogs.ToList());
    }

    [Fact]
    public async Task LogMismatch_SameGameSpecificId_DoesNotLog()
    {
        var svc = CreateService();
        await svc.LogMismatchAsync(
            MakeMatch("same-id", "Card", "S", "1", 90),
            MakeMatch("same-id", "Card", "S", "1", null),
            MakeScannedCard());

        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.MismatchLogs.ToList());
    }

    [Fact]
    public async Task LogMismatch_FieldsPopulatedCorrectly()
    {
        var svc = CreateService();
        var old = MakeMatch("orig-id", "Original", "M10", "42", 95);
        var corrected = MakeMatch("corr-id", "Corrected", "M11", "99", null);
        var scan = new ScannedCard { TempImagePath = "/scans/test.jpg", Hash = 0xABCDUL };

        await svc.LogMismatchAsync(old, corrected, scan);

        using var ctx = _factory.CreateDbContext();
        var log = Assert.Single(ctx.MismatchLogs.ToList());
        Assert.Equal(0xABCDUL, log.ScanHash);
        Assert.Equal("/scans/test.jpg", log.ScanImagePath);
        Assert.Equal("orig-id", log.OriginalCardId);
        Assert.Equal("Original", log.OriginalName);
        Assert.Equal("M10", log.OriginalSetCode);
        Assert.Equal("42", log.OriginalNumber);
        Assert.Equal(95, log.OriginalConfidence);
        Assert.Equal("corr-id", log.CorrectedCardId);
        Assert.Equal("Corrected", log.CorrectedName);
        Assert.Equal("M11", log.CorrectedSetCode);
        Assert.Equal("99", log.CorrectedNumber);
    }

    private class TestDbContextFactory(DbContextOptions<CollectionDbContext> options)
        : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~MismatchLogServiceTests" -v normal
```

Expected: All 5 tests pass.

- [ ] **Step 3: Commit**

```bash
git add OmniCard.Tests/Services/MismatchLogServiceTests.cs
git commit -m "test: add MismatchLogService baseline tests"
```

---

### Task 3: CollectionQueryServiceTests

**Files:**
- Create: `OmniCard.Tests/Services/CollectionQueryServiceTests.cs`

**Interfaces:**
- Consumes: `CollectionQueryService` from `OmniCard.Collection`, `IStorageContainerService`, `ICardService`, `ICardGameService` (mocked via Moq)
- Produces: 9 test cases validating `GetLocationOverviewsAsync` aggregation, filtering, pricing, and cover image logic

- [ ] **Step 1: Create the test file**

Create `OmniCard.Tests/Services/CollectionQueryServiceTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class CollectionQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _factory;

    public CollectionQueryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection).Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CollectionQueryService CreateService(
        List<StorageContainer> containers,
        Dictionary<string, decimal>? prices = null)
    {
        var mockContainerService = new Mock<IStorageContainerService>();
        mockContainerService.Setup(c => c.GetAll()).Returns(containers);

        var mockGameService = new Mock<ICardGameService>();
        mockGameService
            .Setup(g => g.GetCurrentPrices(It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
            .Returns((IEnumerable<string> ids, bool _) =>
            {
                if (prices is null) return new Dictionary<string, decimal>();
                var result = new Dictionary<string, decimal>();
                foreach (var id in ids.Distinct())
                    if (prices.TryGetValue(id, out var p))
                        result[id] = p;
                return result;
            });

        var mockCardService = new Mock<ICardService>();
        mockCardService
            .Setup(c => c.GetGameService(It.IsAny<CardGame>()))
            .Returns(mockGameService.Object);

        return new CollectionQueryService(_factory, mockContainerService.Object, mockCardService.Object);
    }

    private StorageContainer SeedContainer(string name, ContainerType type = ContainerType.Binder, int? coverCardId = null)
    {
        using var ctx = _factory.CreateDbContext();
        var container = new StorageContainer
        {
            Name = name,
            ContainerType = type,
            CoverCardId = coverCardId,
        };
        ctx.StorageContainers.Add(container);
        ctx.SaveChanges();
        return container;
    }

    private CollectionCard SeedCard(int containerId, string gameCardId, string name,
        CardGame game = CardGame.Mtg, decimal? purchasePrice = null,
        bool isFoil = false, string? imageUri = null)
    {
        using var ctx = _factory.CreateDbContext();
        var card = new CollectionCard
        {
            Game = game,
            GameCardId = gameCardId,
            Name = name,
            SetName = "TestSet",
            SetCode = "TST",
            Number = "1",
            Rarity = "common",
            ContainerId = containerId,
            PurchasePrice = purchasePrice,
            IsFoil = isFoil,
            ImageUri = imageUri,
        };
        ctx.Cards.Add(card);
        ctx.SaveChanges();
        return card;
    }

    [Fact]
    public async Task GetLocationOverviews_NoContainers_ReturnsEmpty()
    {
        var svc = CreateService([]);
        var result = await svc.GetLocationOverviewsAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLocationOverviews_ContainersWithNoCards_ReturnsZeroCounts()
    {
        var container = SeedContainer("Empty Box", ContainerType.Box);
        var svc = CreateService([container]);

        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(0, summary.CardCount);
        Assert.Equal(0m, summary.TotalPurchaseCost);
        Assert.Equal(0m, summary.TotalMarketValue);
    }

    [Fact]
    public async Task GetLocationOverviews_CorrectCardCountAndPurchaseTotal()
    {
        var container = SeedContainer("Binder");
        SeedCard(container.Id, "c1", "Card A", purchasePrice: 5.00m);
        SeedCard(container.Id, "c2", "Card B", purchasePrice: 3.00m);

        var svc = CreateService([container]);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(2, summary.CardCount);
        Assert.Equal(8.00m, summary.TotalPurchaseCost);
    }

    [Fact]
    public async Task GetLocationOverviews_GameFilter_OnlyCountsMatchingGame()
    {
        var container = SeedContainer("Mixed");
        SeedCard(container.Id, "mtg1", "MTG Card", game: CardGame.Mtg, purchasePrice: 10m);
        SeedCard(container.Id, "op1", "OP Card", game: CardGame.OnePiece, purchasePrice: 5m);

        var svc = CreateService([container]);
        var result = await svc.GetLocationOverviewsAsync(CardGame.Mtg);

        var summary = Assert.Single(result);
        Assert.Equal(1, summary.CardCount);
        Assert.Equal(10m, summary.TotalPurchaseCost);
    }

    [Fact]
    public async Task GetLocationOverviews_MarketValue_UsesGameServicePrices()
    {
        var container = SeedContainer("Priced");
        SeedCard(container.Id, "c1", "Expensive", purchasePrice: 1m);
        SeedCard(container.Id, "c2", "Cheap", purchasePrice: 1m);

        var prices = new Dictionary<string, decimal>
        {
            ["c1"] = 10.00m,
            ["c2"] = 2.00m,
        };
        var svc = CreateService([container], prices);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(12.00m, summary.TotalMarketValue);
    }

    [Fact]
    public async Task GetLocationOverviews_PriceDelta_CalculatesCorrectly()
    {
        var container = SeedContainer("Delta");
        SeedCard(container.Id, "c1", "Card", purchasePrice: 10m);

        var prices = new Dictionary<string, decimal> { ["c1"] = 15.00m };
        var svc = CreateService([container], prices);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(5.00m, summary.PriceDelta);        // 15 - 10
        Assert.Equal(50.0, summary.PriceDeltaPercent);   // (5/10)*100
    }

    [Fact]
    public async Task GetLocationOverviews_PriceDelta_ZeroPurchase_ZeroPercent()
    {
        var container = SeedContainer("Free");
        SeedCard(container.Id, "c1", "Card", purchasePrice: null);

        var prices = new Dictionary<string, decimal> { ["c1"] = 5.00m };
        var svc = CreateService([container], prices);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal(0.0, summary.PriceDeltaPercent); // no division by zero
    }

    [Fact]
    public async Task GetLocationOverviews_CoverImage_FromExplicitCoverCardId()
    {
        var container = SeedContainer("WithCover");
        var card = SeedCard(container.Id, "c1", "Cover Card", imageUri: "https://img/cover.jpg");

        // Update container's CoverCardId in DB
        using (var ctx = _factory.CreateDbContext())
        {
            var c = ctx.StorageContainers.Find(container.Id)!;
            c.CoverCardId = card.Id;
            ctx.SaveChanges();
        }
        // Pass updated container to mock
        container.CoverCardId = card.Id;

        var svc = CreateService([container]);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.Equal("https://img/cover.jpg", summary.CoverImageUri);
    }

    [Fact]
    public async Task GetLocationOverviews_CoverImage_FallbackToFirstCard()
    {
        var container = SeedContainer("NoCover");
        SeedCard(container.Id, "c1", "First Card", imageUri: "https://img/first.jpg");

        var svc = CreateService([container]);
        var result = await svc.GetLocationOverviewsAsync();

        var summary = Assert.Single(result);
        Assert.NotNull(summary.CoverImageUri);
    }

    private class TestDbContextFactory(DbContextOptions<CollectionDbContext> options)
        : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CollectionQueryServiceTests" -v normal
```

Expected: All 9 tests pass.

- [ ] **Step 3: Commit**

```bash
git add OmniCard.Tests/Services/CollectionQueryServiceTests.cs
git commit -m "test: add CollectionQueryService baseline tests"
```

---

### Task 4: OptcgServiceTests

**Files:**
- Create: `OmniCard.Tests/Services/OptcgServiceTests.cs`

**Interfaces:**
- Consumes: `OptcgService` from `OmniCard.CardMatching`, `OptcgDbContext` from `OmniCard.Data`, `IPerceptualHashService` from `OmniCard.Imaging`
- Produces: 7 test cases covering matching (pure pHash, threshold), search (keyword, qualifier), prices, and correction persistence. Extends coverage beyond existing `OptcgCorrectionTests`.

NOTE: The existing `OptcgCorrectionTests.cs` already covers exact correction, fuzzy correction with trust bonus, and correction upsert. This task covers the remaining untested paths.

- [ ] **Step 1: Create the test file**

Create `OmniCard.Tests/Services/OptcgServiceTests.cs`:

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

public class OptcgServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OptcgDbContext> _factory;

    public OptcgServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OptcgDbContext>()
            .UseSqlite(_connection).Options;
        _factory = new TestOptcgDbFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed test cards with distinct hashes
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-001",
            CardName = "Monkey D. Luffy",
            SetId = "OP01",
            SetName = "Romance Dawn",
            Rarity = "SR",
            CardColor = "Red",
            CardType = "Leader",
            ImageHash = 0x0000000000000000UL,
            MarketPrice = 12.50m,
        });
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-002",
            CardName = "Roronoa Zoro",
            SetId = "OP01",
            SetName = "Romance Dawn",
            Rarity = "R",
            CardColor = "Green",
            CardType = "Character",
            ImageHash = 0x00000000000000FFUL, // Hamming distance 8 from 0x0
            MarketPrice = 5.00m,
        });
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP02-001",
            CardName = "Nami",
            SetId = "OP02",
            SetName = "Paramount War",
            Rarity = "C",
            CardColor = "Blue",
            CardType = "Character",
            ImageHash = 0xFFFFFFFFFFFFFFFFUL, // Hamming distance 64 from 0x0
            MarketPrice = 0.50m,
        });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgService CreateService()
    {
        return new OptcgService(
            new StubHttpClientFactory(),
            _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            NullLogger<OptcgService>.Instance);
    }

    // --- FindClosestMatch (pure pHash, no corrections) ---

    [Fact]
    public void FindClosestMatch_PHashDistance_ReturnsBestMatch()
    {
        var svc = CreateService();

        // Hash 0x01 is Hamming distance 1 from OP01-001 (0x0) and distance 7 from OP01-002 (0xFF)
        var match = svc.FindClosestMatch(0x0000000000000001UL);

        Assert.NotNull(match);
        Assert.Equal("Monkey D. Luffy", match.Name);
        Assert.Equal("OP01-001", match.GameSpecificId);
    }

    [Fact]
    public void FindClosestMatch_BeyondMaxDistance_ReturnsNull()
    {
        var svc = CreateService();

        // Hash that is very far from all seeded cards
        // OP01-001 = 0x0 (distance 32), OP01-002 = 0xFF (distance 24), OP02-001 = 0xFFFF... (distance 32)
        // Use maxDistance=2 so all cards are too far
        var match = svc.FindClosestMatch(0x00000000FFFFFFFFUL, maxDistance: 2);

        Assert.Null(match);
    }

    [Fact]
    public void FindClosestMatch_WithSetFilter_RespectsFilter()
    {
        var svc = CreateService();

        // Hash 0x01 is closest to OP01-001, but filter only allows OP02
        var setFilter = new HashSet<string> { "OP02" };
        var match = svc.FindClosestMatch(0x0000000000000001UL, setFilter: setFilter);

        // OP02-001 has hash 0xFFFF... which is distance 63 from 0x01 — beyond default maxDistance=14
        Assert.Null(match);
    }

    // --- SearchCards ---

    [Fact]
    public void SearchCards_Keyword_MatchesByName()
    {
        var svc = CreateService();
        var results = svc.SearchCards("Luffy");

        Assert.Single(results);
        Assert.Equal("Monkey D. Luffy", results[0].Name);
    }

    [Fact]
    public void SearchCards_SetQualifier_FiltersBySet()
    {
        var svc = CreateService();
        var results = svc.SearchCards("set:OP02");

        Assert.Single(results);
        Assert.Equal("Nami", results[0].Name);
        Assert.Equal("OP02", results[0].SetCode);
    }

    // --- Pricing ---

    [Fact]
    public void GetCurrentPrice_ReturnsStoredMarketPrice()
    {
        var svc = CreateService();
        var price = svc.GetCurrentPrice("OP01-001", isFoil: false);

        Assert.NotNull(price);
        Assert.Equal(12.50m, price.Value);
    }

    [Fact]
    public void GetCurrentPrices_ReturnsBatchPrices()
    {
        var svc = CreateService();
        var prices = svc.GetCurrentPrices(["OP01-001", "OP01-002", "NONEXISTENT"], isFoil: false);

        Assert.Equal(2, prices.Count);
        Assert.Equal(12.50m, prices["OP01-001"]);
        Assert.Equal(5.00m, prices["OP01-002"]);
        Assert.False(prices.ContainsKey("NONEXISTENT"));
    }

    private class TestOptcgDbFactory(DbContextOptions<OptcgDbContext> options)
        : IDbContextFactory<OptcgDbContext>
    {
        public OptcgDbContext CreateDbContext() => new(options);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OptcgServiceTests" -v normal
```

Expected: All 7 tests pass.

- [ ] **Step 3: Commit**

```bash
git add OmniCard.Tests/Services/OptcgServiceTests.cs
git commit -m "test: add OptcgService matching, search, and pricing baseline tests"
```

---

### Task 5: SetSymbolCacheTests

**Files:**
- Create: `OmniCard.Tests/Services/SetSymbolCacheTests.cs`

**Interfaces:**
- Consumes: `SetSymbolCache` from `OmniCard.CardMatching`, `IHttpClientFactory`, `IDataPathService` (mocked via Moq)
- Produces: 6 test cases covering name registration, rarity formatting, SVG download caching, and cache-hit behavior

- [ ] **Step 1: Create the test file**

Create `OmniCard.Tests/Services/SetSymbolCacheTests.cs`:

```csharp
using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Interfaces;

namespace OmniCard.Tests.Services;

public class SetSymbolCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IDataPathService> _mockPathService;

    private const string MinimalSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><circle cx="16" cy="16" r="16" fill="#000"/></svg>""";

    public SetSymbolCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"setsymbol-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _mockPathService = new Mock<IDataPathService>();
        _mockPathService.Setup(p => p.SymbolsCacheDirectory).Returns(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static IHttpClientFactory CreateMockHttpFactory(int callLimit = int.MaxValue)
    {
        var callCount = 0;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                if (callCount > callLimit)
                    throw new InvalidOperationException("HTTP should not have been called again");
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(MinimalSvg)),
                };
            });

        var client = new HttpClient(mockHandler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory.Object;
    }

    private SetSymbolCache CreateCache(IHttpClientFactory? httpFactory = null)
    {
        return new SetSymbolCache(
            httpFactory ?? CreateMockHttpFactory(),
            _mockPathService.Object,
            NullLogger<SetSymbolCache>.Instance);
    }

    // --- Name registration ---

    [Fact]
    public void RegisterSetName_GetSetName_RoundTrip()
    {
        var cache = CreateCache();
        cache.RegisterSetName("m10", "Magic 2010");
        Assert.Equal("Magic 2010", cache.GetSetName("M10")); // case-insensitive
    }

    [Fact]
    public void GetSetName_UnknownCode_ReturnsNull()
    {
        var cache = CreateCache();
        Assert.Null(cache.GetSetName("UNKNOWN"));
    }

    // --- FormatRarityDisplay ---

    [Theory]
    [InlineData("common", "Common")]
    [InlineData("uncommon", "Uncommon")]
    [InlineData("rare", "Rare")]
    [InlineData("mythic", "Mythic Rare")]
    [InlineData("special", "special")]
    [InlineData(null, "")]
    public void FormatRarityDisplay_ReturnsExpected(string? input, string expected)
    {
        Assert.Equal(expected, SetSymbolCache.FormatRarityDisplay(input!));
    }

    // --- GetSetSymbolAsync ---

    [Fact]
    public async Task GetSetSymbolAsync_UnsupportedRarity_ReturnsNull()
    {
        var cache = CreateCache();
        var result = await cache.GetSetSymbolAsync("M10", "special");
        Assert.Null(result);
    }

    [StaFact]
    public async Task GetSetSymbolAsync_Downloads_AndCachesToDisk()
    {
        var cache = CreateCache();
        var result = await cache.GetSetSymbolAsync("M10", "common");

        // File should be saved to disk
        var filePath = Path.Combine(_tempDir, "M10", "C.svg");
        Assert.True(File.Exists(filePath));
    }

    [StaFact]
    public async Task GetSetSymbolAsync_SecondCall_UsesCache_NoExtraHttp()
    {
        var httpFactory = CreateMockHttpFactory(callLimit: 1);
        var cache = CreateCache(httpFactory);

        // First call downloads
        await cache.GetSetSymbolAsync("M10", "common");
        // Second call should use in-memory cache (no HTTP)
        var result = await cache.GetSetSymbolAsync("M10", "common");

        // If this doesn't throw, the HTTP was only called once (callLimit: 1)
        Assert.NotNull(result);
    }

}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~SetSymbolCacheTests" -v normal
```

Expected: All 6 tests pass (the [StaFact] tests may take slightly longer due to WPF rendering).

- [ ] **Step 3: Commit**

```bash
git add OmniCard.Tests/Services/SetSymbolCacheTests.cs
git commit -m "test: add SetSymbolCache baseline tests"
```

---

### Task 6: CardArtCacheTests

**Files:**
- Create: `OmniCard.Tests/Services/CardArtCacheTests.cs`

**Interfaces:**
- Consumes: `CardArtCache` from `OmniCard.Imaging`, `IHttpClientFactory` (mocked via Moq)
- Produces: 7 test cases covering null paths, file loading, HTTP fallback, LRU eviction, evict, and clear

- [ ] **Step 1: Create the test file**

Create `OmniCard.Tests/Services/CardArtCacheTests.cs`:

```csharp
using System.Windows.Media.Imaging;
using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class CardArtCacheTests : IDisposable
{
    private readonly string _tempDir;

    public CardArtCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"artcache-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>Creates a minimal valid PNG file at the given path.</summary>
    private static string CreateTestImage(string directory, string filename = "test.png")
    {
        var path = Path.Combine(directory, filename);
        var bmp = new RenderTargetBitmap(100, 100, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create);
        encoder.Save(fs);
        return path;
    }

    /// <summary>Creates a mock IHttpClientFactory returning the given image bytes.</summary>
    private static IHttpClientFactory CreateMockHttpFactory(byte[] responseBytes)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBytes),
            });

        var client = new HttpClient(mockHandler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory.Object;
    }

    private CardArtCache CreateCache(IHttpClientFactory? httpFactory = null, int capacity = 20)
    {
        httpFactory ??= new Mock<IHttpClientFactory>().Object;
        return new CardArtCache(
            NullLogger<CardArtCache>.Instance,
            httpFactory,
            capacity);
    }

    // --- Null / empty path handling ---

    [Fact]
    public void GetImage_NullPaths_ReturnsNull()
    {
        var cache = CreateCache();
        var result = cache.GetImage(null, null);
        Assert.Null(result);
        Assert.Equal(0, cache.Count);
    }

    // --- Local file loading ---

    [StaFact]
    public void GetImage_LocalFile_ReturnsBitmapImage()
    {
        var cache = CreateCache();
        var path = CreateTestImage(_tempDir);

        var result = cache.GetImage(path, null);

        Assert.NotNull(result);
        Assert.True(result!.IsFrozen);
        Assert.Equal(1, cache.Count);
    }

    [StaFact]
    public void GetImage_SameKey_ReturnsCachedInstance()
    {
        var cache = CreateCache();
        var path = CreateTestImage(_tempDir);

        var first = cache.GetImage(path, null);
        var second = cache.GetImage(path, null);

        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
    }

    // --- HTTP fallback ---

    [StaFact]
    public void GetImage_HttpFallback_WhenLocalFileMissing()
    {
        // Create a PNG in memory for the HTTP mock response
        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            var bmp = new RenderTargetBitmap(50, 50, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(ms);
            pngBytes = ms.ToArray();
        }

        var httpFactory = CreateMockHttpFactory(pngBytes);
        var cache = CreateCache(httpFactory);

        // No local file, provide imageUri
        var result = cache.GetImage(null, "https://example.com/card.png");

        Assert.NotNull(result);
        Assert.True(result!.IsFrozen);
        Assert.Equal(1, cache.Count);
    }

    // --- LRU eviction ---

    [StaFact]
    public void LruEviction_AtCapacity_RemovesOldest()
    {
        var cache = CreateCache(capacity: 2);

        var p1 = CreateTestImage(_tempDir, "1.png");
        var p2 = CreateTestImage(_tempDir, "2.png");
        var p3 = CreateTestImage(_tempDir, "3.png");

        cache.GetImage(p1, null);
        cache.GetImage(p2, null);
        Assert.Equal(2, cache.Count);

        cache.GetImage(p3, null);
        Assert.Equal(2, cache.Count); // oldest (p1) evicted
    }

    // --- Evict / Clear ---

    [StaFact]
    public void Evict_RemovesEntry()
    {
        var cache = CreateCache();
        var path = CreateTestImage(_tempDir);
        cache.GetImage(path, null);
        Assert.Equal(1, cache.Count);

        cache.Evict(path);
        Assert.Equal(0, cache.Count);
    }

    [StaFact]
    public void Clear_EmptiesCache()
    {
        var cache = CreateCache();
        var p1 = CreateTestImage(_tempDir, "a.png");
        var p2 = CreateTestImage(_tempDir, "b.png");
        cache.GetImage(p1, null);
        cache.GetImage(p2, null);
        Assert.Equal(2, cache.Count);

        cache.Clear();
        Assert.Equal(0, cache.Count);
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CardArtCacheTests" -v normal
```

Expected: All 7 tests pass.

- [ ] **Step 3: Commit**

```bash
git add OmniCard.Tests/Services/CardArtCacheTests.cs
git commit -m "test: add CardArtCache baseline tests"
```

---

### Task 7: Web Tests (ScanController + Page Models)

**Files:**
- Create: `OmniCard.Tests/Web/ScanControllerTests.cs`
- Create: `OmniCard.Tests/Web/WebPageTests.cs`

**Interfaces:**
- Consumes: `ScanController` from `OmniCard.Web.Api`, `IndexModel`/`LocationModel`/`CardModel` from `OmniCard.Web.Pages`, `IHubContext<ScanHub>` (mocked via Moq), `CollectionDbContext` (SQLite in-memory)
- Produces: 10 test cases — 4 for ScanController upload validation, 6 for Razor page model queries

- [ ] **Step 1: Create the Web test directory**

```bash
mkdir -p d:/source/repos/OmniCard/OmniCard.Tests/Web
```

- [ ] **Step 2: Create ScanControllerTests**

Create `OmniCard.Tests/Web/ScanControllerTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Web.Api;
using OmniCard.Web.Hubs;
using Microsoft.AspNetCore.Mvc;

namespace OmniCard.Tests.Web;

public class ScanControllerTests
{
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly ScanController _controller;

    public ScanControllerTests()
    {
        _mockClientProxy = new Mock<IClientProxy>();

        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(_mockClientProxy.Object);

        var mockHubContext = new Mock<IHubContext<ScanHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        _controller = new ScanController(
            mockHubContext.Object,
            NullLogger<ScanController>.Instance);
    }

    private static IFormFile CreateFormFile(
        byte[]? content = null,
        string contentType = "image/jpeg",
        string fileName = "test.jpg",
        long? overrideLength = null)
    {
        content ??= [0xFF, 0xD8, 0xFF, 0xE0]; // JPEG magic bytes
        var stream = new MemoryStream(content);
        var file = new FormFile(stream, 0, overrideLength ?? content.Length, "image", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
        return file;
    }

    [Fact]
    public async Task Upload_ValidJpeg_Returns200WithSize()
    {
        var imageBytes = new byte[1024];
        var file = CreateFormFile(imageBytes, "image/jpeg");

        var result = await _controller.Upload(file);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode ?? 200);

        // Verify SignalR broadcast was invoked
        _mockClientProxy.Verify(
            c => c.SendCoreAsync("ImageReceived",
                It.Is<object?[]>(a => a != null && a.Length == 1 && a[0] is byte[]),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Upload_NoFile_Returns400()
    {
        var result = await _controller.Upload(null!);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public async Task Upload_WrongContentType_Returns400()
    {
        var file = CreateFormFile(contentType: "text/plain", fileName: "test.txt");

        var result = await _controller.Upload(file);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public async Task Upload_OversizedFile_Returns400()
    {
        // Create a FormFile that reports > 10 MB
        var file = CreateFormFile(
            content: new byte[1],
            overrideLength: 11 * 1024 * 1024);

        var result = await _controller.Upload(file);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }
}
```

- [ ] **Step 3: Create WebPageTests**

Create `OmniCard.Tests/Web/WebPageTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Web.Pages;

namespace OmniCard.Tests.Web;

public class WebPageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _factory;

    public WebPageTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection).Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static PageContext CreatePageContext()
    {
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        return new PageContext(actionContext);
    }

    // --- IndexModel ---

    [Fact]
    public void IndexModel_OnGet_ReturnsContainersOrdered()
    {
        using (var ctx = _factory.CreateDbContext())
        {
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Zebra Box",
                ContainerType = ContainerType.Box,
                SortOrder = 2,
            });
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Alpha Binder",
                ContainerType = ContainerType.Binder,
                SortOrder = 1,
            });
            ctx.StorageContainers.Add(new StorageContainer
            {
                Name = "Beta Binder",
                ContainerType = ContainerType.Binder,
                SortOrder = 1,
            });
            ctx.SaveChanges();
        }

        var model = new IndexModel(_factory) { PageContext = CreatePageContext() };
        model.OnGet();

        Assert.Equal(3, model.Containers.Count);
        // SortOrder=1 first (Alpha < Beta by Name), then SortOrder=2
        Assert.Equal("Alpha Binder", model.Containers[0].Name);
        Assert.Equal("Beta Binder", model.Containers[1].Name);
        Assert.Equal("Zebra Box", model.Containers[2].Name);
    }

    // --- LocationModel ---

    [Fact]
    public void LocationModel_OnGet_ReturnsCardsGroupedBySet()
    {
        int containerId;
        using (var ctx = _factory.CreateDbContext())
        {
            var container = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
            containerId = container.Id;

            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "c1", Name = "A", SetName = "Alpha", SetCode = "LEA", Number = "1", Rarity = "common", ContainerId = containerId });
            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "c2", Name = "B", SetName = "Alpha", SetCode = "LEA", Number = "2", Rarity = "common", ContainerId = containerId });
            ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "c3", Name = "C", SetName = "Beta", SetCode = "LEB", Number = "1", Rarity = "rare", ContainerId = containerId });
            ctx.SaveChanges();
        }

        var model = new LocationModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(containerId);

        Assert.IsType<PageResult>(result);
        Assert.Equal(3, model.CardCount);
        Assert.Equal(2, model.Sets.Count);
        Assert.Contains(model.Sets, s => s.SetCode == "LEA" && s.Count == 2);
        Assert.Contains(model.Sets, s => s.SetCode == "LEB" && s.Count == 1);
    }

    [Fact]
    public void LocationModel_OnGet_NonexistentContainer_Returns404()
    {
        var model = new LocationModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(99999);
        Assert.IsType<NotFoundResult>(result);
    }

    // --- CardModel ---

    [Fact]
    public void CardModel_OnGet_ReturnsCardWithContainer()
    {
        int cardId;
        using (var ctx = _factory.CreateDbContext())
        {
            var container = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();

            var card = new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "test-id",
                Name = "Lightning Bolt",
                SetName = "Alpha",
                SetCode = "LEA",
                Number = "161",
                Rarity = "common",
                ContainerId = container.Id,
                ImageUri = "https://img/bolt.jpg",
            };
            ctx.Cards.Add(card);
            ctx.SaveChanges();
            cardId = card.Id;
        }

        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(cardId);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Lightning Bolt", model.Card.Name);
        Assert.NotNull(model.Card.Container);
        Assert.Equal("Box", model.Card.Container!.Name);
    }

    [Fact]
    public void CardModel_OnGet_NonexistentCard_Returns404()
    {
        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        var result = model.OnGet(99999);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void CardModel_ImageUrl_ResolvesScanPathOverApiUri()
    {
        int cardId;
        using (var ctx = _factory.CreateDbContext())
        {
            var card = new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = "scan-card",
                Name = "Scanned Card",
                SetName = "Set",
                SetCode = "SET",
                Number = "1",
                Rarity = "common",
                ScanImagePath = "scans/12345.jpg",
                ImageUri = "https://api.example.com/card.jpg",
            };
            ctx.Cards.Add(card);
            ctx.SaveChanges();
            cardId = card.Id;
        }

        var model = new CardModel(_factory) { PageContext = CreatePageContext() };
        model.OnGet(cardId);

        // ScanImagePath takes precedence: extracts filename → /scans/12345.jpg
        Assert.Equal("/scans/12345.jpg", model.ImageUrl);
    }

    private class TestDbContextFactory(DbContextOptions<CollectionDbContext> options)
        : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
```

- [ ] **Step 4: Run all web tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OmniCard.Tests.Web" -v normal
```

Expected: All 10 tests pass (4 ScanController + 6 WebPage).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Tests/Web/
git commit -m "test: add Web layer baseline tests (ScanController + page models)"
```

---

## Verification

After all tasks are complete:

- [ ] **Run the full test suite**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v normal
```

Expected: All tests pass — both existing (~100+) and new (44).

- [ ] **Final commit (if any fixups needed)**

```bash
git add -A OmniCard.Tests/
git commit -m "test: complete baseline test coverage for all untested services"
```
