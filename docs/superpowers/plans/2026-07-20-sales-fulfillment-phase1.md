# Sales & Fulfillment — Phase 1 (Listing & Pick Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user mark collection cards as Listed for Sale (channel + price + qty), see a location-grouped pick list, and mark cards Picked (moving them to a designated For-Sale location) — all persisted in the unified store and surfaced in the collection UI.

**Architecture:** A new channel-agnostic `Listing` entity in the unified `OmniCardDbContext` drives the manual/TCGPlayer workflow (Fork A / Option 1 — eBay untouched). A new `IListingService` owns the Listed→Picked lifecycle and reuses `MovementType.Move` for the pick. A `sales-settings.json` file (mirroring `collection-presets.json`) holds the For-Sale location. The collection view gains right-click commands and a "Listed" tile badge; a new **Sales** tab hosts the Pick List.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, EF Core (SQLite via `IDbContextFactory<OmniCardDbContext>`), xUnit + Moq, QuestPDF (already referenced).

## Global Constraints

- Target framework: .NET 10 WPF (`net10.0-windows`). Match existing code style.
- Unified store only: all new tables live in `OmniCardDbContext` (`inventory.db`). No new DbContext.
- Schema on existing DBs: `EnsureCreated()` only builds a brand-new file, so every new table MUST also be created idempotently in `UnifiedMigrationService.EnsureUnifiedSchema(SqliteConnection)` via `CREATE TABLE IF NOT EXISTS`.
- Enum columns are stored as text: use `e.Property(x => x.Foo).HasConversion<string>()` in `OnModelCreating`, and `TEXT` columns in raw SQL.
- Tests: xUnit with in-memory SQLite, following the `OmniCard.Tests/Services/CollectionSortFilterTests.cs` harness (open a `:memory:` connection, `EnsureCreated()`, seed via `Product`+`InventoryLot`, use the private `MockOmniFactory`).
- Money is `decimal`; SQLite stores it as `TEXT` (EF maps decimal→TEXT by default in this project — see `LastMarketPrice`). Use `REAL` only where existing sibling columns do.
- Do NOT push or merge. Work on a branch; the user reviews and merges via PRs. Keep build + all tests green after every task.

---

## File Structure

- `OmniCard.Shared/Models/SalesChannel.cs` — new enum.
- `OmniCard.Shared/Models/ListingStatus.cs` — new enum.
- `OmniCard.Shared/Models/Listing.cs` — new entity.
- `OmniCard.Shared/Models/PickListEntry.cs` — new read model (record).
- `OmniCard.Shared/Models/SalesSettings.cs` — new settings POCO.
- `OmniCard.Shared/Interfaces/IListingService.cs` — new service interface.
- `OmniCard.Shared/Interfaces/ISalesSettingsService.cs` — new settings-service interface.
- `OmniCard.Data/OmniCardDbContext.cs` — add `DbSet<Listing>` + `OnModelCreating` config.
- `OmniCard.Data/UnifiedMigrationService.cs` — add `Listings` `CREATE TABLE IF NOT EXISTS` + indexes.
- `OmniCard.Collection/ListingService.cs` — new service implementation.
- `OmniCard.Collection/SalesSettingsService.cs` — new settings-service implementation.
- `OmniCard.Collection/CardService.cs` — extend `BuildFilteredQuery` projection with listing status (for badges) + expose a lookup.
- `OmniCard.Shared/Models/CollectionCard.cs` — add `[NotMapped] ListingStatus? ListingStatus`.
- `OmniCard/Views/Root/CollectionViewModel.cs` — add List/Unlist/MarkPicked commands.
- `OmniCard/Views/Root/CardListView.xaml` — context-menu entries + "Listed" badge.
- `OmniCard/Views/SalesListing/ListForSaleDialog.xaml(.cs)` + `ListForSaleViewModel.cs` — the list dialog.
- `OmniCard/Services/DialogService.cs` + `IDialogService.cs` — add `PickListForSale()`.
- `OmniCard/Views/Sales/SalesView.xaml(.cs)` + `SalesViewModel.cs` + `PickListView`/embedded — the Sales tab & pick list.
- `OmniCard/Views/Root/RootView.xaml` + `RootViewModel.cs` — add the Sales tab.
- `OmniCard/App.xaml.cs` — DI registrations.
- `OmniCard.Tests/Services/ListingServiceTests.cs`, `SalesSettingsServiceTests.cs` — new tests.

---

### Task 1: Enums + `Listing` entity + schema

**Files:**
- Create: `OmniCard.Shared/Models/SalesChannel.cs`, `OmniCard.Shared/Models/ListingStatus.cs`, `OmniCard.Shared/Models/Listing.cs`
- Modify: `OmniCard.Data/OmniCardDbContext.cs` (DbSets ~line 12; `OnModelCreating` after the `EbayListing` block ~line 116)
- Modify: `OmniCard.Data/UnifiedMigrationService.cs` (`EnsureUnifiedSchema(SqliteConnection)`, after the `EbayListings` block ~line 155)
- Test: `OmniCard.Tests/Services/ListingServiceTests.cs`

**Interfaces:**
- Produces: `enum SalesChannel { Manual, TcgPlayer, Ebay }`; `enum ListingStatus { Listed, Picked, Sold, Cancelled }`; `class Listing` with `int Id, int LotId, SalesChannel Channel, ListingStatus Status, decimal ListedPrice, int Quantity, int? OriginalLocationId, DateTime ListedAt, DateTime? PickedAt, string? ExternalRef, int? OrderLineId, string? Note`; `OmniCardDbContext.Listings` DbSet.

- [ ] **Step 1: Write the failing test** (verifies the entity round-trips through the real EF model)

Create `OmniCard.Tests/Services/ListingServiceTests.cs`:

