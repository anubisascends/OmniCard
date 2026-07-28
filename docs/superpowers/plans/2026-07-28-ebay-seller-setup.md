# eBay Seller Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a user-triggered, idempotent "Run eBay Setup" action that provisions the eBay seller account (Business Policies opt-in, inventory location, default payment/return/fulfillment policies) and wires the resulting IDs into the listing flow, so a listing actually publishes.

**Architecture:** Three new isolated units — a persisted settings model/service (`EbaySellingSettings` / `IEbaySellingSettingsService`), an orchestration service (`IEbaySellerSetupService`) that calls the eBay Account + Inventory APIs with idempotent detect-then-create steps, and an "eBay Selling" section in the existing Settings dialog. `EbayListingService` reads the stored `merchantLocationKey` + policy IDs.

**Tech Stack:** .NET 10 / C#, WPF + CommunityToolkit.Mvvm, xUnit, System.Text.Json, eBay Sell REST APIs (`sell/account/v1`, `sell/inventory/v1`).

## Global Constraints

- Target framework net10.0-windows; existing project layout (`OmniCard.Shared`, `OmniCard.eBay`, `OmniCard.Collection`, `OmniCard`, `OmniCard.Tests`).
- eBay marketplace is always `EBAY_US`; base URLs come from `EbaySettings.ApiBaseUrl` (sandbox vs production).
- `Content-Language: en-US` is required ONLY on `inventory_item` and `offer` calls (use the existing `JsonContent` helper in `EbayListingService`). Account-API and location calls use plain `application/json`.
- Persisted settings files live in `IDataPathService.DataDirectory`, JSON via `JsonSerializerOptions { WriteIndented = true }`, corrupt files fall back to defaults — mirror `OmniCard.Collection/SalesSettingsService.cs`.
- WPF dark-theme rule: every `TextBlock` needs an explicit `Foreground` (use `{DynamicResource MaterialDesign.Brush.Foreground}`), because implicit styles render near-black.
- The app locks its output DLLs while running. Before `dotnet test`, run `Get-Process OmniCard | Stop-Process -Force`.
- Run tests with: `dotnet test d:\source\repos\OmniCard\OmniCard.Tests\OmniCard.Tests.csproj --filter "FullyQualifiedName~<TestClass>" --nologo`.
- Commit after each task.

---

## File Structure

New:
- `OmniCard.Shared/Models/EbaySellingSettings.cs` — settings + result DTOs data.
- `OmniCard.Shared/Interfaces/IEbaySellingSettingsService.cs`
- `OmniCard.Shared/Interfaces/IEbaySellerSetupService.cs` (+ `EbaySetupResult`, `EbaySetupStep`, `EbaySetupStepStatus`)
- `OmniCard.Collection/EbaySellingSettingsService.cs`
- `OmniCard.eBay/EbaySellerSetupService.cs`
- `OmniCard/Views/Settings/EbaySellingSettingsViewModel.cs`
- `OmniCard/Views/Settings/EbaySellingSettingsView.xaml` (+ `.xaml.cs`)
- `OmniCard.Tests/Services/EbaySellingSettingsServiceTests.cs`
- `OmniCard.Tests/Services/EbaySellerSetupServiceTests.cs`

Modify:
- `OmniCard.eBay/EbayListingService.cs` — `merchantLocationKey` in `BuildOffer`, policy fallback, incomplete-setup guard, new dependency.
- `OmniCard.Tests/Services/EbayListingServiceTests.cs` — offer-location assertions.
- `OmniCard/Views/EbayListing/EbayListingViewModel.cs` — pre-flight setup guard.
- `OmniCard/Views/Settings/SettingsViewModel.cs` — host new section.
- `OmniCard/Views/Settings/SettingsView.xaml` — nav item + content.
- `OmniCard/App.xaml.cs` — DI registrations.

---

## Task 1: EbaySellingSettings model + settings service

**Files:**
- Create: `OmniCard.Shared/Models/EbaySellingSettings.cs`
- Create: `OmniCard.Shared/Interfaces/IEbaySellingSettingsService.cs`
- Create: `OmniCard.Collection/EbaySellingSettingsService.cs`
- Test: `OmniCard.Tests/Services/EbaySellingSettingsServiceTests.cs`

**Interfaces:**
- Produces:
  - `EbaySellingSettings` (POCO, see below) and `enum ReturnShippingPayer { Buyer, Seller }`.
  - `IEbaySellingSettingsService { EbaySellingSettings Get(); void Save(EbaySellingSettings settings); bool IsSetupComplete(); }`
  - `EbaySellingSettingsService(IDataPathService dataPathService)`.

- [ ] **Step 1: Write the model and interface**

`OmniCard.Shared/Models/EbaySellingSettings.cs`:
```csharp
namespace OmniCard.Models;

public enum ReturnShippingPayer { Buyer, Seller }

public class EbaySellingSettings
{
    // Location
    public string MerchantLocationKey { get; set; } = "omnicard-primary";
    public bool LocationProvisioned { get; set; }
    public string? LocationName { get; set; } = "OmniCard Primary";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; } // ISO 3166-1 alpha-2, e.g. "US"
    public string? Phone { get; set; }

    // Fulfillment (shipping) policy inputs
    public bool FreeShipping { get; set; } = true;
    public decimal ShippingCost { get; set; }
    public int HandlingTimeDays { get; set; } = 1;

    // Return policy inputs
    public bool ReturnsAccepted { get; set; } = true;
    public int ReturnWindowDays { get; set; } = 30;
    public ReturnShippingPayer ReturnShippingPaidBy { get; set; } = ReturnShippingPayer.Buyer;

    // Results (written by setup)
    public string? FulfillmentPolicyId { get; set; }
    public string? PaymentPolicyId { get; set; }
    public string? ReturnPolicyId { get; set; }
    public System.DateTime? SetupCompletedAt { get; set; }
}
```

