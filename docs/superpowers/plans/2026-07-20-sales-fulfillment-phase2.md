# Sales & Fulfillment — Phase 2 (Customers, Orders, Sold-on-Ship, Net P&L) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Track customers and orders; build an order from listed/picked cards; on Ship, remove the sold cards from inventory (recording Sell movements + line snapshots) and mark their listings Sold; surface net realized profit (fees + shipping) on the Dashboard.

**Architecture:** New `Customer`, `Order`, `OrderLine` entities in the unified `OmniCardDbContext`. An `IOrderService` owns order lifecycle including the Ship transition (which reuses the eBay sold-pattern: `MovementType.Sell` + lot decrement, plus setting the lot's active `Listing` to `Sold`). `ICustomerService` is CRUD. `AnalyticsService.GetRealized` is extended to net out order-level fees/shipping. The Sales tab becomes a sub-tab host (Pick List / Orders / Customers).

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, EF Core (SQLite via `IDbContextFactory<OmniCardDbContext>`), xUnit + Moq, QuestPDF (already referenced; receipts are Phase 3, not here).

## Global Constraints

- Target framework: .NET 10 WPF (`net10.0-windows`). Match existing code style.
- Unified store only: new tables in `OmniCardDbContext` (`inventory.db`). No new DbContext.
- Schema on existing DBs: every new table MUST also be created idempotently in `UnifiedMigrationService.EnsureUnifiedSchema(SqliteConnection)` via `CREATE TABLE IF NOT EXISTS`, columns/types matching the EF model. Add each new table to the pre-existing-DB guard test in `OmniCard.Tests/Services/UnifiedMigrationTests.cs`.
- Enum columns stored as TEXT via `HasConversion<string>()`; decimal columns stored as TEXT (EF Core SQLite default — matches `Listing.ListedPrice`, `Product.LastMarketPrice`).
- Reuse existing `MovementType.Sell` for sales (no new movement types). The sold-pattern is: add a `Sell` `InventoryMovement` (scalar `UnitValue` = sale price, `Note` = order ref) BEFORE decrementing the lot, then `lot.Quantity -= qty` and remove the lot at `Quantity <= 0` (mirror `EbaySyncService.SeedSellMovementAsync`).
- Order lines carry **snapshots** (name/set/condition/foil) so receipts and P&L survive later product/lot edits or deletion.
- Design decisions (confirmed): order lines can be **any Listed or Picked card**; status flow is **Open → Packed → Shipped → Completed** (+ **Cancelled**); inventory is removed only at **Shipped**; net profit is surfaced by **extending the Dashboard** realized P&L.
- Tests: xUnit + in-memory SQLite, following `OmniCard.Tests/Services/ListingServiceTests.cs` (open `:memory:`, `EnsureCreated()`, seed via `Product`+`InventoryLot`+`StorageContainer`; note the FK `InventoryLot.LocationId → StorageContainers.Id` — seed a container for any location id used).
- Do NOT push or merge. Work on branch `feat/sales-fulfillment-phase2` (off master, which now contains Phase 1). Keep build + all tests green after every task.

---

## File Structure

- `OmniCard.Shared/Models/OrderStatus.cs` — new enum.
- `OmniCard.Shared/Models/Customer.cs`, `Order.cs`, `OrderLine.cs` — new entities.
- `OmniCard.Shared/Models/ActiveListing.cs` — read model for the order editor's card picker.
- `OmniCard.Shared/Interfaces/ICustomerService.cs`, `IOrderService.cs` — new interfaces.
- `OmniCard.Collection/CustomerService.cs`, `OrderService.cs` — new services.
- `OmniCard.Collection/ListingService.cs` — add `GetActiveListings` (Listed+Picked, for the order card picker) + `MarkSold`.
- `OmniCard.Collection/AnalyticsService.cs` — extend `GetRealized` with order fees/shipping.
- `OmniCard.Shared/Models/RealizedSummary.cs` — add fee/shipping fields.
- `OmniCard.Data/OmniCardDbContext.cs` — add `DbSet`s + `OnModelCreating` config.
- `OmniCard.Data/UnifiedMigrationService.cs` — add `Customers`/`Orders`/`OrderLines` tables.
- `OmniCard/Views/Dashboard/DashboardViewModel.cs` + `DashboardView.xaml` — net realized display.
- `OmniCard/Views/Sales/SalesView.xaml` — restructure into a TabControl (Pick List / Orders / Customers).
- `OmniCard/Views/Sales/PickListView.xaml(.cs)` — extract the existing pick list into its own UserControl.
- `OmniCard/Views/Sales/CustomersView.xaml(.cs)` + `CustomersViewModel.cs` — customers CRUD.
- `OmniCard/Views/Sales/OrdersView.xaml(.cs)` + `OrdersViewModel.cs` — orders list + editor.
- `OmniCard/Views/Sales/SalesViewModel.cs` — expose child `Orders`/`Customers` VMs; keep pick-list state.
- `OmniCard/App.xaml.cs` — DI registrations.
- Tests: `OmniCard.Tests/Services/CustomerServiceTests.cs`, `OrderServiceTests.cs`; extend `ListingServiceTests`, `AnalyticsServiceTests` (or the existing analytics test file), `UnifiedMigrationTests`.

---

### Task 1: Enums + `Customer`/`Order`/`OrderLine` entities + schema

**Files:**
- Create: `OmniCard.Shared/Models/OrderStatus.cs`, `Customer.cs`, `Order.cs`, `OrderLine.cs`
- Modify: `OmniCard.Data/OmniCardDbContext.cs`, `OmniCard.Data/UnifiedMigrationService.cs`
- Test: `OmniCard.Tests/Services/OrderServiceTests.cs`, `OmniCard.Tests/Services/UnifiedMigrationTests.cs`

**Interfaces / Produces:**
- `enum OrderStatus { Open, Packed, Shipped, Completed, Cancelled }`
- `Customer { int Id; string Name; string? Email, Phone, TcgPlayerUsername, AddressLine1, AddressLine2, City, State, PostalCode, Country, Notes; DateTime CreatedAt }`
- `Order { int Id; int CustomerId; SalesChannel Channel; string? OrderNumber; DateTime OrderDate; OrderStatus Status; string? TrackingNumber, Carrier; decimal ShippingChargedToBuyer, ShippingCost, MarketplaceFees; string? Notes; DateTime CreatedAt; DateTime? ShippedAt }`
- `OrderLine { int Id; int OrderId; int? LotId; int? ProductId; string NameSnapshot; string? SetSnapshot, ConditionSnapshot; bool IsFoilSnapshot; int Quantity; decimal UnitSalePrice }`
- `OmniCardDbContext.Customers/Orders/OrderLines` DbSets.

- [ ] **Step 1: Write the failing test** — `OrderServiceTests.cs` round-trips the entities through the real model.

```csharp
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class OrderServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    protected readonly DbContextOptions<OmniCardDbContext> _opts;

    public OrderServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void OrderGraph_RoundTrips()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var cust = new Customer { Name = "Ada" };
            ctx.Customers.Add(cust);
            ctx.SaveChanges();
            var order = new Order
            {
                CustomerId = cust.Id,
                Channel = SalesChannel.TcgPlayer,
                OrderNumber = "TCG-1",
                Status = OrderStatus.Open,
                MarketplaceFees = 1.10m,
                ShippingCost = 0.63m,
                ShippingChargedToBuyer = 1.25m,
            };
            ctx.Orders.Add(order);
            ctx.SaveChanges();
            ctx.OrderLines.Add(new OrderLine
            {
                OrderId = order.Id,
                NameSnapshot = "Sol Ring",
                SetSnapshot = "Commander",
                ConditionSnapshot = "NM",
                Quantity = 1,
                UnitSalePrice = 2.50m,
            });
            ctx.SaveChanges();
        }

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var order = Assert.Single(ctx.Orders.ToList());
            Assert.Equal(OrderStatus.Open, order.Status);
            Assert.Equal(1.10m, order.MarketplaceFees);
            var line = Assert.Single(ctx.OrderLines.ToList());
            Assert.Equal("Sol Ring", line.NameSnapshot);
            Assert.Equal(2.50m, line.UnitSalePrice);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests" -v q`
Expected: FAIL — `Customer`/`Order`/`OrderLine` do not exist.

- [ ] **Step 3: Create the enum + entities**

`OmniCard.Shared/Models/OrderStatus.cs`:

```csharp
namespace OmniCard.Models;

public enum OrderStatus { Open, Packed, Shipped, Completed, Cancelled }
```

`OmniCard.Shared/Models/Customer.cs`:

```csharp
namespace OmniCard.Models;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? TcgPlayerUsername { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

`OmniCard.Shared/Models/Order.cs`:

```csharp
namespace OmniCard.Models;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public SalesChannel Channel { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public OrderStatus Status { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public decimal ShippingChargedToBuyer { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal MarketplaceFees { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ShippedAt { get; set; }
}
```

`OmniCard.Shared/Models/OrderLine.cs`:

```csharp
namespace OmniCard.Models;

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    /// <summary>The lot this line sold (nullable — the lot may be removed once shipped).</summary>
    public int? LotId { get; set; }
    public int? ProductId { get; set; }
    public string NameSnapshot { get; set; } = "";
    public string? SetSnapshot { get; set; }
    public string? ConditionSnapshot { get; set; }
    public bool IsFoilSnapshot { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitSalePrice { get; set; }
}
```

- [ ] **Step 4: Register in `OmniCardDbContext`** — add DbSets after `Listings`:

```csharp
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
```

In `OnModelCreating`, after the `Listing` block:

```csharp
        modelBuilder.Entity<Customer>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedOnAdd();
            e.HasIndex(c => c.Name);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).ValueGeneratedOnAdd();
            e.Property(o => o.Channel).HasConversion<string>();
            e.Property(o => o.Status).HasConversion<string>();
            e.HasIndex(o => o.CustomerId);
            e.HasIndex(o => o.Status);
        });

        modelBuilder.Entity<OrderLine>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.HasIndex(l => l.OrderId);
        });
