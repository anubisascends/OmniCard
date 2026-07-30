# Sales & Fulfillment Phase 3 (Settings Page + Receipts) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a receipt / packing-slip feature for orders (print + PDF) and a new app-level Settings page that hosts a Company Profile + Receipt settings and absorbs the app's existing scattered settings (display prefs, data location, For-Sale location).

**Architecture:** Extend the existing `sales-settings.json` (`SalesSettingsService`) with `CompanyProfile` + `ReceiptSettings`. Add a `Settings` top-level tab whose Display section binds directly to the existing `RootViewModel` display properties (one source of truth), whose Data Location section reuses the existing `DataLocationViewModel`, and whose Sales & Receipts section is backed by a new `SalesSettingsViewModel`. Build a single `ReceiptDocument` content model assembled by a testable `ReceiptService`, rendered two ways: a WPF `FlowDocument` for printing (mirrors `PickListPrinter`) and QuestPDF for PDF export (mirrors `DecklistPdfExporter`).

**Tech Stack:** C# / .NET, WPF (CommunityToolkit.Mvvm source-generated `[ObservableProperty]`/`[RelayCommand]`), EF Core (SQLite), QuestPDF 2026.7.0, xUnit, Microsoft.Extensions.DependencyInjection.

**Spec:** `docs/superpowers/specs/2026-07-21-sales-fulfillment-phase3-design.md`

## Global Constraints

- **No P&L changes** — the net-profit extension shipped in phase 2; do not touch `AnalyticsService`.
- **No in-app graphical print preview** — the exported PDF is the preview.
- **No settings-persistence rewrite** — reuse `DisplaySettings` (`IOptions`), `sales-settings.json`, and the data-location service; this phase only *surfaces* them in new UI.
- **MVVM conventions:** ViewModels use `CommunityToolkit.Mvvm` `ObservableObject` with `[ObservableProperty] public partial T Prop { get; set; }` and `[RelayCommand]`. Primary-constructor DI.
- **Models** live in `OmniCard.Shared` (`namespace OmniCard.Models`); **interfaces** in `namespace OmniCard.Interfaces`; **services** in `OmniCard.Collection` (`namespace OmniCard.Collection`); **PDF export** in `OmniCard.Audit` (`namespace OmniCard.Audit`); **views** in `OmniCard/Views/...`.
- **QuestPDF:** set `QuestPDF.Settings.License = LicenseType.Community;` before building a document.
- **Branch:** `feat/sales-fulfillment-phase3` (already created).
- **`docs/` is gitignored** — do not commit files under `docs/`. Commit only code/tests.
- **Build/test commands:** build `dotnet build OmniCard.sln`; test `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`. WPF projects target Windows; run on the Windows host.

---

## File Structure

**Created:**
- `OmniCard.Shared/Models/CompanyProfile.cs` — company header fields (name/address/logo/contact).
- `OmniCard.Shared/Models/ReceiptSettings.cs` — width/margin/font/show-prices/footer/printer.
- `OmniCard.Shared/Models/ReceiptDocument.cs` — receipt content model + `ReceiptLine`.
- `OmniCard.Shared/Interfaces/IReceiptService.cs` — `BuildReceipt(int orderId)`.
- `OmniCard.Shared/Interfaces/IReceiptPdfExporter.cs` — `Export(ReceiptDocument, string)`.
- `OmniCard.Collection/ReceiptService.cs` — assembles `ReceiptDocument`.
- `OmniCard.Audit/ReceiptPdfExporter.cs` — QuestPDF renderer.
- `OmniCard/Views/Settings/SettingsView.xaml(.cs)` — Settings tab host (sub-tabs).
- `OmniCard/Views/Settings/SettingsViewModel.cs` — exposes the three section VMs + lazy load.
- `OmniCard/Views/Settings/DisplaySettingsView.xaml(.cs)` — display prefs (binds `RootViewModel`).
- `OmniCard/Views/Settings/DataLocationSectionView.xaml(.cs)` — embeddable data-location panel.
- `OmniCard/Views/Settings/SalesSettingsView.xaml(.cs)` — For-Sale + Company + Receipt.
- `OmniCard/Views/Settings/SalesSettingsViewModel.cs` — backs the Sales & Receipts section.
- `OmniCard/Views/Sales/ReceiptPrinter.cs` — `FlowDocument` print helper.
- Test files under `OmniCard.Tests/`.

**Modified:**
- `OmniCard.Shared/Models/SalesSettings.cs` — add `Company`, `Receipt`.
- `OmniCard.Shared/Interfaces/ISalesSettingsService.cs` — add Company/Receipt/SetLogo members.
- `OmniCard.Collection/SalesSettingsService.cs` — implement the new members.
- `OmniCard.Tests/Services/OrderServiceTests.cs` + `OmniCard.Tests/Services/ListingServiceTests.cs` (+ any other `ISalesSettingsService` stubs) — extend stubs to satisfy the widened interface.
- `OmniCard/Views/Root/RootViewModel.cs` — inject/expose `SettingsViewModel`.
- `OmniCard/Views/Root/RootView.xaml(.cs)` — add Settings tab, lazy-load, remove duplicate View-menu controls, point Data Location menu at the Settings tab.
- `OmniCard/Views/Sales/PickListView.xaml` + `OmniCard/Views/Sales/SalesViewModel.cs` — remove inline For-Sale picker (read-only hint instead).
- `OmniCard/Views/Sales/OrdersViewModel.cs` + `OmniCard/Views/Sales/OrdersView.xaml` — Print Receipt / Export PDF.
- `OmniCard/App.xaml.cs` — register new services/VMs.

---

## BUILD STEP 1 — Settings foundation & migration

### Task 1: Extend SalesSettings model + service (Company / Receipt / logo)

**Files:**
- Create: `OmniCard.Shared/Models/CompanyProfile.cs`, `OmniCard.Shared/Models/ReceiptSettings.cs`
- Modify: `OmniCard.Shared/Models/SalesSettings.cs`, `OmniCard.Shared/Interfaces/ISalesSettingsService.cs`, `OmniCard.Collection/SalesSettingsService.cs`, `OmniCard.Tests/Services/OrderServiceTests.cs`, `OmniCard.Tests/Services/ListingServiceTests.cs`
- Test: `OmniCard.Tests/Services/SalesSettingsServiceTests.cs` (extend)

**Interfaces:**
- Produces: `CompanyProfile` (string? Name, AddressLine1, AddressLine2, City, State, PostalCode, Country, Email, Phone, LogoPath); `ReceiptSettings` (double WidthMm=80, MarginMm=4, FontPointSize=9; bool ShowPrices=true; string? FooterText, DefaultPrinterName); `ISalesSettingsService.GetCompany()/SaveCompany(CompanyProfile)/GetReceipt()/SaveReceipt(ReceiptSettings)/SetLogo(string sourcePath)→string relativeName`.

- [ ] **Step 1: Write the failing tests**

Append to `OmniCard.Tests/Services/SalesSettingsServiceTests.cs` (inside the class):

```csharp
    [Fact]
    public void Company_And_Receipt_Persist_AcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dps = new DataPathServiceStub(dir);
            var svc = new SalesSettingsService(dps);
            svc.SaveCompany(new OmniCard.Models.CompanyProfile { Name = "Acme Cards", City = "Reno", State = "NV" });
            svc.SaveReceipt(new OmniCard.Models.ReceiptSettings { WidthMm = 58, ShowPrices = false, FooterText = "Thanks!" });

            var reloaded = new SalesSettingsService(dps);
            Assert.Equal("Acme Cards", reloaded.GetCompany().Name);
            Assert.Equal("Reno", reloaded.GetCompany().City);
            Assert.Equal(58, reloaded.GetReceipt().WidthMm);
            Assert.False(reloaded.GetReceipt().ShowPrices);
            Assert.Equal("Thanks!", reloaded.GetReceipt().FooterText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetCompany_And_GetReceipt_ReturnDefaults_ForOldFileWithoutThem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A pre-phase-3 file: only ForSaleLocationId present.
            File.WriteAllText(Path.Combine(dir, "sales-settings.json"), "{\"ForSaleLocationId\":7}");
            var svc = new SalesSettingsService(new DataPathServiceStub(dir));

            Assert.Equal(7, svc.ForSaleLocationId);
            Assert.NotNull(svc.GetCompany());
            Assert.Null(svc.GetCompany().Name);
            Assert.Equal(80, svc.GetReceipt().WidthMm);   // default
            Assert.True(svc.GetReceipt().ShowPrices);      // default
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetLogo_CopiesFileIntoDataDir_ReturnsRelativeName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        var srcDir = Path.Combine(Path.GetTempPath(), "omnicard-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(srcDir);
        try
        {
            var src = Path.Combine(srcDir, "mylogo.png");
            File.WriteAllBytes(src, new byte[] { 1, 2, 3, 4 });
            var svc = new SalesSettingsService(new DataPathServiceStub(dir));

            var rel = svc.SetLogo(src);

            Assert.False(Path.IsPathRooted(rel));                       // relative
            Assert.True(File.Exists(Path.Combine(dir, rel)));           // resolvable against data dir
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(dir, rel)));
        }
        finally { Directory.Delete(dir, recursive: true); Directory.Delete(srcDir, recursive: true); }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~SalesSettingsServiceTests`