`OmniCard.Shared/Interfaces/IEbaySellingSettingsService.cs`:
```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IEbaySellingSettingsService
{
    EbaySellingSettings Get();
    void Save(EbaySellingSettings settings);

    /// <summary>Location provisioned and fulfillment + return policies exist
    /// (payment policy is optional — sandbox managed-payments may block it).</summary>
    bool IsSetupComplete();
}
```

- [ ] **Step 2: Write the failing tests**

`OmniCard.Tests/Services/EbaySellingSettingsServiceTests.cs`:
```csharp
using System.IO;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class EbaySellingSettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnicard-ebaysel-" + Guid.NewGuid().ToString("N"));

    public EbaySellingSettingsServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeDataPath : IDataPathService
    {
        private readonly string _dir;
        public FakeDataPath(string dir) => _dir = dir;
        public string DataDirectory => _dir;
        public string ScansDirectory => _dir;
        public string TempScansDirectory => _dir;
        public string SymbolsCacheDirectory => _dir;
        public string LogsDirectory => _dir;
        public string? PendingDataDirectory => null;
        public void SetPendingDataDirectory(string path) { }
        public void ClearPendingDataDirectory() { }
    }

    private EbaySellingSettingsService Create() => new(new FakeDataPath(_dir));

    [Fact]
    public void Get_ReturnsDefaults_WhenNoFile()
    {
        var s = Create().Get();
        Assert.Equal("omnicard-primary", s.MerchantLocationKey);
        Assert.True(s.FreeShipping);
        Assert.Equal(30, s.ReturnWindowDays);
    }

    [Fact]
    public void SaveThenGet_RoundTrips()
    {
        var svc = Create();
        var s = svc.Get();
        s.AddressLine1 = "1 Main St";
        s.Country = "US";
        s.PostalCode = "97201";
        s.FulfillmentPolicyId = "fp-1";
        svc.Save(s);

        var reloaded = Create().Get();
        Assert.Equal("1 Main St", reloaded.AddressLine1);
        Assert.Equal("US", reloaded.Country);
        Assert.Equal("fp-1", reloaded.FulfillmentPolicyId);
    }

    [Fact]
    public void IsSetupComplete_TrueOnlyWhenLocationAndCorePoliciesPresent()
    {
        var svc = Create();
        Assert.False(svc.IsSetupComplete());

        var s = svc.Get();
        s.LocationProvisioned = true;
        s.FulfillmentPolicyId = "fp-1";
        s.ReturnPolicyId = "rp-1";
        svc.Save(s);
        Assert.True(svc.IsSetupComplete());
    }

    [Fact]
    public void Get_FallsBackToDefaults_WhenFileCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "ebay-selling.json"), "{ not json");
        var s = Create().Get();
        Assert.Equal("omnicard-primary", s.MerchantLocationKey);
    }
}
```

> NOTE: match the real `IDataPathService` member list — open `OmniCard.Shared/Interfaces/IDataPathService.cs` and implement every member on `FakeDataPath` (the ones above mirror `OmniCard.Data/DataPathService.cs`; adjust if the interface differs).

- [ ] **Step 3: Run tests, verify they fail**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbaySellingSettingsServiceTests" --nologo`
Expected: FAIL (type `EbaySellingSettingsService` does not exist).

- [ ] **Step 4: Implement the service**

`OmniCard.Collection/EbaySellingSettingsService.cs`:
```csharp
using System.IO;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class EbaySellingSettingsService : IEbaySellingSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public EbaySellingSettingsService(IDataPathService dataPathService)
        => _filePath = Path.Combine(dataPathService.DataDirectory, "ebay-selling.json");

    public EbaySellingSettings Get()
    {
        if (!File.Exists(_filePath))
            return new EbaySellingSettings();
        try
        {
            return JsonSerializer.Deserialize<EbaySellingSettings>(File.ReadAllText(_filePath), JsonOptions)
                   ?? new EbaySellingSettings();
        }
        catch (JsonException)
        {
            return new EbaySellingSettings();
        }
    }

    public void Save(EbaySellingSettings settings)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));

    public bool IsSetupComplete()
    {
        var s = Get();
        return s.LocationProvisioned
            && !string.IsNullOrEmpty(s.FulfillmentPolicyId)
            && !string.IsNullOrEmpty(s.ReturnPolicyId);
    }
}
```

- [ ] **Step 5: Run tests, verify pass**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbaySellingSettingsServiceTests" --nologo`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/EbaySellingSettings.cs OmniCard.Shared/Interfaces/IEbaySellingSettingsService.cs OmniCard.Collection/EbaySellingSettingsService.cs OmniCard.Tests/Services/EbaySellingSettingsServiceTests.cs
git commit -m "feat(ebay): EbaySellingSettings model + persistence service"
```

---

## Task 2: Setup service — types, opt-in, and location steps

**Files:**
- Create: `OmniCard.Shared/Interfaces/IEbaySellerSetupService.cs`
- Create: `OmniCard.eBay/EbaySellerSetupService.cs`
- Test: `OmniCard.Tests/Services/EbaySellerSetupServiceTests.cs`

**Interfaces:**
- Consumes: `IEbaySellingSettingsService` (Task 1), `IEbayAuthService`, `IHttpClientFactory`, `ILogger`, `IOptions<EbaySettings>`.
- Produces:
  - `enum EbaySetupStepStatus { Ok, SkippedExisting, Failed }`
  - `record EbaySetupStep(string Name, EbaySetupStepStatus Status, string? Message)`
  - `class EbaySetupResult { public List<EbaySetupStep> Steps; public bool Success; }`
  - `IEbaySellerSetupService { Task<EbaySetupResult> RunSetupAsync(IProgress<string>? progress = null); }`
  - internal methods used by Task 3: `Task<EbaySetupStep> EnsureLocationAsync(HttpClient, EbaySellingSettings)`, `Task<EbaySetupStep> OptInAsync(HttpClient)`.

- [ ] **Step 1: Write the interface + DTOs**

`OmniCard.Shared/Interfaces/IEbaySellerSetupService.cs`:
```csharp
namespace OmniCard.Interfaces;

public enum EbaySetupStepStatus { Ok, SkippedExisting, Failed }