```

- [ ] **Step 5: Add idempotent `CREATE TABLE`s** — in `EnsureUnifiedSchema(SqliteConnection conn)`, after the `Listings` block:

```csharp
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Customers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL DEFAULT '',
                Email TEXT, Phone TEXT, TcgPlayerUsername TEXT,
                AddressLine1 TEXT, AddressLine2 TEXT, City TEXT, State TEXT,
                PostalCode TEXT, Country TEXT, Notes TEXT,
                CreatedAt TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Customers_Name ON Customers(Name)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Channel TEXT NOT NULL DEFAULT 'Manual',
                OrderNumber TEXT,
                OrderDate TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Open',
                TrackingNumber TEXT, Carrier TEXT,
                ShippingChargedToBuyer TEXT NOT NULL DEFAULT '0',
                ShippingCost TEXT NOT NULL DEFAULT '0',
                MarketplaceFees TEXT NOT NULL DEFAULT '0',
                Notes TEXT,
                CreatedAt TEXT NOT NULL,
                ShippedAt TEXT
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Orders_CustomerId ON Orders(CustomerId)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Orders_Status ON Orders(Status)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS OrderLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderId INTEGER NOT NULL,
                LotId INTEGER,
                ProductId INTEGER,
                NameSnapshot TEXT NOT NULL DEFAULT '',
                SetSnapshot TEXT, ConditionSnapshot TEXT,
                IsFoilSnapshot INTEGER NOT NULL DEFAULT 0,
                Quantity INTEGER NOT NULL DEFAULT 1,
                UnitSalePrice TEXT NOT NULL DEFAULT '0'
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_OrderLines_OrderId ON OrderLines(OrderId)";
        cmd.ExecuteNonQuery();
```

- [ ] **Step 6: Extend the pre-existing-DB migration guard** — in `OmniCard.Tests/Services/UnifiedMigrationTests.cs`, add `"Customers"`, `"Orders"`, `"OrderLines"` to the table-existence loop, and add a column-presence check for `Orders` (`Status`, `MarketplaceFees`, `ShippedAt`) mirroring the existing `Listings`/`Products` column checks.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests|FullyQualifiedName~UnifiedMigrationTests" -v q`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/OrderStatus.cs OmniCard.Shared/Models/Customer.cs OmniCard.Shared/Models/Order.cs OmniCard.Shared/Models/OrderLine.cs OmniCard.Data/OmniCardDbContext.cs OmniCard.Data/UnifiedMigrationService.cs OmniCard.Tests/Services/OrderServiceTests.cs OmniCard.Tests/Services/UnifiedMigrationTests.cs
git commit -m "feat(sales): Customer/Order/OrderLine entities + schema (phase 2)"
```

---

### Task 2: `CustomerService` (CRUD)

**Files:**
- Create: `OmniCard.Shared/Interfaces/ICustomerService.cs`, `OmniCard.Collection/CustomerService.cs`
- Test: `OmniCard.Tests/Services/CustomerServiceTests.cs`

**Interfaces / Produces:**
- `ICustomerService { List<Customer> GetAll(); Customer? Get(int id); Customer Create(Customer c); void Update(Customer c); void Delete(int id); }`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class CustomerServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;

    public CustomerServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private CustomerService Svc() => new(new Factory(_opts));
    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    [Fact]
    public void Create_Update_Delete_RoundTrip()
    {
        var svc = Svc();
        var created = svc.Create(new Customer { Name = "Ada", Email = "ada@x.com" });
        Assert.True(created.Id > 0);

        created.Email = "ada@y.com";
        svc.Update(created);
        Assert.Equal("ada@y.com", svc.Get(created.Id)!.Email);

        Assert.Single(svc.GetAll());
        svc.Delete(created.Id);
        Assert.Empty(svc.GetAll());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CustomerServiceTests" -v q`
