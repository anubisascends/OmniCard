# Sales & Fulfillment Phase 4 (TCGPlayer Order Import) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import a TCGPlayer Shipping Export CSV to bulk-create customers and order shells (behind a review step), so the seller stops hand-entering that data; cards are still added by hand per order.

**Architecture:** The Shipping Export is one row per order with no card lines. A new `TcgPlayerOrderImportService` parses it (CsvHelper), matches/creates `Customer`s (name + postal) and creates `Order`s (`Channel=TcgPlayer`, `Status=Open`) idempotently (skips existing order numbers). Two nullable columns on `Order` capture the buyer-paid item count + product value for a live "added N of M" reconciliation hint in the order editor. A preview dialog (modeled on the existing `CsvImportView`) gates the commit.

**Tech Stack:** C# / .NET, WPF (CommunityToolkit.Mvvm), EF Core (SQLite), CsvHelper, xUnit, Moq, Microsoft.Extensions.DependencyInjection.

**Spec:** `docs/superpowers/specs/2026-07-21-sales-fulfillment-phase4-design.md`

## Global Constraints

- **Models** in `OmniCard.Shared` (`namespace OmniCard.Models`); **interfaces** in `namespace OmniCard.Interfaces`; **services** in `OmniCard.Collection` (`namespace OmniCard.Collection`); **schema** in `OmniCard.Data`; **views/VMs** in `OmniCard`.
- **No `.sln`** — build AND test via `dotnet build OmniCard.Tests/OmniCard.Tests.csproj` and `dotnet test OmniCard.Tests/OmniCard.Tests.csproj` (this project references the WPF app + all deps).
- **CsvHelper:** read with `new CsvConfiguration(CultureInfo.InvariantCulture) { MissingFieldFound = null }`; all number/date parsing uses `CultureInfo.InvariantCulture`.
- **Decimals persist as TEXT** in SQLite (existing convention); nullable decimals → nullable TEXT columns.
- **Orders created by import:** `Channel = SalesChannel.TcgPlayer`, `Status = OrderStatus.Open`.
- **Customer match key:** full name + postal code, case-insensitive (`StringComparison.OrdinalIgnoreCase`); refresh address on match.
- **Idempotent commit:** never create an order whose `OrderNumber` already exists (pre-existing or created earlier in the same commit).
- **MVVM:** dialog VMs follow the `: ViewModel` + `IView<T>` pattern (like `CsvImportView`); the `OrdersViewModel` uses `: ObservableObject` primary-constructor style.
- **Branch:** create `feat/sales-fulfillment-phase4` off the current `master` (which has phase 3). **`docs/` is gitignored** — commit only code/tests.

---

## File Structure

**Created:**
- `OmniCard.Shared/Models/TcgOrderImportRow.cs` — one parsed CSV row + match/dup status.
- `OmniCard.Shared/Models/TcgOrderImportPreview.cs` — the preview (rows + warnings).
- `OmniCard.Shared/Interfaces/ITcgPlayerOrderImportService.cs` — `PreviewImport` + `Commit`.
- `OmniCard.Collection/TcgPlayerOrderImportService.cs` — parse + match + commit.
- `OmniCard/Views/TcgOrderImport/TcgOrderImportView.xaml(.cs)` — preview dialog.
- `OmniCard/Views/TcgOrderImport/TcgOrderImportViewModel.cs` — backs the dialog.
- Test files under `OmniCard.Tests/`.

**Modified:**
- `OmniCard.Shared/Models/Order.cs` — add `ImportedItemCount`, `ImportedProductValue`.
- `OmniCard.Data/UnifiedMigrationService.cs` — Orders CREATE TABLE + `AddColumnIfMissing` guard.
- `OmniCard.Shared/Interfaces/IDialogService.cs` + `OmniCard/Services/DialogService.cs` — `ShowTcgOrderImportPreview`.
- `OmniCard/Views/Sales/OrdersViewModel.cs` + `OmniCard/Views/Sales/OrdersView.xaml` — Import command/button + reconciliation hint.
- `OmniCard/App.xaml.cs` — DI registrations.
- `OmniCard.Tests/Services/UnifiedMigrationTests.cs` + `OmniCard.Tests/Views/Sales/OrdersViewModelTests.cs` — extend for new columns / new ctor args.

---

## BUILD STEP 1 — Model + import service

### Task 1: Order reconciliation columns + schema/migration

**Files:**
- Modify: `OmniCard.Shared/Models/Order.cs`, `OmniCard.Data/UnifiedMigrationService.cs`, `OmniCard.Tests/Services/UnifiedMigrationTests.cs`
- Test: `OmniCard.Tests/Services/OrderServiceTests.cs` (add a round-trip fact) + `UnifiedMigrationTests.cs`

**Interfaces:**
- Produces: `Order.ImportedItemCount (int?)`, `Order.ImportedProductValue (decimal?)`.

- [ ] **Step 1: Write the failing tests**

Add to `OmniCard.Tests/Services/OrderServiceTests.cs` (inside the class):