public record EbaySetupStep(string Name, EbaySetupStepStatus Status, string? Message);

public class EbaySetupResult
{
    public List<EbaySetupStep> Steps { get; } = [];
    public bool Success { get; set; }
}

public interface IEbaySellerSetupService
{
    Task<EbaySetupResult> RunSetupAsync(IProgress<string>? progress = null);
}
```

- [ ] **Step 2: Write failing tests for opt-in + location**

`OmniCard.Tests/Services/EbaySellerSetupServiceTests.cs`:
```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.eBay;

namespace OmniCard.Tests.Services;

public class EbaySellerSetupServiceTests
{
    private readonly EbaySettings _settings = new() { Environment = "sandbox", AppId = "a", CertId = "c" };

    // In-memory settings service.
    private sealed class MemSettings : IEbaySellingSettingsService
    {
        public EbaySellingSettings Current = new();
        public EbaySellingSettings Get() => Current;
        public void Save(EbaySellingSettings settings) => Current = settings;
        public bool IsSetupComplete() =>
            Current.LocationProvisioned && !string.IsNullOrEmpty(Current.FulfillmentPolicyId) && !string.IsNullOrEmpty(Current.ReturnPolicyId);
    }

    // Routes canned responses by (method, path substring). Records requests.
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = [];
        private readonly Func<HttpRequestMessage, string, (HttpStatusCode, string)> _route;
        public RoutingHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> route) => _route = route;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));
            var (code, resp) = _route(request, body ?? "");
            return new HttpResponseMessage(code) { Content = new StringContent(resp, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public SingleClientFactory(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name) => new(_h, disposeHandler: false);
    }

    private EbaySellerSetupService Create(RoutingHandler handler, MemSettings settings) =>
        new(Options.Create(_settings), new SingleClientFactory(handler),
            new FakeEbayAuthService("token"), settings, NullLogger<EbaySellerSetupService>.Instance);

    private static MemSettings WithValidAddress()
    {
        var m = new MemSettings();
        m.Current.AddressLine1 = "1 Main St";
        m.Current.City = "Portland";
        m.Current.State = "OR";
        m.Current.PostalCode = "97201";
        m.Current.Country = "US";
        return m;
    }

    [Fact]
    public async Task RunSetup_OptIn_And_CreatesLocation_WhenMissing()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.NotFound, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Post) return (HttpStatusCode.NoContent, "");
            // policies: return empty lists then create
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.Contains("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.Contains("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.Contains("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        Assert.True(result.Success);
        Assert.True(settings.Current.LocationProvisioned);
        // Location POST was made to the merchant key
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/location/omnicard-primary"));
    }

    [Fact]
    public async Task RunSetup_LocationStepFails_WhenAddressMissing()
    {
        var settings = new MemSettings(); // no address
        var handler = new RoutingHandler((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var loc = result.Steps.Single(s => s.Name == "Inventory location");
        Assert.Equal(EbaySetupStepStatus.Failed, loc.Status);
        Assert.False(result.Success);
        // No location POST attempted without required fields
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/location/"));
    }

    [Fact]
    public async Task RunSetup_SkipsLocationCreate_WhenAlreadyExists()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { merchantLocationKey = "omnicard-primary" }));
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.Contains("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.Contains("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.Contains("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var loc = result.Steps.Single(s => s.Name == "Inventory location");
        Assert.Equal(EbaySetupStepStatus.SkippedExisting, loc.Status);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/location/"));
    }
}
```

> `FakeEbayAuthService` already exists in `EbayCatalogServiceTests.cs` (same test namespace) — reuse it.

- [ ] **Step 3: Run tests, verify they fail**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbaySellerSetupServiceTests" --nologo`
Expected: FAIL (type `EbaySellerSetupService` not found).

- [ ] **Step 4: Implement the service scaffold + opt-in + location**

`OmniCard.eBay/EbaySellerSetupService.cs`:
```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.eBay;

public class EbaySellerSetupService : IEbaySellerSetupService
{
    private const string Marketplace = "EBAY_US";
    private const string PolicyName = "OmniCard Default";

    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _auth;
    private readonly IEbaySellingSettingsService _sellingSettings;
    private readonly ILogger<EbaySellerSetupService> _logger;

    public EbaySellerSetupService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService auth,
        IEbaySellingSettingsService sellingSettings,
        ILogger<EbaySellerSetupService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _auth = auth;
        _sellingSettings = sellingSettings;
        _logger = logger;
    }

    public async Task<EbaySetupResult> RunSetupAsync(IProgress<string>? progress = null)
    {
        var result = new EbaySetupResult();
        var token = await _auth.GetAccessTokenAsync();
        if (token is null)
        {
            result.Steps.Add(new EbaySetupStep("Authorization", EbaySetupStepStatus.Failed, "Not connected to eBay."));
            return result;
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var s = _sellingSettings.Get();

        progress?.Report("Opting into Business Policies…");
        result.Steps.Add(await OptInAsync(client));

        progress?.Report("Ensuring inventory location…");
        result.Steps.Add(await EnsureLocationAsync(client, s));

        // Policy steps are added by Task 3.
        await FinalizeAsync(client, s, result, progress);

        _sellingSettings.Save(s);
        return result;
    }

    // Task 3 replaces this stub with real policy steps + Success calculation.
    protected virtual Task FinalizeAsync(HttpClient client, EbaySellingSettings s, EbaySetupResult result, IProgress<string>? progress)
        => Task.CompletedTask;

    private async Task<EbaySetupStep> OptInAsync(HttpClient client)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { programType = "SELLING_POLICY_MANAGEMENT" });
            var resp = await client.PostAsync($"{_settings.ApiBaseUrl}/sell/account/v1/program/opt_in",
                new StringContent(body, Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
                return new EbaySetupStep("Business Policies opt-in", EbaySetupStepStatus.Ok, null);

            var err = await resp.Content.ReadAsStringAsync();
            // Already opted in is reported as an error by eBay; treat as existing.
            if (err.Contains("already opted in", StringComparison.OrdinalIgnoreCase))
                return new EbaySetupStep("Business Policies opt-in", EbaySetupStepStatus.SkippedExisting, null);

            _logger.LogWarning("Opt-in failed: {Status} — {Error}", resp.StatusCode, err);
            return new EbaySetupStep("Business Policies opt-in", EbaySetupStepStatus.Failed, $"{resp.StatusCode}: {err}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Opt-in threw");
            return new EbaySetupStep("Business Policies opt-in", EbaySetupStepStatus.Failed, ex.Message);
        }
    }

    private async Task<EbaySetupStep> EnsureLocationAsync(HttpClient client, EbaySellingSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.Country) || string.IsNullOrWhiteSpace(s.PostalCode)
            || string.IsNullOrWhiteSpace(s.AddressLine1) || string.IsNullOrWhiteSpace(s.City))
        {
            return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Failed,
                "Address is incomplete — set Address, City, Postal code and Country (ISO-2, e.g. US) in Settings ▸ eBay Selling.");
        }

        var key = Uri.EscapeDataString(s.MerchantLocationKey);
        var url = $"{_settings.ApiBaseUrl}/sell/inventory/v1/location/{key}";
        try
        {
            var existing = await client.GetAsync(url);
            if (existing.IsSuccessStatusCode)
            {
                s.LocationProvisioned = true;
                return new EbaySetupStep("Inventory location", EbaySetupStepStatus.SkippedExisting, null);
            }

            var payload = new
            {
                location = new
                {
                    address = new
                    {
                        addressLine1 = s.AddressLine1,
                        addressLine2 = s.AddressLine2,
                        city = s.City,
                        stateOrProvince = s.State,
                        postalCode = s.PostalCode,
                        country = s.Country,
                    }
                },
                name = string.IsNullOrWhiteSpace(s.LocationName) ? "OmniCard Primary" : s.LocationName,
                merchantLocationStatus = "ENABLED",
                locationTypes = new[] { "WAREHOUSE" },
                phone = s.Phone,
            };
            var resp = await client.PostAsync(url,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
            {
                s.LocationProvisioned = true;
                return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Ok, null);
            }

            var err = await resp.Content.ReadAsStringAsync();
            _logger.LogWarning("Create location failed: {Status} — {Error}", resp.StatusCode, err);
            return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Failed, $"{resp.StatusCode}: {err}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ensure location threw");
            return new EbaySetupStep("Inventory location", EbaySetupStepStatus.Failed, ex.Message);
        }
    }
}
```

> The tests in this task exercise the full `RunSetupAsync` including policy calls, so the routing handler already returns policy responses. Because `FinalizeAsync` is a stub here, `result.Success` is still `false` at end of Task 2 — the two location tests assert step status + settings, not `Success`, EXCEPT `RunSetup_OptIn_And_CreatesLocation_WhenMissing` asserts `result.Success`. **Move that `Assert.True(result.Success)` line to Task 3** (where Finalize sets Success); in Task 2 assert only `settings.Current.LocationProvisioned` and the location POST. Keep the two negative/skip tests as written.

- [ ] **Step 5: Run tests, verify pass**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbaySellerSetupServiceTests" --nologo`
Expected: PASS (after moving the `Success` assertion as noted).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/IEbaySellerSetupService.cs OmniCard.eBay/EbaySellerSetupService.cs OmniCard.Tests/Services/EbaySellerSetupServiceTests.cs
git commit -m "feat(ebay): seller setup service — opt-in + inventory location"
```

---

## Task 3: Setup service — policy creation + orchestration

**Files:**
- Modify: `OmniCard.eBay/EbaySellerSetupService.cs` (replace `FinalizeAsync` stub with real policy logic)
- Modify: `OmniCard.Tests/Services/EbaySellerSetupServiceTests.cs` (add policy tests; restore `Success` assertion)

**Interfaces:**
- Consumes: `EnsureLocationAsync`, `OptInAsync` (Task 2).
- Produces: private `Task<EbaySetupStep> EnsurePolicyAsync(HttpClient, EbaySellingSettings, string policyType)` used inside `FinalizeAsync`.

- [ ] **Step 1: Write failing policy tests**

Add to `EbaySellerSetupServiceTests`:
```csharp
    [Fact]
    public async Task RunSetup_CreatesPolicies_AndStoresIds()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Get) return (HttpStatusCode.NotFound, "{}");
            if (u.Contains("/location/") && req.Method == HttpMethod.Post) return (HttpStatusCode.NoContent, "");
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { fulfillmentPolicies = Array.Empty<object>() }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { returnPolicies = Array.Empty<object>() }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { paymentPolicies = Array.Empty<object>() }));
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { paymentPolicyId = "pp-1" }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        Assert.True(result.Success);
        Assert.Equal("fp-1", settings.Current.FulfillmentPolicyId);
        Assert.Equal("rp-1", settings.Current.ReturnPolicyId);
        Assert.Equal("pp-1", settings.Current.PaymentPolicyId);
        Assert.NotNull(settings.Current.SetupCompletedAt);
    }

    [Fact]
    public async Task RunSetup_ReusesExistingPolicy_ByName()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/")) return (req.Method == HttpMethod.Get) ? (HttpStatusCode.OK, JsonSerializer.Serialize(new { merchantLocationKey = "omnicard-primary" })) : (HttpStatusCode.NoContent, "");
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { fulfillmentPolicies = new[] { new { fulfillmentPolicyId = "fp-existing", name = "OmniCard Default" } } }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { returnPolicies = new[] { new { returnPolicyId = "rp-existing", name = "OmniCard Default" } } }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, JsonSerializer.Serialize(new { paymentPolicies = new[] { new { paymentPolicyId = "pp-existing", name = "OmniCard Default" } } }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        Assert.Equal("fp-existing", settings.Current.FulfillmentPolicyId);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.EndsWith("fulfillment_policy"));
    }

    [Fact]
    public async Task RunSetup_PaymentPolicyFailure_IsNonFatal()
    {
        var settings = WithValidAddress();
        var handler = new RoutingHandler((req, body) =>
        {
            var u = req.RequestUri!.AbsolutePath;
            if (u.Contains("/program/opt_in")) return (HttpStatusCode.OK, "{}");
            if (u.Contains("/location/")) return (req.Method == HttpMethod.Get) ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.NoContent, "");
            if (u.EndsWith("_policy") && req.Method == HttpMethod.Get) return (HttpStatusCode.OK, "{}");
            if (u.EndsWith("fulfillment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { fulfillmentPolicyId = "fp-1" }));
            if (u.EndsWith("return_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.Created, JsonSerializer.Serialize(new { returnPolicyId = "rp-1" }));
            if (u.EndsWith("payment_policy") && req.Method == HttpMethod.Post) return (HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { errors = new[] { new { message = "not eligible for managed payments" } } }));
            return (HttpStatusCode.OK, "{}");
        });
        var svc = Create(handler, settings);

        var result = await svc.RunSetupAsync();

        var pay = result.Steps.Single(s => s.Name == "Payment policy");
        Assert.Equal(EbaySetupStepStatus.Failed, pay.Status);
        Assert.True(result.Success); // location + fulfillment + return succeeded
        Assert.Null(settings.Current.PaymentPolicyId);
    }
```

Restore the `Assert.True(result.Success);` line to `RunSetup_OptIn_And_CreatesLocation_WhenMissing` (deferred from Task 2).

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbaySellerSetupServiceTests" --nologo`
Expected: FAIL (policies not created; `Success` false; ids null).

- [ ] **Step 3: Replace `FinalizeAsync` stub with real implementation**

In `EbaySellerSetupService.cs`, delete the `protected virtual Task FinalizeAsync(...)` stub and add:
```csharp
    private async Task FinalizeAsync(HttpClient client, EbaySellingSettings s, EbaySetupResult result, IProgress<string>? progress)
    {
        progress?.Report("Ensuring fulfillment (shipping) policy…");
        var fulfillment = await EnsurePolicyAsync(client, s, "fulfillment");
        result.Steps.Add(fulfillment);

        progress?.Report("Ensuring payment policy…");
        result.Steps.Add(await EnsurePolicyAsync(client, s, "payment"));

        progress?.Report("Ensuring return policy…");
        var ret = await EnsurePolicyAsync(client, s, "return");
        result.Steps.Add(ret);

        var locationOk = s.LocationProvisioned;
        result.Success = locationOk
            && !string.IsNullOrEmpty(s.FulfillmentPolicyId)
            && !string.IsNullOrEmpty(s.ReturnPolicyId);
        if (result.Success)
            s.SetupCompletedAt = DateTime.UtcNow;
    }

    private async Task<EbaySetupStep> EnsurePolicyAsync(HttpClient client, EbaySellingSettings s, string policyType)
    {
        var stepName = policyType switch
        {
            "fulfillment" => "Fulfillment (shipping) policy",
            "payment" => "Payment policy",
            "return" => "Return policy",
            _ => policyType,
        };
        try
        {
            // 1. Look for an existing policy named PolicyName.
            var listUrl = $"{_settings.ApiBaseUrl}/sell/account/v1/{policyType}_policy?marketplace_id={Marketplace}";
            var listResp = await client.GetAsync(listUrl);
            if (listResp.IsSuccessStatusCode)
            {
                var listJson = await listResp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(listJson);
                if (doc.RootElement.TryGetProperty($"{policyType}Policies", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in arr.EnumerateArray())
                    {
                        if (p.TryGetProperty("name", out var n) && n.GetString() == PolicyName
                            && p.TryGetProperty($"{policyType}PolicyId", out var idEl))
                        {
                            StorePolicyId(s, policyType, idEl.GetString());
                            return new EbaySetupStep(stepName, EbaySetupStepStatus.SkippedExisting, null);
                        }
                    }
                }
            }

            // 2. Create it.
            var payload = BuildPolicyPayload(policyType, s);
            var createResp = await client.PostAsync(
                $"{_settings.ApiBaseUrl}/sell/account/v1/{policyType}_policy",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (!createResp.IsSuccessStatusCode)
            {
                var err = await createResp.Content.ReadAsStringAsync();
                _logger.LogWarning("Create {PolicyType} policy failed: {Status} — {Error}", policyType, createResp.StatusCode, err);
                return new EbaySetupStep(stepName, EbaySetupStepStatus.Failed, $"{createResp.StatusCode}: {err}");
            }

            var createJson = await createResp.Content.ReadAsStringAsync();
            using var created = JsonDocument.Parse(createJson);
            var id = created.RootElement.TryGetProperty($"{policyType}PolicyId", out var cid) ? cid.GetString() : null;
            StorePolicyId(s, policyType, id);
            return new EbaySetupStep(stepName, EbaySetupStepStatus.Ok, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ensure {PolicyType} policy threw", policyType);
            return new EbaySetupStep(stepName, EbaySetupStepStatus.Failed, ex.Message);
        }
    }

    private static void StorePolicyId(EbaySellingSettings s, string policyType, string? id)
    {
        switch (policyType)
        {
            case "fulfillment": s.FulfillmentPolicyId = id; break;
            case "payment": s.PaymentPolicyId = id; break;
            case "return": s.ReturnPolicyId = id; break;
        }
    }

    private static object BuildPolicyPayload(string policyType, EbaySellingSettings s)
    {
        var categoryTypes = new[] { new { name = "ALL_EXCLUDING_MOTORS_VEHICLES" } };
        return policyType switch
        {
            "fulfillment" => new
            {
                name = PolicyName,
                marketplaceId = Marketplace,
                categoryTypes,
                handlingTime = new { unit = "DAY", value = s.HandlingTimeDays },
                shippingOptions = new[]
                {
                    new
                    {
                        costType = "FLAT_RATE",
                        optionType = "DOMESTIC",
                        shippingServices = new[]
                        {
                            new
                            {
                                sortOrder = 1,
                                shippingCarrierCode = "USPS",
                                shippingServiceCode = "USPSGround",
                                freeShipping = s.FreeShipping,
                                shippingCost = new { value = (s.FreeShipping ? 0m : s.ShippingCost).ToString("F2"), currency = "USD" },
                            }
                        }
                    }
                },
            },
            "payment" => new
            {
                name = PolicyName,
                marketplaceId = Marketplace,
                categoryTypes,
                paymentMethods = Array.Empty<object>(),
            },
            "return" => (object)new
            {
                name = PolicyName,
                marketplaceId = Marketplace,
                categoryTypes,
                returnsAccepted = s.ReturnsAccepted,
                returnPeriod = new { unit = "DAY", value = s.ReturnWindowDays },
                returnShippingCostPayer = s.ReturnShippingPaidBy == ReturnShippingPayer.Seller ? "SELLER" : "BUYER",
            },
            _ => new { name = PolicyName, marketplaceId = Marketplace, categoryTypes },
        };
    }
```

> When `returnsAccepted` is false eBay ignores `returnPeriod`/`returnShippingCostPayer`; sending them is harmless, so keep the payload uniform.

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbaySellerSetupServiceTests" --nologo`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.eBay/EbaySellerSetupService.cs OmniCard.Tests/Services/EbaySellerSetupServiceTests.cs
git commit -m "feat(ebay): seller setup service — business policies + orchestration"
```

---

## Task 4: Wire location + policies into the listing flow

**Files:**
- Modify: `OmniCard.eBay/EbayListingService.cs`
- Modify: `OmniCard/Views/EbayListing/EbayListingViewModel.cs`
- Test: `OmniCard.Tests/Services/EbayListingServiceTests.cs`

**Interfaces:**
- Consumes: `IEbaySellingSettingsService` (Task 1).
- Produces: `EbayListingService` constructor gains a 6th param `IEbaySellingSettingsService sellingSettings` (all call sites/tests must pass it).

- [ ] **Step 1: Write failing test — offer carries merchantLocationKey + policy fallback**

Add to `EbayListingServiceTests` (this test needs the recording handler from the existing file and a settings stub):
```csharp
    private sealed class StubSellingSettings : IEbaySellingSettingsService
    {
        private readonly EbaySellingSettings _s;
        public StubSellingSettings(EbaySellingSettings s) => _s = s;
        public EbaySellingSettings Get() => _s;
        public void Save(EbaySellingSettings settings) { }
        public bool IsSetupComplete() =>
            _s.LocationProvisioned && !string.IsNullOrEmpty(_s.FulfillmentPolicyId) && !string.IsNullOrEmpty(_s.ReturnPolicyId);
    }

    [Fact]
    public async Task CreateListingAsync_OfferIncludesMerchantLocationKey_AndStoredPolicies()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext()) lotId = SeedLot(ctx);

        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new { listingId = "L1" }));
        var selling = new StubSellingSettings(new EbaySellingSettings
        {
            MerchantLocationKey = "omnicard-primary",
            LocationProvisioned = true,
            FulfillmentPolicyId = "fp-1",
            ReturnPolicyId = "rp-1",
            PaymentPolicyId = "pp-1",
        });

        var svc = new EbayListingService(
            Options.Create(_settings), new FakeHttpClientFactory(handler),
            new FakeEbayAuthService("t"), dbFactory, selling,
            NullLogger<EbayListingService>.Instance);

        var options = new EbayListingOptions { Title = "t", Description = "d", Price = 5m, ListingType = EbayListingType.FixedPrice };
        var ok = await svc.CreateListingAsync(new CollectionCard { Id = lotId, Name = "n" }, options);

        Assert.True(ok);
        var offerReq = handler.Requests.First(r => r.Method == HttpMethod.Post && r.Uri!.ToString().EndsWith("/offer"));
        Assert.Contains("omnicard-primary", offerReq.Body);
        Assert.Contains("fp-1", offerReq.Body);
    }

    [Fact]
    public async Task CreateListingAsync_Fails_WhenSetupIncomplete()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext()) lotId = SeedLot(ctx);

        var handler = new RecordingHttpHandler(HttpStatusCode.OK, "{}");
        var selling = new StubSellingSettings(new EbaySellingSettings()); // not provisioned

        var svc = new EbayListingService(
            Options.Create(_settings), new FakeHttpClientFactory(handler),
            new FakeEbayAuthService("t"), dbFactory, selling,
            NullLogger<EbayListingService>.Instance);

        var ok = await svc.CreateListingAsync(new CollectionCard { Id = lotId, Name = "n" },
            new EbayListingOptions { Title = "t", Price = 5m });

        Assert.False(ok);
        Assert.Empty(handler.Requests.Where(r => r.Uri!.ToString().Contains("inventory_item")));
    }