Expected: FAIL — `CustomerService` does not exist.

- [ ] **Step 3: Create the interface**

`OmniCard.Shared/Interfaces/ICustomerService.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICustomerService
{
    List<Customer> GetAll();
    Customer? Get(int id);
    Customer Create(Customer customer);
    void Update(Customer customer);
    void Delete(int id);
}
```

- [ ] **Step 4: Implement the service**

`OmniCard.Collection/CustomerService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class CustomerService(IDbContextFactory<OmniCardDbContext> dbContextFactory) : ICustomerService
{
    public List<Customer> GetAll()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Customers.AsNoTracking().OrderBy(c => c.Name).ToList();
    }

    public Customer? Get(int id)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Customers.AsNoTracking().FirstOrDefault(c => c.Id == id);
    }

    public Customer Create(Customer customer)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Customers.Add(customer);
        ctx.SaveChanges();
        return customer;
    }

    public void Update(Customer customer)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Customers.Update(customer);
        ctx.SaveChanges();
    }

    public void Delete(int id)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var existing = ctx.Customers.FirstOrDefault(c => c.Id == id);
        if (existing is null) return;
        ctx.Customers.Remove(existing);
        ctx.SaveChanges();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~CustomerServiceTests" -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/ICustomerService.cs OmniCard.Collection/CustomerService.cs OmniCard.Tests/Services/CustomerServiceTests.cs
git commit -m "feat(sales): CustomerService CRUD"
```

---

### Task 3: `ListingService.GetActiveListings` + `MarkSold` (order card picker + ship support)

**Files:**
- Modify: `OmniCard.Collection/ListingService.cs`, `OmniCard.Shared/Interfaces/IListingService.cs`
- Create: `OmniCard.Shared/Models/ActiveListing.cs`
- Test: extend `OmniCard.Tests/Services/ListingServiceTests.cs`

**Interfaces / Produces:**
- `record ActiveListing(int LotId, string Name, string SetName, string SetCode, string? Condition, bool IsFoil, decimal ListedPrice, ListingStatus Status)`
- `IListingService.List<ActiveListing> GetActiveListings(CardGame? game = null)` — all `Listed` OR `Picked` listings joined to Lot+Product, for the order editor's card picker.
- `IListingService.void MarkSold(int lotId, int orderLineId)` — sets the lot's active listing to `Sold` and records `OrderLineId`. No-op if the lot has no active listing (supports selling an unlisted card).

- [ ] **Step 1: Write the failing test** (add to `ListingServiceTests`; reuse existing `SeedLot`, `CreateService`, `StubSalesSettings`)

```csharp
    [Fact]
    public void GetActiveListings_ReturnsListedAndPicked()
    {
        var a = SeedLot(_opts).lotId;
        var b = SeedLot(_opts, locationId: 8).lotId;
        var svc = CreateService();
        svc.ListForSale([a, b], SalesChannel.Manual, 2m, 1);
        svc.MarkPicked([b]); // requires StorageContainer 99 for ForSale — seed it
        // NOTE: seed StorageContainer Id 99 before MarkPicked (StubSalesSettings.ForSaleLocationId=99),
        // mirroring MarkPicked tests.

        var active = svc.GetActiveListings();
        Assert.Equal(2, active.Count);
        Assert.Contains(active, x => x.LotId == a && x.Status == ListingStatus.Listed);
        Assert.Contains(active, x => x.LotId == b && x.Status == ListingStatus.Picked);
    }

    [Fact]
    public void MarkSold_SetsListingSoldWithOrderLine()
    {
        var lotId = SeedLot(_opts).lotId;
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 2m, 1);

        svc.MarkSold(lotId, orderLineId: 77);

        using var ctx = new OmniCardDbContext(_opts);
        var listing = ctx.Listings.Single(l => l.LotId == lotId);
        Assert.Equal(ListingStatus.Sold, listing.Status);
        Assert.Equal(77, listing.OrderLineId);
    }
```

> For the `GetActiveListings` test, seed a `StorageContainer` with `Id = 99` before `MarkPicked` (the FK + StubSalesSettings.ForSaleLocationId=99), exactly as the existing `MarkPicked_...` test does.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: FAIL — `GetActiveListings`/`MarkSold` do not exist.

- [ ] **Step 3: Create the `ActiveListing` record**

`OmniCard.Shared/Models/ActiveListing.cs`:

```csharp
namespace OmniCard.Models;

public record ActiveListing(
    int LotId,
    string Name,
    string SetName,
    string SetCode,
    string? Condition,
    bool IsFoil,
    decimal ListedPrice,
    ListingStatus Status);
```

- [ ] **Step 4: Add to the interface**

In `IListingService`:

```csharp
    List<ActiveListing> GetActiveListings(CardGame? game = null);
    void MarkSold(int lotId, int orderLineId);
```

- [ ] **Step 5: Implement both** (in `ListingService`, after `GetActiveListingStatusByLot`)