```csharp
    [Fact]
    public void Order_ImportedReconciliationFields_RoundTrip()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Customers.Add(new Customer { Id = 1, Name = "Ada" });
            ctx.SaveChanges();
            ctx.Orders.Add(new Order
            {
                CustomerId = 1,
                Channel = SalesChannel.TcgPlayer,
                Status = OrderStatus.Open,
                OrderNumber = "TCG-1",
                OrderDate = new DateTime(2026, 7, 17),
                ImportedItemCount = 8,
                ImportedProductValue = 320.00m,
            });
            ctx.SaveChanges();
        }

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var order = ctx.Orders.Single(o => o.OrderNumber == "TCG-1");
            Assert.Equal(8, order.ImportedItemCount);
            Assert.Equal(320.00m, order.ImportedProductValue);
        }
    }
```

In `OmniCard.Tests/Services/UnifiedMigrationTests.cs`, find the pre-existing-DB column-verification list (search for `("Orders", "MarketplaceFees")`) and add two tuples to it:

```csharp
            ("Orders", "ImportedItemCount"), ("Orders", "ImportedProductValue"),
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests|FullyQualifiedName~UnifiedMigrationTests"`
Expected: FAIL — `Order.ImportedItemCount`/`ImportedProductValue` don't exist (compile error).

- [ ] **Step 3: Add the fields to `Order`**

In `OmniCard.Shared/Models/Order.cs`, add before the closing brace:

```csharp
    /// <summary>Buyer-paid item count from a TCGPlayer import (null for non-imported orders);
    /// used for the order-editor reconciliation hint.</summary>
    public int? ImportedItemCount { get; set; }
    /// <summary>Buyer-paid product subtotal from a TCGPlayer import (null for non-imported orders).</summary>
    public decimal? ImportedProductValue { get; set; }
```

- [ ] **Step 4: Add the columns to the schema (fresh + existing DBs)**

In `OmniCard.Data/UnifiedMigrationService.cs`, in the `CREATE TABLE IF NOT EXISTS Orders (...)` statement, change the last data line so it reads:

```
                CreatedAt TEXT NOT NULL,
                ShippedAt TEXT,
                ImportedItemCount INTEGER,
                ImportedProductValue TEXT
```

Then, for already-existing databases, add a guard block next to the existing `if (TableExists(cmd, "Lots"))` block (after its closing brace, before the `CREATE TABLE` statements):

```csharp
        if (TableExists(cmd, "Orders"))
        {
            AddColumnIfMissing(cmd, "Orders", "ImportedItemCount", "INTEGER");
            AddColumnIfMissing(cmd, "Orders", "ImportedProductValue", "TEXT");
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests|FullyQualifiedName~UnifiedMigrationTests"`
Expected: PASS.

- [ ] **Step 6: Build + full suite**

Run: `dotnet build OmniCard.Tests/OmniCard.Tests.csproj` then `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: Build 0 errors; all tests pass.

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Models/Order.cs OmniCard.Data/UnifiedMigrationService.cs \
        OmniCard.Tests/Services/OrderServiceTests.cs OmniCard.Tests/Services/UnifiedMigrationTests.cs
git commit -m "feat(sales): Order reconciliation columns for TCGPlayer import"
```

---

### Task 2: TcgPlayerOrderImportService (parse + match + commit)

**Files:**
- Create: `OmniCard.Shared/Models/TcgOrderImportRow.cs`, `OmniCard.Shared/Models/TcgOrderImportPreview.cs`, `OmniCard.Shared/Interfaces/ITcgPlayerOrderImportService.cs`, `OmniCard.Collection/TcgPlayerOrderImportService.cs`
- Modify: `OmniCard/App.xaml.cs`
- Test: `OmniCard.Tests/Services/TcgPlayerOrderImportServiceTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<OmniCardDbContext>`, `Customer`, `Order`, `Order.ImportedItemCount/ImportedProductValue` (Task 1).
- Produces: `TcgOrderImportRow`, `TcgOrderImportPreview`; `ITcgPlayerOrderImportService.PreviewImport(string filePath) → TcgOrderImportPreview`, `Commit(TcgOrderImportPreview) → int`.

- [ ] **Step 1: Create the preview models**

`OmniCard.Shared/Models/TcgOrderImportRow.cs`:

```csharp
namespace OmniCard.Models;

/// <summary>One row of a parsed TCGPlayer Shipping Export, plus how it maps onto existing data.</summary>
public class TcgOrderImportRow
{
    public string OrderNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal ShippingFeePaid { get; set; }
    public int ItemCount { get; set; }
    public decimal ValueOfProducts { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }

    /// <summary>Existing customer this row matched (name + postal), if any.</summary>
    public int? MatchedCustomerId { get; set; }
    public bool IsNewCustomer { get; set; }
    /// <summary>The order number already exists in the app — this row is skipped on commit.</summary>
    public bool IsDuplicateOrder { get; set; }
    /// <summary>Whether the user has this row selected for commit (defaults false for duplicates).</summary>
    public bool Include { get; set; } = true;

    /// <summary>Whether the Include checkbox may be toggled (duplicates are locked off).</summary>
    public bool CanInclude => !IsDuplicateOrder;

    /// <summary>Human-readable status shown in the preview grid.</summary>
    public string StatusText =>
        IsDuplicateOrder ? "Already imported"
        : IsNewCustomer ? "New customer · New order"
        : "Matched customer · New order";
}
```