```csharp
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListingServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;

    public ListingServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void Listing_RoundTrips_ThroughModel()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Listings.Add(new Listing
            {
                LotId = 5,
                Channel = SalesChannel.TcgPlayer,
                Status = ListingStatus.Listed,
                ListedPrice = 1.25m,
                Quantity = 1,
                OriginalLocationId = 3,
                ListedAt = new DateTime(2026, 1, 1),
            });
            ctx.SaveChanges();
        }

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var listing = Assert.Single(ctx.Listings.ToList());
            Assert.Equal(SalesChannel.TcgPlayer, listing.Channel);
            Assert.Equal(ListingStatus.Listed, listing.Status);
            Assert.Equal(1.25m, listing.ListedPrice);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: FAIL — compile error, `Listing` / `Listings` do not exist.

- [ ] **Step 3: Create the enums**

`OmniCard.Shared/Models/SalesChannel.cs`:

```csharp
namespace OmniCard.Models;

public enum SalesChannel { Manual, TcgPlayer, Ebay }
```

`OmniCard.Shared/Models/ListingStatus.cs`:

```csharp
namespace OmniCard.Models;

public enum ListingStatus { Listed, Picked, Sold, Cancelled }
```

- [ ] **Step 4: Create the `Listing` entity**

`OmniCard.Shared/Models/Listing.cs`:

```csharp
namespace OmniCard.Models;

public class Listing
{
    public int Id { get; set; }
    public int LotId { get; set; }
    public SalesChannel Channel { get; set; }
    public ListingStatus Status { get; set; }
    public decimal ListedPrice { get; set; }
    public int Quantity { get; set; } = 1;
    /// <summary>The lot's location when it was listed, so the pick list can show where to find it and Unlist can restore it.</summary>
    public int? OriginalLocationId { get; set; }
    public DateTime ListedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PickedAt { get; set; }
    public string? ExternalRef { get; set; }
    /// <summary>Set when the listing is sold (links to the order line). Populated in Phase 2.</summary>
    public int? OrderLineId { get; set; }
    public string? Note { get; set; }
}
```

- [ ] **Step 5: Register in `OmniCardDbContext`**

Add the DbSet next to the others (after line 12, `EbayListings`):

```csharp
    public DbSet<Listing> Listings => Set<Listing>();
```

In `OnModelCreating`, after the `EbayListing` entity block (~line 131), add:

```csharp
        modelBuilder.Entity<Listing>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.Property(l => l.Channel).HasConversion<string>();
            e.Property(l => l.Status).HasConversion<string>();
            e.HasIndex(l => l.LotId);
            e.HasIndex(l => l.Status);
        });
```

- [ ] **Step 6: Add idempotent `CREATE TABLE` for existing DBs**

In `UnifiedMigrationService.EnsureUnifiedSchema(SqliteConnection conn)`, after the `EbayListings` index statements (~line 155, before the `MismatchLogs` block), add:

```csharp
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Listings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LotId INTEGER NOT NULL,
                Channel TEXT NOT NULL DEFAULT 'Manual',
                Status TEXT NOT NULL DEFAULT 'Listed',
                ListedPrice TEXT NOT NULL DEFAULT '0',
                Quantity INTEGER NOT NULL DEFAULT 1,
                OriginalLocationId INTEGER,
                ListedAt TEXT NOT NULL,
                PickedAt TEXT,
                ExternalRef TEXT,
                OrderLineId INTEGER,
                Note TEXT
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Listings_LotId ON Listings(LotId)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Listings_Status ON Listings(Status)";
        cmd.ExecuteNonQuery();
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/SalesChannel.cs OmniCard.Shared/Models/ListingStatus.cs OmniCard.Shared/Models/Listing.cs OmniCard.Data/OmniCardDbContext.cs OmniCard.Data/UnifiedMigrationService.cs OmniCard.Tests/Services/ListingServiceTests.cs
git commit -m "feat(sales): add Listing entity + schema for sales/fulfillment phase 1"
```

---

### Task 2: `SalesSettingsService` (For-Sale location, `sales-settings.json`)

**Files:**
- Create: `OmniCard.Shared/Models/SalesSettings.cs`, `OmniCard.Shared/Interfaces/ISalesSettingsService.cs`, `OmniCard.Collection/SalesSettingsService.cs`
- Test: `OmniCard.Tests/Services/SalesSettingsServiceTests.cs`

**Interfaces:**
- Consumes: `IDataPathService.DataDirectory` (existing).
- Produces: `ISalesSettingsService { int? ForSaleLocationId { get; } void SetForSaleLocationId(int? id); }`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/SalesSettingsServiceTests.cs`:

```csharp
using System.IO;
using OmniCard.Collection;
using OmniCard.Data;
using Xunit;

namespace OmniCard.Tests.Services;

public class SalesSettingsServiceTests
{
    [Fact]
    public void ForSaleLocationId_Persists_AcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dps = new DataPathServiceStub(dir);
            new SalesSettingsService(dps).SetForSaleLocationId(42);
            Assert.Equal(42, new SalesSettingsService(dps).ForSaleLocationId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class DataPathServiceStub(string dir) : OmniCard.Interfaces.IDataPathService
    {
        public string DataDirectory => dir;
        public string ScansDirectory => dir;
        public string TempScansDirectory => dir;
        public string SymbolsCacheDirectory => dir;
        public string LogsDirectory => dir;
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
```

> Note: if `IDataPathService` has more members than shown, implement them as trivial stubs returning `dir`/`null`/no-op. Check `OmniCard.Shared/Interfaces/IDataPathService.cs` and match its exact surface.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~SalesSettingsServiceTests" -v q`
Expected: FAIL — `SalesSettingsService` does not exist.

- [ ] **Step 3: Create the settings POCO + interface**

`OmniCard.Shared/Models/SalesSettings.cs`:

```csharp
namespace OmniCard.Models;

public class SalesSettings
{
    public int? ForSaleLocationId { get; set; }
}
```

`OmniCard.Shared/Interfaces/ISalesSettingsService.cs`:

```csharp
namespace OmniCard.Interfaces;

public interface ISalesSettingsService
{
    int? ForSaleLocationId { get; }
    void SetForSaleLocationId(int? id);
}
```

- [ ] **Step 4: Implement the service** (mirrors `CollectionPresetService` JSON handling)

`OmniCard.Collection/SalesSettingsService.cs`:

```csharp
using System.IO;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class SalesSettingsService : ISalesSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public SalesSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "sales-settings.json");
    }

    public int? ForSaleLocationId => Load().ForSaleLocationId;

    public void SetForSaleLocationId(int? id)
    {
        var settings = Load();
        settings.ForSaleLocationId = id;
        Save(settings);
    }

    private SalesSettings Load()
    {
        if (!File.Exists(_filePath))
            return new SalesSettings();
        return JsonSerializer.Deserialize<SalesSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new SalesSettings();
    }

    private void Save(SalesSettings settings)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~SalesSettingsServiceTests" -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/SalesSettings.cs OmniCard.Shared/Interfaces/ISalesSettingsService.cs OmniCard.Collection/SalesSettingsService.cs OmniCard.Tests/Services/SalesSettingsServiceTests.cs
git commit -m "feat(sales): add SalesSettingsService for For-Sale location"
```

---

### Task 3: `IListingService.ListForSale` + `Unlist`

**Files:**
- Create: `OmniCard.Shared/Interfaces/IListingService.cs`, `OmniCard.Collection/ListingService.cs`
- Modify: `OmniCard.Tests/Services/ListingServiceTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<OmniCardDbContext>`, `ISalesSettingsService` (used in Task 4).
- Produces:
  - `int ListForSale(IEnumerable<int> lotIds, SalesChannel channel, decimal price, int quantity, string? note = null)` — creates a `Listed` `Listing` per lot that has no active listing; snapshots `OriginalLocationId` from the lot; returns count created.
  - `void Unlist(IEnumerable<int> lotIds)` — cancels the active (Listed/Picked) listing for each lot; if it was `Picked`, moves the lot back to `OriginalLocationId` and records a `Move` movement.
  - Helper (internal, tested): `bool HasActiveListing(int lotId)`.

- [ ] **Step 1: Write the failing test** (add to `ListingServiceTests`)

Add a seed helper and tests:

```csharp
    private static (int lotId, int locId) SeedLot(DbContextOptions<OmniCardDbContext> opts, int? locationId = 7)
    {
        using var ctx = new OmniCardDbContext(opts);
        var p = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring" };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = p.Id, Quantity = 1, LocationId = locationId };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return (lot.Id, locationId ?? 0);
    }

    private ListingService CreateService()
        => new(new MockFactory(_opts), new StubSalesSettings());

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> o)
        : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private sealed class StubSalesSettings : OmniCard.Interfaces.ISalesSettingsService
    {
        public int? ForSaleLocationId { get; private set; } = 99;
        public void SetForSaleLocationId(int? id) => ForSaleLocationId = id;
    }

    [Fact]
    public void ListForSale_CreatesListedListing_WithLocationSnapshot()
    {
        var (lotId, _) = SeedLot(_opts, locationId: 7);
        var count = CreateService().ListForSale([lotId], SalesChannel.TcgPlayer, 1.50m, 1);
        Assert.Equal(1, count);
        using var ctx = new OmniCardDbContext(_opts);
        var listing = Assert.Single(ctx.Listings.ToList());
        Assert.Equal(ListingStatus.Listed, listing.Status);
        Assert.Equal(7, listing.OriginalLocationId);
        Assert.Equal(1.50m, listing.ListedPrice);
    }

    [Fact]
    public void ListForSale_SkipsLotWithActiveListing()
    {
        var (lotId, _) = SeedLot(_opts);
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);
        var second = svc.ListForSale([lotId], SalesChannel.Manual, 2m, 1);
        Assert.Equal(0, second);
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Listings.ToList());
    }

    [Fact]
    public void Unlist_CancelsActiveListing()
    {
        var (lotId, _) = SeedLot(_opts);
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);
        svc.Unlist([lotId]);
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(ListingStatus.Cancelled, Assert.Single(ctx.Listings.ToList()).Status);
    }
```

Add `using Microsoft.EntityFrameworkCore;` and `using OmniCard.Collection;` at the top if not present.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: FAIL — `ListingService` does not exist.

- [ ] **Step 3: Create the interface**

`OmniCard.Shared/Interfaces/IListingService.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IListingService
{
    int ListForSale(IEnumerable<int> lotIds, SalesChannel channel, decimal price, int quantity, string? note = null);
    void Unlist(IEnumerable<int> lotIds);
    int MarkPicked(IEnumerable<int> lotIds);
    List<PickListEntry> GetPickList(CardGame? game = null);
    Dictionary<int, ListingStatus> GetActiveListingStatusByLot(IEnumerable<int> lotIds);
}
```

> `PickListEntry`, `MarkPicked`, `GetPickList`, and `GetActiveListingStatusByLot` are implemented in Tasks 4–5; include them in the interface now and stub the not-yet-implemented ones with `throw new NotImplementedException();` so the project compiles. Each later task replaces its stub with a tested implementation.

- [ ] **Step 4: Implement `ListForSale` + `Unlist` (stub the rest)**