```csharp
    private static readonly ListingStatus[] SellableStatuses = [ListingStatus.Listed, ListingStatus.Picked];

    public List<ActiveListing> GetActiveListings(CardGame? game = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var query =
            from listing in ctx.Listings.AsNoTracking()
            where SellableStatuses.Contains(listing.Status)
            join lot in ctx.Lots.AsNoTracking() on listing.LotId equals lot.Id
            join p in ctx.Products.AsNoTracking() on lot.ProductId equals p.Id
            where game == null || p.Game == game
            orderby p.Name
            select new ActiveListing(
                lot.Id,
                p.Name,
                p.SetName ?? "",
                p.SetCode ?? "",
                lot.Condition,
                p.Foil,
                listing.ListedPrice,
                listing.Status);
        return query.ToList();
    }

    public void MarkSold(int lotId, int orderLineId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var listing = ctx.Listings
            .Where(l => l.LotId == lotId && SellableStatuses.Contains(l.Status))
            .OrderByDescending(l => l.Status)
            .FirstOrDefault();
        if (listing is null) return;
        listing.Status = ListingStatus.Sold;
        listing.OrderLineId = orderLineId;
        ctx.SaveChanges();
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ListingServiceTests" -v q`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Models/ActiveListing.cs OmniCard.Shared/Interfaces/IListingService.cs OmniCard.Collection/ListingService.cs OmniCard.Tests/Services/ListingServiceTests.cs
git commit -m "feat(sales): active-listings picker + MarkSold in ListingService"
```

---

### Task 4: `OrderService` — create/update, add/remove lines (with snapshots), queries

**Files:**
- Create: `OmniCard.Shared/Interfaces/IOrderService.cs`, `OmniCard.Collection/OrderService.cs`
- Test: extend `OmniCard.Tests/Services/OrderServiceTests.cs`

**Interfaces / Produces:**
- `IOrderService`:
  - `List<Order> GetOrders();`
  - `Order? GetOrder(int id);`
  - `List<OrderLine> GetLines(int orderId);`
  - `Order CreateOrder(int customerId, SalesChannel channel, string? orderNumber);`
  - `void UpdateOrder(Order order);` (header fields: number/tracking/carrier/fees/shipping/notes/date)
  - `OrderLine AddLine(int orderId, int lotId, decimal unitSalePrice);` — snapshots Name/Set/Condition/Foil from the lot's Product/Lot at add time; sets `ProductId`/`LotId`.
  - `void RemoveLine(int orderLineId);`
- Consumes: `IDbContextFactory<OmniCardDbContext>` (ship logic + MarkSold added in Task 5).

- [ ] **Step 1: Write the failing test** (extend `OrderServiceTests`; add seed helpers for a Product+Lot)

```csharp
    private OrderService OrderSvc() => new(new Factory(_opts), new ListingService(new Factory(_opts), new StubSettings()));

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }
    private sealed class StubSettings : OmniCard.Interfaces.ISalesSettingsService
    { public int? ForSaleLocationId => 99; public void SetForSaleLocationId(int? id) { } }

    private (int customerId, int lotId) SeedCustomerAndLot()
    {
        using var ctx = new OmniCardDbContext(_opts);
        var c = new Customer { Name = "Ada" };
        ctx.Customers.Add(c);
        var p = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring", SetName = "Commander", Foil = false };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = p.Id, Quantity = 1, Condition = "NM", UnitCost = 1.00m };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return (c.Id, lot.Id);
    }

    [Fact]
    public void CreateOrder_AddLine_SnapshotsCardAndRemoveLine()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "TCG-42");
        var line = svc.AddLine(order.Id, lotId, 3.50m);

        Assert.Equal("Sol Ring", line.NameSnapshot);
        Assert.Equal("Commander", line.SetSnapshot);
        Assert.Equal("NM", line.ConditionSnapshot);
        Assert.Equal(3.50m, line.UnitSalePrice);
        Assert.Equal(lotId, line.LotId);
        Assert.Single(svc.GetLines(order.Id));

        svc.RemoveLine(line.Id);
        Assert.Empty(svc.GetLines(order.Id));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests" -v q`
Expected: FAIL — `OrderService` does not exist.

- [ ] **Step 3: Create the interface** (`IOrderService.cs`)

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IOrderService
{
    List<Order> GetOrders();
    Order? GetOrder(int id);
    List<OrderLine> GetLines(int orderId);
    Order CreateOrder(int customerId, SalesChannel channel, string? orderNumber);
    void UpdateOrder(Order order);
    OrderLine AddLine(int orderId, int lotId, decimal unitSalePrice);
    void RemoveLine(int orderLineId);
    void SetStatus(int orderId, OrderStatus status); // implemented in Task 5
}
```

- [ ] **Step 4: Implement create/update/lines/queries (stub `SetStatus`)** (`OrderService.cs`)

```csharp
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class OrderService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    IListingService listingService) : IOrderService
{
    public List<Order> GetOrders()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Orders.AsNoTracking().OrderByDescending(o => o.OrderDate).ToList();
    }

    public Order? GetOrder(int id)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Orders.AsNoTracking().FirstOrDefault(o => o.Id == id);
    }

    public List<OrderLine> GetLines(int orderId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.OrderLines.AsNoTracking().Where(l => l.OrderId == orderId).ToList();
    }

    public Order CreateOrder(int customerId, SalesChannel channel, string? orderNumber)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = new Order
        {
            CustomerId = customerId,
            Channel = channel,
            OrderNumber = orderNumber,
            Status = OrderStatus.Open,
            OrderDate = DateTime.UtcNow,
        };
        ctx.Orders.Add(order);
        ctx.SaveChanges();
        return order;
    }

    public void UpdateOrder(Order order)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Orders.Update(order);
        ctx.SaveChanges();
    }

    public OrderLine AddLine(int orderId, int lotId, decimal unitSalePrice)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var lot = ctx.Lots.AsNoTracking().FirstOrDefault(l => l.Id == lotId)
                  ?? throw new InvalidOperationException($"Lot {lotId} not found.");
        var product = ctx.Products.AsNoTracking().FirstOrDefault(p => p.Id == lot.ProductId);

        var line = new OrderLine
        {
            OrderId = orderId,
            LotId = lotId,
            ProductId = lot.ProductId,
            NameSnapshot = product?.Name ?? "",
            SetSnapshot = product?.SetName,
            ConditionSnapshot = lot.Condition,
            IsFoilSnapshot = product?.Foil ?? false,
            Quantity = 1,
            UnitSalePrice = unitSalePrice,
        };
        ctx.OrderLines.Add(line);
        ctx.SaveChanges();
        return line;
    }

    public void RemoveLine(int orderLineId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var line = ctx.OrderLines.FirstOrDefault(l => l.Id == orderLineId);
        if (line is null) return;
        ctx.OrderLines.Remove(line);
        ctx.SaveChanges();
    }

    public void SetStatus(int orderId, OrderStatus status) => throw new NotImplementedException();
}
```

> `SetStatus` is stubbed here (plan-mandated, transitional) and implemented with the Ship logic in Task 5.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests" -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/IOrderService.cs OmniCard.Collection/OrderService.cs OmniCard.Tests/Services/OrderServiceTests.cs
git commit -m "feat(sales): OrderService create/update/lines with snapshots"
```