```

> `RecordingHttpHandler.RecordedRequest` currently records `(Method, Uri, ContentLanguage)`. Add a `string? Body` to `RecordedRequest` and capture `request.Content?.ReadAsStringAsync()` in `SendAsync` (read synchronously via `.Result` is fine in the test handler, or make `SendAsync` async and await). Update the existing `Content-Language` test's record construction accordingly.

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbayListingServiceTests" --nologo`
Expected: FAIL (constructor arity; `merchantLocationKey`/guard absent).

- [ ] **Step 3: Implement service changes**

In `EbayListingService.cs`:

Add field + constructor param:
```csharp
    private readonly IEbaySellingSettingsService _sellingSettings;
```
Add `IEbaySellingSettingsService sellingSettings` as the 5th constructor parameter (before `ILogger`), assign `_sellingSettings = sellingSettings;`.

At the top of `CreateListingAsync`, after the token null-check, add the guard:
```csharp
        var selling = _sellingSettings.Get();
        if (!_sellingSettings.IsSetupComplete())
        {
            _logger.LogWarning("eBay listing blocked — seller setup incomplete for card {CardId}", card.Id);
            await SaveListingError(card.Id, options, "eBay setup incomplete — run Settings ▸ eBay Selling ▸ Run eBay Setup.");
            return false;
        }
```

