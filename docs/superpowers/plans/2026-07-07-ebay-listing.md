# eBay Listing Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable users to list collection cards on eBay with catalog matching, market-based auto-pricing, full lifecycle tracking (Active/Sold/Ended), and periodic status sync.

**Architecture:** Layered services (EbayCatalogService, EbayListingService, EbaySyncService) following the existing singleton service + DI pattern. New `EbayListing` entity linked to `CollectionCard` via navigation property. Listing dialog opened from collection context menu. Status sync via DispatcherTimer in RootViewModel.

**Tech Stack:** WPF + CommunityToolkit.Mvvm, SQLite via EF Core, eBay REST APIs (Browse, Inventory, Account, Fulfillment), HttpClientFactory, WebView2 (existing auth).

## Global Constraints

- All eBay API calls must use the existing `IEbayAuthService.GetAccessTokenAsync()` for Bearer tokens.
- Scopes already requested: `sell.inventory`, `sell.account`, `sell.fulfillment`, base `api_scope`.
- Follow existing MVVM patterns: `[ObservableProperty]`, `[RelayCommand]`, `IView<T>`, `ViewModel` base class.
- Use `IHttpClientFactory.CreateClient()` for all HTTP calls (no raw `new HttpClient()`).
- Database migrations use the `EnsureXxxColumn` pattern in `App.xaml.cs` with raw SQLite commands.
- Dialog registration: transient View + ViewModel in `App.xaml.cs`, interface method in `IDialogService`.

## File Structure

| File | Responsibility |
|------|---------------|
| `OmniCard.Shared/Models/EbayListing.cs` | Entity model for eBay listing tracking |
| `OmniCard.Shared/Models/EbayListingStatus.cs` | Status enum (Draft, Active, Sold, Ended, Error) |
| `OmniCard.Shared/Models/EbayListingType.cs` | Listing type enum (FixedPrice, Auction) |
| `OmniCard.Shared/Models/CollectionCard.cs` | Add `EbayListing?` navigation property |
| `OmniCard.Shared/Data/CollectionDbContext.cs` | Register `DbSet<EbayListing>`, configure entity |
| `OmniCard/Models/EbayListingOptions.cs` | Options DTO for create/revise operations |
| `OmniCard/Models/EbayMarketPrice.cs` | Market price data (median, low, high, count) |
| `OmniCard/Models/EbayCatalogMatch.cs` | Catalog search result model |
| `OmniCard/Models/EbaySellerPolicy.cs` | Seller policy model (shipping/return/payment) |
| `OmniCard/Services/EbayCatalogService.cs` | Catalog search + market pricing via Browse API |
| `OmniCard/Services/EbayListingService.cs` | Create/revise/end listings via Inventory API |
| `OmniCard/Services/EbaySyncService.cs` | Periodic status sync via Fulfillment API |
| `OmniCard/Views/EbayListing/EbayListingView.xaml` | Listing dialog XAML |
| `OmniCard/Views/EbayListing/EbayListingView.xaml.cs` | Listing dialog code-behind |
| `OmniCard/Views/EbayListing/EbayListingViewModel.cs` | Listing dialog ViewModel |
| `OmniCard/Views/Root/CardListView.xaml` | Add eBay context menu items + grid column |
| `OmniCard/Views/Root/CollectionViewModel.cs` | Add eBay commands, column visibility |
| `OmniCard/Views/Root/RootViewModel.cs` | Add sync timer |
| `OmniCard/Views/CollectionCardEditor/CollectionCardEditorViewModel.cs` | Add eBay status section |
| `OmniCard/Views/CollectionCardEditor/CollectionCardEditorView.xaml` | Add eBay status UI |
| `OmniCard/Services/DialogService.cs` | Add `OpenEbayListingDialog` method |
| `OmniCard/App.xaml.cs` | DI registration + DB migration |
| `OmniCard.Tests/Services/EbayCatalogServiceTests.cs` | Catalog service tests |
| `OmniCard.Tests/Services/EbayListingServiceTests.cs` | Listing service tests |
| `OmniCard.Tests/Services/EbaySyncServiceTests.cs` | Sync service tests |

---

### Task 1: EbayListing Entity and Database Schema

**Files:**
- Create: `OmniCard.Shared/Models/EbayListing.cs`
- Create: `OmniCard.Shared/Models/EbayListingStatus.cs`
- Create: `OmniCard.Shared/Models/EbayListingType.cs`
- Modify: `OmniCard.Shared/Models/CollectionCard.cs:28` (add navigation property)
- Modify: `OmniCard.Shared/Data/CollectionDbContext.cs:12` (add DbSet + config)
- Modify: `OmniCard/App.xaml.cs` (add migration method + call in OnStartup)

**Interfaces:**
- Consumes: Nothing (foundational task)
- Produces: `EbayListing` entity with properties: `Id`, `CollectionCardId`, `EbayItemId`, `EbayCatalogProductId`, `Status` (EbayListingStatus), `ListingType` (EbayListingType), `ListedPrice`, `SoldPrice`, `StartTime`, `EndTime`, `AuctionDuration`, `BuyerUsername`, `LastSyncedAt`, `CreatedAt`, `ErrorMessage`. `CollectionCard.EbayListing` navigation property. `DbSet<EbayListing> EbayListings` on context.

- [ ] **Step 1: Create the EbayListingStatus enum**

Create `OmniCard.Shared/Models/EbayListingStatus.cs`:

```csharp
namespace OmniCard.Models;

public enum EbayListingStatus
{
    Draft,
    Active,
    Sold,
    Ended,
    Error,
}
```

- [ ] **Step 2: Create the EbayListingType enum**

Create `OmniCard.Shared/Models/EbayListingType.cs`:

```csharp
namespace OmniCard.Models;

public enum EbayListingType
{
    FixedPrice,
    Auction,
}
```

- [ ] **Step 3: Create the EbayListing entity**

Create `OmniCard.Shared/Models/EbayListing.cs`:

```csharp
namespace OmniCard.Models;

public class EbayListing
{
    public int Id { get; set; }
    public int CollectionCardId { get; set; }
    public CollectionCard CollectionCard { get; set; } = null!;
    public string EbayItemId { get; set; } = "";
    public string? EbayCatalogProductId { get; set; }
    public EbayListingStatus Status { get; set; }
    public EbayListingType ListingType { get; set; }
    public decimal ListedPrice { get; set; }
    public decimal? SoldPrice { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? AuctionDuration { get; set; }
    public string? BuyerUsername { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}
```

- [ ] **Step 4: Add navigation property to CollectionCard**

In `OmniCard.Shared/Models/CollectionCard.cs`, after the `CardType` property (line 28), add:

```csharp
    public EbayListing? EbayListing { get; set; }
```

- [ ] **Step 5: Register DbSet and configure entity in CollectionDbContext**

In `OmniCard.Shared/Data/CollectionDbContext.cs`:

Add after line 12 (`DbSet<ScanDiagnosticEvent>`):
```csharp
    public DbSet<EbayListing> EbayListings => Set<EbayListing>();
```