`OmniCard.Shared/Models/TcgOrderImportPreview.cs`:

```csharp
using System.Collections.Generic;

namespace OmniCard.Models;

public class TcgOrderImportPreview
{
    public List<TcgOrderImportRow> Rows { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
```

- [ ] **Step 2: Create the interface**

`OmniCard.Shared/Interfaces/ITcgPlayerOrderImportService.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ITcgPlayerOrderImportService
{
    /// <summary>Parses a TCGPlayer Shipping Export and resolves customer-match / duplicate-order
    /// status for each row against the current database.</summary>
    TcgOrderImportPreview PreviewImport(string filePath);

    /// <summary>Creates customers/orders for the included, non-duplicate rows. Idempotent:
    /// order numbers that already exist are skipped. Returns the number of orders created.</summary>
    int Commit(TcgOrderImportPreview preview);
}
```

- [ ] **Step 3: Write the failing tests**

Create `OmniCard.Tests/Services/TcgPlayerOrderImportServiceTests.cs`:

```csharp
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class TcgPlayerOrderImportServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly string _dir;

    public TcgPlayerOrderImportServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
        _dir = Path.Combine(Path.GetTempPath(), "omnicard-tcgimport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { _conn.Dispose(); if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private const string Header =
        "Order #,FirstName,LastName,Address1,Address2,City,State,PostalCode,Country,Order Date,Product Weight,Shipping Method,Item Count,Value Of Products,Shipping Fee Paid,Tracking #,Carrier";

    private string WriteCsv(params string[] dataRows)
    {
        var path = Path.Combine(_dir, "orders-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllLines(path, new[] { Header }.Concat(dataRows));
        return path;
    }

    private static string Row(string orderNo, string first, string last, string postal,
        string date = "2026-07-17", string items = "8", string value = "320.00", string ship = "19.99")
        => $"\"{orderNo}\",\"{first}\",\"{last}\",\"11323 174th Ave\",\"\",\"Bonney Lake\",\"WA\",\"{postal}\",\"US\",\"{date}\",\"0.56\",\"Standard (7-10 days)\",\"{items}\",\"{value}\",\"{ship}\",\"\",\"\"";

    private TcgPlayerOrderImportService Svc() => new(new Factory(_opts));

    [Fact]
    public void PreviewImport_ParsesFields_AndFlagsNewCustomer()
    {
        var path = WriteCsv(Row("BF5A9364-382FDC-D66E4", "Tad", "Cutright", "98391-8194"));
        var preview = Svc().PreviewImport(path);

        var row = Assert.Single(preview.Rows);
        Assert.Equal("BF5A9364-382FDC-D66E4", row.OrderNumber);
        Assert.Equal("Tad Cutright", row.CustomerName);
        Assert.Equal("Bonney Lake", row.City);
        Assert.Equal("98391-8194", row.PostalCode);
        Assert.Equal(new DateTime(2026, 7, 17), row.OrderDate);
        Assert.Equal(8, row.ItemCount);
        Assert.Equal(320.00m, row.ValueOfProducts);
        Assert.Equal(19.99m, row.ShippingFeePaid);
        Assert.True(row.IsNewCustomer);
        Assert.False(row.IsDuplicateOrder);
        Assert.True(row.Include);
    }

    [Fact]
    public void PreviewImport_MatchesExistingCustomer_OnNameAndPostal()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Customers.Add(new Customer { Name = "Tad Cutright", PostalCode = "98391-8194" });
            ctx.SaveChanges();
        }
        var preview = Svc().PreviewImport(WriteCsv(Row("ORD-1", "Tad", "Cutright", "98391-8194")));
        var row = Assert.Single(preview.Rows);
        Assert.False(row.IsNewCustomer);
        Assert.NotNull(row.MatchedCustomerId);
    }

    [Fact]
    public void PreviewImport_FlagsDuplicateOrder_AndDefaultsIncludeFalse()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Customers.Add(new Customer { Id = 5, Name = "X" });
            ctx.Orders.Add(new Order { CustomerId = 5, OrderNumber = "ORD-DUP", OrderDate = DateTime.UtcNow, Status = OrderStatus.Open });
            ctx.SaveChanges();
        }
        var preview = Svc().PreviewImport(WriteCsv(Row("ORD-DUP", "Tad", "Cutright", "98391")));
        var row = Assert.Single(preview.Rows);
        Assert.True(row.IsDuplicateOrder);
        Assert.False(row.Include);
        Assert.False(row.CanInclude);
    }

    [Fact]
    public void Commit_CreatesCustomerAndOrder_ThenIsIdempotent()
    {
        var svc = Svc();
        var preview = svc.PreviewImport(WriteCsv(Row("ORD-100", "Tad", "Cutright", "98391-8194")));

        Assert.Equal(1, svc.Commit(preview));

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var order = ctx.Orders.Single(o => o.OrderNumber == "ORD-100");
            Assert.Equal(SalesChannel.TcgPlayer, order.Channel);
            Assert.Equal(OrderStatus.Open, order.Status);
            Assert.Equal(19.99m, order.ShippingChargedToBuyer);
            Assert.Equal(8, order.ImportedItemCount);
            Assert.Equal(320.00m, order.ImportedProductValue);
            var customer = ctx.Customers.Single(c => c.Id == order.CustomerId);
            Assert.Equal("Tad Cutright", customer.Name);
            Assert.Equal("98391-8194", customer.PostalCode);
        }

        // Re-committing the same preview creates nothing (order number now exists).
        Assert.Equal(0, svc.Commit(preview));
        using (var ctx = new OmniCardDbContext(_opts))
            Assert.Single(ctx.Orders.Where(o => o.OrderNumber == "ORD-100"));
    }

    [Fact]
    public void Commit_RepeatBuyerInSameFile_ReusesOneCustomer()
    {
        var svc = Svc();
        var preview = svc.PreviewImport(WriteCsv(
            Row("ORD-A", "Tad", "Cutright", "98391-8194"),
            Row("ORD-B", "Tad", "Cutright", "98391-8194")));

        Assert.Equal(2, svc.Commit(preview));

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Customers.Where(c => c.Name == "Tad Cutright"));
        Assert.Equal(2, ctx.Orders.Count(o => o.OrderNumber == "ORD-A" || o.OrderNumber == "ORD-B"));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~TcgPlayerOrderImportServiceTests`