Change `BuildOffer` to accept settings and emit `merchantLocationKey` + fallback policy IDs:
```csharp
    private object BuildOffer(string sku, EbayListingOptions options, EbaySellingSettings selling)
    {
        return new
        {
            sku,
            marketplaceId = "EBAY_US",
            format = options.ListingType == EbayListingType.Auction ? "AUCTION" : "FIXED_PRICE",
            listingDescription = options.Description,
            merchantLocationKey = selling.MerchantLocationKey,
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
                fulfillmentPolicyId = options.ShippingPolicyId ?? selling.FulfillmentPolicyId,
                returnPolicyId = options.ReturnPolicyId ?? selling.ReturnPolicyId,
                paymentPolicyId = options.PaymentPolicyId ?? selling.PaymentPolicyId,
            },
            categoryId = options.EbayCategoryId ?? "38292",
        };
    }
```
Update the `BuildOffer(sku, options)` call site in `CreateListingAsync` to `BuildOffer(sku, options, selling)`.

- [ ] **Step 4: Add the ViewModel pre-flight guard**

In `OmniCard/Views/EbayListing/EbayListingViewModel.cs`, inject `IEbaySellingSettingsService sellingSettings` (add to the primary constructor param list) and, at the start of the `CreateListing` command (before calling `listingService.CreateListingAsync`), add:
```csharp
        if (!sellingSettings.IsSetupComplete())
        {
            ErrorMessage = "eBay setup incomplete. Open Settings ▸ eBay Selling and click Run eBay Setup first.";
            return;
        }
```