Add at end of `OnModelCreating`, before the closing brace (after line 69):
```csharp
        modelBuilder.Entity<EbayListing>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.Property(l => l.Status).HasConversion<string>();
            e.Property(l => l.ListingType).HasConversion<string>();
            e.HasIndex(l => l.CollectionCardId).IsUnique();
            e.HasIndex(l => l.Status);
            e.HasIndex(l => l.EbayItemId);

            e.HasOne(l => l.CollectionCard)
                .WithOne(c => c.EbayListing)
                .HasForeignKey<EbayListing>(l => l.CollectionCardId)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

- [ ] **Step 6: Add database migration method in App.xaml.cs**

Add a new method in `App.xaml.cs` following the existing `EnsureScanImagePathColumn` pattern:

```csharp
    private static void EnsureEbayListingsTable(
        string dataDirectory,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var dbPath = Path.Combine(dataDirectory, "collection.db");
        if (!File.Exists(dbPath))
            return;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();

        // Check if table exists
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='EbayListings'";
        var exists = cmd.ExecuteScalar() is long count && count > 0;

        if (!exists)
        {
            cmd.CommandText = """
                CREATE TABLE EbayListings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CollectionCardId INTEGER NOT NULL UNIQUE,
                    EbayItemId TEXT NOT NULL DEFAULT '',
                    EbayCatalogProductId TEXT,
                    Status TEXT NOT NULL DEFAULT 'Draft',
                    ListingType TEXT NOT NULL DEFAULT 'FixedPrice',
                    ListedPrice REAL NOT NULL DEFAULT 0,
                    SoldPrice REAL,
                    StartTime TEXT,
                    EndTime TEXT,
                    AuctionDuration INTEGER,
                    BuyerUsername TEXT,
                    LastSyncedAt TEXT,
                    CreatedAt TEXT NOT NULL,
                    ErrorMessage TEXT,
                    FOREIGN KEY (CollectionCardId) REFERENCES Cards(Id) ON DELETE CASCADE
                )
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX IX_EbayListings_Status ON EbayListings(Status)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX IX_EbayListings_EbayItemId ON EbayListings(EbayItemId)";
            cmd.ExecuteNonQuery();

            logger.LogInformation("Created EbayListings table");
        }
    }
```

Call it in `OnStartup` alongside the other migration calls (find the block that calls `EnsureScanImagePathColumn`, `EnsureColorCardTypeColumns`, etc. and add):

```csharp
EnsureEbayListingsTable(dataDir, migrationLogger);
```

- [ ] **Step 7: Build and verify**

Run: `dotnet build --no-restore`
Expected: 0 errors

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/EbayListing.cs OmniCard.Shared/Models/EbayListingStatus.cs OmniCard.Shared/Models/EbayListingType.cs OmniCard.Shared/Models/CollectionCard.cs OmniCard.Shared/Data/CollectionDbContext.cs OmniCard/App.xaml.cs
git commit -m "feat: add EbayListing entity and database schema"
```

---

### Task 2: EbayCatalogService — Catalog Search and Market Pricing

**Files:**
- Create: `OmniCard/Models/EbayCatalogMatch.cs`
- Create: `OmniCard/Models/EbayMarketPrice.cs`
- Create: `OmniCard/Services/EbayCatalogService.cs`
- Create: `OmniCard.Tests/Services/EbayCatalogServiceTests.cs`
- Modify: `OmniCard/App.xaml.cs` (DI registration)

**Interfaces:**
- Consumes: `IEbayAuthService.GetAccessTokenAsync()`, `EbaySettings.ApiBaseUrl`
- Produces: `IEbayCatalogService` with methods: `Task<List<EbayCatalogMatch>> SearchCatalogAsync(string cardName, string setName, string? collectorNumber)`, `Task<EbayMarketPrice?> GetMarketPriceAsync(string searchQuery, string condition, bool isFoil)`

- [ ] **Step 1: Create EbayCatalogMatch model**

Create `OmniCard/Models/EbayCatalogMatch.cs`:

```csharp
namespace OmniCard.Models;

public class EbayCatalogMatch
{
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? Condition { get; set; }
    public string? CategoryId { get; set; }
}
```

- [ ] **Step 2: Create EbayMarketPrice model**

Create `OmniCard/Models/EbayMarketPrice.cs`:

```csharp
namespace OmniCard.Models;

public class EbayMarketPrice
{
    public decimal MedianPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal HighPrice { get; set; }
    public int SampleCount { get; set; }
}
```

- [ ] **Step 3: Write the failing test for SearchCatalogAsync**

Create `OmniCard.Tests/Services/EbayCatalogServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Models;
using OmniCard.Services;
using System.Net;
using System.Text.Json;

namespace OmniCard.Tests.Services;

public class EbayCatalogServiceTests
{
    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        DevId = "test-dev-id",
        RuName = "test-ru-name",
        Environment = "sandbox",
    };

    [Fact]
    public async Task SearchCatalogAsync_ReturnsMatches_WhenApiReturnsResults()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            itemSummaries = new[]
            {
                new
                {
                    itemId = "v1|123|0",
                    title = "MTG Black Lotus Alpha NM",
                    price = new { value = "5000.00", currency = "USD" },
                    condition = "Near Mint",
                    image = new { imageUrl = "https://img.ebay.com/123.jpg" },
                    categories = new[] { new { categoryId = "38292" } },
                }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayCatalogService(
            Options.Create(_settings),
            factory,
            authService,
            NullLogger<EbayCatalogService>.Instance);

        var results = await svc.SearchCatalogAsync("Black Lotus", "Alpha", null);

        Assert.Single(results);
        Assert.Equal("v1|123|0", results[0].ItemId);
        Assert.Equal("MTG Black Lotus Alpha NM", results[0].Title);
        Assert.Equal(5000.00m, results[0].Price);
    }

    [Fact]
    public async Task SearchCatalogAsync_ReturnsEmpty_WhenNotConnected()
    {
        var authService = new FakeEbayAuthService(null);
        var factory = new FakeHttpClientFactory(new FakeHttpHandler(HttpStatusCode.OK, "{}"));

        var svc = new EbayCatalogService(
            Options.Create(_settings),
            factory,
            authService,
            NullLogger<EbayCatalogService>.Instance);

        var results = await svc.SearchCatalogAsync("Black Lotus", "Alpha", null);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetMarketPriceAsync_CalculatesMedian()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            itemSummaries = new[]
            {
                new { itemId = "1", title = "Card", price = new { value = "10.00", currency = "USD" }, condition = "Near Mint", image = (object?)null, categories = (object?)null },
                new { itemId = "2", title = "Card", price = new { value = "20.00", currency = "USD" }, condition = "Near Mint", image = (object?)null, categories = (object?)null },
                new { itemId = "3", title = "Card", price = new { value = "30.00", currency = "USD" }, condition = "Near Mint", image = (object?)null, categories = (object?)null },
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayCatalogService(
            Options.Create(_settings),
            factory,
            authService,
            NullLogger<EbayCatalogService>.Instance);

        var price = await svc.GetMarketPriceAsync("Black Lotus Alpha NM", "NM", false);

        Assert.NotNull(price);
        Assert.Equal(20.00m, price.MedianPrice);
        Assert.Equal(10.00m, price.LowPrice);
        Assert.Equal(30.00m, price.HighPrice);
        Assert.Equal(3, price.SampleCount);
    }
}

// --- Test doubles ---

public class FakeEbayAuthService : IEbayAuthService
{
    private readonly string? _token;
    public FakeEbayAuthService(string? token) => _token = token;
    public bool IsConnected => _token is not null;
    public Task<string?> GetAccessTokenAsync() => Task.FromResult(_token);
    public Task<bool> ExchangeCodeForTokensAsync(string authCode) => Task.FromResult(true);
    public void Disconnect() { }
    public string GetAuthorizationUrl() => "";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public class FakeHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public FakeHttpHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}

public class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler);
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test --no-restore --filter "EbayCatalogServiceTests" -- -v`
Expected: FAIL — `EbayCatalogService` does not exist yet.

- [ ] **Step 5: Implement EbayCatalogService**

Create `OmniCard/Services/EbayCatalogService.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IEbayCatalogService
{
    Task<List<EbayCatalogMatch>> SearchCatalogAsync(string cardName, string setName, string? collectorNumber);
    Task<EbayMarketPrice?> GetMarketPriceAsync(string searchQuery, string condition, bool isFoil);
}

public class EbayCatalogService : IEbayCatalogService
{
    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _ebayAuthService;
    private readonly ILogger<EbayCatalogService> _logger;

    public EbayCatalogService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService ebayAuthService,
        ILogger<EbayCatalogService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _ebayAuthService = ebayAuthService;
        _logger = logger;
    }

    public async Task<List<EbayCatalogMatch>> SearchCatalogAsync(string cardName, string setName, string? collectorNumber)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return [];

        var query = $"{cardName} {setName}";
        if (collectorNumber is not null)
            query += $" {collectorNumber}";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{_settings.ApiBaseUrl}/buy/browse/v1/item_summary/search?q={encodedQuery}&category_ids=38292&limit=10";

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("eBay catalog search failed: {Status}", response.StatusCode);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var results = new List<EbayCatalogMatch>();
        if (!doc.RootElement.TryGetProperty("itemSummaries", out var summaries))
            return results;

        foreach (var item in summaries.EnumerateArray())
        {
            var match = new EbayCatalogMatch
            {
                ItemId = item.GetProperty("itemId").GetString() ?? "",
                Title = item.GetProperty("title").GetString() ?? "",
            };

            if (item.TryGetProperty("price", out var price))
            {
                if (decimal.TryParse(price.GetProperty("value").GetString(), out var priceValue))
                    match.Price = priceValue;
                match.Currency = price.TryGetProperty("currency", out var curr) ? curr.GetString() : null;
            }

            if (item.TryGetProperty("condition", out var cond))
                match.Condition = cond.GetString();

            if (item.TryGetProperty("image", out var img) && img.TryGetProperty("imageUrl", out var imgUrl))
                match.ImageUrl = imgUrl.GetString();

            if (item.TryGetProperty("categories", out var cats))
            {
                foreach (var cat in cats.EnumerateArray())
                {
                    if (cat.TryGetProperty("categoryId", out var catId))
                    {
                        match.CategoryId = catId.GetString();
                        break;
                    }
                }
            }

            results.Add(match);
        }

        _logger.LogInformation("eBay catalog search for '{Query}' returned {Count} results", query, results.Count);
        return results;
    }

    public async Task<EbayMarketPrice?> GetMarketPriceAsync(string searchQuery, string condition, bool isFoil)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return null;

        var query = searchQuery;
        if (isFoil)
            query += " foil";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{_settings.ApiBaseUrl}/buy/browse/v1/item_summary/search?q={encodedQuery}&category_ids=38292&limit=50&filter=buyingOptions:{{FIXED_PRICE}}";

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("eBay market price search failed: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("itemSummaries", out var summaries))
            return null;

        var prices = new List<decimal>();
        foreach (var item in summaries.EnumerateArray())
        {
            if (item.TryGetProperty("price", out var price)
                && decimal.TryParse(price.GetProperty("value").GetString(), out var val))
            {
                prices.Add(val);
            }
        }

        if (prices.Count == 0)
            return null;

        prices.Sort();
        var median = prices.Count % 2 == 0
            ? (prices[prices.Count / 2 - 1] + prices[prices.Count / 2]) / 2
            : prices[prices.Count / 2];

        return new EbayMarketPrice
        {
            MedianPrice = median,
            LowPrice = prices[0],
            HighPrice = prices[^1],
            SampleCount = prices.Count,
        };
    }
}
```

- [ ] **Step 6: Register in DI**

In `OmniCard/App.xaml.cs`, add after the eBay auth service registration (`services.AddSingleton<IEbayAuthService, EbayAuthService>()`):

```csharp
services.AddSingleton<IEbayCatalogService, EbayCatalogService>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --no-restore --filter "EbayCatalogServiceTests"`
Expected: 3 tests PASS

- [ ] **Step 8: Commit**

```bash
git add OmniCard/Models/EbayCatalogMatch.cs OmniCard/Models/EbayMarketPrice.cs OmniCard/Services/EbayCatalogService.cs OmniCard.Tests/Services/EbayCatalogServiceTests.cs OmniCard/App.xaml.cs
git commit -m "feat: add EbayCatalogService for catalog search and market pricing"
```

---

### Task 3: EbayListingService — Create, Revise, and End Listings

**Files:**
- Create: `OmniCard/Models/EbayListingOptions.cs`
- Create: `OmniCard/Models/EbaySellerPolicy.cs`
- Create: `OmniCard/Services/EbayListingService.cs`
- Create: `OmniCard.Tests/Services/EbayListingServiceTests.cs`
- Modify: `OmniCard/App.xaml.cs` (DI registration)

**Interfaces:**
- Consumes: `IEbayAuthService.GetAccessTokenAsync()`, `EbaySettings.ApiBaseUrl`, `IDbContextFactory<CollectionDbContext>`, `EbayListing` entity, `CollectionCard` entity
- Produces: `IEbayListingService` with methods: `Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options)`, `Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options)`, `Task<bool> EndListingAsync(EbayListing listing)`, `Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType)`

- [ ] **Step 1: Create EbayListingOptions model**

Create `OmniCard/Models/EbayListingOptions.cs`:

```csharp
namespace OmniCard.Models;

public class EbayListingOptions
{
    public EbayListingType ListingType { get; set; } = EbayListingType.FixedPrice;
    public decimal Price { get; set; }
    public int? AuctionDuration { get; set; }
    public string Condition { get; set; } = "NM";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IncludeScanImage { get; set; } = true;
    public bool IncludeStockImage { get; set; } = true;
    public string? ShippingPolicyId { get; set; }
    public string? ReturnPolicyId { get; set; }
    public string? PaymentPolicyId { get; set; }
    public string? EbayCategoryId { get; set; }
}
```

- [ ] **Step 2: Create EbaySellerPolicy model**

Create `OmniCard/Models/EbaySellerPolicy.cs`:

```csharp
namespace OmniCard.Models;

public class EbaySellerPolicy
{
    public string PolicyId { get; set; } = "";
    public string Name { get; set; } = "";
    public string PolicyType { get; set; } = "";

    public override string ToString() => Name;
}
```

- [ ] **Step 3: Write the failing test**

Create `OmniCard.Tests/Services/EbayListingServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Services;
using System.Net;
using System.Text.Json;

namespace OmniCard.Tests.Services;

public class EbayListingServiceTests
{
    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        Environment = "sandbox",
    };

    private IDbContextFactory<CollectionDbContext> CreateInMemoryDbFactory()
    {
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(options);
    }

    [Fact]
    public async Task CreateListingAsync_SavesEbayListing_WhenApiSucceeds()
    {
        var dbFactory = CreateInMemoryDbFactory();

        // Seed a card
        using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Id = 1, Name = "Black Lotus", SetName = "Alpha", SetCode = "LEA",
                Number = "232", Rarity = "Rare", GameCardId = "scryfall-123",
            });
            ctx.SaveChanges();
        }

        var responseJson = JsonSerializer.Serialize(new { listingId = "ebay-item-12345" });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbayListingService>.Instance);

        var options = new EbayListingOptions
        {
            Title = "MTG Black Lotus [LEA] #232 NM",
            Description = "Near Mint Black Lotus from Alpha",
            Price = 5000m,
            ListingType = EbayListingType.FixedPrice,
        };

        var card = dbFactory.CreateDbContext().Cards.First(c => c.Id == 1);
        var result = await svc.CreateListingAsync(card, options);

        Assert.True(result);

        using var verifyCtx = dbFactory.CreateDbContext();
        var listing = verifyCtx.EbayListings.FirstOrDefault(l => l.CollectionCardId == 1);
        Assert.NotNull(listing);
        Assert.Equal(EbayListingStatus.Active, listing.Status);
        Assert.Equal(5000m, listing.ListedPrice);
    }

    [Fact]
    public async Task EndListingAsync_UpdatesStatusToEnded()
    {
        var dbFactory = CreateInMemoryDbFactory();

        using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Id = 1, Name = "Test", SetName = "Set", SetCode = "TST",
                Number = "1", Rarity = "Common", GameCardId = "test-1",
            });
            ctx.EbayListings.Add(new EbayListing
            {
                Id = 1, CollectionCardId = 1, EbayItemId = "ebay-123",
                Status = EbayListingStatus.Active, ListedPrice = 10m,
            });
            ctx.SaveChanges();
        }

        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbayListingService>.Instance);

        var listing = dbFactory.CreateDbContext().EbayListings.First(l => l.Id == 1);
        var result = await svc.EndListingAsync(listing);

        Assert.True(result);

        using var verifyCtx = dbFactory.CreateDbContext();
        var updated = verifyCtx.EbayListings.First(l => l.Id == 1);
        Assert.Equal(EbayListingStatus.Ended, updated.Status);
    }

    [Fact]
    public async Task GetSellerPoliciesAsync_ReturnsPolicies()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            fulfillmentPolicies = new[]
            {
                new { fulfillmentPolicyId = "policy-1", name = "Standard Shipping" }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");
        var dbFactory = CreateInMemoryDbFactory();

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbayListingService>.Instance);

        var policies = await svc.GetSellerPoliciesAsync("fulfillment");

        Assert.Single(policies);
        Assert.Equal("policy-1", policies[0].PolicyId);
        Assert.Equal("Standard Shipping", policies[0].Name);
    }
}

public class TestDbContextFactory : IDbContextFactory<CollectionDbContext>
{
    private readonly DbContextOptions<CollectionDbContext> _options;
    public TestDbContextFactory(DbContextOptions<CollectionDbContext> options) => _options = options;
    public CollectionDbContext CreateDbContext() => new(_options);
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --no-restore --filter "EbayListingServiceTests"`
Expected: FAIL — `EbayListingService` does not exist yet.

- [ ] **Step 5: Implement EbayListingService**

Create `OmniCard/Services/EbayListingService.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IEbayListingService
{
    Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options);
    Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options);
    Task<bool> EndListingAsync(EbayListing listing);
    Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType);
}

public class EbayListingService : IEbayListingService
{
    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _ebayAuthService;
    private readonly IDbContextFactory<CollectionDbContext> _dbContextFactory;
    private readonly ILogger<EbayListingService> _logger;

    private static readonly Dictionary<string, int> ConditionMap = new()
    {
        ["NM"] = 3000, // Near Mint
        ["LP"] = 4000, // Lightly Played
        ["MP"] = 5000, // Moderately Played
        ["HP"] = 6000, // Heavily Played
        ["D"] = 7000,  // Damaged
    };

    public EbayListingService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService ebayAuthService,
        IDbContextFactory<CollectionDbContext> dbContextFactory,
        ILogger<EbayListingService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _ebayAuthService = ebayAuthService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Add("Content-Language", "en-US");

            // Create inventory item
            var sku = $"omnicard-{card.Id}";
            var inventoryItem = BuildInventoryItem(card, options);
            var inventoryJson = JsonSerializer.Serialize(inventoryItem);

            var inventoryResponse = await client.PutAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                new StringContent(inventoryJson, Encoding.UTF8, "application/json"));

            if (!inventoryResponse.IsSuccessStatusCode)
            {
                var error = await inventoryResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to create inventory item: {Status} — {Error}", inventoryResponse.StatusCode, error);
                await SaveListingError(card.Id, options, $"Inventory creation failed: {inventoryResponse.StatusCode}");
                return false;
            }

            // Create offer
            var offer = BuildOffer(sku, options);
            var offerJson = JsonSerializer.Serialize(offer);

            var offerResponse = await client.PostAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer",
                new StringContent(offerJson, Encoding.UTF8, "application/json"));

            if (!offerResponse.IsSuccessStatusCode)
            {
                var error = await offerResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to create offer: {Status} — {Error}", offerResponse.StatusCode, error);
                await SaveListingError(card.Id, options, $"Offer creation failed: {offerResponse.StatusCode}");
                return false;
            }

            var offerResponseJson = await offerResponse.Content.ReadAsStringAsync();
            var offerDoc = JsonDocument.Parse(offerResponseJson);
            var offerId = offerDoc.RootElement.GetProperty("offerId").GetString()!;

            // Publish offer
            var publishResponse = await client.PostAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer/{offerId}/publish",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            string ebayItemId;
            if (publishResponse.IsSuccessStatusCode)
            {
                var publishJson = await publishResponse.Content.ReadAsStringAsync();
                var publishDoc = JsonDocument.Parse(publishJson);
                ebayItemId = publishDoc.RootElement.GetProperty("listingId").GetString()!;
            }
            else
            {
                var error = await publishResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to publish offer: {Status} — {Error}", publishResponse.StatusCode, error);
                await SaveListingError(card.Id, options, $"Publish failed: {publishResponse.StatusCode}");
                return false;
            }

            // Save listing record
            using var ctx = _dbContextFactory.CreateDbContext();
            ctx.EbayListings.Add(new EbayListing
            {
                CollectionCardId = card.Id,
                EbayItemId = ebayItemId,
                Status = EbayListingStatus.Active,
                ListingType = options.ListingType,
                ListedPrice = options.Price,
                StartTime = DateTime.UtcNow,
                AuctionDuration = options.AuctionDuration,
            });
            await ctx.SaveChangesAsync();

            _logger.LogInformation("Created eBay listing {ItemId} for card {CardId} ({CardName})",
                ebayItemId, card.Id, card.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create eBay listing for card {CardId}", card.Id);
            return false;
        }
    }

    public async Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var sku = $"omnicard-{listing.CollectionCardId}";
            var inventoryItem = BuildInventoryItem(null, options);
            var inventoryJson = JsonSerializer.Serialize(inventoryItem);

            var response = await client.PutAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                new StringContent(inventoryJson, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to revise listing {ItemId}: {Status}", listing.EbayItemId, response.StatusCode);
                return false;
            }

            using var ctx = _dbContextFactory.CreateDbContext();
            var tracked = await ctx.EbayListings.FindAsync(listing.Id);
            if (tracked is not null)
            {
                tracked.ListedPrice = options.Price;
                tracked.LastSyncedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }

            _logger.LogInformation("Revised eBay listing {ItemId}", listing.EbayItemId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revise eBay listing {ItemId}", listing.EbayItemId);
            return false;
        }
    }

    public async Task<bool> EndListingAsync(EbayListing listing)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var sku = $"omnicard-{listing.CollectionCardId}";

            var response = await client.DeleteAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}");

            // 204 No Content = success, 404 = already ended
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Failed to end listing {ItemId}: {Status}", listing.EbayItemId, response.StatusCode);
                return false;
            }

            using var ctx = _dbContextFactory.CreateDbContext();
            var tracked = await ctx.EbayListings.FindAsync(listing.Id);
            if (tracked is not null)
            {
                tracked.Status = EbayListingStatus.Ended;
                tracked.EndTime = DateTime.UtcNow;
                tracked.LastSyncedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }

            _logger.LogInformation("Ended eBay listing {ItemId}", listing.EbayItemId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end eBay listing {ItemId}", listing.EbayItemId);
            return false;
        }
    }

    public async Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return [];

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync(
                $"{_settings.ApiBaseUrl}/sell/account/v1/{policyType}_policy?marketplace_id=EBAY_US");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch {PolicyType} policies: {Status}", policyType, response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var results = new List<EbaySellerPolicy>();
            var arrayProp = $"{policyType}Policies";
            if (!doc.RootElement.TryGetProperty(arrayProp, out var policies))
                return results;

            var idProp = $"{policyType}PolicyId";
            foreach (var policy in policies.EnumerateArray())
            {
                results.Add(new EbaySellerPolicy
                {
                    PolicyId = policy.GetProperty(idProp).GetString() ?? "",
                    Name = policy.GetProperty("name").GetString() ?? "",
                    PolicyType = policyType,
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch {PolicyType} policies", policyType);
            return [];
        }
    }

    private static object BuildInventoryItem(CollectionCard? card, EbayListingOptions options)
    {
        var conditionEnum = ConditionMap.GetValueOrDefault(options.Condition, 3000);

        return new
        {
            availability = new
            {
                shipToLocationAvailability = new { quantity = 1 }
            },
            condition = conditionEnum switch
            {
                3000 => "NEW_OTHER",
                4000 => "USED_GOOD",
                5000 => "USED_ACCEPTABLE",
                6000 => "USED_ACCEPTABLE",
                7000 => "FOR_PARTS_OR_NOT_WORKING",
                _ => "NEW_OTHER",
            },
            conditionDescription = options.Condition switch
            {
                "NM" => "Near Mint",
                "LP" => "Lightly Played",
                "MP" => "Moderately Played",
                "HP" => "Heavily Played",
                "D" => "Damaged",
                _ => options.Condition,
            },
            product = new
            {
                title = options.Title,
                description = options.Description,
            },
        };
    }

    private object BuildOffer(string sku, EbayListingOptions options)
    {
        return new
        {
            sku,
            marketplaceId = "EBAY_US",
            format = options.ListingType == EbayListingType.Auction ? "AUCTION" : "FIXED_PRICE",
            listingDescription = options.Description,
            pricingSummary = new
            {
                price = new { value = options.Price.ToString("F2"), currency = "USD" },
                auctionStartPrice = options.ListingType == EbayListingType.Auction
                    ? new { value = options.Price.ToString("F2"), currency = "USD" }
                    : null,
            },
            listingDuration = options.ListingType == EbayListingType.Auction && options.AuctionDuration.HasValue
                ? $"DAYS_{options.AuctionDuration.Value}"
                : null,
            listingPolicies = new
            {
                fulfillmentPolicyId = options.ShippingPolicyId,
                returnPolicyId = options.ReturnPolicyId,
                paymentPolicyId = options.PaymentPolicyId,
            },
            categoryId = options.EbayCategoryId ?? "38292",
        };
    }

    private async Task SaveListingError(int cardId, EbayListingOptions options, string error)
    {
        try
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            var existing = await ctx.EbayListings.FirstOrDefaultAsync(l => l.CollectionCardId == cardId);
            if (existing is not null)
            {
                existing.Status = EbayListingStatus.Error;
                existing.ErrorMessage = error;
            }
            else
            {
                ctx.EbayListings.Add(new EbayListing
                {
                    CollectionCardId = cardId,
                    Status = EbayListingStatus.Error,
                    ListingType = options.ListingType,
                    ListedPrice = options.Price,
                    ErrorMessage = error,
                });
            }
            await ctx.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save listing error for card {CardId}", cardId);
        }
    }
}
```

- [ ] **Step 6: Register in DI**

In `OmniCard/App.xaml.cs`, add after the `IEbayCatalogService` registration:

```csharp
services.AddSingleton<IEbayListingService, EbayListingService>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --no-restore --filter "EbayListingServiceTests"`
Expected: 3 tests PASS

- [ ] **Step 8: Commit**

```bash
git add OmniCard/Models/EbayListingOptions.cs OmniCard/Models/EbaySellerPolicy.cs OmniCard/Services/EbayListingService.cs OmniCard.Tests/Services/EbayListingServiceTests.cs OmniCard/App.xaml.cs
git commit -m "feat: add EbayListingService for creating and managing eBay listings"
```

---

### Task 4: EbaySyncService — Periodic Status Synchronization

**Files:**
- Create: `OmniCard/Services/EbaySyncService.cs`
- Create: `OmniCard.Tests/Services/EbaySyncServiceTests.cs`
- Modify: `OmniCard/App.xaml.cs` (DI registration)

**Interfaces:**
- Consumes: `IEbayAuthService.GetAccessTokenAsync()`, `EbaySettings.ApiBaseUrl`, `IDbContextFactory<CollectionDbContext>`, `EbayListing` entity
- Produces: `IEbaySyncService` with methods: `Task<int> SyncAllActiveAsync()`, `Task SyncSingleAsync(EbayListing listing)`

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/EbaySyncServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Services;
using System.Net;
using System.Text.Json;

namespace OmniCard.Tests.Services;

public class EbaySyncServiceTests
{
    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        Environment = "sandbox",
    };

    private IDbContextFactory<CollectionDbContext> CreateInMemoryDbFactory()
    {
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(options);
    }

    [Fact]
    public async Task SyncAllActiveAsync_UpdatesSoldListings()
    {
        var dbFactory = CreateInMemoryDbFactory();

        using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Id = 1, Name = "Test Card", SetName = "Set", SetCode = "TST",
                Number = "1", Rarity = "Common", GameCardId = "test-1",
            });
            ctx.EbayListings.Add(new EbayListing
            {
                Id = 1, CollectionCardId = 1, EbayItemId = "ebay-sold-123",
                Status = EbayListingStatus.Active, ListedPrice = 10m,
            });
            ctx.SaveChanges();
        }

        // Simulate order found for this item
        var ordersJson = JsonSerializer.Serialize(new
        {
            orders = new[]
            {
                new
                {
                    orderId = "order-1",
                    buyer = new { username = "buyer123" },
                    lineItems = new[]
                    {
                        new { legacyItemId = "ebay-sold-123", total = new { value = "10.00", currency = "USD" } }
                    },
                    orderFulfillmentStatus = "FULFILLED",
                }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, ordersJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbaySyncService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbaySyncService>.Instance);

        var synced = await svc.SyncAllActiveAsync();

        Assert.Equal(1, synced);

        using var verifyCtx = dbFactory.CreateDbContext();
        var listing = verifyCtx.EbayListings.First(l => l.Id == 1);
        Assert.Equal(EbayListingStatus.Sold, listing.Status);
        Assert.Equal("buyer123", listing.BuyerUsername);
        Assert.Equal(10.00m, listing.SoldPrice);
    }

    [Fact]
    public async Task SyncAllActiveAsync_ReturnsZero_WhenNotConnected()
    {
        var dbFactory = CreateInMemoryDbFactory();
        var authService = new FakeEbayAuthService(null);
        var factory = new FakeHttpClientFactory(new FakeHttpHandler(HttpStatusCode.OK, "{}"));

        var svc = new EbaySyncService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbaySyncService>.Instance);

        var synced = await svc.SyncAllActiveAsync();
        Assert.Equal(0, synced);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --no-restore --filter "EbaySyncServiceTests"`
Expected: FAIL — `EbaySyncService` does not exist yet.

- [ ] **Step 3: Implement EbaySyncService**

Create `OmniCard/Services/EbaySyncService.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IEbaySyncService
{
    Task<int> SyncAllActiveAsync();
    Task SyncSingleAsync(EbayListing listing);
}

public class EbaySyncService : IEbaySyncService
{
    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _ebayAuthService;
    private readonly IDbContextFactory<CollectionDbContext> _dbContextFactory;
    private readonly ILogger<EbaySyncService> _logger;

    public EbaySyncService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService ebayAuthService,
        IDbContextFactory<CollectionDbContext> dbContextFactory,
        ILogger<EbaySyncService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _ebayAuthService = ebayAuthService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<int> SyncAllActiveAsync()
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return 0;

        using var ctx = _dbContextFactory.CreateDbContext();
        var activeListings = await ctx.EbayListings
            .Where(l => l.Status == EbayListingStatus.Active)
            .ToListAsync();

        if (activeListings.Count == 0)
            return 0;

        var soldItemIds = await FetchSoldItemIdsAsync(token);
        var syncedCount = 0;

        foreach (var listing in activeListings)
        {
            if (soldItemIds.TryGetValue(listing.EbayItemId, out var saleInfo))
            {
                listing.Status = EbayListingStatus.Sold;
                listing.SoldPrice = saleInfo.SoldPrice;
                listing.BuyerUsername = saleInfo.BuyerUsername;
                listing.EndTime = DateTime.UtcNow;
                listing.LastSyncedAt = DateTime.UtcNow;
                syncedCount++;
                _logger.LogInformation("Listing {ItemId} marked as sold to {Buyer} for {Price}",
                    listing.EbayItemId, saleInfo.BuyerUsername, saleInfo.SoldPrice);
            }
            else
            {
                listing.LastSyncedAt = DateTime.UtcNow;
            }
        }

        await ctx.SaveChangesAsync();
        _logger.LogInformation("eBay sync complete: {Synced} of {Total} active listings updated", syncedCount, activeListings.Count);
        return syncedCount;
    }

    public async Task SyncSingleAsync(EbayListing listing)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return;

        var soldItemIds = await FetchSoldItemIdsAsync(token);

        using var ctx = _dbContextFactory.CreateDbContext();
        var tracked = await ctx.EbayListings.FindAsync(listing.Id);
        if (tracked is null)
            return;

        if (soldItemIds.TryGetValue(tracked.EbayItemId, out var saleInfo))
        {
            tracked.Status = EbayListingStatus.Sold;
            tracked.SoldPrice = saleInfo.SoldPrice;
            tracked.BuyerUsername = saleInfo.BuyerUsername;
            tracked.EndTime = DateTime.UtcNow;
        }

        tracked.LastSyncedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();
    }

    private async Task<Dictionary<string, SaleInfo>> FetchSoldItemIdsAsync(string token)
    {
        var result = new Dictionary<string, SaleInfo>();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync(
                $"{_settings.ApiBaseUrl}/sell/fulfillment/v1/order?limit=50&orderBy=creationdate%20desc");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch eBay orders: {Status}", response.StatusCode);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("orders", out var orders))
                return result;

            foreach (var order in orders.EnumerateArray())
            {
                string? buyerUsername = null;
                if (order.TryGetProperty("buyer", out var buyer)
                    && buyer.TryGetProperty("username", out var username))
                {
                    buyerUsername = username.GetString();
                }

                if (!order.TryGetProperty("lineItems", out var lineItems))
                    continue;

                foreach (var lineItem in lineItems.EnumerateArray())
                {
                    if (lineItem.TryGetProperty("legacyItemId", out var itemIdElem))
                    {
                        var itemId = itemIdElem.GetString();
                        if (itemId is null) continue;

                        decimal? soldPrice = null;
                        if (lineItem.TryGetProperty("total", out var total)
                            && total.TryGetProperty("value", out var val)
                            && decimal.TryParse(val.GetString(), out var price))
                        {
                            soldPrice = price;
                        }

                        result.TryAdd(itemId, new SaleInfo(soldPrice, buyerUsername));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch sold items from eBay");
        }

        return result;
    }

    private record SaleInfo(decimal? SoldPrice, string? BuyerUsername);
}
```

- [ ] **Step 4: Register in DI**

In `OmniCard/App.xaml.cs`, add after the `IEbayListingService` registration:

```csharp
services.AddSingleton<IEbaySyncService, EbaySyncService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --no-restore --filter "EbaySyncServiceTests"`
Expected: 2 tests PASS

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/EbaySyncService.cs OmniCard.Tests/Services/EbaySyncServiceTests.cs OmniCard/App.xaml.cs
git commit -m "feat: add EbaySyncService for periodic eBay listing status sync"
```

---

### Task 5: Listing Dialog — ViewModel

**Files:**
- Create: `OmniCard/Views/EbayListing/EbayListingViewModel.cs`

**Interfaces:**
- Consumes: `IEbayCatalogService.SearchCatalogAsync()`, `IEbayCatalogService.GetMarketPriceAsync()`, `IEbayListingService.CreateListingAsync()`, `IEbayListingService.GetSellerPoliciesAsync()`, `CollectionCard`, `EbayListingOptions`
- Produces: `EbayListingViewModel` with properties: `CardName`, `SetInfo`, `Condition`, `IsFoil`, `PurchasePrice`, `ScanImagePath`, `ApiImageUri`, `Title`, `Description`, `ListingType`, `Price`, `AuctionDuration`, `IncludeScanImage`, `IncludeStockImage`, `CatalogMatches`, `MarketPrice`, `ShippingPolicies`, `ReturnPolicies`, `PaymentPolicies`, `SelectedShippingPolicy`, `SelectedReturnPolicy`, `SelectedPaymentPolicy`, `IsLoading`, `ErrorMessage`. Commands: `SearchCatalogCommand`, `CreateListingCommand`, `SaveDraftCommand`. Method: `LoadCard(CollectionCard card)`. Action: `CloseDialog`.

- [ ] **Step 1: Create the ViewModel**

Create `OmniCard/Views/EbayListing/EbayListingViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.EbayListing;

public sealed partial class EbayListingViewModel(
    IEbayCatalogService catalogService,
    IEbayListingService listingService,
    ILogger<EbayListingViewModel> logger) : ViewModel
{
    private CollectionCard _card = null!;

    public Action<bool?>? CloseDialog { get; set; }

    // --- Card info (read-only) ---
    [ObservableProperty] public partial string CardName { get; set; } = "";
    [ObservableProperty] public partial string SetInfo { get; set; } = "";
    [ObservableProperty] public partial string CardNumber { get; set; } = "";
    [ObservableProperty] public partial string Rarity { get; set; } = "";
    [ObservableProperty] public partial string SetCode { get; set; } = "";
    [ObservableProperty] public partial string Condition { get; set; } = "";
    [ObservableProperty] public partial bool IsFoil { get; set; }
    [ObservableProperty] public partial decimal? PurchasePrice { get; set; }
    [ObservableProperty] public partial string? ScanImagePath { get; set; }
    [ObservableProperty] public partial string? ApiImageUri { get; set; }

    // --- Listing configuration ---
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial EbayListingType ListingType { get; set; } = EbayListingType.FixedPrice;
    [ObservableProperty] public partial decimal Price { get; set; }
    [ObservableProperty] public partial int AuctionDuration { get; set; } = 7;
    [ObservableProperty] public partial bool IncludeScanImage { get; set; } = true;
    [ObservableProperty] public partial bool IncludeStockImage { get; set; } = true;
    [ObservableProperty] public partial string? EbayCategoryId { get; set; }

    public bool IsAuction => ListingType == EbayListingType.Auction;
    partial void OnListingTypeChanged(EbayListingType value) => OnPropertyChanged(nameof(IsAuction));

    // --- Catalog / Market ---
    public ObservableCollection<EbayCatalogMatch> CatalogMatches { get; } = [];
    [ObservableProperty] public partial EbayCatalogMatch? SelectedCatalogMatch { get; set; }
    [ObservableProperty] public partial EbayMarketPrice? MarketPrice { get; set; }
    [ObservableProperty] public partial bool IsSearchingCatalog { get; set; }

    // --- Seller policies ---
    public ObservableCollection<EbaySellerPolicy> ShippingPolicies { get; } = [];
    public ObservableCollection<EbaySellerPolicy> ReturnPolicies { get; } = [];
    public ObservableCollection<EbaySellerPolicy> PaymentPolicies { get; } = [];
    [ObservableProperty] public partial EbaySellerPolicy? SelectedShippingPolicy { get; set; }
    [ObservableProperty] public partial EbaySellerPolicy? SelectedReturnPolicy { get; set; }
    [ObservableProperty] public partial EbaySellerPolicy? SelectedPaymentPolicy { get; set; }

    // --- State ---
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public decimal? EstimatedProfit => MarketPrice is not null && PurchasePrice.HasValue
        ? MarketPrice.MedianPrice - PurchasePrice.Value
        : null;

    public void LoadCard(CollectionCard card)
    {
        _card = card;
        CardName = card.Name;
        SetInfo = card.SetName;
        SetCode = card.SetCode;
        CardNumber = card.Number;
        Rarity = card.Rarity;
        Condition = card.Condition;
        IsFoil = card.IsFoil;
        PurchasePrice = card.PurchasePrice;
        ScanImagePath = card.ScanImagePath;
        ApiImageUri = card.ImageUri;

        // Auto-generate title and description
        var foilStr = card.IsFoil ? " FOIL" : "";
        Title = $"MTG {card.Name} [{card.SetCode}] #{card.Number} {card.Condition}{foilStr}";
        Description = $"{card.Name} from {card.SetName} ({card.SetCode}) #{card.Number}.\n" +
                      $"Condition: {card.Condition}. {(card.IsFoil ? "Foil finish." : "")}";

        // Kick off catalog search and policy fetch
        _ = SearchCatalogCommand.ExecuteAsync(null);
        _ = LoadPoliciesAsync();
    }

    [RelayCommand]
    public async Task SearchCatalog()
    {
        IsSearchingCatalog = true;
        ErrorMessage = null;

        try
        {
            CatalogMatches.Clear();
            var results = await catalogService.SearchCatalogAsync(CardName, SetInfo, CardNumber);
            foreach (var match in results)
                CatalogMatches.Add(match);

            if (CatalogMatches.Count > 0)
            {
                SelectedCatalogMatch = CatalogMatches[0];
                EbayCategoryId = SelectedCatalogMatch.CategoryId;
            }

            // Fetch market price
            var marketPrice = await catalogService.GetMarketPriceAsync(
                $"{CardName} {SetInfo} {Condition}", Condition, IsFoil);

            MarketPrice = marketPrice;
            OnPropertyChanged(nameof(EstimatedProfit));

            if (marketPrice is not null && Price == 0)
                Price = marketPrice.MedianPrice;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Catalog search failed");
            ErrorMessage = "Failed to search eBay catalog.";
        }
        finally
        {
            IsSearchingCatalog = false;
        }
    }

    private async Task LoadPoliciesAsync()
    {
        try
        {
            var shipping = await listingService.GetSellerPoliciesAsync("fulfillment");
            var returns = await listingService.GetSellerPoliciesAsync("return");
            var payment = await listingService.GetSellerPoliciesAsync("payment");

            ShippingPolicies.Clear();
            foreach (var p in shipping) ShippingPolicies.Add(p);
            if (ShippingPolicies.Count > 0) SelectedShippingPolicy = ShippingPolicies[0];

            ReturnPolicies.Clear();
            foreach (var p in returns) ReturnPolicies.Add(p);
            if (ReturnPolicies.Count > 0) SelectedReturnPolicy = ReturnPolicies[0];

            PaymentPolicies.Clear();
            foreach (var p in payment) PaymentPolicies.Add(p);
            if (PaymentPolicies.Count > 0) SelectedPaymentPolicy = PaymentPolicies[0];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load seller policies");
        }
    }

    [RelayCommand]
    public async Task CreateListing()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = BuildOptions();
            var success = await listingService.CreateListingAsync(_card, options);

            if (success)
            {
                CloseDialog?.Invoke(true);
            }
            else
            {
                ErrorMessage = "Failed to create eBay listing. Check logs for details.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create listing");
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);

    private EbayListingOptions BuildOptions() => new()
    {
        ListingType = ListingType,
        Price = Price,
        AuctionDuration = IsAuction ? AuctionDuration : null,
        Condition = Condition,
        Title = Title,
        Description = Description,
        IncludeScanImage = IncludeScanImage,
        IncludeStockImage = IncludeStockImage,
        ShippingPolicyId = SelectedShippingPolicy?.PolicyId,
        ReturnPolicyId = SelectedReturnPolicy?.PolicyId,
        PaymentPolicyId = SelectedPaymentPolicy?.PolicyId,
        EbayCategoryId = EbayCategoryId,
    };
}
```

- [ ] **Step 2: Register ViewModel in DI**

In `OmniCard/App.xaml.cs`, add with the other transient ViewModel registrations:

```csharp
services.AddTransient<EbayListing.EbayListingViewModel>();
```

Add the using at the top of the DI section if needed:
```csharp
using OmniCard.Views.EbayListing;
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build --no-restore`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Views/EbayListing/EbayListingViewModel.cs OmniCard/App.xaml.cs
git commit -m "feat: add EbayListingViewModel for listing dialog"
```

---

### Task 6: Listing Dialog — View (XAML + Code-Behind)

**Files:**
- Create: `OmniCard/Views/EbayListing/EbayListingView.xaml`
- Create: `OmniCard/Views/EbayListing/EbayListingView.xaml.cs`
- Modify: `OmniCard/Services/DialogService.cs` (add `OpenEbayListingDialog`)
- Modify: `OmniCard/App.xaml.cs` (register View in DI)

**Interfaces:**
- Consumes: `EbayListingViewModel` (all properties/commands), `IView<EbayListingViewModel>` pattern
- Produces: `EbayListingView` window, `IDialogService.OpenEbayListingDialog(CollectionCard card)` returning `bool?`

- [ ] **Step 1: Create the View XAML**

Create `OmniCard/Views/EbayListing/EbayListingView.xaml`:

```xml
<Window x:Class="OmniCard.Views.EbayListing.EbayListingView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:OmniCard.Views.EbayListing"
        xmlns:helpers="clr-namespace:OmniCard.Helpers"
        xmlns:root="clr-namespace:OmniCard.Views.Root"
        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
        xmlns:sys="clr-namespace:System;assembly=System.Runtime"
        ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        WindowStyle="ToolWindow"
        d:DataContext="{d:DesignInstance {x:Type local:EbayListingView}}"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        Title="List on eBay" Height="750" Width="1000"
        Background="{DynamicResource MaterialDesign.Brush.Background}"
        TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
        TextElement.FontSize="13"
        FontFamily="{StaticResource AppFont}">

    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
    </Window.Resources>

    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="280"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- LEFT: Card Info -->
            <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                    CornerRadius="4" Padding="12" Margin="0,0,8,0">
                <StackPanel>
                    <!-- Card Image -->
                    <Image Source="{Binding ViewModel.ScanImagePath}"
                           MaxHeight="300" Stretch="Uniform" Margin="0,0,0,8"
                           RenderOptions.BitmapScalingMode="HighQuality"/>

                    <TextBlock Text="{Binding ViewModel.CardName}" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,4"/>
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                        <Border Width="14" Height="14" Margin="0,0,4,0"
                                helpers:SetSymbol.SetCode="{Binding ViewModel.SetCode}"
                                helpers:SetSymbol.Rarity="{Binding ViewModel.Rarity}"/>
                        <TextBlock Text="{Binding ViewModel.SetInfo}"
                                   Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                    </StackPanel>
                    <TextBlock Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}" Margin="0,0,0,2">
                        <Run Text="#"/><Run Text="{Binding ViewModel.CardNumber, Mode=OneWay}"/>
                        <Run Text=" · "/><Run Text="{Binding ViewModel.Rarity, Mode=OneWay}"/>
                    </TextBlock>
                    <TextBlock Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}" Margin="0,0,0,2">
                        <Run Text="Condition: "/><Run Text="{Binding ViewModel.Condition, Mode=OneWay}" FontWeight="SemiBold"/>
                        <Run Text="{Binding ViewModel.IsFoil, Mode=OneWay, StringFormat=' {0}', Converter={root:FoilToFinishConverter}}"/>
                    </TextBlock>
                    <TextBlock Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                               Visibility="{Binding ViewModel.PurchasePrice, Converter={root:NullToCollapsedConverter}}">
                        <Run Text="Purchase: $"/><Run Text="{Binding ViewModel.PurchasePrice, Mode=OneWay, StringFormat=F2}"/>
                    </TextBlock>
                </StackPanel>
            </Border>

            <!-- RIGHT: Listing Configuration -->
            <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto">
                <StackPanel>

                    <!-- Market Data -->
                    <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                            CornerRadius="4" Padding="12" Margin="0,0,0,8">
                        <StackPanel>
                            <TextBlock Text="Market Data" FontWeight="SemiBold" FontSize="14" Margin="0,0,0,8"/>

                            <ProgressBar IsIndeterminate="True"
                                         Visibility="{Binding ViewModel.IsSearchingCatalog, Converter={StaticResource BoolToVis}}"
                                         Margin="0,0,0,8"/>

                            <StackPanel Visibility="{Binding ViewModel.MarketPrice, Converter={root:NullToCollapsedConverter}}">
                                <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                                    <TextBlock Text="Median:" Width="80"
                                               Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                                    <TextBlock Text="{Binding ViewModel.MarketPrice.MedianPrice, StringFormat='${0:F2}'}" FontWeight="SemiBold"/>
                                </StackPanel>
                                <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                                    <TextBlock Text="Range:" Width="80"
                                               Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                                    <TextBlock>
                                        <Run Text="{Binding ViewModel.MarketPrice.LowPrice, StringFormat='${0:F2}', Mode=OneWay}"/>
                                        <Run Text=" — "/>
                                        <Run Text="{Binding ViewModel.MarketPrice.HighPrice, StringFormat='${0:F2}', Mode=OneWay}"/>
                                    </TextBlock>
                                </StackPanel>
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="Samples:" Width="80"
                                               Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                                    <TextBlock Text="{Binding ViewModel.MarketPrice.SampleCount}"/>
                                </StackPanel>
                            </StackPanel>

                            <Button Content="Refresh Market Data" Padding="8,4" Margin="0,8,0,0"
                                    HorizontalAlignment="Left"
                                    Command="{Binding ViewModel.SearchCatalogCommand}"
                                    Style="{StaticResource MaterialDesignFlatButton}"/>
                        </StackPanel>
                    </Border>

                    <!-- Pricing -->
                    <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                            CornerRadius="4" Padding="12" Margin="0,0,0,8">
                        <StackPanel>
                            <TextBlock Text="Pricing" FontWeight="SemiBold" FontSize="14" Margin="0,0,0,8"/>

                            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                <RadioButton Content="Buy It Now" GroupName="ListingType"
                                             IsChecked="{Binding ViewModel.ListingType, Converter={root:EnumBoolConverter}, ConverterParameter=FixedPrice}"
                                             Margin="0,0,16,0"/>
                                <RadioButton Content="Auction" GroupName="ListingType"
                                             IsChecked="{Binding ViewModel.ListingType, Converter={root:EnumBoolConverter}, ConverterParameter=Auction}"/>
                            </StackPanel>

                            <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                                <TextBlock Text="Price: $" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                <TextBox Width="100" Text="{Binding ViewModel.Price, UpdateSourceTrigger=LostFocus, StringFormat=F2}"/>
                            </StackPanel>

                            <StackPanel Orientation="Horizontal" Margin="0,4,0,0"
                                        Visibility="{Binding ViewModel.IsAuction, Converter={StaticResource BoolToVis}}">
                                <TextBlock Text="Duration:" VerticalAlignment="Center" Margin="0,0,8,0"/>
                                <ComboBox SelectedItem="{Binding ViewModel.AuctionDuration}" Width="80">
                                    <sys:Int32>1</sys:Int32>
                                    <sys:Int32>3</sys:Int32>
                                    <sys:Int32>5</sys:Int32>
                                    <sys:Int32>7</sys:Int32>
                                    <sys:Int32>10</sys:Int32>
                                </ComboBox>
                                <TextBlock Text=" days" VerticalAlignment="Center"/>
                            </StackPanel>
                        </StackPanel>
                    </Border>

                    <!-- Photos -->
                    <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                            CornerRadius="4" Padding="12" Margin="0,0,0,8">
                        <StackPanel>
                            <TextBlock Text="Photos" FontWeight="SemiBold" FontSize="14" Margin="0,0,0,8"/>
                            <CheckBox Content="Include scan image" IsChecked="{Binding ViewModel.IncludeScanImage}" Margin="0,0,0,4"/>
                            <CheckBox Content="Include stock image" IsChecked="{Binding ViewModel.IncludeStockImage}"/>
                        </StackPanel>
                    </Border>

                    <!-- Details -->
                    <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                            CornerRadius="4" Padding="12" Margin="0,0,0,8">
                        <StackPanel>
                            <TextBlock Text="Listing Details" FontWeight="SemiBold" FontSize="14" Margin="0,0,0,8"/>
                            <TextBlock Text="Title" FontSize="11"
                                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}" Margin="0,0,0,4"/>
                            <TextBox Text="{Binding ViewModel.Title, UpdateSourceTrigger=PropertyChanged}"
                                     MaxLength="80" Margin="0,0,0,8"/>
                            <TextBlock Text="Description" FontSize="11"
                                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}" Margin="0,0,0,4"/>
                            <TextBox Text="{Binding ViewModel.Description, UpdateSourceTrigger=PropertyChanged}"
                                     AcceptsReturn="True" TextWrapping="Wrap" MinHeight="80"/>
                        </StackPanel>
                    </Border>

                    <!-- Policies -->
                    <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                            CornerRadius="4" Padding="12" Margin="0,0,0,8">
                        <StackPanel>
                            <TextBlock Text="Seller Policies" FontWeight="SemiBold" FontSize="14" Margin="0,0,0,8"/>
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                                <TextBlock Text="Shipping:" Width="80" VerticalAlignment="Center"
                                           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                                <ComboBox ItemsSource="{Binding ViewModel.ShippingPolicies}"
                                          SelectedItem="{Binding ViewModel.SelectedShippingPolicy}"
                                          DisplayMemberPath="Name" Width="250"/>
                            </StackPanel>
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                                <TextBlock Text="Returns:" Width="80" VerticalAlignment="Center"
                                           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                                <ComboBox ItemsSource="{Binding ViewModel.ReturnPolicies}"
                                          SelectedItem="{Binding ViewModel.SelectedReturnPolicy}"
                                          DisplayMemberPath="Name" Width="250"/>
                            </StackPanel>
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="Payment:" Width="80" VerticalAlignment="Center"
                                           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
                                <ComboBox ItemsSource="{Binding ViewModel.PaymentPolicies}"
                                          SelectedItem="{Binding ViewModel.SelectedPaymentPolicy}"
                                          DisplayMemberPath="Name" Width="250"/>
                            </StackPanel>
                        </StackPanel>
                    </Border>

                    <!-- Error -->
                    <TextBlock Text="{Binding ViewModel.ErrorMessage}" Foreground="Red" TextWrapping="Wrap"
                               Visibility="{Binding ViewModel.ErrorMessage, Converter={root:NullToCollapsedConverter}}"
                               Margin="0,0,0,8"/>
                </StackPanel>
            </ScrollViewer>
        </Grid>

        <!-- Bottom Action Bar -->
        <Grid Grid.Row="1" Margin="0,8,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <ProgressBar IsIndeterminate="True"
                         Visibility="{Binding ViewModel.IsLoading, Converter={StaticResource BoolToVis}}"
                         VerticalAlignment="Center" Margin="0,0,12,0"/>

            <StackPanel Grid.Column="1" Orientation="Horizontal">
                <Button Content="Cancel" Command="{Binding ViewModel.CancelCommand}"
                        Padding="16,6" Margin="0,0,8,0"/>
                <Button Content="List on eBay" Command="{Binding ViewModel.CreateListingCommand}"
                        Padding="16,6" FontWeight="SemiBold"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `OmniCard/Views/EbayListing/EbayListingView.xaml.cs`:

```csharp
using System.Windows;
using OmniCard.Views;

namespace OmniCard.Views.EbayListing;

public partial class EbayListingView : Window, IView<EbayListingViewModel>
{
    public EbayListingView(EbayListingViewModel viewModel)
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

    public EbayListingViewModel ViewModel { get; }
    IViewModel IView.ViewModel => ViewModel;
}
```

- [ ] **Step 3: Add OpenEbayListingDialog to IDialogService and DialogService**

In `OmniCard/Services/DialogService.cs`, add to the `IDialogService` interface:

```csharp
    bool? OpenEbayListingDialog(CollectionCard card);
```

Add the implementation in `DialogService`:

```csharp
    public bool? OpenEbayListingDialog(CollectionCard card)
    {
        var wnd = Services.GetRequiredService<EbayListingView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.LoadCard(card);
        return wnd.ShowDialog();
    }
```

Add the using at the top:
```csharp
using OmniCard.Views.EbayListing;
```

- [ ] **Step 4: Register View in DI**

In `OmniCard/App.xaml.cs`, add with the other transient View registrations:

```csharp
services.AddTransient<EbayListing.EbayListingView>();
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build --no-restore`
Expected: 0 errors (XAML binding errors may appear at runtime but won't block compilation — some converters like `EnumBoolConverter` and `NullToCollapsedConverter` may need to be checked against existing converters in the project. If they don't exist, create simple `IValueConverter` implementations in `OmniCard/Views/Root/Converters.cs` or use existing alternatives.)

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/EbayListing/ OmniCard/Services/DialogService.cs OmniCard/App.xaml.cs
git commit -m "feat: add eBay listing dialog with catalog search and market pricing"
```

---

### Task 7: Collection Integration — Context Menu, Grid Column, and Sync Timer

**Files:**
- Modify: `OmniCard/Views/Root/CardListView.xaml` (add context menu items + grid column)
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` (add commands, column visibility)
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (add sync timer)

**Interfaces:**
- Consumes: `IDialogService.OpenEbayListingDialog()`, `IEbayListingService.EndListingAsync()`, `IEbaySyncService.SyncAllActiveAsync()`, `CollectionCard.EbayListing` navigation, `EbayListingStatus`
- Produces: `ListOnEbayCommand`, `ViewOnEbayCommand`, `EndEbayListingCommand` on `CollectionViewModel`. eBay Status column in grid. Sync timer in `RootViewModel`.

- [ ] **Step 1: Add eBay commands to CollectionViewModel**

In `OmniCard/Views/Root/CollectionViewModel.cs`, add these fields and commands. Add `IEbayListingService` and `IEbaySyncService` as constructor parameters and store them as fields:

```csharp
private readonly IEbayListingService _ebayListingService;
private readonly IEbaySyncService _ebaySyncService;
```

Add commands:

```csharp
    [RelayCommand]
    public void ListOnEbay()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null || selected.Count != 1) return;
        var card = selected[0];
        if (card.EbayListing?.Status == EbayListingStatus.Active) return;

        var result = _dialogService.OpenEbayListingDialog(card);
        if (result == true)
        {
            ReportMessage?.Invoke($"Listed \"{card.Name}\" on eBay.");
            _ = SearchCollection();
        }
    }

    [RelayCommand]
    public void ViewOnEbay()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null || selected.Count != 1) return;
        var listing = selected[0].EbayListing;
        if (listing is null || string.IsNullOrEmpty(listing.EbayItemId)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = $"https://www.ebay.com/itm/{listing.EbayItemId}",
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    public async Task EndEbayListing()
    {
        var selected = GetSelectedCards?.Invoke();
        if (selected is null || selected.Count != 1) return;
        var listing = selected[0].EbayListing;
        if (listing is null || listing.Status != EbayListingStatus.Active) return;

        var result = System.Windows.MessageBox.Show(
            $"End the eBay listing for \"{selected[0].Name}\"?",
            "End Listing",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        var success = await _ebayListingService.EndListingAsync(listing);
        if (success)
        {
            ReportMessage?.Invoke($"Ended eBay listing for \"{selected[0].Name}\".");
            _ = SearchCollection();
        }
        else
        {
            ReportMessage?.Invoke("Failed to end eBay listing.");
        }
    }
```

- [ ] **Step 2: Add eBay Status column visibility**

In the `AllColumns` dictionary in `CollectionViewModel.cs`, add:

```csharp
        ["EbayStatus"] = false,
```

- [ ] **Step 3: Add eBay context menu items to CardListView.xaml**

In `OmniCard/Views/Root/CardListView.xaml`, add before the "Delete Selected" separator (after the "Set Non-Foil" MenuItem, before line 164's separator):

```xml
                <Separator/>
                <MenuItem Header="List on eBay"
                          Command="{Binding ListOnEbayCommand}"/>
                <MenuItem Header="View on eBay"
                          Command="{Binding ViewOnEbayCommand}"/>
                <MenuItem Header="End eBay Listing"
                          Command="{Binding EndEbayListingCommand}"/>
```

- [ ] **Step 4: Add eBay Status column to the grid**

In `OmniCard/Views/Root/CardListView.xaml`, add after the SetCode column (before line 139 `</DataGrid.Columns>`):

```xml
            <DataGridTextColumn Header="eBay"
                                local:ColumnTag.Key="EbayStatus"
                                Binding="{Binding EbayListing.Status}"
                                Width="70"
                                CanUserSort="False"/>
```

- [ ] **Step 5: Add sync timer to RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, add a field and update `Initialize()`:

Add field in the class body:

```csharp
    private System.Windows.Threading.DispatcherTimer? _ebaySyncTimer;
```

Add to the end of the `Initialize()` method (after `RefreshDiagnosticCount()`):

```csharp
        // Start eBay listing sync timer (every 5 minutes)
        if (ebayAuthService.IsConnected)
        {
            _ebaySyncTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5),
            };
            _ebaySyncTimer.Tick += async (_, _) => await SyncEbayListings();
            _ebaySyncTimer.Start();

            // Initial sync
            _ = SyncEbayListings();
        }
```

Add the sync method:

```csharp
    private IEbaySyncService EbaySyncService => App.Host.Services.GetRequiredService<IEbaySyncService>();

    private async Task SyncEbayListings()
    {
        try
        {
            var synced = await EbaySyncService.SyncAllActiveAsync();
            if (synced > 0)
            {
                Message = $"eBay sync: {synced} listing(s) updated.";
                _ = Collection.SearchCollection();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "eBay sync failed");
        }
    }
```

- [ ] **Step 6: Update SearchCollection to include EbayListing navigation**

In `OmniCard/Services/CardSevice.cs`, find the `BuildFilteredQuery` method and ensure the query includes `.Include(c => c.EbayListing)` alongside the existing `.Include(c => c.Container)`.

- [ ] **Step 7: Build and verify**

Run: `dotnet build --no-restore`
Expected: 0 errors

- [ ] **Step 8: Commit**

```bash
git add OmniCard/Views/Root/CardListView.xaml OmniCard/Views/Root/CollectionViewModel.cs OmniCard/Views/Root/RootViewModel.cs OmniCard/Services/CardSevice.cs
git commit -m "feat: add eBay context menu, grid column, and sync timer"
```

---

### Task 8: Card Editor — eBay Status Section

**Files:**
- Modify: `OmniCard/Views/CollectionCardEditor/CollectionCardEditorViewModel.cs` (add eBay properties)
- Modify: `OmniCard/Views/CollectionCardEditor/CollectionCardEditorView.xaml` (add eBay section)

**Interfaces:**
- Consumes: `CollectionCard.EbayListing` navigation, `IDialogService.OpenEbayListingDialog()`, `IEbayListingService.EndListingAsync()`, `EbayListingStatus`
- Produces: eBay status section in card editor showing status, listing actions, and sold info

- [ ] **Step 1: Add eBay properties to CollectionCardEditorViewModel**

In `OmniCard/Views/CollectionCardEditor/CollectionCardEditorViewModel.cs`, add observable properties:

```csharp
    [ObservableProperty] public partial EbayListingStatus? EbayStatus { get; set; }
    [ObservableProperty] public partial string? EbayItemId { get; set; }
    [ObservableProperty] public partial decimal? EbayListedPrice { get; set; }
    [ObservableProperty] public partial decimal? EbaySoldPrice { get; set; }
    [ObservableProperty] public partial string? EbayBuyerUsername { get; set; }

    public bool HasEbayListing => EbayStatus.HasValue;
    public bool IsEbayActive => EbayStatus == EbayListingStatus.Active;
    public bool IsEbaySold => EbayStatus == EbayListingStatus.Sold;
    public bool CanListOnEbay => !IsEbayActive;

    public string EbayStatusDisplay => EbayStatus?.ToString() ?? "Not Listed";
```

In the existing `LoadCard()` method, add at the end:

```csharp
        // eBay listing info
        EbayStatus = card.EbayListing?.Status;
        EbayItemId = card.EbayListing?.EbayItemId;
        EbayListedPrice = card.EbayListing?.ListedPrice;
        EbaySoldPrice = card.EbayListing?.SoldPrice;
        EbayBuyerUsername = card.EbayListing?.BuyerUsername;
        OnPropertyChanged(nameof(HasEbayListing));
        OnPropertyChanged(nameof(IsEbayActive));
        OnPropertyChanged(nameof(IsEbaySold));
        OnPropertyChanged(nameof(CanListOnEbay));
        OnPropertyChanged(nameof(EbayStatusDisplay));
```

- [ ] **Step 2: Add eBay section to CollectionCardEditorView.xaml**

In `OmniCard/Views/CollectionCardEditor/CollectionCardEditorView.xaml`, add a new row definition in the main Grid (after the Properties row, before the Action Buttons row). Insert between the Properties border (Grid.Row="2") and the Action Buttons grid (Grid.Row="3"):

Add a new RowDefinition: `<RowDefinition Height="Auto"/>` for the eBay section, and shift Action Buttons to Grid.Row="4".

Add the eBay section:

```xml
        <!-- eBay Status -->
        <Border Grid.Row="3" Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                CornerRadius="4" Padding="10,8" Margin="0,0,0,8">
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <TextBlock Text="eBay" FontSize="11" FontWeight="SemiBold"
                           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                           VerticalAlignment="Center" Margin="0,0,8,0"/>
                <TextBlock Text="{Binding ViewModel.EbayStatusDisplay}" VerticalAlignment="Center" Margin="0,0,12,0"/>

                <TextBlock VerticalAlignment="Center" Margin="0,0,12,0"
                           Visibility="{Binding ViewModel.IsEbayActive, Converter={StaticResource BoolToVis}}">
                    <Run Text="$"/><Run Text="{Binding ViewModel.EbayListedPrice, Mode=OneWay, StringFormat=F2}"/>
                </TextBlock>

                <TextBlock VerticalAlignment="Center" Margin="0,0,12,0"
                           Visibility="{Binding ViewModel.IsEbaySold, Converter={StaticResource BoolToVis}}">
                    <Run Text="Sold $"/><Run Text="{Binding ViewModel.EbaySoldPrice, Mode=OneWay, StringFormat=F2}"/>
                    <Run Text=" to "/><Run Text="{Binding ViewModel.EbayBuyerUsername, Mode=OneWay}"/>
                </TextBlock>
            </StackPanel>
        </Border>
```

Update the Action Buttons grid to `Grid.Row="4"` (was `Grid.Row="3"`).

- [ ] **Step 3: Build and verify**

Run: `dotnet build --no-restore`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Views/CollectionCardEditor/CollectionCardEditorViewModel.cs OmniCard/Views/CollectionCardEditor/CollectionCardEditorView.xaml
git commit -m "feat: add eBay status section to card editor dialog"
```