---

### Task 5: `OrderService.SetStatus` — Ship removes inventory + records Sell movements + marks listings Sold

**Files:**
- Modify: `OmniCard.Collection/OrderService.cs`
- Test: extend `OmniCard.Tests/Services/OrderServiceTests.cs`

**Interfaces / Produces:**
- `void SetStatus(int orderId, OrderStatus status)`:
  - Transitioning **to `Shipped`** (from a non-shipped, non-cancelled state): set `ShippedAt = now`; for each line with a `LotId`, add a `MovementType.Sell` movement (`Quantity` = line qty, `UnitValue` = line `UnitSalePrice`, `Note` = order number/id) BEFORE decrementing; `lot.Quantity -= line.Quantity`, remove the lot at `<= 0`; call `listingService.MarkSold(lotId, line.Id)`. Idempotent: shipping an already-shipped order is a no-op.
  - Any other status: just set `Status` (and clear `ShippedAt` only when moving back out of Shipped is NOT supported — keep it simple: only Open/Packed/Cancelled before ship; Completed after).

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void SetStatus_Shipped_RemovesInventory_RecordsSell_MarksListingSold()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var listing = new ListingService(new Factory(_opts), new StubSettings());
        listing.ListForSale([lotId], SalesChannel.TcgPlayer, 3.50m, 1);

        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.TcgPlayer, "TCG-42");
        var line = svc.AddLine(order.Id, lotId, 3.50m);

        svc.SetStatus(order.Id, OrderStatus.Shipped);

        using var ctx = new OmniCardDbContext(_opts);
        // Lot removed (qty 1 -> 0)
        Assert.Null(ctx.Lots.FirstOrDefault(l => l.Id == lotId));
        // Sell movement recorded with proceeds
        var sell = Assert.Single(ctx.Movements.Where(m => m.Type == MovementType.Sell && m.LotId == lotId).ToList());
        Assert.Equal(3.50m, sell.UnitValue);
        // Listing marked Sold
        Assert.Equal(ListingStatus.Sold, ctx.Listings.Single(l => l.LotId == lotId).Status);
        // Order stamped
        var shipped = ctx.Orders.Single(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Shipped, shipped.Status);
        Assert.NotNull(shipped.ShippedAt);
    }

    [Fact]
    public void SetStatus_Shipped_IsIdempotent()
    {
        var (customerId, lotId) = SeedCustomerAndLot();
        var svc = OrderSvc();
        var order = svc.CreateOrder(customerId, SalesChannel.Manual, null);
        svc.AddLine(order.Id, lotId, 2m);
        svc.SetStatus(order.Id, OrderStatus.Shipped);
        svc.SetStatus(order.Id, OrderStatus.Shipped); // second call must not double-decrement

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Movements.Where(m => m.Type == MovementType.Sell && m.LotId == lotId).ToList());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests" -v q`
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Implement `SetStatus`** (replace the stub)

```csharp
    public void SetStatus(int orderId, OrderStatus status)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var order = ctx.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return;

        var shipping = status == OrderStatus.Shipped && order.Status != OrderStatus.Shipped
                       && order.Status != OrderStatus.Completed && order.Status != OrderStatus.Cancelled;

        order.Status = status;

        if (shipping)
        {
            order.ShippedAt = DateTime.UtcNow;
            var lines = ctx.OrderLines.Where(l => l.OrderId == orderId).ToList();
            foreach (var line in lines)
            {
                if (line.LotId is not int lotId) continue;
                var lot = ctx.Lots.FirstOrDefault(l => l.Id == lotId);
                if (lot is null) continue;

                // Record the sale first (scalar values survive the lot removal below).
                ctx.Movements.Add(new InventoryMovement
                {
                    ProductId = lot.ProductId,
                    LotId = lot.Id,
                    Type = MovementType.Sell,
                    Quantity = line.Quantity,
                    UnitValue = line.UnitSalePrice,
                    Timestamp = DateTime.UtcNow,
                    Note = order.OrderNumber ?? $"Order {order.Id}",
                });

                lot.Quantity -= line.Quantity;
                if (lot.Quantity <= 0)
                    ctx.Lots.Remove(lot);
            }
            ctx.SaveChanges();

            // Mark each sold lot's active listing Sold (separate context inside the service).
            foreach (var line in lines.Where(l => l.LotId is not null))
                listingService.MarkSold(line.LotId!.Value, line.Id);
        }
        else
        {
            ctx.SaveChanges();
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests" -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/OrderService.cs OmniCard.Tests/Services/OrderServiceTests.cs
git commit -m "feat(sales): ship order -> Sell movements + lot decrement + listings Sold"
```

---

### Task 6: Net realized P&L (fees + shipping) in `AnalyticsService`

**Files:**
- Modify: `OmniCard.Shared/Models/RealizedSummary.cs`, `OmniCard.Collection/AnalyticsService.cs`
- Test: `OmniCard.Tests/Services/` — add to the existing analytics test class if present (search for `GetRealized` tests), else a new `AnalyticsRealizedNetTests.cs`