- [ ] **Step 5: Run tests, verify pass**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbayListingServiceTests" --nologo`
Expected: PASS. Also re-run the full eBay filter to confirm no regressions:
`--filter "FullyQualifiedName~Ebay|FullyQualifiedName~CredentialStore"`

- [ ] **Step 6: Commit**

```bash
git add OmniCard.eBay/EbayListingService.cs OmniCard/Views/EbayListing/EbayListingViewModel.cs OmniCard.Tests/Services/EbayListingServiceTests.cs
git commit -m "feat(ebay): use merchant location + policies in listings, guard incomplete setup"
```

---

## Task 5: Settings UI section + DI wiring

**Files:**
- Create: `OmniCard/Views/Settings/EbaySellingSettingsViewModel.cs`
- Create: `OmniCard/Views/Settings/EbaySellingSettingsView.xaml` (+ `.xaml.cs`)
- Modify: `OmniCard/Views/Settings/SettingsViewModel.cs`
- Modify: `OmniCard/Views/Settings/SettingsView.xaml`
- Modify: `OmniCard/App.xaml.cs`
- Test: add `EbaySellingSettingsViewModelTests` to `OmniCard.Tests/Services/` (VM logic)

**Interfaces:**
- Consumes: `IEbaySellingSettingsService`, `IEbaySellerSetupService`, `IEbayAuthService`.