Expected: FAIL — `CompanyProfile`/`ReceiptSettings` and the new service members don't exist (compile error).

- [ ] **Step 3: Create the models**

`OmniCard.Shared/Models/CompanyProfile.cs`:

```csharp
namespace OmniCard.Models;

public class CompanyProfile
{
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>Path to the logo image, relative to the data directory.</summary>
    public string? LogoPath { get; set; }
}
```

`OmniCard.Shared/Models/ReceiptSettings.cs`:

```csharp
namespace OmniCard.Models;

public class ReceiptSettings
{
    public double WidthMm { get; set; } = 80;
    public double MarginMm { get; set; } = 4;
    public double FontPointSize { get; set; } = 9;
    public bool ShowPrices { get; set; } = true;
    public string? FooterText { get; set; }
    public string? DefaultPrinterName { get; set; }
}
```

- [ ] **Step 4: Extend `SalesSettings`**

Replace `OmniCard.Shared/Models/SalesSettings.cs` with:

```csharp
namespace OmniCard.Models;

public class SalesSettings
{
    public int? ForSaleLocationId { get; set; }
    public CompanyProfile Company { get; set; } = new();
    public ReceiptSettings Receipt { get; set; } = new();
}
```

- [ ] **Step 5: Widen the interface**

Replace `OmniCard.Shared/Interfaces/ISalesSettingsService.cs` with:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ISalesSettingsService
{
    int? ForSaleLocationId { get; }
    void SetForSaleLocationId(int? id);

    CompanyProfile GetCompany();
    void SaveCompany(CompanyProfile company);
    ReceiptSettings GetReceipt();
    void SaveReceipt(ReceiptSettings receipt);

    /// <summary>Copies the chosen image into the data directory and returns the stored
    /// path relative to the data directory (does not persist it — the caller assigns it
    /// to <see cref="CompanyProfile.LogoPath"/> and saves).</summary>
    string SetLogo(string sourcePath);
}
```

- [ ] **Step 6: Implement in `SalesSettingsService`**

Replace `OmniCard.Collection/SalesSettingsService.cs` with:

```csharp
using System.IO;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class SalesSettingsService : ISalesSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IDataPathService _dataPath;
    private readonly string _filePath;

    public SalesSettingsService(IDataPathService dataPathService)
    {
        _dataPath = dataPathService;
        _filePath = Path.Combine(dataPathService.DataDirectory, "sales-settings.json");
    }

    public int? ForSaleLocationId => Load().ForSaleLocationId;

    public void SetForSaleLocationId(int? id)
    {
        var settings = Load();
        settings.ForSaleLocationId = id;
        Save(settings);
    }

    public CompanyProfile GetCompany() => Load().Company;

    public void SaveCompany(CompanyProfile company)
    {
        var settings = Load();
        settings.Company = company;
        Save(settings);
    }

    public ReceiptSettings GetReceipt() => Load().Receipt;

    public void SaveReceipt(ReceiptSettings receipt)
    {
        var settings = Load();
        settings.Receipt = receipt;
        Save(settings);
    }

    public string SetLogo(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath);
        var destName = "company-logo" + ext;
        var dest = Path.Combine(_dataPath.DataDirectory, destName);
        File.Copy(sourcePath, dest, overwrite: true);
        return destName;
    }

    private SalesSettings Load()
    {
        SalesSettings settings;
        if (!File.Exists(_filePath))
            settings = new SalesSettings();
        else
        {
            try
            {
                settings = JsonSerializer.Deserialize<SalesSettings>(File.ReadAllText(_filePath), JsonOptions)
                           ?? new SalesSettings();
            }
            catch (JsonException)
            {
                settings = new SalesSettings();
            }
        }

        // Guard against old files (or explicit nulls) lacking the phase-3 sections.
        settings.Company ??= new CompanyProfile();
        settings.Receipt ??= new ReceiptSettings();
        return settings;
    }

    private void Save(SalesSettings settings)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
}
```

- [ ] **Step 7: Fix the broken test stubs**

The widened interface breaks the two hand-written stubs. Update both.

In `OmniCard.Tests/Services/OrderServiceTests.cs`, replace the `StubSettings` class with:

```csharp
    private sealed class StubSettings : OmniCard.Interfaces.ISalesSettingsService
    {
        public int? ForSaleLocationId => 99;
        public void SetForSaleLocationId(int? id) { }
        public OmniCard.Models.CompanyProfile GetCompany() => new();
        public void SaveCompany(OmniCard.Models.CompanyProfile company) { }
        public OmniCard.Models.ReceiptSettings GetReceipt() => new();
        public void SaveReceipt(OmniCard.Models.ReceiptSettings receipt) { }
        public string SetLogo(string sourcePath) => "company-logo.png";
    }
```

In `OmniCard.Tests/Services/ListingServiceTests.cs`, find `StubSalesSettings` (near line 115) and add the same five new members (keep its existing `ForSaleLocationId`/`SetForSaleLocationId`):

```csharp
        public OmniCard.Models.CompanyProfile GetCompany() => new();
        public void SaveCompany(OmniCard.Models.CompanyProfile company) { }
        public OmniCard.Models.ReceiptSettings GetReceipt() => new();
        public void SaveReceipt(OmniCard.Models.ReceiptSettings receipt) { }
        public string SetLogo(string sourcePath) => "company-logo.png";
```

If any other class implements `ISalesSettingsService` (search: `grep -rn "ISalesSettingsService" OmniCard.Tests`), add the same members there too.

- [ ] **Step 8: Run the full test project to verify green**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS (the three new tests pass; previously-passing tests still pass).

- [ ] **Step 9: Commit**

```bash
git add OmniCard.Shared/Models/CompanyProfile.cs OmniCard.Shared/Models/ReceiptSettings.cs \
        OmniCard.Shared/Models/SalesSettings.cs OmniCard.Shared/Interfaces/ISalesSettingsService.cs \
        OmniCard.Collection/SalesSettingsService.cs \
        OmniCard.Tests/Services/SalesSettingsServiceTests.cs \
        OmniCard.Tests/Services/OrderServiceTests.cs OmniCard.Tests/Services/ListingServiceTests.cs