Expected: FAIL — `TcgPlayerOrderImportService` doesn't exist.

- [ ] **Step 5: Implement the service**

Create `OmniCard.Collection/TcgPlayerOrderImportService.cs`:

```csharp
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class TcgPlayerOrderImportService(IDbContextFactory<OmniCardDbContext> dbContextFactory)
    : ITcgPlayerOrderImportService
{
    public TcgOrderImportPreview PreviewImport(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { MissingFieldFound = null };
        using var csv = new CsvReader(reader, config);
        csv.Read();
        csv.ReadHeader();

        using var ctx = dbContextFactory.CreateDbContext();
        var customers = ctx.Customers.AsNoTracking().ToList();
        var existingOrderNumbers = ctx.Orders.AsNoTracking()
            .Where(o => o.OrderNumber != null)
            .Select(o => o.OrderNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var preview = new TcgOrderImportPreview();
        var rowNum = 0;
        while (csv.Read())
        {
            rowNum++;
            try
            {
                var row = ParseRow(csv);
                var match = customers.FirstOrDefault(c => IsSameCustomer(c, row));
                row.MatchedCustomerId = match?.Id;
                row.IsNewCustomer = match is null;
                row.IsDuplicateOrder = !string.IsNullOrWhiteSpace(row.OrderNumber)
                                       && existingOrderNumbers.Contains(row.OrderNumber);
                row.Include = !row.IsDuplicateOrder;
                preview.Rows.Add(row);
            }
            catch (Exception ex)
            {
                preview.Warnings.Add($"Row {rowNum}: {ex.Message}");
            }
        }
        return preview;
    }

    public int Commit(TcgOrderImportPreview preview)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var customers = ctx.Customers.ToList(); // tracked
        var seenOrderNumbers = ctx.Orders
            .Where(o => o.OrderNumber != null)
            .Select(o => o.OrderNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = 0;
        foreach (var row in preview.Rows.Where(r => r.Include && !r.IsDuplicateOrder))
        {
            // Idempotent + intra-file dedup: skip blank or already-seen order numbers.
            if (string.IsNullOrWhiteSpace(row.OrderNumber) || !seenOrderNumbers.Add(row.OrderNumber))
                continue;

            var customer = customers.FirstOrDefault(c => IsSameCustomer(c, row));
            if (customer is null)
            {
                customer = new Customer { Name = row.CustomerName, CreatedAt = DateTime.UtcNow };
                ApplyAddress(customer, row);
                ctx.Customers.Add(customer);
                ctx.SaveChanges();          // assign Id
                customers.Add(customer);    // so a repeat buyer later in the file reuses it
            }
            else
            {
                ApplyAddress(customer, row); // refresh address; persisted in the final SaveChanges
            }

            ctx.Orders.Add(new Order
            {
                CustomerId = customer.Id,
                Channel = SalesChannel.TcgPlayer,
                OrderNumber = row.OrderNumber,
                OrderDate = row.OrderDate,
                Status = OrderStatus.Open,
                ShippingChargedToBuyer = row.ShippingFeePaid,
                TrackingNumber = row.TrackingNumber,
                Carrier = row.Carrier,
                ImportedItemCount = row.ItemCount,
                ImportedProductValue = row.ValueOfProducts,
                CreatedAt = DateTime.UtcNow,
            });
            created++;
        }

        ctx.SaveChanges();
        return created;
    }

    private static bool IsSameCustomer(Customer c, TcgOrderImportRow row)
        => string.Equals(c.Name, row.CustomerName, StringComparison.OrdinalIgnoreCase)
           && string.Equals(c.PostalCode ?? "", row.PostalCode ?? "", StringComparison.OrdinalIgnoreCase);

    private static void ApplyAddress(Customer c, TcgOrderImportRow row)
    {
        c.AddressLine1 = row.AddressLine1;
        c.AddressLine2 = row.AddressLine2;
        c.City = row.City;
        c.State = row.State;
        c.PostalCode = row.PostalCode;
        c.Country = row.Country;
    }

    private static TcgOrderImportRow ParseRow(CsvReader csv)
    {
        var first = csv.GetField("FirstName")?.Trim() ?? "";
        var last = csv.GetField("LastName")?.Trim() ?? "";
        var name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new TcgOrderImportRow
        {
            OrderNumber = csv.GetField("Order #")?.Trim() ?? "",
            CustomerName = name,
            AddressLine1 = NullIfBlank(csv.GetField("Address1")),
            AddressLine2 = NullIfBlank(csv.GetField("Address2")),
            City = NullIfBlank(csv.GetField("City")),
            State = NullIfBlank(csv.GetField("State")),
            PostalCode = NullIfBlank(csv.GetField("PostalCode")),
            Country = NullIfBlank(csv.GetField("Country")),
            OrderDate = DateTime.TryParse(csv.GetField("Order Date"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d) ? d : DateTime.UtcNow.Date,
            ShippingFeePaid = ParseDecimal(csv.GetField("Shipping Fee Paid")),
            ItemCount = int.TryParse(csv.GetField("Item Count"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var ic) ? ic : 0,
            ValueOfProducts = ParseDecimal(csv.GetField("Value Of Products")),
            TrackingNumber = NullIfBlank(csv.GetField("Tracking #")),
            Carrier = NullIfBlank(csv.GetField("Carrier")),
        };
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static decimal ParseDecimal(string? s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~TcgPlayerOrderImportServiceTests`
Expected: PASS (5 tests).