- [ ] **Step 1: Write failing VM test**

`OmniCard.Tests/Services/EbaySellingSettingsViewModelTests.cs`:
```csharp
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Settings;

namespace OmniCard.Tests.Services;

public class EbaySellingSettingsViewModelTests
{
    private sealed class MemSettings : IEbaySellingSettingsService
    {
        public EbaySellingSettings Current = new();
        public EbaySellingSettings Get() => Current;
        public void Save(EbaySellingSettings s) => Current = s;
        public bool IsSetupComplete() => Current.LocationProvisioned && !string.IsNullOrEmpty(Current.FulfillmentPolicyId) && !string.IsNullOrEmpty(Current.ReturnPolicyId);
    }
    private sealed class FakeSetup : IEbaySellerSetupService
    {
        public EbaySetupResult Result = new();
        public Task<EbaySetupResult> RunSetupAsync(IProgress<string>? p = null) => Task.FromResult(Result);
    }

    [Fact]
    public void Load_PopulatesFieldsFromSettings()
    {
        var settings = new MemSettings();
        settings.Current.City = "Portland";
        var vm = new EbaySellingSettingsViewModel(settings, new FakeSetup());
        vm.Load();
        Assert.Equal("Portland", vm.Settings.City);
    }

    [Fact]
    public async Task RunSetup_AppendsStepStatusesToStatusLog()
    {
        var setup = new FakeSetup();
        setup.Result.Steps.Add(new EbaySetupStep("Inventory location", EbaySetupStepStatus.Ok, null));
        setup.Result.Steps.Add(new EbaySetupStep("Payment policy", EbaySetupStepStatus.Failed, "not eligible"));
        setup.Result.Success = true;

        var vm = new EbaySellingSettingsViewModel(new MemSettings(), setup);
        vm.Load();
        await vm.RunSetupCommand.ExecuteAsync(null);

        Assert.Contains(vm.StatusLog, l => l.Contains("Inventory location") && l.Contains("OK"));
        Assert.Contains(vm.StatusLog, l => l.Contains("Payment policy") && l.Contains("not eligible"));
    }
}
```

- [ ] **Step 2: Run test, verify fail**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbaySellingSettingsViewModelTests" --nologo`
Expected: FAIL (type not found).

- [ ] **Step 3: Implement the ViewModel**

`OmniCard/Views/Settings/EbaySellingSettingsViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Settings;

public partial class EbaySellingSettingsViewModel(
    IEbaySellingSettingsService sellingSettings,
    IEbaySellerSetupService setupService) : ObservableObject
{
    [ObservableProperty]
    public partial EbaySellingSettings Settings { get; set; } = new();

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<string> StatusLog { get; } = [];

    public void Load() => Settings = sellingSettings.Get();

    [RelayCommand]
    public void Save()
    {
        sellingSettings.Save(Settings);
        StatusLog.Add("Saved.");
    }

    [RelayCommand]
    public async Task RunSetup()
    {
        sellingSettings.Save(Settings); // persist form before setup reads it
        IsBusy = true;
        StatusLog.Clear();
        var progress = new System.Progress<string>(m => StatusLog.Add(m));
        try
        {
            var result = await setupService.RunSetupAsync(progress);
            foreach (var step in result.Steps)
            {
                var status = step.Status switch
                {
                    EbaySetupStepStatus.Ok => "OK",
                    EbaySetupStepStatus.SkippedExisting => "already set up",
                    _ => "FAILED",
                };
                StatusLog.Add($"{step.Name}: {status}{(step.Message is null ? "" : " — " + step.Message)}");
            }
            StatusLog.Add(result.Success ? "eBay setup complete." : "Setup finished with errors — see above.");
            Settings = sellingSettings.Get(); // reflect stored IDs/flags
        }
        finally { IsBusy = false; }
    }
}
```