**Interfaces / Produces:**
- `RealizedSummary` gains `decimal TotalFees, decimal TotalShippingCost, decimal TotalShippingCharged` (append to the record). `Net = TotalProceeds - TotalCost - TotalFees - TotalShippingCost + TotalShippingCharged` is computed by consumers (Dashboard).
- `GetRealized(since)` additionally sums fees/shipping from `Orders` with `Status` in {`Shipped`,`Completed`} and `ShippedAt` within the period.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class AnalyticsRealizedNetTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    public AnalyticsRealizedNetTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }
    public void Dispose() => _conn.Dispose();
    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    [Fact]
    public void GetRealized_IncludesOrderFeesAndShipping()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            // One sold lot via a Sell movement (proceeds 10, cost 4)
            var p = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "X" };
            ctx.Products.Add(p); ctx.SaveChanges();
            ctx.Movements.Add(new InventoryMovement { ProductId = p.Id, LotId = 500, Type = MovementType.Acquire, Quantity = 1, UnitValue = 4m });
            ctx.Movements.Add(new InventoryMovement { ProductId = p.Id, LotId = 500, Type = MovementType.Sell, Quantity = 1, UnitValue = 10m });
            // A shipped order carrying fees + shipping
            var c = new Customer { Name = "A" }; ctx.Customers.Add(c); ctx.SaveChanges();
            ctx.Orders.Add(new Order { CustomerId = c.Id, Status = OrderStatus.Shipped, ShippedAt = DateTime.UtcNow,
                MarketplaceFees = 1.5m, ShippingCost = 0.8m, ShippingChargedToBuyer = 1.0m });
            ctx.SaveChanges();
        }

        var svc = new AnalyticsService(new Factory(_opts), NullLogger<AnalyticsService>.Instance);
        var r = svc.GetRealized();

        Assert.Equal(10m, r.TotalProceeds);
        Assert.Equal(4m, r.TotalCost);
        Assert.Equal(1.5m, r.TotalFees);
        Assert.Equal(0.8m, r.TotalShippingCost);
        Assert.Equal(1.0m, r.TotalShippingCharged);
    }
}
```

> Verify `AnalyticsService`'s constructor signature (`IDbContextFactory<OmniCardDbContext>` + `ILogger<AnalyticsService>`) against the current file and adjust the `new AnalyticsService(...)` call if it differs.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~AnalyticsRealizedNetTests" -v q`
Expected: FAIL — `RealizedSummary` has no `TotalFees`.

- [ ] **Step 3: Extend `RealizedSummary`**

```csharp
namespace OmniCard.Models;

public record RealizedSummary(
    int TotalSold,
    decimal TotalProceeds,
    decimal TotalCost,
    IReadOnlyList<RealizedLine> ByGame,
    decimal TotalFees = 0m,
    decimal TotalShippingCost = 0m,
    decimal TotalShippingCharged = 0m);
```

- [ ] **Step 4: Aggregate order fees/shipping in `GetRealized`** — before the `return new RealizedSummary(...)`, add:

```csharp
        var orders = ctx.Orders.AsNoTracking()
            .Where(o => (o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Completed)
                        && o.ShippedAt != null
                        && (!since.HasValue || o.ShippedAt >= since.Value))
            .ToList();
        var totalFees = orders.Sum(o => o.MarketplaceFees);
        var totalShipCost = orders.Sum(o => o.ShippingCost);
        var totalShipCharged = orders.Sum(o => o.ShippingChargedToBuyer);
```

And update the return:

```csharp
        return new RealizedSummary(totalSold, totalProceeds, totalCost, byGame,
            totalFees, totalShipCost, totalShipCharged);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~AnalyticsRealizedNetTests" -v q`
Expected: PASS. Then run the existing analytics tests to confirm no regression: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~Analytics" -v q`.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/RealizedSummary.cs OmniCard.Collection/AnalyticsService.cs OmniCard.Tests/Services/AnalyticsRealizedNetTests.cs
git commit -m "feat(sales): net realized P&L (order fees + shipping)"
```

---

### Task 7: Dashboard — show net realized profit

**Files:**
- Modify: `OmniCard/Views/Dashboard/DashboardViewModel.cs`, `OmniCard/Views/Dashboard/DashboardView.xaml`

**Interfaces / Produces:**
- `DashboardViewModel.RealizedFees/RealizedShippingCost/RealizedShippingCharged/RealizedNet` computed properties; `RealizedNet = RealizedProceeds - RealizedCost - RealizedFees - RealizedShippingCost + RealizedShippingCharged`.

- [ ] **Step 1: Add derived properties** (next to `RealizedProfit` ~line 72)

```csharp
    public decimal RealizedFees => Realized?.TotalFees ?? 0m;
    public decimal RealizedShippingCost => Realized?.TotalShippingCost ?? 0m;
    public decimal RealizedShippingCharged => Realized?.TotalShippingCharged ?? 0m;
    public decimal RealizedNet => RealizedProfit - RealizedFees - RealizedShippingCost + RealizedShippingCharged;
```

- [ ] **Step 2: Raise change notifications** — in `OnRealizedChanged` (~line 126), add:

```csharp
        OnPropertyChanged(nameof(RealizedFees));
        OnPropertyChanged(nameof(RealizedShippingCost));
        OnPropertyChanged(nameof(RealizedShippingCharged));
        OnPropertyChanged(nameof(RealizedNet));
```

- [ ] **Step 3: Add a "Realized (net)" tile to `DashboardView.xaml`** — next to the existing realized profit tile, bind a `TextBlock` to `RealizedNet` with `StringFormat=C`, following the exact tile markup/style already used for `RealizedProfit` (copy that tile's `Border`/`StackPanel`/`TextBlock` structure and swap the binding + label to "Realized (net)"). Match the theming pattern used by the other tiles (DynamicResource brushes).

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build OmniCard/OmniCard.csproj -v q && dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v q`
Expected: `Build succeeded` + all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Dashboard/DashboardViewModel.cs OmniCard/Views/Dashboard/DashboardView.xaml
git commit -m "feat(sales): Dashboard shows net realized profit"
```

---

### Task 8: Customers view (CRUD UI)

**Files:**
- Create: `OmniCard/Views/Sales/CustomersView.xaml(.cs)`, `OmniCard/Views/Sales/CustomersViewModel.cs`
- Modify: `OmniCard/App.xaml.cs` (DI, if not registered in Task 8)

**Interfaces / Produces:**
- `CustomersViewModel`: `ObservableCollection<Customer> Customers`, `Customer? SelectedCustomer`, editable fields, `Load()`, `NewCustomerCommand`, `SaveCommand`, `DeleteCommand`. Consumes `ICustomerService`.

- [ ] **Step 1: Implement `CustomersViewModel`**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public partial class CustomersViewModel(ICustomerService customerService) : ObservableObject
{
    public ObservableCollection<Customer> Customers { get; } = [];

    [ObservableProperty]
    public partial Customer? SelectedCustomer { get; set; }

    public void Load()
    {
        Customers.Clear();
        foreach (var c in customerService.GetAll())
            Customers.Add(c);
    }

    [RelayCommand]
    public void NewCustomer() => SelectedCustomer = new Customer { Name = "New Customer" };

    [RelayCommand]
    public void Save()
    {
        if (SelectedCustomer is null || string.IsNullOrWhiteSpace(SelectedCustomer.Name)) return;
        if (SelectedCustomer.Id == 0) customerService.Create(SelectedCustomer);
        else customerService.Update(SelectedCustomer);
        Load();
    }

    [RelayCommand]
    public void Delete()
    {
        if (SelectedCustomer is { Id: > 0 } c)
        {
            customerService.Delete(c.Id);
            SelectedCustomer = null;
            Load();
        }
    }
}
```