git commit -m "feat(sales): company profile + receipt settings in SalesSettingsService"
```

---

### Task 2: SalesSettingsViewModel (For-Sale + Company + Receipt)

**Files:**
- Create: `OmniCard/Views/Settings/SalesSettingsViewModel.cs`
- Modify: `OmniCard/App.xaml.cs`
- Test: `OmniCard.Tests/Views/Sales/SalesSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `ISalesSettingsService` (Task 1), `IStorageContainerService.GetAll()`.
- Produces: `SalesSettingsViewModel` with `ObservableCollection<StorageContainer> Locations`, `StorageContainer? ForSaleLocation`, `CompanyProfile Company`, `ReceiptSettings Receipt`, `string? StatusMessage`; methods `void Load()`, `SaveCommand` (`Save()`), `PickLogoCommand` (`PickLogo()`).

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Views/Sales/SalesSettingsViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Settings;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class SalesSettingsViewModelTests
{
    private sealed class FakeContainers : IStorageContainerService
    {
        public List<StorageContainer> Containers { get; } =
            [new StorageContainer { Id = 1, Name = "Bulk" }, new StorageContainer { Id = 2, Name = "For Sale" }];
        public List<StorageContainer> GetAll() => Containers;
        public StorageContainer GetBulk() => Containers[0];
        public StorageContainer Create(string name, ContainerType type) => throw new System.NotImplementedException();
        public void Rename(int id, string newName) { }
        public void Delete(int id, bool moveCardsToBulk = true) { }
        public int GetCardCount(int containerId) => 0;
        public void SetCoverCard(int containerId, int? cardId) { }
        public List<CollectionCard> GetCardsInContainer(int containerId) => [];
        public void SetExcludeFromDeckCheck(int containerId, bool exclude) { }
    }

    private sealed class FakeSettings : ISalesSettingsService
    {
        public int? StoredLocation { get; set; } = 2;
        public CompanyProfile StoredCompany { get; set; } = new() { Name = "Existing Co" };
        public ReceiptSettings StoredReceipt { get; set; } = new() { WidthMm = 80 };
        public string? LastLogoSource { get; private set; }

        public int? ForSaleLocationId => StoredLocation;
        public void SetForSaleLocationId(int? id) => StoredLocation = id;
        public CompanyProfile GetCompany() => StoredCompany;
        public void SaveCompany(CompanyProfile company) => StoredCompany = company;
        public ReceiptSettings GetReceipt() => StoredReceipt;
        public void SaveReceipt(ReceiptSettings receipt) => StoredReceipt = receipt;
        public string SetLogo(string sourcePath) { LastLogoSource = sourcePath; return "company-logo.png"; }
    }

    [Fact]
    public void Load_PopulatesLocations_SelectsSavedLocation_AndCopiesCompanyReceipt()
    {
        var settings = new FakeSettings();
        var vm = new SalesSettingsViewModel(settings, new FakeContainers());

        vm.Load();

        Assert.Equal(2, vm.Locations.Count);
        Assert.NotNull(vm.ForSaleLocation);
        Assert.Equal(2, vm.ForSaleLocation!.Id);
        Assert.Equal("Existing Co", vm.Company.Name);
        Assert.Equal(80, vm.Receipt.WidthMm);
    }

    [Fact]
    public void Save_PersistsForSaleLocation_Company_AndReceipt()
    {
        var settings = new FakeSettings();
        var vm = new SalesSettingsViewModel(settings, new FakeContainers());
        vm.Load();

        vm.ForSaleLocation = vm.Locations[0];       // Bulk (Id 1)
        vm.Company.Name = "New Name";
        vm.Receipt.WidthMm = 58;
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, settings.StoredLocation);
        Assert.Equal("New Name", settings.StoredCompany.Name);
        Assert.Equal(58, settings.StoredReceipt.WidthMm);
        Assert.Equal("Saved.", vm.StatusMessage);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~SalesSettingsViewModelTests`
Expected: FAIL — `SalesSettingsViewModel` doesn't exist.

- [ ] **Step 3: Implement `SalesSettingsViewModel`**

Create `OmniCard/Views/Settings/SalesSettingsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings page's "Sales &amp; Receipts" section: the For-Sale storage location,
/// the company profile, and receipt settings, all persisted via <see cref="ISalesSettingsService"/>.
/// </summary>
public partial class SalesSettingsViewModel(
    ISalesSettingsService salesSettings,
    IStorageContainerService storageContainers) : ObservableObject
{
    public ObservableCollection<StorageContainer> Locations { get; } = [];

    [ObservableProperty]
    public partial StorageContainer? ForSaleLocation { get; set; }

    [ObservableProperty]
    public partial CompanyProfile Company { get; set; } = new();

    [ObservableProperty]
    public partial ReceiptSettings Receipt { get; set; } = new();

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>Loads locations + persisted company/receipt. Safe to call on every activation.</summary>
    public void Load()
    {
        Locations.Clear();
        foreach (var c in storageContainers.GetAll())
            Locations.Add(c);

        ForSaleLocation = Locations.FirstOrDefault(c => c.Id == salesSettings.ForSaleLocationId);
        Company = salesSettings.GetCompany();
        Receipt = salesSettings.GetReceipt();
    }

    [RelayCommand]
    public void Save()
    {
        salesSettings.SetForSaleLocationId(ForSaleLocation?.Id);
        salesSettings.SaveCompany(Company);
        salesSettings.SaveReceipt(Receipt);
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    public void PickLogo()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select company logo",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
        };
        if (dialog.ShowDialog() != true) return;

        Company.LogoPath = salesSettings.SetLogo(dialog.FileName);
        OnPropertyChanged(nameof(Company));
        StatusMessage = "Logo set (remember to Save).";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~SalesSettingsViewModelTests`
Expected: PASS.

- [ ] **Step 5: Register in DI**

In `OmniCard/App.xaml.cs`, in the block near the other Sales VM registrations (around lines 77-79), add:

```csharp
            services.AddSingleton<Views.Settings.SalesSettingsViewModel>();
```

- [ ] **Step 6: Build to verify DI compiles**

Run: `dotnet build OmniCard.sln`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Settings/SalesSettingsViewModel.cs OmniCard/App.xaml.cs \
        OmniCard.Tests/Views/Sales/SalesSettingsViewModelTests.cs
git commit -m "feat(sales): SalesSettingsViewModel for the settings page section"
```

---

### Task 3: Settings tab shell + Display & Data Location sections + Root wiring

**Files:**
- Create: `OmniCard/Views/Settings/SettingsViewModel.cs`, `SettingsView.xaml(.cs)`, `DisplaySettingsView.xaml(.cs)`, `DataLocationSectionView.xaml(.cs)`
- Modify: `OmniCard/Views/Root/RootViewModel.cs`, `OmniCard/Views/Root/RootView.xaml`, `OmniCard/Views/Root/RootView.xaml.cs`, `OmniCard/App.xaml.cs`

**Interfaces:**
- Consumes: `SalesSettingsViewModel` (Task 2), existing `DataLocationViewModel`, existing `RootViewModel` display properties (`IsDarkTheme`, `CardDetailFontSize`, `CardPreviewScale`, `StackDuplicates`, `ScannerFontSize`, `ScanQuality`, `DefaultScannerName`).
- Produces: `SettingsViewModel` with `SalesSettingsViewModel Sales`, `DataLocationViewModel DataLocation`, and `Task Load()`; a `Settings` tab in `RootView`.

*Note:* this task is verified by build + human E2E (WPF views). The section VM logic is already unit-tested (Task 2) and reuses `DataLocationViewModel` unchanged. `DataLocationView` is a `Window`, so the Data Location section is a new UserControl (`DataLocationSectionView`) that binds the same `DataLocationViewModel` directly and omits the dialog's Close button.

- [ ] **Step 1: Create `SettingsViewModel`**

Create `OmniCard/Views/Settings/SettingsViewModel.cs`:

```csharp
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Views.DataLocation;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings tab. Composes the section view-models. Display prefs are bound directly
/// to the RootViewModel (one source of truth) and so are not represented here.
/// </summary>
public partial class SettingsViewModel(
    SalesSettingsViewModel sales,
    DataLocationViewModel dataLocation) : ObservableObject
{
    public SalesSettingsViewModel Sales { get; } = sales;
    public DataLocationViewModel DataLocation { get; } = dataLocation;

    /// <summary>Loads section data. Called when the Settings tab is activated.</summary>
    public async Task Load()
    {
        Sales.Load();
        await DataLocation.LoadAsync();
    }
}
```

- [ ] **Step 2: Create `DisplaySettingsView`** (binds to `RootViewModel`)

Create `OmniCard/Views/Settings/DisplaySettingsView.xaml`:

```xml
<UserControl x:Class="OmniCard.Views.Settings.DisplaySettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d">
    <!-- DataContext is the RootViewModel (set by SettingsView host). -->
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16" MaxWidth="520" HorizontalAlignment="Left">
            <TextBlock Text="Display" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,12"/>

            <CheckBox Content="Dark theme" IsChecked="{Binding IsDarkTheme}" Margin="0,0,0,12"/>

            <TextBlock Text="Card detail font size" Margin="0,0,0,4"/>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                <Slider Width="200" Minimum="10" Maximum="24" TickFrequency="1" IsSnapToTickEnabled="True"
                        Value="{Binding CardDetailFontSize}" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding CardDetailFontSize, StringFormat='{}{0:F0}'}"
                           VerticalAlignment="Center" Margin="8,0,0,0"/>
            </StackPanel>

            <TextBlock Text="Card preview size" Margin="0,0,0,4"/>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                <Slider Width="200" Minimum="100" Maximum="175" TickFrequency="25" IsSnapToTickEnabled="True"
                        Value="{Binding CardPreviewScale}" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding CardPreviewScale, StringFormat='{}{0:F0}%'}"
                           VerticalAlignment="Center" Margin="8,0,0,0"/>
            </StackPanel>

            <CheckBox Content="Stack duplicates" IsChecked="{Binding StackDuplicates}" Margin="0,0,0,12"/>

            <TextBlock Text="Scanner font size" Margin="0,0,0,4"/>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                <Slider Width="200" Minimum="10" Maximum="24" TickFrequency="1" IsSnapToTickEnabled="True"
                        Value="{Binding ScannerFontSize}" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding ScannerFontSize, StringFormat='{}{0:F0}'}"
                           VerticalAlignment="Center" Margin="8,0,0,0"/>
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

Create `OmniCard/Views/Settings/DisplaySettingsView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace OmniCard.Views.Settings;

public partial class DisplaySettingsView : UserControl
{
    public DisplaySettingsView() => InitializeComponent();
}
```

- [ ] **Step 3: Create `DataLocationSectionView`** (embeddable panel over the existing VM)

Create `OmniCard/Views/Settings/DataLocationSectionView.xaml` (same controls as the dialog minus the Close button; DataContext is a `DataLocationViewModel`, so bindings drop the `ViewModel.` prefix):

```xml
<UserControl x:Class="OmniCard.Views.Settings.DataLocationSectionView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:dl="clr-namespace:OmniCard.Views.DataLocation"
             mc:Ignorable="d">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
        <dl:InvertBooleanConverter x:Key="InvertBool"/>
    </UserControl.Resources>
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16" MaxWidth="560" HorizontalAlignment="Left">
            <TextBlock Text="Data Location" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,12"/>

            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Current path:" VerticalAlignment="Center" Margin="0,0,8,0"/>
                <TextBox Grid.Column="1" Text="{Binding CurrentPath, Mode=OneWay}"
                         IsReadOnly="True" Margin="0,0,8,0" VerticalAlignment="Center"/>
                <Button Grid.Column="2" Content="Browse..." Command="{Binding BrowseCommand}"
                        Padding="12,4" IsEnabled="{Binding IsMigrating, Converter={StaticResource InvertBool}}"/>
            </Grid>

            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Text="Status:" Margin="0,0,8,0" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding StatusText}" VerticalAlignment="Center" FontWeight="SemiBold"/>
            </StackPanel>

            <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                    CornerRadius="4" Padding="12" Margin="0,0,0,8"
                    Visibility="{Binding IsMigrationPending, Converter={StaticResource BoolToVis}}">
                <StackPanel>
                    <TextBlock TextWrapping="Wrap" Margin="0,0,0,8">
                        <Run Text="Target: "/>
                        <Run Text="{Binding PendingPath, Mode=OneWay}" FontWeight="SemiBold"/>
                    </TextBlock>
                    <StackPanel Orientation="Horizontal">
                        <Button Content="Migrate Now" Command="{Binding MigrateCommand}"
                                Padding="12,4" Margin="0,0,8,0"
                                IsEnabled="{Binding IsMigrating, Converter={StaticResource InvertBool}}"/>
                        <Button Content="Cancel" Command="{Binding CancelMigrationCommand}" Padding="12,4"/>
                    </StackPanel>
                </StackPanel>
            </Border>

            <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                    CornerRadius="4" Padding="12" Margin="0,0,0,8"
                    Visibility="{Binding IsMigrating, Converter={StaticResource BoolToVis}}">
                <StackPanel>
                    <ProgressBar Value="{Binding MigrationProgress}" Minimum="0" Maximum="100"
                                 Height="20" Margin="0,0,0,8"/>
                    <TextBlock Text="{Binding MigrationStatusText}" TextTrimming="CharacterEllipsis"/>
                    <Button Content="Cancel Migration" Command="{Binding CancelMigrationCommand}"
                            Padding="12,4" Margin="0,8,0,0" HorizontalAlignment="Left"/>
                </StackPanel>
            </Border>

            <TextBlock Text="{Binding DataSummary}"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}" Margin="0,8,0,0"/>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