`OmniCard.Collection/ListingService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class ListingService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    ISalesSettingsService salesSettings) : IListingService
{
    private static readonly ListingStatus[] ActiveStatuses = [ListingStatus.Listed, ListingStatus.Picked];

    public int ListForSale(IEnumerable<int> lotIds, SalesChannel channel, decimal price, int quantity, string? note = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var ids = lotIds.Distinct().ToList();

        var alreadyListed = ctx.Listings
            .Where(l => ids.Contains(l.LotId) && ActiveStatuses.Contains(l.Status))
            .Select(l => l.LotId)
            .ToHashSet();

        var lotLocations = ctx.Lots
            .Where(l => ids.Contains(l.Id))
            .ToDictionary(l => l.Id, l => l.LocationId);

        var created = 0;
        foreach (var lotId in ids)
        {
            if (alreadyListed.Contains(lotId) || !lotLocations.ContainsKey(lotId))
                continue;

            ctx.Listings.Add(new Listing
            {
                LotId = lotId,
                Channel = channel,
                Status = ListingStatus.Listed,
                ListedPrice = price,
                Quantity = quantity,
                OriginalLocationId = lotLocations[lotId],
                ListedAt = DateTime.UtcNow,
                Note = note,
            });
            created++;
        }

        ctx.SaveChanges();
        return created;
    }

    public void Unlist(IEnumerable<int> lotIds)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var ids = lotIds.Distinct().ToList();

        var listings = ctx.Listings
            .Where(l => ids.Contains(l.LotId) && ActiveStatuses.Contains(l.Status))
            .ToList();

        foreach (var listing in listings)
        {
            // If already picked, physically return it to its original location.
            if (listing.Status == ListingStatus.Picked && listing.OriginalLocationId is not null)
            {
                var lot = ctx.Lots.FirstOrDefault(l => l.Id == listing.LotId);
                if (lot is not null && lot.LocationId != listing.OriginalLocationId)
                {
                    lot.LocationId = listing.OriginalLocationId;
                    ctx.Movements.Add(new InventoryMovement
                    {
                        ProductId = lot.ProductId,
                        LotId = lot.Id,
                        Type = MovementType.Move,
                        Quantity = lot.Quantity,
                        Timestamp = DateTime.UtcNow,
                        Note = "Unlisted — returned to original location",
                    });
                }
            }
            listing.Status = ListingStatus.Cancelled;
        }

        ctx.SaveChanges();
    }

    public int MarkPicked(IEnumerable<int> lotIds) => throw new NotImplementedException();
    public List<PickListEntry> GetPickList(CardGame? game = null) => throw new NotImplementedException();
    public Dictionary<int, ListingStatus> GetActiveListingStatusByLot(IEnumerable<int> lotIds) => throw new NotImplementedException();
}
```

> `PickListEntry` doesn't exist yet — Task 4 Step 3 creates it. To compile now, create the file `OmniCard.Shared/Models/PickListEntry.cs` as part of this task with the record defined in Task 4 Step 3 (move that file creation here if executing strictly in order). Simplest: create `PickListEntry.cs` now (see Task 4 Step 3 for the exact record).

- [ ] **Step 5: Create `PickListEntry` so the interface compiles**

`OmniCard.Shared/Models/PickListEntry.cs`:

```csharp
namespace OmniCard.Models;

public record PickListEntry(
    int LotId,
    string Name,
    string SetName,
    string? Condition,
    bool IsFoil,
    string LocationName,
    string? Section,
    int? Page,
    int? Slot,
    decimal ListedPrice,
    int Quantity);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: PASS (3 new tests + Task 1 test).

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Interfaces/IListingService.cs OmniCard.Shared/Models/PickListEntry.cs OmniCard.Collection/ListingService.cs OmniCard.Tests/Services/ListingServiceTests.cs
git commit -m "feat(sales): ListForSale + Unlist in ListingService"
```

---

### Task 4: `IListingService.MarkPicked` (move to For-Sale location + movement)

**Files:**
- Modify: `OmniCard.Collection/ListingService.cs` (replace `MarkPicked` stub)
- Modify: `OmniCard.Tests/Services/ListingServiceTests.cs`

**Interfaces:**
- Produces: `int MarkPicked(IEnumerable<int> lotIds)` — for each lot with a `Listed` listing: set `Status=Picked`, `PickedAt=now`; set `lot.LocationId = salesSettings.ForSaleLocationId`; record a `MovementType.Move` movement. Returns count picked. If `ForSaleLocationId` is null, throws `InvalidOperationException` (caller must configure it first).

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void MarkPicked_MovesLotToForSaleLocation_AndRecordsMovement()
    {
        var (lotId, _) = SeedLot(_opts, locationId: 7);
        var svc = CreateService(); // StubSalesSettings.ForSaleLocationId = 99
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);

        var count = svc.MarkPicked([lotId]);

        Assert.Equal(1, count);
        using var ctx = new OmniCardDbContext(_opts);
        var listing = Assert.Single(ctx.Listings.ToList());
        Assert.Equal(ListingStatus.Picked, listing.Status);
        Assert.NotNull(listing.PickedAt);
        Assert.Equal(99, ctx.Lots.Single(l => l.Id == lotId).LocationId);
        Assert.Contains(ctx.Movements.ToList(), m => m.Type == MovementType.Move && m.LotId == lotId);
    }

    [Fact]
    public void MarkPicked_Throws_WhenNoForSaleLocationConfigured()
    {
        var (lotId, _) = SeedLot(_opts);
        var settings = new StubSalesSettings();
        settings.SetForSaleLocationId(null);
        var svc = new ListingService(new MockFactory(_opts), settings);
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);
        Assert.Throws<InvalidOperationException>(() => svc.MarkPicked([lotId]));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Implement `MarkPicked`** (replace the stub)

```csharp
    public int MarkPicked(IEnumerable<int> lotIds)
    {
        var forSaleLocationId = salesSettings.ForSaleLocationId
            ?? throw new InvalidOperationException("No 'For Sale' location is configured. Set one in Sales settings before picking.");

        using var ctx = dbContextFactory.CreateDbContext();
        var ids = lotIds.Distinct().ToList();

        var listings = ctx.Listings
            .Where(l => ids.Contains(l.LotId) && l.Status == ListingStatus.Listed)
            .ToList();

        var picked = 0;
        foreach (var listing in listings)
        {
            var lot = ctx.Lots.FirstOrDefault(l => l.Id == listing.LotId);
            if (lot is null) continue;

            listing.Status = ListingStatus.Picked;
            listing.PickedAt = DateTime.UtcNow;
            lot.LocationId = forSaleLocationId;
            ctx.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Move,
                Quantity = lot.Quantity,
                Timestamp = DateTime.UtcNow,
                Note = "Picked for sale",
            });
            picked++;
        }

        ctx.SaveChanges();
        return picked;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/ListingService.cs OmniCard.Tests/Services/ListingServiceTests.cs
git commit -m "feat(sales): MarkPicked moves lot to For-Sale location + records movement"
```

---

### Task 5: `GetPickList` + `GetActiveListingStatusByLot`

**Files:**
- Modify: `OmniCard.Collection/ListingService.cs` (replace both stubs)
- Modify: `OmniCard.Tests/Services/ListingServiceTests.cs`