- [ ] **Step 2: Implement `CustomersView.xaml`** — a two-pane UserControl: a `ListBox`/`DataGrid` of `Customers` (DisplayMember Name) bound to `SelectedCustomer` on the left; an editable form (TextBoxes for Name/Email/Phone/TcgPlayerUsername/Address.../Notes bound to `SelectedCustomer.*`) on the right, with New/Save/Delete buttons. Use the MaterialDesign-themed controls consistent with existing views; the `DataGrid` (if used) must use the implicit MD style or `Style="{StaticResource {x:Type DataGrid}}"`. Add `CustomersView.xaml.cs` with `InitializeComponent()`.

- [ ] **Step 3: Load on activation** — in `SalesView.xaml`'s Customers `TabItem`, trigger `Customers.Load()` when selected. Simplest: call `Load()` from the `CustomersView`'s `Loaded` event handler in code-behind (`(s,e) => (DataContext as CustomersViewModel)?.Load();`), OR expose an `IsSelected`-driven load. Prefer the `Loaded` handler for reliability.

- [ ] **Step 4: DI** — ensure `CustomersViewModel` + `ICustomerService` are registered in `App.xaml.cs`.

- [ ] **Step 5: Build + full test suite**

Run: `dotnet build OmniCard/OmniCard.csproj -v q && dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v q`
Expected: `Build succeeded` + all PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Sales/CustomersView.xaml OmniCard/Views/Sales/CustomersView.xaml.cs OmniCard/Views/Sales/CustomersViewModel.cs OmniCard/App.xaml.cs
git commit -m "feat(sales): Customers CRUD view"
```

---

### Task 9: Orders view (list + editor: customer, add cards, fees/shipping, status/ship)

**Files:**
- Create: `OmniCard/Views/Sales/OrdersView.xaml(.cs)`, `OmniCard/Views/Sales/OrdersViewModel.cs`
- Modify: `OmniCard/App.xaml.cs` (DI)

**Interfaces / Produces:**
- `OrdersViewModel`: `ObservableCollection<Order> Orders`, `Order? SelectedOrder`, `ObservableCollection<OrderLine> Lines`, `ObservableCollection<Customer> Customers`, `ObservableCollection<ActiveListing> AvailableCards`, commands: `NewOrder`, `SaveOrder`, `AddCard` (from a selected `ActiveListing`), `RemoveLine`, `SetStatus(OrderStatus)` (Pack/Ship/Complete/Cancel), `Load()`. Consumes `IOrderService`, `ICustomerService`, `IListingService`. `OrderTotal` computed from `Lines`.

- [ ] **Step 1: Implement `OrdersViewModel`**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

public partial class OrdersViewModel(
    IOrderService orderService,
    ICustomerService customerService,
    IListingService listingService) : ObservableObject
{
    public ObservableCollection<Order> Orders { get; } = [];
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<OrderLine> Lines { get; } = [];
    public ObservableCollection<ActiveListing> AvailableCards { get; } = [];

    [ObservableProperty]
    public partial Order? SelectedOrder { get; set; }

    [ObservableProperty]
    public partial Customer? SelectedCustomer { get; set; }

    [ObservableProperty]
    public partial ActiveListing? SelectedAvailableCard { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public decimal OrderTotal => Lines.Sum(l => l.Quantity * l.UnitSalePrice);

    public void Load()
    {
        Orders.Clear();
        foreach (var o in orderService.GetOrders()) Orders.Add(o);
        Customers.Clear();
        foreach (var c in customerService.GetAll()) Customers.Add(c);
        AvailableCards.Clear();
        foreach (var a in listingService.GetActiveListings()) AvailableCards.Add(a);
    }

    partial void OnSelectedOrderChanged(Order? value)
    {
        Lines.Clear();
        if (value is not null)
            foreach (var l in orderService.GetLines(value.Id)) Lines.Add(l);
        SelectedCustomer = value is null ? null : Customers.FirstOrDefault(c => c.Id == value.CustomerId);
        OnPropertyChanged(nameof(OrderTotal));
    }

    [RelayCommand]
    public void NewOrder()
    {
        if (SelectedCustomer is null) { StatusMessage = "Pick a customer first."; return; }
        var order = orderService.CreateOrder(SelectedCustomer.Id, SalesChannel.TcgPlayer, null);
        Load();
        SelectedOrder = Orders.FirstOrDefault(o => o.Id == order.Id);
    }

    [RelayCommand]
    public void AddCard()
    {
        if (SelectedOrder is null || SelectedAvailableCard is null) return;
        if (SelectedOrder.Status != OrderStatus.Open) { StatusMessage = "Can only edit an Open order."; return; }
        orderService.AddLine(SelectedOrder.Id, SelectedAvailableCard.LotId, SelectedAvailableCard.ListedPrice);
        RefreshLines();
        AvailableCards.Remove(SelectedAvailableCard);
    }

    [RelayCommand]
    public void RemoveLine(OrderLine? line)
    {
        if (SelectedOrder is null || line is null) return;
        if (SelectedOrder.Status != OrderStatus.Open) { StatusMessage = "Can only edit an Open order."; return; }
        orderService.RemoveLine(line.Id);
        RefreshLines();
    }

    [RelayCommand]
    public void SaveOrder()
    {
        if (SelectedOrder is null) return;
        orderService.UpdateOrder(SelectedOrder);
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    public void SetStatus(OrderStatus status)
    {
        if (SelectedOrder is null) return;
        orderService.SetStatus(SelectedOrder.Id, status);
        var id = SelectedOrder.Id;
        Load();
        SelectedOrder = Orders.FirstOrDefault(o => o.Id == id);
        StatusMessage = $"Order marked {status}.";
    }

    private void RefreshLines()
    {
        Lines.Clear();
        if (SelectedOrder is not null)
            foreach (var l in orderService.GetLines(SelectedOrder.Id)) Lines.Add(l);
        OnPropertyChanged(nameof(OrderTotal));
    }
}
```