Create `OmniCard/Views/Settings/DataLocationSectionView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace OmniCard.Views.Settings;

public partial class DataLocationSectionView : UserControl
{
    public DataLocationSectionView() => InitializeComponent();
}
```

*Verify* `InvertBooleanConverter` is `public` in `namespace OmniCard.Views.DataLocation` (it is referenced by the existing dialog). If it is not public, make it `public`.

- [ ] **Step 4: Create `SettingsView`** (sub-tab host)

Create `OmniCard/Views/Settings/SettingsView.xaml`:

```xml
<UserControl x:Class="OmniCard.Views.Settings.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:OmniCard.Views.Settings"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             d:DataContext="{d:DesignInstance {x:Type local:SettingsViewModel}}">
    <!-- DataContext for this control is the RootViewModel (set in RootView), which exposes
         a `Settings` SettingsViewModel. The Display section binds to the RootViewModel itself. -->
    <TabControl>
        <TabItem Header="Display">
            <local:DisplaySettingsView/>
        </TabItem>
        <TabItem Header="Data Location">
            <local:DataLocationSectionView DataContext="{Binding Settings.DataLocation}"/>
        </TabItem>
        <TabItem Header="Sales &amp; Receipts">
            <local:SalesSettingsView DataContext="{Binding Settings.Sales}"/>
        </TabItem>
    </TabControl>
</UserControl>
```

*Note:* `SalesSettingsView` is created in Task 4. Until then this reference won't resolve — Task 3 and Task 4 build together. If implementing Task 3 alone, temporarily comment out the "Sales & Receipts" `TabItem`, then restore it in Task 4.

Create `OmniCard/Views/Settings/SettingsView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace OmniCard.Views.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}
```

- [ ] **Step 5: Expose `SettingsViewModel` from `RootViewModel`**

In `OmniCard/Views/Root/RootViewModel.cs`, add `Views.Settings.SettingsViewModel settings` to the primary constructor parameter list (place it next to `Views.Sales.SalesViewModel sales,`):

```csharp
    Views.Sales.SalesViewModel sales,
    Views.Settings.SettingsViewModel settings,
```

Then add the property next to `public Views.Sales.SalesViewModel Sales { get; } = sales;` (around line 173):

```csharp
    public Views.Settings.SettingsViewModel Settings { get; } = settings;
```

- [ ] **Step 6: Register `SettingsViewModel` in DI**

In `OmniCard/App.xaml.cs`, near the Sales VM registrations, add:

```csharp
            services.AddSingleton<Views.Settings.SettingsViewModel>();
```

`DataLocationViewModel` is already registered (transient, line ~173) and will be injected into `SettingsViewModel`.

- [ ] **Step 7: Add the Settings tab to `RootView.xaml`**

Add the `xmlns` for settings near the other view namespaces (line ~7-9):

```xml
        xmlns:settings="clr-namespace:OmniCard.Views.Settings"
```

Add a new `TabItem` after the Sales tab (after line 266, before `</TabControl>`):

```xml
            <TabItem Header="Settings"
                     x:Name="tabItemSettings">
                <settings:SettingsView DataContext="{Binding ViewModel}"/>
            </TabItem>
```