**Interfaces:**
- Produces:
  - `List<PickListEntry> GetPickList(CardGame? game = null)` — all `Listed` (not yet picked) listings joined to `Lot`+`Product`(+`StorageContainer` for name), ordered by `LocationName, Section, Page, Slot`.
  - `Dictionary<int, ListingStatus> GetActiveListingStatusByLot(IEnumerable<int> lotIds)` — for the given lots, the status of any active (Listed/Picked) listing.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void GetPickList_ReturnsListedNotPicked_GroupedByLocation()
    {
        var (lotId, _) = SeedLot(_opts, locationId: null);
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 3.25m, 1);

        var pick = svc.GetPickList(CardGame.Mtg);

        var entry = Assert.Single(pick);
        Assert.Equal(lotId, entry.LotId);
        Assert.Equal("Sol Ring", entry.Name);
        Assert.Equal(3.25m, entry.ListedPrice);
    }

    [Fact]
    public void GetPickList_ExcludesPicked()
    {
        var (lotId, _) = SeedLot(_opts, locationId: 7);
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);
        svc.MarkPicked([lotId]);
        Assert.Empty(svc.GetPickList());
    }

    [Fact]
    public void GetActiveListingStatusByLot_ReportsListedAndPicked()
    {
        var a = SeedLot(_opts).lotId;
        var b = SeedLot(_opts).lotId;
        var svc = CreateService();
        svc.ListForSale([a, b], SalesChannel.Manual, 1m, 1);
        svc.MarkPicked([b]);

        var map = svc.GetActiveListingStatusByLot([a, b]);
        Assert.Equal(ListingStatus.Listed, map[a]);
        Assert.Equal(ListingStatus.Picked, map[b]);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Implement both methods** (replace stubs)

```csharp
    public List<PickListEntry> GetPickList(CardGame? game = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var query =
            from listing in ctx.Listings.AsNoTracking()
            where listing.Status == ListingStatus.Listed
            join lot in ctx.Lots.AsNoTracking() on listing.LotId equals lot.Id
            join p in ctx.Products.AsNoTracking() on lot.ProductId equals p.Id
            join sc in ctx.StorageContainers.AsNoTracking() on lot.LocationId equals sc.Id into scj
            from sc in scj.DefaultIfEmpty()
            where game == null || p.Game == game
            select new PickListEntry(
                lot.Id,
                p.Name,
                p.SetName ?? "",
                lot.Condition,
                p.Foil,
                sc != null ? sc.Name : "(unassigned)",
                lot.Section,
                lot.Page,
                lot.Slot,
                listing.ListedPrice,
                listing.Quantity);

        return query
            .OrderBy(e => e.LocationName).ThenBy(e => e.Section).ThenBy(e => e.Page).ThenBy(e => e.Slot)
            .ToList();
    }

    public Dictionary<int, ListingStatus> GetActiveListingStatusByLot(IEnumerable<int> lotIds)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var ids = lotIds.Distinct().ToList();
        return ctx.Listings.AsNoTracking()
            .Where(l => ids.Contains(l.LotId) && ActiveStatuses.Contains(l.Status))
            .ToList()
            .GroupBy(l => l.LotId)
            .ToDictionary(g => g.Key, g => g.Max(l => l.Status));
    }
```

> Verify the `Product` foil property name: the pick-list join uses `p.Foil` (matches `CollectionCardMapper`/`BuildFilteredQuery` which read `p.Foil`). If the property is named differently, use that name.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: PASS (all ListingService tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/ListingService.cs OmniCard.Tests/Services/ListingServiceTests.cs
git commit -m "feat(sales): pick-list query + active-listing status lookup"
```

---

### Task 6: Surface listing status on `CollectionCard` (for badges)

**Files:**
- Modify: `OmniCard.Shared/Models/CollectionCard.cs` (add `[NotMapped] ListingStatus? ListingStatus`)
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (after a search completes, populate listing status via `IListingService.GetActiveListingStatusByLot`)
- Test: `OmniCard.Tests/Services/ListingStatusHydrationTests.cs` (optional lightweight test of the mapping helper)

**Interfaces:**
- Consumes: `IListingService.GetActiveListingStatusByLot`.
- Produces: `CollectionCard.ListingStatus` populated on displayed cards.

- [ ] **Step 1: Add the property**

In `OmniCard.Shared/Models/CollectionCard.cs`, after `Quantity` (~line 50):

```csharp
    /// <summary>Active listing status for this lot (Listed/Picked), or null if not on the market. Not persisted.</summary>
    [NotMapped]
    public ListingStatus? ListingStatus { get; set; }
```

- [ ] **Step 2: Populate it after a search** (in `CollectionViewModel.SearchCollectionCore`, inside the `Task.Run`, right after `HydrateMissingImageUris(results);` ~line 475)

```csharp
                // Tag on-market cards so the tile badge can render.
                var statusByLot = _listingService.GetActiveListingStatusByLot(results.Select(c => c.Id));
                foreach (var card in results)
                    card.ListingStatus = statusByLot.TryGetValue(card.Id, out var st) ? st : null;
```

- [ ] **Step 3: Inject `IListingService` into `CollectionViewModel`**

Add `IListingService listingService` to the constructor parameter list and store it as `private readonly IListingService _listingService = listingService;` following the existing field convention in that file. (Check the constructor style — it uses primary-constructor-style injected params assigned to `_`-prefixed fields.)

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -v q`
Expected: `Build succeeded` (DI wiring added in Task 9; constructor param is fine to add now since DI registration comes in Task 9 — but to keep the build+run correct, do Task 9's registration in the same commit if the app is run between tasks).

> Because adding a constructor dependency without its DI registration would break app startup (not the build), register `IListingService` in DI now as part of this task (copy the one-liner from Task 9 Step for `services.AddSingleton<IListingService, ListingService>();` and `services.AddSingleton<ISalesSettingsService, SalesSettingsService>();`). Task 9 then only adds the remaining VM/dialog registrations.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v q`
Expected: PASS (no regressions).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/CollectionCard.cs OmniCard/Views/Root/CollectionViewModel.cs OmniCard/App.xaml.cs
git commit -m "feat(sales): tag displayed cards with active listing status"
```

---

### Task 7: List-for-Sale dialog

**Files:**
- Create: `OmniCard/Views/SalesListing/ListForSaleViewModel.cs`, `OmniCard/Views/SalesListing/ListForSaleDialog.xaml`, `ListForSaleDialog.xaml.cs`
- Modify: `OmniCard/Services/DialogService.cs`, `OmniCard.Shared/Interfaces/IDialogService.cs`
- Create: `OmniCard.Shared/Models/ListForSaleResult.cs`

**Interfaces:**
- Produces:
  - `record ListForSaleResult(SalesChannel Channel, decimal Price, int Quantity)`.
  - `IDialogService.ListForSaleResult? PickListForSale(decimal suggestedPrice)`.

- [ ] **Step 1: Create the result record**

`OmniCard.Shared/Models/ListForSaleResult.cs`:

```csharp
namespace OmniCard.Models;

public record ListForSaleResult(SalesChannel Channel, decimal Price, int Quantity);
```

- [ ] **Step 2: Create the dialog view model**

`OmniCard/Views/SalesListing/ListForSaleViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.SalesListing;

public partial class ListForSaleViewModel : ObservableObject
{
    public ListForSaleViewModel(decimal suggestedPrice)
    {
        Price = suggestedPrice;
    }

    public SalesChannel[] Channels { get; } = [SalesChannel.TcgPlayer, SalesChannel.Manual];

    [ObservableProperty]
    public partial SalesChannel SelectedChannel { get; set; } = SalesChannel.TcgPlayer;

    [ObservableProperty]
    public partial decimal Price { get; set; }

    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    public ListForSaleResult ToResult() => new(SelectedChannel, Price, Quantity);
}
```

- [ ] **Step 3: Create the dialog window** (follow the existing `SortFilterBuilderView` window pattern for styling/DataContext)

`OmniCard/Views/SalesListing/ListForSaleDialog.xaml`:

```xml
<Window x:Class="OmniCard.Views.SalesListing.ListForSaleDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="List for Sale" SizeToContent="WidthAndHeight"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <StackPanel Margin="16" MinWidth="260">
        <TextBlock Text="Channel"/>
        <ComboBox ItemsSource="{Binding Channels}" SelectedItem="{Binding SelectedChannel}" Margin="0,4,0,8"/>
        <TextBlock Text="Price"/>
        <TextBox Text="{Binding Price, StringFormat=F2, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,8"/>
        <TextBlock Text="Quantity"/>
        <TextBox Text="{Binding Quantity, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Cancel" IsCancel="True" MinWidth="72" Margin="0,0,8,0"/>
            <Button Content="List" IsDefault="True" MinWidth="72" Click="OnList"/>
        </StackPanel>
    </StackPanel>
</Window>
```

`OmniCard/Views/SalesListing/ListForSaleDialog.xaml.cs`:

```csharp
using System.Windows;

namespace OmniCard.Views.SalesListing;

public partial class ListForSaleDialog : Window
{
    public ListForSaleDialog(ListForSaleViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnList(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
```

- [ ] **Step 4: Add the `IDialogService` method** (interface + impl, following `PickMoveToLocation` at `DialogService.cs:141`)

In `OmniCard.Shared/Interfaces/IDialogService.cs`:

```csharp
    ListForSaleResult? PickListForSale(decimal suggestedPrice);
```

In `OmniCard/Services/DialogService.cs` (match the owner-window + `ShowDialog()` pattern used by `PickMoveToLocation`):

```csharp
    public ListForSaleResult? PickListForSale(decimal suggestedPrice)
    {
        var vm = new Views.SalesListing.ListForSaleViewModel(suggestedPrice);
        var dialog = new Views.SalesListing.ListForSaleDialog(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        return dialog.ShowDialog() == true ? vm.ToResult() : null;
    }
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -v q`
Expected: `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/ListForSaleResult.cs OmniCard/Views/SalesListing/ OmniCard/Services/DialogService.cs OmniCard.Shared/Interfaces/IDialogService.cs
git commit -m "feat(sales): List-for-Sale dialog"
```

---

### Task 8: Collection right-click commands (List / Unlist / Mark Picked)

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (add commands near the other bulk commands ~line 756)
- Modify: `OmniCard/Views/Root/CardListView.xaml` (context menu ~line 176)

**Interfaces:**
- Consumes: `IListingService`, `IDialogService.PickListForSale`, existing `GetAllSelectedCardIds()`, `ReportMessage`, `SearchCollection()`.

- [ ] **Step 1: Add the commands** (in `CollectionViewModel`, following the `BulkSetCollectionCondition` pattern)

```csharp
    [RelayCommand]
    public void ListForSale()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;

        var suggested = GetSelectedCards?.Invoke()?.FirstOrDefault()?.MarketPrice ?? 0m;
        var result = _dialogService.PickListForSale(suggested);
        if (result is null) return;

        var count = _listingService.ListForSale(ids, result.Channel, result.Price, result.Quantity);
        ReportMessage?.Invoke($"Listed {count} card(s) for sale.");
        _ = SearchCollection();
    }

    [RelayCommand]
    public void UnlistForSale()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        _listingService.Unlist(ids);
        ReportMessage?.Invoke($"Unlisted {ids.Count} card(s).");
        _ = SearchCollection();
    }

    [RelayCommand]
    public void MarkPicked()
    {
        var ids = GetAllSelectedCardIds();
        if (ids.Count == 0) return;
        try
        {
            var count = _listingService.MarkPicked(ids);
            ReportMessage?.Invoke($"Marked {count} card(s) picked.");
            _ = SearchCollection();
        }
        catch (InvalidOperationException ex)
        {
            ReportMessage?.Invoke(ex.Message);
        }
    }
```

- [ ] **Step 2: Add context-menu entries** (in `CardListView.xaml`, inside the `<ContextMenu>` after the "Move to Location..." item ~line 161)

```xml
                <Separator/>
                <MenuItem Header="List for Sale..."
                          Command="{Binding ListForSaleCommand}"/>
                <MenuItem Header="Mark Picked"
                          Command="{Binding MarkPickedCommand}"/>
                <MenuItem Header="Unlist"
                          Command="{Binding UnlistForSaleCommand}"/>
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -v q`
Expected: `Build succeeded`.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/CollectionViewModel.cs OmniCard/Views/Root/CardListView.xaml
git commit -m "feat(sales): right-click List/Unlist/Mark-Picked in collection"
```

---

### Task 9: "Listed" tile badge + DI wiring

**Files:**
- Modify: `OmniCard/Views/Root/CardListView.xaml` (tile `DataTemplate` ~line 104)
- Modify: `OmniCard/App.xaml.cs` (DI registrations — any not already added in Task 6)
- Create: `OmniCard.Controls/Converters/ListingStatusToBadgeConverter.cs` (or reuse an existing converter pattern)

**Interfaces:**
- Consumes: `CollectionCard.ListingStatus`.

- [ ] **Step 1: Add a converter** for `ListingStatus? → badge text/visibility. Follow the converter style in `OmniCard.Controls/Converters/RootConverters.cs`.

`OmniCard.Controls/Converters/ListingStatusToBadgeConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using OmniCard.Models;

namespace OmniCard.Controls.Converters;

public sealed class ListingStatusToBadgeConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ListingStatus s ? (s == ListingStatus.Picked ? "PICKED" : "LISTED") : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
```

> If a null-to-visibility converter already exists (e.g. `NullToVisibleConverter` used in this file), reuse it for the badge's `Visibility` bound to `ListingStatus`.

- [ ] **Step 2: Add the badge to the tile** (in `CardListView.xaml`, overlay on the art `Grid` ~line 106, as a sibling `Border` in that `Grid`)

```xml
                            <Border VerticalAlignment="Top" HorizontalAlignment="Left"
                                    Margin="4" Padding="4,1" CornerRadius="3"
                                    Background="{DynamicResource MaterialDesign.Brush.Primary}"
                                    Visibility="{Binding ListingStatus, Converter={conv:NullToVisibleConverter}, ConverterParameter=Invert}">
                                <TextBlock Text="{Binding ListingStatus, Converter={conv:ListingStatusToBadgeConverter}}"
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="White"/>
                            </Border>
```

> Verify `NullToVisibleConverter` semantics in this project (the file already uses it at ~line 110 for a placeholder). If it shows on null, you need the inverse — either add an `Invert` parameter path or create a `NotNullToVisibleConverter`. Pick whichever matches the existing converter's contract; the badge must be visible only when `ListingStatus != null`.

- [ ] **Step 3: Ensure DI registrations exist** (in `App.xaml.cs`, near the other `AddSingleton` calls ~line 115). Add any not already added in Task 6:

```csharp
            services.AddSingleton<ISalesSettingsService, SalesSettingsService>();
            services.AddSingleton<IListingService, ListingService>();
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -v q`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Controls/Converters/ListingStatusToBadgeConverter.cs OmniCard/Views/Root/CardListView.xaml OmniCard/App.xaml.cs
git commit -m "feat(sales): Listed/Picked tile badge + DI wiring"
```

---

### Task 10: Sales tab + Pick List view

**Files:**
- Create: `OmniCard/Views/Sales/SalesViewModel.cs`, `OmniCard/Views/Sales/SalesView.xaml`, `SalesView.xaml.cs`
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (inject + expose `Sales`), `OmniCard/Views/Root/RootView.xaml` (add `TabItem`), `OmniCard/App.xaml.cs` (register `SalesViewModel`)

**Interfaces:**
- Consumes: `IListingService.GetPickList`, `IListingService.MarkPicked`, `ISalesSettingsService`, `IStorageContainerService.GetAll` (for the For-Sale location picker).

- [ ] **Step 1: Create `SalesViewModel`**

`OmniCard/Views/Sales/SalesViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public partial class SalesViewModel(
    IListingService listingService,
    ISalesSettingsService salesSettings,
    IStorageContainerService storageContainers) : ObservableObject
{
    public ObservableCollection<PickListEntry> PickList { get; } = [];
    public ObservableCollection<StorageContainer> Locations { get; } = [];

    [ObservableProperty]
    public partial StorageContainer? ForSaleLocation { get; set; }

    public void Load()
    {
        Locations.Clear();
        foreach (var c in storageContainers.GetAll())
            Locations.Add(c);
        ForSaleLocation = Locations.FirstOrDefault(c => c.Id == salesSettings.ForSaleLocationId);
        RefreshPickList();
    }

    partial void OnForSaleLocationChanged(StorageContainer? value)
        => salesSettings.SetForSaleLocationId(value?.Id);

    [RelayCommand]
    public void RefreshPickList()
    {
        PickList.Clear();
        foreach (var e in listingService.GetPickList())
            PickList.Add(e);
    }

    [RelayCommand]
    public void MarkAllPicked()
    {
        var ids = PickList.Select(e => e.LotId).ToList();
        if (ids.Count == 0) return;
        listingService.MarkPicked(ids);
        RefreshPickList();
    }
}
```

- [ ] **Step 2: Create `SalesView`** (DataGrid of the pick list, grouped by location; For-Sale location picker; buttons)

`OmniCard/Views/Sales/SalesView.xaml`:

```xml
<UserControl x:Class="OmniCard.Views.Sales.SalesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="For-Sale location:" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <ComboBox Width="180" DisplayMemberPath="Name"
                      ItemsSource="{Binding Locations}" SelectedItem="{Binding ForSaleLocation}"/>
            <Button Content="Refresh" Margin="12,0,0,0" Command="{Binding RefreshPickListCommand}"/>
            <Button Content="Mark All Picked" Margin="6,0,0,0" Command="{Binding MarkAllPickedCommand}"/>
        </StackPanel>
        <DataGrid ItemsSource="{Binding PickList}" AutoGenerateColumns="False" IsReadOnly="True"
                  Style="{StaticResource {x:Type DataGrid}}">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Location" Binding="{Binding LocationName}"/>
                <DataGridTextColumn Header="Section" Binding="{Binding Section}"/>
                <DataGridTextColumn Header="Pg" Binding="{Binding Page}"/>
                <DataGridTextColumn Header="Slot" Binding="{Binding Slot}"/>
                <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="*"/>
                <DataGridTextColumn Header="Set" Binding="{Binding SetName}"/>
                <DataGridTextColumn Header="Cond" Binding="{Binding Condition}"/>
                <DataGridTextColumn Header="Price" Binding="{Binding ListedPrice, StringFormat=C}"/>
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</UserControl>
```

`OmniCard/Views/Sales/SalesView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace OmniCard.Views.Sales;

public partial class SalesView : UserControl
{
    public SalesView() => InitializeComponent();
}
```

- [ ] **Step 3: Wire into `RootViewModel`** — add `Views.Sales.SalesViewModel sales` to the constructor params and expose:

```csharp
    public Views.Sales.SalesViewModel Sales { get; } = sales;
```

- [ ] **Step 4: Add the tab** (in `RootView.xaml`, inside the `TabControl` after the Dashboard `TabItem` ~line 260)

```xml
            <TabItem Header="Sales">
                <sales:SalesView DataContext="{Binding ViewModel.Sales}"/>
            </TabItem>
```

Add the namespace to the `Window` root element: `xmlns:sales="clr-namespace:OmniCard.Views.Sales"`. Load the pick list when the tab activates — the simplest reliable hook is to call `Sales.Load()` from the existing `SelectedTabIndex` change handler in `RootViewModel` (find where `SelectedTabIndex` is defined; when it equals the Sales tab index, call `Sales.Load()`). If no such handler exists, call `Sales.Load()` in `RootViewModel`'s initialization and add a `Loaded`-triggered refresh.

- [ ] **Step 5: Register in DI** (`App.xaml.cs`, near other view-model registrations)

```csharp
            services.AddSingleton<Views.Sales.SalesViewModel>();
```

> Check how `CollectionViewModel`/`InventoryViewModel`/`DashboardViewModel` are registered and mirror it exactly (singleton vs transient).

- [ ] **Step 6: Build + full test suite**

Run: `dotnet build OmniCard/OmniCard.csproj -v q && dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v q`
Expected: `Build succeeded` + all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Sales/ OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/RootView.xaml OmniCard/App.xaml.cs
git commit -m "feat(sales): Sales tab with location-grouped pick list"
```

---

### Task 11: Print the pick list

**Files:**
- Create: `OmniCard/Views/Sales/PickListPrinter.cs`
- Modify: `OmniCard/Views/Sales/SalesViewModel.cs` (add `PrintPickListCommand`), `SalesView.xaml` (add a Print button)

**Interfaces:**
- Consumes: `PickList` (the loaded entries).
- Produces: `PickListPrinter.Print(IReadOnlyList<PickListEntry> entries)` — builds a `FlowDocument` and shows the native `PrintDialog`.

- [ ] **Step 1: Create the printer** (A4/Letter FlowDocument via native PrintDialog — receipt-width printing comes in Phase 3)

`OmniCard/Views/Sales/PickListPrinter.cs`:

```csharp
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public static class PickListPrinter
{
    public static void Print(IReadOnlyList<PickListEntry> entries)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var doc = new FlowDocument { PagePadding = new Thickness(40), ColumnWidth = double.PositiveInfinity };
        doc.Blocks.Add(new Paragraph(new Run($"Pick List ({entries.Count} cards)")) { FontSize = 16, FontWeight = FontWeights.Bold });

        var table = new Table();
        for (int i = 0; i < 5; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        AddRow(group, "Location", "Slot", "Name", "Set", "Price", bold: true);
        foreach (var e in entries)
            AddRow(group, $"{e.LocationName} {e.Section}", $"{e.Page}/{e.Slot}", e.Name, e.SetName, e.ListedPrice.ToString("C"));
        table.RowGroups.Add(group);
        doc.Blocks.Add(table);

        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Pick List");
    }

    private static void AddRow(TableRowGroup group, string a, string b, string c, string d, string e, bool bold = false)
    {
        var row = new TableRow();
        foreach (var text in new[] { a, b, c, d, e })
        {
            var p = new Paragraph(new Run(text));
            if (bold) p.FontWeight = FontWeights.Bold;
            row.Cells.Add(new TableCell(p));
        }
        group.Rows.Add(row);
    }
}
```

- [ ] **Step 2: Add the command** (in `SalesViewModel`)

```csharp
    [RelayCommand]
    public void PrintPickList() => PickListPrinter.Print(PickList.ToList());
```

- [ ] **Step 3: Add the button** (in `SalesView.xaml`, in the top `StackPanel`)

```xml
            <Button Content="Print Pick List" Margin="6,0,0,0" Command="{Binding PrintPickListCommand}"/>
```

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build OmniCard/OmniCard.csproj -v q && dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v q`
Expected: `Build succeeded` + all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Sales/PickListPrinter.cs OmniCard/Views/Sales/SalesViewModel.cs OmniCard/Views/Sales/SalesView.xaml
git commit -m "feat(sales): print the pick list"
```

---

## Phase 1 exit criteria (human E2E before merge)

- Right-click one or more collection cards → **List for Sale…** → dialog → cards get a "LISTED" badge.
- **Sales** tab → set a For-Sale location → pick list shows the listed cards grouped by original location → **Print Pick List** opens the print dialog.
- Right-click a listed card → **Mark Picked** → badge becomes "PICKED", card's location is now the For-Sale location, pick list no longer shows it.
- Right-click → **Unlist** removes the badge (and, if it was picked, returns it to its original location).
- Restart the app on the existing `inventory.db` — no schema errors (the `Listings` table is created by `EnsureUnifiedSchema`).
- `dotnet test` green.

## Self-Review Notes (coverage vs. spec §Phase 1)

- Listing entity + schema → Task 1. ✅
- For-Sale location setting → Task 2 (service) + Task 10 (UI picker). ✅
- Right-click List/Unlist/Mark-Picked (+ bulk via `GetAllSelectedCardIds`) → Tasks 3–5 (service), 8 (commands/menu). ✅
- Move-on-pick (Move movement) → Task 4. ✅
- "Listed" tile badge → Tasks 6 (data) + 9 (badge). ✅
- Sales tab → Pick List (view + print) → Tasks 10–11. ✅
- Tests via in-memory SQLite harness → Tasks 1–5. ✅