- [ ] **Step 7: Register in DI**

In `OmniCard/App.xaml.cs`, in the "Sales & fulfillment" block (near `IOrderService`), add:

```csharp
            services.AddSingleton<ITcgPlayerOrderImportService, TcgPlayerOrderImportService>();
```

- [ ] **Step 8: Build + full suite**

Run: `dotnet build OmniCard.Tests/OmniCard.Tests.csproj` then `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: Build 0 errors; all tests pass.

- [ ] **Step 9: Commit**

```bash
git add OmniCard.Shared/Models/TcgOrderImportRow.cs OmniCard.Shared/Models/TcgOrderImportPreview.cs \
        OmniCard.Shared/Interfaces/ITcgPlayerOrderImportService.cs \
        OmniCard.Collection/TcgPlayerOrderImportService.cs OmniCard/App.xaml.cs \
        OmniCard.Tests/Services/TcgPlayerOrderImportServiceTests.cs
git commit -m "feat(sales): TCGPlayer order import service (parse + match + idempotent commit)"
```

---

## BUILD STEP 2 — UI

### Task 3: Preview dialog

**Files:**
- Create: `OmniCard/Views/TcgOrderImport/TcgOrderImportViewModel.cs`, `TcgOrderImportView.xaml(.cs)`
- Modify: `OmniCard.Shared/Interfaces/IDialogService.cs`, `OmniCard/Services/DialogService.cs`, `OmniCard/App.xaml.cs`
- Test: `OmniCard.Tests/Views/Sales/TcgOrderImportViewModelTests.cs`

**Interfaces:**
- Consumes: `ITcgPlayerOrderImportService` (Task 2), `TcgOrderImportPreview`.
- Produces: `TcgOrderImportViewModel` (`LoadPreview`, `ImportCommand`, `CancelCommand`, `ImportedCount`, `Rows`); `IDialogService.ShowTcgOrderImportPreview(TcgOrderImportPreview) → int`.

- [ ] **Step 1: Write the failing VM tests**

Create `OmniCard.Tests/Views/Sales/TcgOrderImportViewModelTests.cs`:

```csharp
using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.TcgOrderImport;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class TcgOrderImportViewModelTests
{
    private static TcgOrderImportPreview Preview() => new()
    {
        Rows =
        {
            new TcgOrderImportRow { OrderNumber = "A", CustomerName = "Ada", Include = true },
            new TcgOrderImportRow { OrderNumber = "B", CustomerName = "Bo", IsDuplicateOrder = true, Include = false },
        },
    };

    [Fact]
    public void LoadPreview_PopulatesRows_AndCanImportWhenAnyIncluded()
    {
        var vm = new TcgOrderImportViewModel(Mock.Of<ITcgPlayerOrderImportService>());
        vm.LoadPreview(Preview());
        Assert.Equal(2, vm.Rows.Count);
        Assert.True(vm.CanImport);
    }

    [Fact]
    public void Import_CommitsPreview_SetsImportedCount_AndCloses()
    {
        var preview = Preview();
        var svc = new Mock<ITcgPlayerOrderImportService>();
        svc.Setup(s => s.Commit(preview)).Returns(1);
        var vm = new TcgOrderImportViewModel(svc.Object);
        vm.LoadPreview(preview);

        bool? closed = null;
        vm.CloseDialog = r => closed = r;
        vm.ImportCommand.Execute(null);

        Assert.Equal(1, vm.ImportedCount);
        Assert.True(closed);
        svc.Verify(s => s.Commit(preview), Times.Once);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~TcgOrderImportViewModelTests`
Expected: FAIL — `TcgOrderImportViewModel` doesn't exist.

- [ ] **Step 3: Implement the view-model**

Create `OmniCard/Views/TcgOrderImport/TcgOrderImportViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.TcgOrderImport;

public sealed partial class TcgOrderImportViewModel(ITcgPlayerOrderImportService importService) : ViewModel
{
    private TcgOrderImportPreview _preview = new();

    public ObservableCollection<TcgOrderImportRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string SummaryLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool CanImport { get; set; }

    public int ImportedCount { get; private set; }

    public Action<bool>? CloseDialog { get; set; }

    public void LoadPreview(TcgOrderImportPreview preview)
    {
        _preview = preview;
        Rows.Clear();
        foreach (var row in preview.Rows)
            Rows.Add(row);

        var newCount = preview.Rows.Count(r => !r.IsDuplicateOrder);
        var dupCount = preview.Rows.Count(r => r.IsDuplicateOrder);
        SummaryLabel = $"{newCount} new order(s), {dupCount} already imported"
                       + (preview.Warnings.Count > 0 ? $", {preview.Warnings.Count} row(s) skipped" : "");
        CanImport = newCount > 0;
    }

    [RelayCommand]
    public void Import()
    {
        ImportedCount = importService.Commit(_preview);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~TcgOrderImportViewModelTests`
Expected: PASS.

- [ ] **Step 5: Create the dialog window**

Create `OmniCard/Views/TcgOrderImport/TcgOrderImportView.xaml`:

```xml
<Window x:Class="OmniCard.Views.TcgOrderImport.TcgOrderImportView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="clr-namespace:OmniCard.Views.TcgOrderImport"
        mc:Ignorable="d"
        d:DataContext="{d:DesignInstance {x:Type local:TcgOrderImportView}}"
        Title="Import TCGPlayer Orders" Height="520" Width="880"
        ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource MaterialDesign.Brush.Background}"
        TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
        TextElement.FontWeight="Regular"
        TextElement.FontSize="13"
        FontFamily="{StaticResource AppFont}">
    <DockPanel Margin="12">
        <TextBlock DockPanel.Dock="Top" Text="{Binding ViewModel.SummaryLabel}"
                   FontWeight="SemiBold" Margin="0,0,0,8"/>

        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
            <Button Content="Import" Command="{Binding ViewModel.ImportCommand}"
                    IsEnabled="{Binding ViewModel.CanImport}" Padding="16,4" Margin="0,0,8,0"/>
            <Button Content="Cancel" Command="{Binding ViewModel.CancelCommand}" Padding="16,4"/>
        </StackPanel>

        <DataGrid ItemsSource="{Binding ViewModel.Rows}" AutoGenerateColumns="False"
                  CanUserAddRows="False" Style="{StaticResource {x:Type DataGrid}}">
            <DataGrid.Columns>
                <DataGridCheckBoxColumn Header="Import" Binding="{Binding Include, UpdateSourceTrigger=PropertyChanged}"
                                        IsReadOnly="False"/>
                <DataGridTextColumn Header="Order #" Binding="{Binding OrderNumber}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Buyer" Binding="{Binding CustomerName}" IsReadOnly="True"/>
                <DataGridTextColumn Header="City" Binding="{Binding City}" IsReadOnly="True"/>
                <DataGridTextColumn Header="ST" Binding="{Binding State}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Date" Binding="{Binding OrderDate, StringFormat=yyyy-MM-dd}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Items" Binding="{Binding ItemCount}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Products" Binding="{Binding ValueOfProducts, StringFormat=C}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Shipping" Binding="{Binding ShippingFeePaid, StringFormat=C}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Status" Binding="{Binding StatusText}" IsReadOnly="True" Width="*"/>
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</Window>
```

Create `OmniCard/Views/TcgOrderImport/TcgOrderImportView.xaml.cs`:

```csharp
using System.Windows;

namespace OmniCard.Views.TcgOrderImport;

public partial class TcgOrderImportView : Window, IView<TcgOrderImportViewModel>
{
    public TcgOrderImportView(TcgOrderImportViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = result =>
        {
            DialogResult = result;
            Close();
        };
        DataContext = this;
    }

    public TcgOrderImportViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
```

*Note:* `IView<T>`/`IViewModel` are the same interfaces `CsvImportView` uses; confirm the `using`/namespace by matching `OmniCard/Views/CsvImport/CsvImportView.xaml.cs` (it references them without an extra `using`, so they live in a namespace already in scope — replicate exactly).

- [ ] **Step 6: Add the `IDialogService` method**

In `OmniCard.Shared/Interfaces/IDialogService.cs`, add:

```csharp
    int ShowTcgOrderImportPreview(TcgOrderImportPreview preview);
```

In `OmniCard/Services/DialogService.cs`, add `using OmniCard.Views.TcgOrderImport;` and the method (mirrors `ShowImportPreview`):

```csharp
    public int ShowTcgOrderImportPreview(TcgOrderImportPreview preview)
    {
        var wnd = Services.GetRequiredService<TcgOrderImportView>();
        wnd.ViewModel.LoadPreview(preview);
        SetOwner(wnd);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.ImportedCount : 0;
    }
```

- [ ] **Step 7: Register the dialog in DI**

In `OmniCard/App.xaml.cs`, in the views `ConfigureServices` block (near `CsvImportView`/`CsvImportViewModel`), add:

```csharp
            services.AddTransient<Views.TcgOrderImport.TcgOrderImportView>();
            services.AddTransient<Views.TcgOrderImport.TcgOrderImportViewModel>();
```

- [ ] **Step 8: Build + full suite**

Run: `dotnet build OmniCard.Tests/OmniCard.Tests.csproj` then `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: Build 0 errors; all tests pass.

- [ ] **Step 9: Commit**

```bash
git add OmniCard/Views/TcgOrderImport/ OmniCard.Shared/Interfaces/IDialogService.cs \
        OmniCard/Services/DialogService.cs OmniCard/App.xaml.cs \
        OmniCard.Tests/Views/Sales/TcgOrderImportViewModelTests.cs
git commit -m "feat(sales): TCGPlayer order import preview dialog"
```

---

### Task 4: Orders view — Import button + reconciliation hint

**Files:**
- Modify: `OmniCard/Views/Sales/OrdersViewModel.cs`, `OmniCard/Views/Sales/OrdersView.xaml`, `OmniCard.Tests/Views/Sales/OrdersViewModelTests.cs`

**Interfaces:**
- Consumes: `ITcgPlayerOrderImportService` (Task 2), `IDialogService.ShowTcgOrderImportPreview` (Task 3), `Order.ImportedItemCount/ImportedProductValue` (Task 1).
- Produces: `OrdersViewModel.ImportTcgPlayerCommand`, `HasReconciliation`, `ReconciliationHint`.

- [ ] **Step 1: Write the failing VM test**

Add to `OmniCard.Tests/Views/Sales/OrdersViewModelTests.cs` (inside the class). Match how the file constructs `OrdersViewModel` — if it uses a helper, extend it; the two new ctor args are `ITcgPlayerOrderImportService` and `IDialogService`, both satisfiable via `Mock.Of<>()`:

```csharp
    [Fact]
    public void ReconciliationHint_ShownForImportedOrder_HiddenOtherwise()
    {
        var vm = /* construct OrdersViewModel with mocked services, as the other tests do */;
        vm.Load();

        // Non-imported order: hint hidden.
        var plain = vm.Orders.First(); // or a seeded order with null Imported* fields
        vm.SelectedOrder = plain;
        Assert.False(vm.HasReconciliation);

        // Imported order: hint shown and references the target counts.
        var imported = new Order { Id = 999, ImportedItemCount = 8, ImportedProductValue = 320.00m };
        vm.Orders.Add(imported);
        vm.SelectedOrder = imported;
        Assert.True(vm.HasReconciliation);
        Assert.Contains("of 8 items", vm.ReconciliationHint);
    }
```

*Implementer note:* seed via the existing test harness's in-memory context/fakes so `vm.Load()` and `SelectedOrder` behave as in the neighboring tests; keep the assertions above. If the existing tests construct the VM inline, add `Mock.Of<ITcgPlayerOrderImportService>()` and `Mock.Of<IDialogService>()` (Moq is already referenced) to every construction site.

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~OrdersViewModelTests`
Expected: FAIL — new ctor args / `HasReconciliation` / `ReconciliationHint` don't exist.

- [ ] **Step 3: Extend `OrdersViewModel`**

In `OmniCard/Views/Sales/OrdersViewModel.cs`:

Add the two dependencies to the primary constructor:

```csharp
public partial class OrdersViewModel(
    IOrderService orderService,
    ICustomerService customerService,
    IListingService listingService,
    ITcgPlayerOrderImportService importService,
    IDialogService dialogService) : ObservableObject
```

Add reconciliation members (near `OrderTotal`):

```csharp
    public bool HasReconciliation =>
        SelectedOrder?.ImportedItemCount is not null || SelectedOrder?.ImportedProductValue is not null;

    public string ReconciliationHint
    {
        get
        {
            if (SelectedOrder is null) return "";
            var addedItems = Lines.Sum(l => l.Quantity);
            var itemPart = SelectedOrder.ImportedItemCount is int ic
                ? $"added {addedItems} of {ic} items"
                : $"added {addedItems} items";
            var valuePart = SelectedOrder.ImportedProductValue is decimal pv
                ? $"{OrderTotal:C} of {pv:C}"
                : $"{OrderTotal:C}";
            return $"{itemPart} · {valuePart}";
        }
    }
```

In `OnSelectedOrderChanged` and `RefreshLines` (and after `AddCard`/`RemoveLine` mutate lines), raise the new properties alongside the existing `OrderTotal` notification. Where the code currently calls `OnPropertyChanged(nameof(OrderTotal));`, add:

```csharp
        OnPropertyChanged(nameof(HasReconciliation));
        OnPropertyChanged(nameof(ReconciliationHint));
```

(In `OnSelectedOrderChanged`, raise all three after `Lines` is repopulated.)

Add the import command:

```csharp
    [RelayCommand]
    public void ImportTcgPlayer()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import TCGPlayer Shipping Export",
            Filter = "CSV files|*.csv|All files|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var preview = importService.PreviewImport(dialog.FileName);
            if (preview.Rows.Count == 0)
            {
                StatusMessage = "No orders found in that file.";
                return;
            }

            var imported = dialogService.ShowTcgOrderImportPreview(preview);
            if (imported > 0)
            {
                Load();
                StatusMessage = $"Imported {imported} order(s).";
            }
            else
            {
                StatusMessage = "Import cancelled.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }
```

- [ ] **Step 4: Add the button + hint to `OrdersView.xaml`**

In `OmniCard/Views/Sales/OrdersView.xaml`, add an Import button under the New Order button in the left column (after the `New Order` button, before the `ListBox`):

```xml
            <Button DockPanel.Dock="Top" Content="Import from TCGPlayer CSV…"
                    Command="{Binding ImportTcgPlayerCommand}"
                    Padding="12,4" Margin="0,0,0,8" HorizontalAlignment="Left"/>
```

Add the reconciliation hint in the editor's status row (in the bottom `StackPanel`, after the `OrderTotal` TextBlock):

```xml
                <TextBlock Text="{Binding ReconciliationHint}"
                           Visibility="{Binding HasReconciliation, Converter={conv:BoolToVisibilityConverter}}"
                           VerticalAlignment="Center" Margin="16,0,0,0"
                           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
```

(`conv:` is already declared in this file's root element — verify the `xmlns:conv="clr-namespace:OmniCard.Controls.Converters;assembly=OmniCard.Controls"` line is present; it is used elsewhere in the Sales views. If absent, add it.)

- [ ] **Step 5: Run the VM tests to verify pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~OrdersViewModelTests`
Expected: PASS.

- [ ] **Step 6: Build + full suite**

Run: `dotnet build OmniCard.Tests/OmniCard.Tests.csproj` then `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: Build 0 errors; all tests pass.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Sales/OrdersViewModel.cs OmniCard/Views/Sales/OrdersView.xaml \
        OmniCard.Tests/Views/Sales/OrdersViewModelTests.cs
git commit -m "feat(sales): import TCGPlayer orders from the Orders view + reconciliation hint"
```

- [ ] **Step 8: Human E2E note**

Flag for the reviewer: on the Sales ▸ Orders tab, click "Import from TCGPlayer CSV…", pick a real Shipping Export → the preview lists orders with correct statuses (new/matched customer; already-imported rows unchecked & locked) → Import → customers + orders appear (status Open, channel TCGPlayer, shipping fee populated) → open an imported order, add cards → the "added N of M items · $X of $Y" hint updates → Ship (inventory removed + Sell movements) → re-import the same file imports nothing.

---

## Self-Review

**1. Spec coverage:**
- §1/§2 bulk order+customer intake, cards manual → Tasks 2, 4. ✅
- §4 `Order.ImportedItemCount/ImportedProductValue` + schema/migration → Task 1. ✅
- §5 preview model + `PreviewImport` + idempotent `Commit` (name+postal match, address refresh, dup-order skip, intra-file repeat buyer) → Task 2 (+ tests for each). ✅
- §6 Orders-view button, preview dialog (statuses + include checkbox), reconciliation hint → Tasks 3, 4. ✅
- §7 tests: service parse/match/dup/idempotent, schema round-trip, migration guard, VM hint math → Tasks 1–4. ✅
- §8 two build checkpoints → Build Step 1 (Tasks 1–2), Build Step 2 (Tasks 3–4). ✅
- Non-goals honored: no card-line matching; fees/shipping-cost left manual; no tracking update on re-import (dups skipped). ✅

**2. Placeholder scan:** Two deliberate "match the existing harness" notes (Task 4 Step 1 VM construction, Task 3 Step 5 `IView`/`IViewModel` namespace) point at concrete existing files to copy — not vague TODOs. All code steps carry complete code.

**3. Type consistency:** `TcgOrderImportPreview.Rows`/`TcgOrderImportRow` fields defined in Task 2 are used identically in the service, the dialog VM (Task 3), and tests. `ITcgPlayerOrderImportService.PreviewImport/Commit` signatures match across definition (Task 2) and consumers (Tasks 3, 4). `IDialogService.ShowTcgOrderImportPreview(TcgOrderImportPreview) → int` matches between Task 3's definition and Task 4's call. `Order.ImportedItemCount (int?)` / `ImportedProductValue (decimal?)` consistent across Tasks 1, 2, 4.

**Known verification points for the implementer (not blockers):**
- `OrdersViewModelTests` construction sites need the two new ctor args (`Mock.Of<ITcgPlayerOrderImportService>()`, `Mock.Of<IDialogService>()`) — Task 4 Step 1 calls this out.
- `IView`/`IViewModel` namespace for the dialog window — copy from `CsvImportView.xaml.cs` (Task 3 Step 5).
- `xmlns:conv` presence in `OrdersView.xaml` (Task 4 Step 4).
- Confirm SQLite maps `decimal?` → nullable TEXT with no extra `OnModelCreating` config (the existing non-null decimals rely on the same provider behavior; the round-trip test in Task 1 proves it).