(The Settings tab's DataContext is the `RootViewModel` so the Display section binds directly to it; nested sections bind through `Settings.*`.)

- [ ] **Step 8: Lazy-load the Settings tab in `RootView.xaml.cs`**

In the `MainTabControl.SelectionChanged` handler (around lines 43-46), add a branch:

```csharp
            if (MainTabControl.SelectedItem == tabItemDashboard)
                viewModel.Dashboard.Load();
            else if (MainTabControl.SelectedItem == tabItemSales)
                _ = viewModel.Sales.Load();
            else if (MainTabControl.SelectedItem == tabItemSettings)
                _ = viewModel.Settings.Load();
```

- [ ] **Step 9: Remove duplicate View-menu display controls + point Data Location menu at Settings**

In `OmniCard/Views/Root/RootView.xaml`:

- Delete the three MenuItem blocks in the `_View` menu that host the "Dark Theme:" checkbox, "Font Size:" slider, "Card Preview Size:" slider, and "Scanner Font Size:" slider (lines ~85-155). **Keep** the "Show pHash Preview" checkable MenuItem (it is a diagnostic, not a migrated display setting) and the Separator above it. If removing all sliders leaves the `_View` menu with only the pHash toggle, that is fine.
- Change the Edit → "Data Location..." menu item (line ~81-82) to select the Settings tab instead of opening the dialog. Replace its `Command` with a click handler:

```xml
                <MenuItem Header="Data _Location..."
                          Click="DataLocationMenuItem_Click"/>
```

In `OmniCard/Views/Root/RootView.xaml.cs`, add the handler (near the other `*_Click` handlers):

```csharp
    private void DataLocationMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        => MainTabControl.SelectedIndex = MainTabControl.Items.IndexOf(tabItemSettings);
```

*Note:* leave `ShowDataLocationCommand` and the existing `DataLocationView` dialog in place (unreferenced by the menu now, but harmless and possibly used elsewhere). Do not delete working code beyond the menu entries above.

- [ ] **Step 10: Build**

Run: `dotnet build OmniCard.sln`
Expected: Build succeeded (0 errors). Fix any binding-namespace or missing-`SalesSettingsView` issues (see Step 4 note).

- [ ] **Step 11: Run the test project (no behavior regressions)**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add OmniCard/Views/Settings/ OmniCard/Views/Root/RootViewModel.cs \
        OmniCard/Views/Root/RootView.xaml OmniCard/Views/Root/RootView.xaml.cs OmniCard/App.xaml.cs
git commit -m "feat(settings): app-level Settings tab with Display + Data Location sections"
```

- [ ] **Step 13: Human E2E note**

Flag for the reviewer: launch the app, open Settings, verify (a) Display toggles/sliders live-apply (dark theme, font sizes, preview scale, stack duplicates, scanner font) and persist across restart; (b) the View menu no longer shows the moved controls but still has Show pHash Preview; (c) Edit → Data Location selects the Settings tab; (d) the Data Location section shows current path and Browse/Migrate work.

---

### Task 4: Sales & Receipts settings section + move For-Sale picker out of Pick List

**Files:**
- Create: `OmniCard/Views/Settings/SalesSettingsView.xaml(.cs)`
- Modify: `OmniCard/Views/Sales/PickListView.xaml`, `OmniCard/Views/Sales/SalesViewModel.cs`

**Interfaces:**
- Consumes: `SalesSettingsViewModel` (Task 2), `SalesViewModel.ForSaleLocation` (existing).
- Produces: the `SalesSettingsView` referenced by `SettingsView` (Task 3, Step 4).

*Verified by build + human E2E.*

- [ ] **Step 1: Create `SalesSettingsView`**

Create `OmniCard/Views/Settings/SalesSettingsView.xaml`:

```xml
<UserControl x:Class="OmniCard.Views.Settings.SalesSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:OmniCard.Views.Settings"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             d:DataContext="{d:DesignInstance {x:Type local:SalesSettingsViewModel}}">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16" MaxWidth="560" HorizontalAlignment="Left">

            <TextBlock Text="Fulfillment" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,12"/>
            <TextBlock Text="For-Sale location (cards move here when picked):" Margin="0,0,0,4"/>
            <ComboBox Width="240" HorizontalAlignment="Left" DisplayMemberPath="Name"
                      ItemsSource="{Binding Locations}" SelectedItem="{Binding ForSaleLocation}"
                      Margin="0,0,0,16"/>

            <TextBlock Text="Company Profile" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,12"/>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="140"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Grid.Column="0" Text="Name" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="0" Grid.Column="1" Margin="0,4" Text="{Binding Company.Name, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="1" Grid.Column="0" Text="Address line 1" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="1" Grid.Column="1" Margin="0,4" Text="{Binding Company.AddressLine1, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="2" Grid.Column="0" Text="Address line 2" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="2" Grid.Column="1" Margin="0,4" Text="{Binding Company.AddressLine2, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="3" Grid.Column="0" Text="City" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="3" Grid.Column="1" Margin="0,4" Text="{Binding Company.City, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="4" Grid.Column="0" Text="State" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="4" Grid.Column="1" Margin="0,4" Text="{Binding Company.State, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="5" Grid.Column="0" Text="Postal code" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="5" Grid.Column="1" Margin="0,4" Text="{Binding Company.PostalCode, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="6" Grid.Column="0" Text="Country" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="6" Grid.Column="1" Margin="0,4" Text="{Binding Company.Country, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="7" Grid.Column="0" Text="Email" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="7" Grid.Column="1" Margin="0,4" Text="{Binding Company.Email, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="8" Grid.Column="0" Text="Phone" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="8" Grid.Column="1" Margin="0,4" Text="{Binding Company.Phone, UpdateSourceTrigger=PropertyChanged}"/>
            </Grid>

            <StackPanel Orientation="Horizontal" Margin="0,8,0,16">
                <Button Content="Choose logo..." Command="{Binding PickLogoCommand}" Padding="12,4" Margin="0,0,8,0"/>
                <TextBlock Text="{Binding Company.LogoPath, TargetNullValue='(no logo)'}" VerticalAlignment="Center"/>
            </StackPanel>

            <TextBlock Text="Receipt" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,12"/>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="140"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Grid.Column="0" Text="Width (mm)" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="0" Grid.Column="1" Width="80" HorizontalAlignment="Left" Margin="0,4"
                           Text="{Binding Receipt.WidthMm, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="1" Grid.Column="0" Text="Margin (mm)" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="1" Grid.Column="1" Width="80" HorizontalAlignment="Left" Margin="0,4"
                           Text="{Binding Receipt.MarginMm, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="2" Grid.Column="0" Text="Font size (pt)" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="2" Grid.Column="1" Width="80" HorizontalAlignment="Left" Margin="0,4"
                           Text="{Binding Receipt.FontPointSize, UpdateSourceTrigger=PropertyChanged}"/>
                <CheckBox  Grid.Row="3" Grid.Column="1" Content="Show prices" Margin="0,4"
                           IsChecked="{Binding Receipt.ShowPrices}"/>
                <TextBlock Grid.Row="4" Grid.Column="0" Text="Footer text" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="4" Grid.Column="1" Margin="0,4" Text="{Binding Receipt.FooterText, UpdateSourceTrigger=PropertyChanged}"/>
                <TextBlock Grid.Row="5" Grid.Column="0" Text="Default printer" VerticalAlignment="Center" Margin="0,4"/>
                <TextBox   Grid.Row="5" Grid.Column="1" Margin="0,4" Text="{Binding Receipt.DefaultPrinterName, UpdateSourceTrigger=PropertyChanged}"/>
            </Grid>

            <StackPanel Orientation="Horizontal" Margin="0,16,0,0">
                <Button Content="Save" Command="{Binding SaveCommand}" Padding="16,4" Margin="0,0,12,0"/>
                <TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center"/>
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

Create `OmniCard/Views/Settings/SalesSettingsView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace OmniCard.Views.Settings;

public partial class SalesSettingsView : UserControl
{
    public SalesSettingsView() => InitializeComponent();
}
```

- [ ] **Step 2: If Task 3 Step 4 commented out the Sales & Receipts TabItem, restore it now.**

Ensure `SettingsView.xaml` contains the `<TabItem Header="Sales &amp; Receipts">` block.

- [ ] **Step 3: Remove the inline For-Sale picker from the Pick List header**

Replace the top `StackPanel` in `OmniCard/Views/Sales/PickListView.xaml` (lines 10-18) with a version that shows the location read-only and points to Settings:

```xml
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="For-Sale location:" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBlock Text="{Binding ForSaleLocation.Name, TargetNullValue='(set in Settings ▸ Sales &amp; Receipts)'}"
                       VerticalAlignment="Center" FontWeight="SemiBold" Margin="0,0,12,0"/>
            <Button Content="Refresh" Margin="0,0,0,0" Command="{Binding RefreshPickListCommand}"/>
            <Button Content="Mark All Picked" Margin="6,0,0,0" Command="{Binding MarkAllPickedCommand}"/>
            <Button Content="Print Pick List" Margin="6,0,0,0" Command="{Binding PrintPickListCommand}"/>
            <TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center" Margin="12,0,0,0"/>
        </StackPanel>
```

- [ ] **Step 4: Simplify `SalesViewModel` For-Sale handling**

The picker no longer changes `ForSaleLocation` from this view, so the persist-on-change side effect and its `_suppressPersist` guard are no longer needed. In `OmniCard/Views/Sales/SalesViewModel.cs`:

- Delete the `_suppressPersist` field and its doc comment (lines ~28-34).
- In `Load()`, remove the `_suppressPersist = true; try { ... } finally { _suppressPersist = false; }` wrapping but keep the body:

```csharp
            Locations.Clear();
            foreach (var c in containers)
                Locations.Add(c);

            ForSaleLocation = Locations.FirstOrDefault(c => c.Id == salesSettings.ForSaleLocationId);
```

- Delete the `OnForSaleLocationChanged` partial method (lines ~85-89) — the setting is now written only from `SalesSettingsViewModel.Save()`.

`ForSaleLocation` remains an `[ObservableProperty]` so the read-only header binding works and `MarkAllPicked`'s null-check still functions. `Locations` remains populated (harmless).

- [ ] **Step 5: Build**

Run: `dotnet build OmniCard.sln`
Expected: Build succeeded.

- [ ] **Step 6: Run the test project**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS. If `SalesViewModelTests` asserted the removed persist-on-change behavior, update those assertions to reflect that changing `ForSaleLocation` no longer calls `SetForSaleLocationId` (persistence now happens via `SalesSettingsViewModel.Save`). Keep tests that assert `Load()` selects the saved location.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Settings/SalesSettingsView.xaml OmniCard/Views/Settings/SalesSettingsView.xaml.cs \
        OmniCard/Views/Settings/SettingsView.xaml \
        OmniCard/Views/Sales/PickListView.xaml OmniCard/Views/Sales/SalesViewModel.cs \
        OmniCard.Tests/Views/Sales/SalesViewModelTests.cs
git commit -m "feat(settings): Sales & Receipts section; move For-Sale picker to Settings"
```

- [ ] **Step 8: Human E2E note**

Flag for the reviewer: in Settings ▸ Sales & Receipts, set the For-Sale location, fill company profile, choose a logo, set receipt width/font/footer, Save; reopen the app and confirm values persisted. On the Sales ▸ Pick List tab, confirm the For-Sale location now shows read-only and Mark All Picked still moves cards to it.

---

## BUILD STEP 2 — Receipt

### Task 5: ReceiptDocument model + ReceiptService.BuildReceipt

**Files:**
- Create: `OmniCard.Shared/Models/ReceiptDocument.cs`, `OmniCard.Shared/Interfaces/IReceiptService.cs`, `OmniCard.Collection/ReceiptService.cs`
- Modify: `OmniCard/App.xaml.cs`
- Test: `OmniCard.Tests/Services/ReceiptServiceTests.cs`

**Interfaces:**
- Consumes: `IOrderService.GetOrder/GetLines`, `ICustomerService.Get`, `ISalesSettingsService.GetCompany/GetReceipt`, `IDataPathService.DataDirectory`.
- Produces: `ReceiptDocument` + `ReceiptLine`; `IReceiptService.BuildReceipt(int orderId) → ReceiptDocument`.

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/ReceiptServiceTests.cs`:

```csharp
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ReceiptServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly string _dataDir;

    public ReceiptServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();

        _dataDir = Path.Combine(Path.GetTempPath(), "omnicard-receipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose() { _conn.Dispose(); if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true); }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private sealed class DataPathStub(string dir) : OmniCard.Interfaces.IDataPathService
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

    private ReceiptService BuildService(out int orderId)
    {
        // Seed customer, product, lot, order + line via the real services.
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Customers.Add(new Customer { Id = 1, Name = "Ada Lovelace", AddressLine1 = "12 Analytical Way", City = "London", PostalCode = "EC1" });
            var p = new Product { Id = 1, Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring", SetName = "Commander", Foil = true };
            ctx.Products.Add(p);
            ctx.SaveChanges();
            ctx.Lots.Add(new InventoryLot { Id = 1, ProductId = 1, Quantity = 2, Condition = "NM", UnitCost = 1m });
            ctx.SaveChanges();
        }

        var settings = new SalesSettingsService(new DataPathStub(_dataDir));
        settings.SaveCompany(new CompanyProfile { Name = "Acme Cards", AddressLine1 = "1 Main", City = "Reno", State = "NV", PostalCode = "89501" });
        settings.SaveReceipt(new ReceiptSettings { WidthMm = 80, ShowPrices = true, FooterText = "Thank you!" });

        var orders = new OrderService(new Factory(_opts), new ListingService(new Factory(_opts), settings));
        var customers = new CustomerService(new Factory(_opts));
        var order = orders.CreateOrder(1, SalesChannel.TcgPlayer, "TCG-100");
        orders.AddLine(order.Id, 1, 3.50m);
        order.ShippingChargedToBuyer = 1.00m;
        orders.UpdateOrder(order);
        orderId = order.Id;

        return new ReceiptService(orders, customers, settings, new DataPathStub(_dataDir));
    }

    [Fact]
    public void BuildReceipt_AssemblesLines_Totals_AndBlocks()
    {
        var svc = BuildService(out var orderId);

        var doc = svc.BuildReceipt(orderId);

        Assert.Equal("Acme Cards", doc.CompanyName);
        Assert.Contains("Reno", doc.CompanyAddressBlock);
        Assert.Equal("Ada Lovelace", doc.CustomerName);
        Assert.Contains("London", doc.CustomerAddressBlock);
        Assert.Equal("TCG-100", doc.OrderNumber);

        var line = Assert.Single(doc.Lines);
        Assert.Equal("Sol Ring", line.Name);
        Assert.Equal("Commander", line.Set);
        Assert.Equal("NM", line.Condition);
        Assert.True(line.IsFoil);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(3.50m, line.UnitSalePrice);
        Assert.Equal(3.50m, line.LineTotal);

        Assert.True(doc.ShowPrices);
        Assert.Equal(3.50m, doc.ItemsTotal);
        Assert.Equal(1.00m, doc.Shipping);
        Assert.Equal(4.50m, doc.GrandTotal);
        Assert.Equal("Thank you!", doc.FooterText);
        Assert.Equal(80, doc.WidthMm);
        Assert.Null(doc.CompanyLogoAbsolutePath);   // no logo set
    }

    [Fact]
    public void BuildReceipt_UnknownOrder_Throws()
    {
        var svc = BuildService(out _);
        Assert.Throws<InvalidOperationException>(() => svc.BuildReceipt(99999));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~ReceiptServiceTests`
Expected: FAIL — `ReceiptDocument`/`ReceiptService` don't exist.

- [ ] **Step 3: Create the content model**

Create `OmniCard.Shared/Models/ReceiptDocument.cs`:

```csharp
using System.Collections.Generic;

namespace OmniCard.Models;

public class ReceiptDocument
{
    // Company header
    public string? CompanyName { get; set; }
    public string? CompanyAddressBlock { get; set; }
    public string? CompanyLogoAbsolutePath { get; set; }
    public string? CompanyEmail { get; set; }
    public string? CompanyPhone { get; set; }

    // Order info
    public string? OrderNumber { get; set; }
    public DateTime OrderDate { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }

    // Customer
    public string CustomerName { get; set; } = "";
    public string? CustomerAddressBlock { get; set; }

    // Lines + totals
    public IReadOnlyList<ReceiptLine> Lines { get; set; } = [];
    public bool ShowPrices { get; set; }
    public decimal ItemsTotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal GrandTotal { get; set; }
    public string? FooterText { get; set; }

    // Layout
    public double WidthMm { get; set; }
    public double MarginMm { get; set; }
    public double FontPointSize { get; set; }
}

public class ReceiptLine
{
    public string Name { get; set; } = "";
    public string? Set { get; set; }
    public string? Condition { get; set; }
    public bool IsFoil { get; set; }
    public int Quantity { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal LineTotal { get; set; }
}
```

- [ ] **Step 4: Create the interface**

Create `OmniCard.Shared/Interfaces/IReceiptService.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IReceiptService
{
    /// <summary>Builds the receipt content model for an order. Throws if the order is not found.</summary>
    ReceiptDocument BuildReceipt(int orderId);
}
```

- [ ] **Step 5: Implement `ReceiptService`**

Create `OmniCard.Collection/ReceiptService.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class ReceiptService(
    IOrderService orderService,
    ICustomerService customerService,
    ISalesSettingsService salesSettings,
    IDataPathService dataPathService) : IReceiptService
{
    public ReceiptDocument BuildReceipt(int orderId)
    {
        var order = orderService.GetOrder(orderId)
                    ?? throw new InvalidOperationException($"Order {orderId} not found.");
        var lines = orderService.GetLines(orderId);
        var customer = customerService.Get(order.CustomerId);
        var company = salesSettings.GetCompany();
        var receipt = salesSettings.GetReceipt();

        var receiptLines = lines.Select(l => new ReceiptLine
        {
            Name = l.NameSnapshot,
            Set = l.SetSnapshot,
            Condition = l.ConditionSnapshot,
            IsFoil = l.IsFoilSnapshot,
            Quantity = l.Quantity,
            UnitSalePrice = l.UnitSalePrice,
            LineTotal = l.Quantity * l.UnitSalePrice,
        }).ToList();

        var itemsTotal = receiptLines.Sum(l => l.LineTotal);

        string? logoAbs = null;
        if (!string.IsNullOrWhiteSpace(company.LogoPath))
        {
            var candidate = Path.Combine(dataPathService.DataDirectory, company.LogoPath);
            if (File.Exists(candidate)) logoAbs = candidate;
        }

        return new ReceiptDocument
        {
            CompanyName = company.Name,
            CompanyAddressBlock = JoinBlock(
                company.AddressLine1, company.AddressLine2,
                JoinInline(company.City, company.State, company.PostalCode), company.Country),
            CompanyLogoAbsolutePath = logoAbs,
            CompanyEmail = company.Email,
            CompanyPhone = company.Phone,

            OrderNumber = order.OrderNumber,
            OrderDate = order.OrderDate,
            TrackingNumber = order.TrackingNumber,
            Carrier = order.Carrier,

            CustomerName = customer?.Name ?? "",
            CustomerAddressBlock = customer is null ? null : JoinBlock(
                customer.AddressLine1, customer.AddressLine2,
                JoinInline(customer.City, customer.State, customer.PostalCode), customer.Country),

            Lines = receiptLines,
            ShowPrices = receipt.ShowPrices,
            ItemsTotal = itemsTotal,
            Shipping = order.ShippingChargedToBuyer,
            GrandTotal = itemsTotal + order.ShippingChargedToBuyer,
            FooterText = receipt.FooterText,

            WidthMm = receipt.WidthMm,
            MarginMm = receipt.MarginMm,
            FontPointSize = receipt.FontPointSize,
        };
    }

    /// <summary>Joins non-empty parts with newlines (multi-line address block).</summary>
    private static string? JoinBlock(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return kept.Count == 0 ? null : string.Join("\n", kept);
    }

    /// <summary>Joins non-empty parts with spaces (e.g. "City ST 12345").</summary>
    private static string? JoinInline(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return kept.Count == 0 ? null : string.Join(" ", kept);
    }
}
```

*Verify* `Customer` exposes `AddressLine1/AddressLine2/City/State/PostalCode/Country` (per the phase-2 model in the parent spec §3). If a property name differs, adjust the mapping.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~ReceiptServiceTests`
Expected: PASS.

- [ ] **Step 7: Register in DI**

In `OmniCard/App.xaml.cs`, in the "Sales & fulfillment" block (near line 124), add:

```csharp
            services.AddSingleton<IReceiptService, ReceiptService>();
```

- [ ] **Step 8: Build**

Run: `dotnet build OmniCard.sln`
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add OmniCard.Shared/Models/ReceiptDocument.cs OmniCard.Shared/Interfaces/IReceiptService.cs \
        OmniCard.Collection/ReceiptService.cs OmniCard/App.xaml.cs \
        OmniCard.Tests/Services/ReceiptServiceTests.cs
git commit -m "feat(sales): ReceiptDocument content model + ReceiptService"
```

---

### Task 6: ReceiptPdfExporter (QuestPDF)

**Files:**
- Create: `OmniCard.Shared/Interfaces/IReceiptPdfExporter.cs`, `OmniCard.Audit/ReceiptPdfExporter.cs`
- Modify: `OmniCard/App.xaml.cs`
- Test: `OmniCard.Tests/Services/ReceiptPdfExporterTests.cs`

**Interfaces:**
- Consumes: `ReceiptDocument` (Task 5).
- Produces: `IReceiptPdfExporter.Export(ReceiptDocument doc, string filePath)`.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/ReceiptPdfExporterTests.cs`:

```csharp
using System.IO;
using OmniCard.Audit;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ReceiptPdfExporterTests : IDisposable
{
    private readonly string _tempDir;

    public ReceiptPdfExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardReceipt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    private static ReceiptDocument Sample(bool showPrices) => new()
    {
        CompanyName = "Acme Cards",
        CompanyAddressBlock = "1 Main\nReno NV 89501",
        OrderNumber = "TCG-100",
        OrderDate = new DateTime(2026, 7, 21),
        TrackingNumber = "1Z999",
        Carrier = "UPS",
        CustomerName = "Ada Lovelace",
        CustomerAddressBlock = "12 Analytical Way\nLondon EC1",
        Lines =
        [
            new ReceiptLine { Name = "Sol Ring", Set = "Commander", Condition = "NM", IsFoil = true, Quantity = 1, UnitSalePrice = 3.50m, LineTotal = 3.50m },
            new ReceiptLine { Name = "Counterspell", Set = "MH2", Condition = "LP", IsFoil = false, Quantity = 2, UnitSalePrice = 1.00m, LineTotal = 2.00m },
        ],
        ShowPrices = showPrices,
        ItemsTotal = 5.50m,
        Shipping = 1.00m,
        GrandTotal = 6.50m,
        FooterText = "Thank you!",
        WidthMm = 80,
        MarginMm = 4,
        FontPointSize = 9,
    };

    [Fact]
    public void Export_WritesValidPdf_WithPrices()
    {
        var path = Path.Combine(_tempDir, "receipt.pdf");
        new ReceiptPdfExporter().Export(Sample(showPrices: true), path);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void Export_WritesValidPdf_WithoutPrices()
    {
        var path = Path.Combine(_tempDir, "receipt_noprice.pdf");
        new ReceiptPdfExporter().Export(Sample(showPrices: false), path);
        Assert.True(File.Exists(path));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~ReceiptPdfExporterTests`
Expected: FAIL — `ReceiptPdfExporter` doesn't exist.

- [ ] **Step 3: Create the interface**

Create `OmniCard.Shared/Interfaces/IReceiptPdfExporter.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IReceiptPdfExporter
{
    void Export(ReceiptDocument document, string filePath);
}
```

- [ ] **Step 4: Implement `ReceiptPdfExporter`**

Create `OmniCard.Audit/ReceiptPdfExporter.cs`:

```csharp
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

public sealed class ReceiptPdfExporter : IReceiptPdfExporter
{
    public void Export(ReceiptDocument doc, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                // Continuous roll sized to the configured thermal width.
                page.ContinuousSize((float)doc.WidthMm, Unit.Millimetre);
                page.Margin((float)doc.MarginMm, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize((float)doc.FontPointSize));

                page.Content().Column(col =>
                {
                    // Header: logo + company
                    if (doc.CompanyLogoAbsolutePath is not null && File.Exists(doc.CompanyLogoAbsolutePath))
                        col.Item().AlignCenter().MaxHeight(60).Image(doc.CompanyLogoAbsolutePath).FitHeight();

                    if (!string.IsNullOrWhiteSpace(doc.CompanyName))
                        col.Item().AlignCenter().Text(doc.CompanyName).Bold().FontSize((float)doc.FontPointSize + 3);
                    if (!string.IsNullOrWhiteSpace(doc.CompanyAddressBlock))
                        col.Item().AlignCenter().Text(doc.CompanyAddressBlock);
                    if (!string.IsNullOrWhiteSpace(doc.CompanyPhone))
                        col.Item().AlignCenter().Text(doc.CompanyPhone);
                    if (!string.IsNullOrWhiteSpace(doc.CompanyEmail))
                        col.Item().AlignCenter().Text(doc.CompanyEmail);

                    col.Item().PaddingVertical(4).LineHorizontal(0.5f);

                    // Order info
                    if (!string.IsNullOrWhiteSpace(doc.OrderNumber))
                        col.Item().Text($"Order: {doc.OrderNumber}").Bold();
                    col.Item().Text($"Date: {doc.OrderDate:yyyy-MM-dd}");
                    if (!string.IsNullOrWhiteSpace(doc.Carrier) || !string.IsNullOrWhiteSpace(doc.TrackingNumber))
                        col.Item().Text($"Ship: {doc.Carrier} {doc.TrackingNumber}".Trim());

                    // Customer
                    col.Item().PaddingTop(4).Text("Ship to:").Bold();
                    col.Item().Text(doc.CustomerName);
                    if (!string.IsNullOrWhiteSpace(doc.CustomerAddressBlock))
                        col.Item().Text(doc.CustomerAddressBlock);

                    col.Item().PaddingVertical(4).LineHorizontal(0.5f);

                    // Line items
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);      // name/set/cond
                            columns.ConstantColumn(24);     // qty
                            if (doc.ShowPrices)
                                columns.ConstantColumn(48); // line total
                        });

                        foreach (var line in doc.Lines)
                        {
                            var label = line.Name
                                        + (string.IsNullOrWhiteSpace(line.Set) ? "" : $" ({line.Set})")
                                        + (string.IsNullOrWhiteSpace(line.Condition) ? "" : $" {line.Condition}")
                                        + (line.IsFoil ? " *foil" : "");
                            table.Cell().PaddingVertical(1).Text(label);
                            table.Cell().PaddingVertical(1).AlignRight().Text($"x{line.Quantity}");
                            if (doc.ShowPrices)
                                table.Cell().PaddingVertical(1).AlignRight().Text($"${line.LineTotal:N2}");
                        }
                    });

                    // Totals
                    if (doc.ShowPrices)
                    {
                        col.Item().PaddingVertical(4).LineHorizontal(0.5f);
                        col.Item().AlignRight().Text($"Items: ${doc.ItemsTotal:N2}");
                        col.Item().AlignRight().Text($"Shipping: ${doc.Shipping:N2}");
                        col.Item().AlignRight().Text($"Total: ${doc.GrandTotal:N2}").Bold();
                    }

                    // Footer
                    if (!string.IsNullOrWhiteSpace(doc.FooterText))
                        col.Item().PaddingTop(8).AlignCenter().Text(doc.FooterText);
                });
            });
        }).GeneratePdf(filePath);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter FullyQualifiedName~ReceiptPdfExporterTests`
Expected: PASS. If the `ContinuousSize`/`Unit` API differs in QuestPDF 2026.7.0, consult the installed package's `Page` API and use the equivalent continuous-size call (width in millimetres); the rest of the layout is version-stable.

- [ ] **Step 6: Register in DI**

In `OmniCard/App.xaml.cs`, near the other PDF exporters (line ~138-142), add:

```csharp
            services.AddSingleton<IReceiptPdfExporter, ReceiptPdfExporter>();
```

- [ ] **Step 7: Build**

Run: `dotnet build OmniCard.sln`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Interfaces/IReceiptPdfExporter.cs OmniCard.Audit/ReceiptPdfExporter.cs \
        OmniCard/App.xaml.cs OmniCard.Tests/Services/ReceiptPdfExporterTests.cs
git commit -m "feat(sales): QuestPDF receipt PDF exporter"
```

---

### Task 7: ReceiptPrinter (FlowDocument) + Orders view Print/Export wiring

**Files:**
- Create: `OmniCard/Views/Sales/ReceiptPrinter.cs`
- Modify: `OmniCard/Views/Sales/OrdersViewModel.cs`, `OmniCard/Views/Sales/OrdersView.xaml`

**Interfaces:**
- Consumes: `IReceiptService.BuildReceipt` (Task 5), `IReceiptPdfExporter.Export` (Task 6), `ReceiptDocument` (Task 5).
- Produces: `ReceiptPrinter.Print(ReceiptDocument)`; `OrdersViewModel.PrintReceiptCommand`, `ExportPdfCommand`.

*Verified by build + human E2E (WPF printing/dialogs). The receipt content is already unit-tested via `ReceiptService`.*

- [ ] **Step 1: Implement `ReceiptPrinter`**

Create `OmniCard/Views/Sales/ReceiptPrinter.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// Prints a <see cref="ReceiptDocument"/> as a WPF <see cref="FlowDocument"/> sized to the
/// configured thermal width. Mirrors <see cref="PickListPrinter"/>. No preview (the PDF export
/// is the preview).
/// </summary>
public static class ReceiptPrinter
{
    private const double DipPerMm = 96.0 / 25.4;

    public static void Print(ReceiptDocument receipt)
    {
        // Phase 3: the user selects the thermal printer in the PrintDialog. (Pre-selecting a
        // saved default printer is a deferred nice-to-have — see the note after this code.)
        var dialog = new PrintDialog();

        var pageWidth = receipt.WidthMm * DipPerMm;
        var padding = receipt.MarginMm * DipPerMm;

        var doc = new FlowDocument
        {
            PageWidth = pageWidth,
            ColumnWidth = double.PositiveInfinity,
            PagePadding = new Thickness(padding),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = receipt.FontPointSize * 96.0 / 72.0,   // pt → DIP
        };

        // Logo
        if (receipt.CompanyLogoAbsolutePath is not null && File.Exists(receipt.CompanyLogoAbsolutePath))
        {
            var img = new Image
            {
                Source = new BitmapImage(new System.Uri(receipt.CompanyLogoAbsolutePath)),
                MaxHeight = 60,
                Stretch = Stretch.Uniform,
            };
            var logoPara = new Paragraph(new InlineUIContainer(img)) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0) };
            doc.Blocks.Add(logoPara);
        }

        AddCentered(doc, receipt.CompanyName, bold: true, sizeDelta: 3);
        AddCentered(doc, receipt.CompanyAddressBlock);
        AddCentered(doc, receipt.CompanyPhone);
        AddCentered(doc, receipt.CompanyEmail);

        AddSeparator(doc);

        if (!string.IsNullOrWhiteSpace(receipt.OrderNumber)) AddLine(doc, $"Order: {receipt.OrderNumber}", bold: true);
        AddLine(doc, $"Date: {receipt.OrderDate:yyyy-MM-dd}");
        var ship = $"{receipt.Carrier} {receipt.TrackingNumber}".Trim();
        if (!string.IsNullOrWhiteSpace(ship)) AddLine(doc, $"Ship: {ship}");

        AddLine(doc, "Ship to:", bold: true);
        AddLine(doc, receipt.CustomerName);
        if (!string.IsNullOrWhiteSpace(receipt.CustomerAddressBlock)) AddLine(doc, receipt.CustomerAddressBlock);

        AddSeparator(doc);

        // Line items
        foreach (var line in receipt.Lines)
        {
            var label = line.Name
                        + (string.IsNullOrWhiteSpace(line.Set) ? "" : $" ({line.Set})")
                        + (string.IsNullOrWhiteSpace(line.Condition) ? "" : $" {line.Condition}")
                        + (line.IsFoil ? " *foil" : "");
            var text = receipt.ShowPrices
                ? $"{label}  x{line.Quantity}   ${line.LineTotal:N2}"
                : $"{label}  x{line.Quantity}";
            AddLine(doc, text);
        }

        if (receipt.ShowPrices)
        {
            AddSeparator(doc);
            AddLine(doc, $"Items: ${receipt.ItemsTotal:N2}", align: TextAlignment.Right);
            AddLine(doc, $"Shipping: ${receipt.Shipping:N2}", align: TextAlignment.Right);
            AddLine(doc, $"Total: ${receipt.GrandTotal:N2}", bold: true, align: TextAlignment.Right);
        }

        if (!string.IsNullOrWhiteSpace(receipt.FooterText))
            AddCentered(doc, receipt.FooterText);

        if (dialog.ShowDialog() != true) return;
        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Receipt");
    }

    private static void AddCentered(FlowDocument doc, string? text, bool bold = false, double sizeDelta = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var run = new Run(text);
        var para = new Paragraph(run) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
        if (bold) para.FontWeight = FontWeights.Bold;
        if (sizeDelta != 0) para.FontSize = doc.FontSize + sizeDelta;
        doc.Blocks.Add(para);
    }

    private static void AddLine(FlowDocument doc, string? text, bool bold = false, TextAlignment align = TextAlignment.Left)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var para = new Paragraph(new Run(text)) { TextAlignment = align, Margin = new Thickness(0, 0, 0, 2) };
        if (bold) para.FontWeight = FontWeights.Bold;
        doc.Blocks.Add(para);
    }

    private static void AddSeparator(FlowDocument doc)
        => doc.Blocks.Add(new Paragraph(new Run(new string('-', 32))) { Margin = new Thickness(0, 2, 0, 2) });
}
```

*Deferred nice-to-have (do NOT build now):* honoring `ReceiptSettings.DefaultPrinterName` would mean adding it to `ReceiptDocument` and selecting the matching `PrintQueue` on a `LocalPrintServer` (from `System.Printing`) before `ShowDialog()`. Phase 3 keeps it simple — the user picks the printer in the dialog.

- [ ] **Step 2: Add commands to `OrdersViewModel`**

In `OmniCard/Views/Sales/OrdersViewModel.cs`, add `IReceiptService` and `IReceiptPdfExporter` to the primary constructor:

```csharp
public partial class OrdersViewModel(
    IOrderService orderService,
    ICustomerService customerService,
    IListingService listingService,
    IReceiptService receiptService,
    IReceiptPdfExporter receiptPdfExporter) : ObservableObject
```

Add two commands (place after `SetStatus`):

```csharp
    [RelayCommand]
    public void PrintReceipt()
    {
        if (SelectedOrder is null) { StatusMessage = "Select an order first."; return; }
        var doc = receiptService.BuildReceipt(SelectedOrder.Id);
        ReceiptPrinter.Print(doc);
    }

    [RelayCommand]
    public void ExportPdf()
    {
        if (SelectedOrder is null) { StatusMessage = "Select an order first."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export receipt PDF",
            Filter = "PDF|*.pdf",
            FileName = $"receipt-{SelectedOrder.OrderNumber ?? SelectedOrder.Id.ToString()}.pdf",
        };
        if (dialog.ShowDialog() != true) return;

        var doc = receiptService.BuildReceipt(SelectedOrder.Id);
        receiptPdfExporter.Export(doc, dialog.FileName);
        StatusMessage = $"Exported to {dialog.FileName}";
    }
```

`OrdersViewModel` is registered as a singleton (App.xaml.cs line ~79); DI will supply the two new dependencies (registered in Tasks 5 & 6). No registration change needed.

- [ ] **Step 3: Add buttons to `OrdersView.xaml`**

In the status-buttons `StackPanel` (lines ~83-95), add two buttons before the total `TextBlock` (after the Cancel button):

```xml
                <Button Content="Print Receipt" Command="{Binding PrintReceiptCommand}" Padding="12,4" Margin="0,0,8,0"/>
                <Button Content="Export PDF" Command="{Binding ExportPdfCommand}" Padding="12,4" Margin="0,0,16,0"/>
```

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard.sln`
Expected: Build succeeded.

- [ ] **Step 5: Run the full test project**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS (no regressions; `OrdersViewModelTests` still pass — if they construct `OrdersViewModel` directly, add fakes for the two new constructor params).

*If `OrdersViewModelTests` construct `OrdersViewModel(...)` directly*, add minimal fakes:

```csharp
    private sealed class FakeReceiptService : IReceiptService
    { public ReceiptDocument BuildReceipt(int orderId) => new(); }
    private sealed class FakeReceiptPdfExporter : IReceiptPdfExporter
    { public void Export(ReceiptDocument document, string filePath) { } }
```

and pass `new FakeReceiptService(), new FakeReceiptPdfExporter()` to the constructor calls.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Sales/ReceiptPrinter.cs OmniCard/Views/Sales/OrdersViewModel.cs \
        OmniCard/Views/Sales/OrdersView.xaml OmniCard.Tests/Views/Sales/OrdersViewModelTests.cs
git commit -m "feat(sales): print receipt + export receipt PDF from Orders view"
```

- [ ] **Step 7: Human E2E note**

Flag for the reviewer: with a company profile + logo + receipt settings configured, select an order and (a) **Export PDF** → open it, confirm logo/company/order/customer/lines/totals/footer render at the configured width and `ShowPrices` toggles totals; (b) **Print Receipt** → confirm the thermal printer prints at the configured width. Test both `ShowPrices = true` and `false`.

---

## Self-Review

**1. Spec coverage:**
- §1 receipt print + PDF → Tasks 6, 7. ✅
- §1 company profile + receipt settings persist (incl. logo, data-dir migration) → Task 1 (`SetLogo` relative path, JSON in data dir). ✅
- §1 Settings page with Display / Data Location / Sales & Receipts → Tasks 3, 4. ✅
- §3/§4 data model + service extension → Task 1. ✅
- §5 `ReceiptDocument` + `ReceiptService` + `ReceiptPrinter` + `ReceiptPdfExporter` → Tasks 5, 6, 7. ✅
- §6 Display binds to RootViewModel; Data Location section; For-Sale moved out of Pick List; menu cleanup; Orders wiring → Tasks 3, 4, 7. ✅
- §7 testing (settings round-trip, ReceiptService assembly, PDF smoke, human E2E) → Tasks 1, 2, 5, 6 + E2E notes. ✅
- §8 two build checkpoints → Build Step 1 (Tasks 1-4), Build Step 2 (Tasks 5-7). ✅
- Non-goals honored: no P&L changes; no in-app graphical preview; no persistence rewrite. ✅

**2. Placeholder scan:** The only intentionally-flagged artifact is the dead code in Task 7 Step 1, which Step 2 explicitly removes with concrete instructions. No `TBD`/`TODO`/"handle edge cases" left. Two "*Verify*" notes (InvertBooleanConverter visibility; Customer address property names) point at exact things to confirm against existing code, with the fallback action stated.

**3. Type consistency:** `ISalesSettingsService` members (`GetCompany/SaveCompany/GetReceipt/SaveReceipt/SetLogo`) are defined in Task 1 and consumed identically in Tasks 2, 5. `ReceiptDocument`/`ReceiptLine` field names defined in Task 5 are used identically in Tasks 6, 7. `IReceiptService.BuildReceipt(int)` and `IReceiptPdfExporter.Export(ReceiptDocument, string)` signatures match across definition and use. `SalesSettingsViewModel` members (`Load`, `SaveCommand`, `PickLogoCommand`, `Company`, `Receipt`, `ForSaleLocation`, `Locations`) match between Task 2 (impl) and Task 4 (XAML bindings).

**Known verification points for the implementer (not blockers):**
- QuestPDF 2026.7.0 `ContinuousSize(float, Unit)` / `Margin(float, Unit)` — confirm exact API on first PDF build (Task 6 Step 5).
- `Customer` address property names (Task 5 Step 5).
- `InvertBooleanConverter` accessibility for reuse (Task 3 Step 3).
- Existing `SalesViewModelTests` / `OrdersViewModelTests` may need updated constructor args / assertions (Tasks 4, 7 call this out).