- [ ] **Step 2: Implement `OrdersView.xaml`** — layout: left = orders `ListBox` (bound to `Orders`/`SelectedOrder`, show OrderNumber + Status). Right (for `SelectedOrder`): a customer `ComboBox` (Customers/SelectedCustomer) + `New Order` button; header fields (OrderNumber, TrackingNumber, Carrier, MarketplaceFees, ShippingCost, ShippingChargedToBuyer) bound to `SelectedOrder.*` with a `Save` button; a `DataGrid` of `Lines` (Name/Set/Condition/Qty/Price) with a Remove button per row; an "add card" row = `ComboBox`/searchable list bound to `AvailableCards`/`SelectedAvailableCard` + `Add` button; status buttons (`Pack`/`Ship`/`Complete`/`Cancel`) bound to `SetStatusCommand` with `CommandParameter` of the `OrderStatus` (use `{x:Static models:OrderStatus.Shipped}` etc., adding `xmlns:models="clr-namespace:OmniCard.Models;assembly=OmniCard.Shared"`); `OrderTotal` (StringFormat=C) and `StatusMessage`. Use themed controls; any `DataGrid` uses the implicit MD style. Add `OrdersView.xaml.cs` with `InitializeComponent()` and a `Loaded` handler calling `(DataContext as OrdersViewModel)?.Load();`.

- [ ] **Step 3: DI** — register `OrdersViewModel` + `IOrderService` + `ICustomerService` in `App.xaml.cs` (some may already be registered in earlier tasks; do not duplicate).

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build OmniCard/OmniCard.csproj -v q && dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v q`
Expected: `Build succeeded` + all PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Sales/OrdersView.xaml OmniCard/Views/Sales/OrdersView.xaml.cs OmniCard/Views/Sales/OrdersViewModel.cs OmniCard/App.xaml.cs
git commit -m "feat(sales): Orders view (create/edit, add cards, status/ship)"
```

---

### Task 10: Sales tab → sub-tabs (Pick List / Orders / Customers); extract PickListView

**Files:**
- Create: `OmniCard/Views/Sales/PickListView.xaml(.cs)` (move the existing pick-list UI out of `SalesView`)
- Modify: `OmniCard/Views/Sales/SalesView.xaml` (become a `TabControl`), `OmniCard/Views/Sales/SalesViewModel.cs` (expose child VMs), `OmniCard/App.xaml.cs`

**Interfaces / Produces:**
- `SalesViewModel.Orders` (`OrdersViewModel`, from Task 9) and `SalesViewModel.Customers` (`CustomersViewModel`, from Task 8) exposed as properties for the sub-tabs to bind. Both concrete VMs and their views (`OrdersView`, `CustomersView`) already exist from Tasks 8–9.

- [ ] **Step 1: Extract the pick list into `PickListView`** — create `PickListView.xaml` as a `UserControl` whose content is the EXACT current `SalesView.xaml` `<DockPanel>...</DockPanel>` (location picker + pick-list `DataGrid` + buttons). Its DataContext is inherited (the `SalesViewModel`), so bindings (`PickList`, `ForSaleLocation`, `RefreshPickListCommand`, `MarkAllPickedCommand`, `PrintPickListCommand`, `StatusMessage`) are unchanged. Add `PickListView.xaml.cs` with `public PickListView() => InitializeComponent();`.

- [ ] **Step 2: Add child VM properties to `SalesViewModel`** — add `OrdersViewModel orders, CustomersViewModel customers` to the primary constructor and expose:

```csharp
    public OrdersViewModel Orders { get; } = orders;
    public CustomersViewModel Customers { get; } = customers;
```

Leave all existing `SalesViewModel` members (Load, pick-list state, `_suppressPersist`, etc.) unchanged.

- [ ] **Step 3: Rewrite `SalesView.xaml` as a TabControl**

```xml
<UserControl x:Class="OmniCard.Views.Sales.SalesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:OmniCard.Views.Sales">
    <TabControl>
        <TabItem Header="Pick List">
            <local:PickListView/>
        </TabItem>
        <TabItem Header="Orders">
            <local:OrdersView DataContext="{Binding Orders}"/>
        </TabItem>
        <TabItem Header="Customers">
            <local:CustomersView DataContext="{Binding Customers}"/>
        </TabItem>
    </TabControl>
</UserControl>
```

> NOTE: this nested `TabControl` raises `SelectionChanged` that bubbles to `MainTabControl`. The Phase-1 guard (`if (!ReferenceEquals(e.OriginalSource, MainTabControl)) return;` in `RootView.xaml.cs`) already prevents that from re-triggering `Sales.Load()` — do not remove that guard. The pick list still loads via the existing `MainTabControl` handler calling `Sales.Load()` when the Sales tab is opened; the Orders/Customers sub-views load via their own `Loaded` handlers (Tasks 8–9).

- [ ] **Step 4: DI** — `OrdersViewModel`/`CustomersViewModel` are already registered (Tasks 8–9). No new registration needed here unless missing; verify `SalesViewModel` resolves with the two new constructor deps.

- [ ] **Step 5: Build + full test suite**

Run: `dotnet build OmniCard/OmniCard.csproj -v q && dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v q`
Expected: `Build succeeded` + all PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Sales/ OmniCard/App.xaml.cs
git commit -m "feat(sales): Sales tab sub-tabs (Pick List / Orders / Customers)"
```

---

## Phase 2 exit criteria (human E2E before merge)

- Customers tab: create/edit/delete a customer; persists across restart.
- Orders tab: pick a customer → New Order → add one of your Listed/Picked cards (it appears in the available-cards list) → enter fees/shipping/tracking → Save.
- Mark the order **Shipped** → the card leaves the collection (badge gone; not in pick list), a Sell movement is recorded (Movement History shows it), and the listing is Sold.
- Dashboard shows **Realized (net)** reflecting proceeds − cost − fees − shipping + shipping charged.
- Restart on the existing `inventory.db` — no schema errors (Customers/Orders/OrderLines auto-created).
- `dotnet test` green.

## Self-Review Notes (coverage vs. spec Phase 2)

- Customer/Order/OrderLine entities + schema → Task 1. ✅
- Customers CRUD → Tasks 2 (service), 9 (UI). ✅
- Orders create/edit + add any Listed/Picked card → Tasks 3 (active-listings), 4 (service), 10 (UI). ✅
- Status flow Open→Packed→Shipped→Completed(+Cancelled); Ship ⇒ remove inventory + Sell movements + snapshots + listings Sold → Tasks 4–5, 10. ✅
- Net P&L (fees/shipping) unioning manual + eBay → Task 6 (Sell movements already cover both channels), Dashboard → Task 7. ✅
- Sales sub-tabs → Task 8. ✅
- Receipt printing is Phase 3 (not in this plan). ✅