- [ ] **Step 4: Run test, verify pass**

Run: `dotnet test ...OmniCard.Tests.csproj --filter "FullyQualifiedName~EbaySellingSettingsViewModelTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Create the View**

`OmniCard/Views/Settings/EbaySellingSettingsView.xaml` — a `UserControl` with a scrolling `StackPanel`, mirroring `SalesSettingsView.xaml`. Every `TextBlock` needs `Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"`. Include:
- A header `TextBlock` "eBay Selling".
- `TextBox`es bound to `Settings.LocationName`, `AddressLine1`, `AddressLine2`, `City`, `State`, `PostalCode`, `Country`, `Phone` (each with a labelled `TextBlock`).
- `CheckBox` `IsChecked="{Binding Settings.FreeShipping}"` "Free shipping"; `TextBox` `Settings.ShippingCost`; `TextBox` `Settings.HandlingTimeDays`.
- `CheckBox` `Settings.ReturnsAccepted` "Accept returns"; `TextBox` `Settings.ReturnWindowDays`.
- Buttons: "Save" → `SaveCommand`; "Run eBay Setup" → `RunSetupCommand`, `IsEnabled="{Binding IsBusy, Converter=...}"` (or bind to a NotBusy property — add `public bool NotBusy => !IsBusy;` and raise it in `OnIsBusyChanged`).
- An `ItemsControl ItemsSource="{Binding StatusLog}"` rendering each line in a `TextBlock` with explicit `Foreground`.

`EbaySellingSettingsView.xaml.cs`: standard `InitializeComponent()` code-behind (copy the shape of `SalesSettingsView.xaml.cs`).

- [ ] **Step 6: Host the section in the Settings dialog**

In `SettingsViewModel.cs`:
- Add ctor param `EbaySellingSettingsViewModel ebaySelling` and property `public EbaySellingSettingsViewModel EbaySelling { get; } = ebaySelling;`.
- Add `public bool ShowEbaySelling => SelectedSectionIndex == 3;` and raise it in `OnSelectedSectionIndexChanged`.
- In `Load()`, add `EbaySelling.Load();`.

In `SettingsView.xaml`:
- Add a `<ListBoxItem Content="eBay Selling" Padding="16,10" Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>` after "Sales &amp; Receipts".
- Add a content block:
```xml
<ContentControl Visibility="{Binding Settings.ShowEbaySelling, Converter={StaticResource BoolToVis}}">
    <local:EbaySellingSettingsView DataContext="{Binding Settings.EbaySelling}"/>
</ContentControl>
```

- [ ] **Step 7: DI registrations**

In `App.xaml.cs`:
```csharp
services.AddSingleton<IEbaySellingSettingsService, EbaySellingSettingsService>();
services.AddSingleton<IEbaySellerSetupService, EbaySellerSetupService>();
services.AddSingleton<Views.Settings.EbaySellingSettingsViewModel>();
```
(Place the first next to `ISalesSettingsService` at line ~146; the eBay services next to the other `IEbay*` at line ~181; the VM next to `SalesSettingsViewModel` at line ~82. `EbaySellingSettingsService` lives in `OmniCard.Collection` — same namespace already imported.)

- [ ] **Step 8: Build, stop app if running, run the full eBay suite**

```bash
Get-Process OmniCard | Stop-Process -Force
dotnet test d:\source\repos\OmniCard\OmniCard.Tests\OmniCard.Tests.csproj --filter "FullyQualifiedName~Ebay|FullyQualifiedName~CredentialStore" --nologo
```
Expected: PASS (all eBay + credential tests).

- [ ] **Step 9: Manual verification (human)**

Launch the app, open Settings ▸ eBay Selling, fill address (Country `US`, a valid postal code), click **Run eBay Setup**, watch the step log. Then create a listing and confirm it publishes (or that the payment step's message explains a remaining manual eBay step). Check `X:\TCG Card Scanner\logs\tcgcardscanner-*.log` for the publish result.

- [ ] **Step 10: Commit**

```bash
git add OmniCard/Views/Settings/EbaySellingSettingsView.xaml OmniCard/Views/Settings/EbaySellingSettingsView.xaml.cs OmniCard/Views/Settings/EbaySellingSettingsViewModel.cs OmniCard/Views/Settings/SettingsViewModel.cs OmniCard/Views/Settings/SettingsView.xaml OmniCard/App.xaml.cs OmniCard.Tests/Services/EbaySellingSettingsViewModelTests.cs
git commit -m "feat(ebay): eBay Selling settings section + Run Setup wiring"
```

---

## Self-Review Notes

- **Spec coverage:** §1 persistence → Task 1; §2 orchestration (opt-in/location/policies) → Tasks 2–3; §3 UI → Task 5; §4 listing integration → Task 4; §5 error handling (non-fatal payment, specific guard message) → Tasks 3–4; §6 testing → tests in each task. All covered.
- **Payment non-fatal:** encoded in `FinalizeAsync` (Success ignores payment) and `IsSetupComplete` (Task 1) — consistent.
- **Type consistency:** `EbaySetupStep(Name, Status, Message)`, `EbaySetupStepStatus`, `IsSetupComplete()`, `MerchantLocationKey`, `LocationProvisioned`, `FulfillmentPolicyId/ReturnPolicyId/PaymentPolicyId` used identically across tasks.
- **Confirm-at-runtime:** eBay's exact "already opted in" text and policy list JSON shapes are validated against the logged response bodies during Task 9; adjust the substring/property checks if the live payloads differ.
