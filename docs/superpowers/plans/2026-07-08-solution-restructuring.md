# OmniCard Solution Restructuring — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the monolithic OmniCard WPF app into 10 feature-based projects to improve maintainability, testability, reusability, and extensibility.

**Architecture:** Extract interfaces/models/DTOs into OmniCard.Shared as the contracts layer, DbContexts into OmniCard.Data, then feature implementations into isolated projects (Imaging, CardMatching, Collection, eBay, Scanner, Audit, Controls). Feature projects depend only on Shared and Data, never on each other. The App shell retains Views/ViewModels and the DI composition root.

**Tech Stack:** .NET 10.0, WPF, Entity Framework Core, CommunityToolkit.Mvvm, MaterialDesignThemes, NTwain, QuestPDF, CsvHelper

## Global Constraints

- Target framework: `net10.0` for platform-agnostic projects, `net10.0-windows10.0.22621.0` for WPF projects, `net10.0-windows` for Windows-only non-WPF projects
- Nullable and ImplicitUsings enabled on all projects
- Feature projects MUST NOT reference each other — only Shared and Data (exceptions: Controls → Imaging for static converter caches, CardMatching → Imaging, Collection → Imaging + CardMatching since CardService orchestrates matching)
- All existing tests must continue to pass after each task
- Namespace must match project name (e.g., `OmniCard.Data` namespace in `OmniCard.Data` project)
- Interface files are named `I{ServiceName}.cs` (one interface per file in Shared)
- `InternalsVisibleTo` for OmniCard.Tests on projects that need it

---

### Task 1: Create Project Scaffolding

Create all 8 new project directories and `.csproj` files, add them to the solution, and verify the solution builds (empty projects).

**Files:**
- Create: `OmniCard.Data/OmniCard.Data.csproj`
- Create: `OmniCard.Imaging/OmniCard.Imaging.csproj`
- Create: `OmniCard.CardMatching/OmniCard.CardMatching.csproj`
- Create: `OmniCard.Collection/OmniCard.Collection.csproj`
- Create: `OmniCard.eBay/OmniCard.eBay.csproj`
- Create: `OmniCard.Scanner/OmniCard.Scanner.csproj`
- Create: `OmniCard.Audit/OmniCard.Audit.csproj`
- Create: `OmniCard.Controls/OmniCard.Controls.csproj`
- Modify: `OmniCard.slnx`

**Interfaces:**
- Produces: 8 empty class library projects wired into the solution

- [ ] **Step 1: Create OmniCard.Data project**

```bash
cd d:/source/repos/OmniCard
mkdir -p OmniCard.Data
```

```xml
<!-- OmniCard.Data/OmniCard.Data.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.9" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create OmniCard.Imaging project**

```bash
mkdir -p OmniCard.Imaging
```

```xml
<!-- OmniCard.Imaging/OmniCard.Imaging.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
  </ItemGroup>
</Project>
```

Note: `AllowUnsafeBlocks` is needed because `PerceptualHashService` uses unsafe code for bitmap manipulation. Verify during implementation — if no unsafe code is in the moved files, remove this flag.

- [ ] **Step 3: Create OmniCard.CardMatching project**

```bash
mkdir -p OmniCard.CardMatching
```

```xml
<!-- OmniCard.CardMatching/OmniCard.CardMatching.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
    <ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
    <ProjectReference Include="..\OmniCard.Imaging\OmniCard.Imaging.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create OmniCard.Collection project**

```bash
mkdir -p OmniCard.Collection
```

```xml
<!-- OmniCard.Collection/OmniCard.Collection.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CsvHelper" Version="33.1.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
    <ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Create OmniCard.eBay project**

```bash
mkdir -p OmniCard.eBay
```

```xml
<!-- OmniCard.eBay/OmniCard.eBay.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AdysTech.CredentialManager" Version="3.1.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
    <ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create OmniCard.Scanner project**

```bash
mkdir -p OmniCard.Scanner
```

```xml
<!-- OmniCard.Scanner/OmniCard.Scanner.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="NTwain" Version="3.7.6" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Create OmniCard.Audit project**

```bash
mkdir -p OmniCard.Audit
```

```xml
<!-- OmniCard.Audit/OmniCard.Audit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="QuestPDF" Version="2026.7.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
    <ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 8: Create OmniCard.Controls project**

```bash
mkdir -p OmniCard.Controls
```

```xml
<!-- OmniCard.Controls/OmniCard.Controls.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MaterialDesignThemes" Version="5.3.2" />
    <PackageReference Include="SharpVectors.Wpf" Version="1.8.5" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 9: Update solution file**

Replace `OmniCard.slnx` to include all 12 projects:

```xml
<Solution>
  <Project Path="OmniCard.Shared/OmniCard.Shared.csproj" />
  <Project Path="OmniCard.Data/OmniCard.Data.csproj" />
  <Project Path="OmniCard.Imaging/OmniCard.Imaging.csproj" />
  <Project Path="OmniCard.CardMatching/OmniCard.CardMatching.csproj" />
  <Project Path="OmniCard.Collection/OmniCard.Collection.csproj" />
  <Project Path="OmniCard.eBay/OmniCard.eBay.csproj" />
  <Project Path="OmniCard.Scanner/OmniCard.Scanner.csproj" />
  <Project Path="OmniCard.Audit/OmniCard.Audit.csproj" />
  <Project Path="OmniCard.Controls/OmniCard.Controls.csproj" />
  <Project Path="OmniCard/OmniCard.csproj" />
  <Project Path="OmniCard.Tests/OmniCard.Tests.csproj" />
  <Project Path="OmniCard.Web/OmniCard.Web.csproj" />
</Solution>
```

- [ ] **Step 10: Build to verify scaffolding**

```bash
cd d:/source/repos/OmniCard
dotnet build OmniCard.slnx
```

Expected: BUILD SUCCEEDED (empty projects compile fine, existing projects unchanged)

- [ ] **Step 11: Commit**

```bash
git add OmniCard.Data/ OmniCard.Imaging/ OmniCard.CardMatching/ OmniCard.Collection/ OmniCard.eBay/ OmniCard.Scanner/ OmniCard.Audit/ OmniCard.Controls/ OmniCard.slnx
git commit -m "feat: scaffold 8 new projects for solution restructuring"
```

---

### Task 2: Expand OmniCard.Shared — Interfaces

Extract all service interfaces from their co-located implementation files into separate files in OmniCard.Shared. This is the prerequisite for all subsequent moves.

**Files:**
- Create: `OmniCard.Shared/Interfaces/ICardService.cs`
- Create: `OmniCard.Shared/Interfaces/ICardGameService.cs`
- Create: `OmniCard.Shared/Interfaces/IScryfallService.cs`
- Create: `OmniCard.Shared/Interfaces/IPerceptualHashService.cs`
- Create: `OmniCard.Shared/Interfaces/IOcrMatchingService.cs`
- Create: `OmniCard.Shared/Interfaces/IEbayAuthService.cs`
- Create: `OmniCard.Shared/Interfaces/IEbayCatalogService.cs`
- Create: `OmniCard.Shared/Interfaces/IEbayListingService.cs`
- Create: `OmniCard.Shared/Interfaces/IEbaySyncService.cs`
- Create: `OmniCard.Shared/Interfaces/IAuditService.cs`
- Create: `OmniCard.Shared/Interfaces/IAuditPdfExporter.cs`
- Create: `OmniCard.Shared/Interfaces/IScanDiagnosticService.cs`
- Create: `OmniCard.Shared/Interfaces/IDialogService.cs`
- Create: `OmniCard.Shared/Interfaces/IStorageContainerService.cs`
- Create: `OmniCard.Shared/Interfaces/ICollectionPresetService.cs`
- Create: `OmniCard.Shared/Interfaces/ICsvExportImportService.cs`
- Create: `OmniCard.Shared/Interfaces/IDataMigrationService.cs`
- Create: `OmniCard.Shared/Interfaces/IDataPathService.cs`
- Create: `OmniCard.Shared/Interfaces/ISealedProductService.cs`
- Create: `OmniCard.Shared/Interfaces/ICredentialStore.cs`
- Create: `OmniCard.Shared/Interfaces/ICollectionQueryService.cs` (NEW)
- Create: `OmniCard.Shared/Interfaces/IMismatchLogService.cs` (NEW)
- Modify: `OmniCard.Shared/OmniCard.Shared.csproj` (add CommunityToolkit.Mvvm)
- Modify: Each original service file in `OmniCard/Services/` — remove the interface definition, keep only the implementation class
- Delete: `OmniCard/Services/ICardGameService.cs`, `OmniCard/Services/IDataPathService.cs`, `OmniCard/Services/IOcrMatchingService.cs`, `OmniCard/Services/IDataMigrationService.cs` (standalone interface files that move entirely)

**Interfaces:**
- Produces: All service interfaces in `OmniCard.Shared/Interfaces/` namespace `OmniCard.Interfaces`

**Process for each interface:**

1. Read the current interface definition from the service file (e.g., `ICardService` from `CardSevice.cs:16-53`)
2. Create a new file in `OmniCard.Shared/Interfaces/` with just the interface
3. Update the namespace to `OmniCard.Interfaces`
4. Remove the interface definition from the original service file
5. Add `using OmniCard.Interfaces;` to the original service file

- [ ] **Step 1: Add CommunityToolkit.Mvvm to OmniCard.Shared**

Some interfaces use `INotifyPropertyChanged` (e.g., `IEbayAuthService`). Update the Shared csproj:

```xml
<!-- Add to OmniCard.Shared/OmniCard.Shared.csproj ItemGroup -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
```

- [ ] **Step 2: Create Interfaces directory and extract each interface**

```bash
mkdir -p OmniCard.Shared/Interfaces
```

For each service file in `OmniCard/Services/` that contains an interface:
1. Read the interface definition
2. Create `OmniCard.Shared/Interfaces/I{Name}.cs` with namespace `OmniCard.Interfaces`
3. Include all necessary `using` statements for types the interface references (these types are in `OmniCard.Models` which is still in the main project — but will be moved in Task 3)
4. Remove the interface from the original file
5. Add `using OmniCard.Interfaces;` to the original file

**Critical:** The interfaces reference types like `CardMatch`, `ScannedCard`, `CollectionCard`, etc. from `OmniCard.Models`. These models haven't moved yet, so temporarily the Shared project will need a reference to the main project — which creates a circular dependency. **Solution:** Move the models FIRST (Step 3 below), then extract interfaces.

- [ ] **Step 3: Move all Models to OmniCard.Shared**

Move all 44 files from `OmniCard/Models/` to `OmniCard.Shared/Models/`:

```bash
# Move all model files
cp OmniCard/Models/*.cs OmniCard.Shared/Models/
```

Update namespace in each moved file from `OmniCard.Models` to `OmniCard.Models` (namespace stays the same — models are already in this namespace, and keeping it avoids mass using-statement changes across the codebase).

Verify that no model files have dependencies on `OmniCard.Services` or `OmniCard.Data` namespaces. If any do (e.g., models referencing DbContext types), those dependencies need to be removed or inverted.

**Known issue:** `CardPreviewImageConverter` in `Converters.cs` references `ScanImageCache.Instance` and `CardArtCache.Instance` — these are service types. The converters file stays in Views/Root (not a model). Only pure model/DTO/enum files move.

After moving, delete the originals from `OmniCard/Models/`.

- [ ] **Step 4: Update OmniCard.Shared.csproj if needed**

If any moved models need packages (check for `[NotMapped]` attribute which needs `System.ComponentModel.DataAnnotations`), add:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Abstractions" Version="10.0.9" />
```

(`CollectionCard.cs` uses `[NotMapped]` which is in `System.ComponentModel.DataAnnotations.Schema` — available in EF Core abstractions or the base framework.)

- [ ] **Step 5: Extract all interfaces to OmniCard.Shared/Interfaces/**

Now that models are in Shared, create each interface file. For every interface found in Step 2 of the exploration:

Read the interface from its source file, create in `OmniCard.Shared/Interfaces/`, remove from source file.

Each interface file follows this template:
```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IServiceName
{
    // exact methods from original
}
```

- [ ] **Step 6: Create the two NEW interfaces**

```csharp
// OmniCard.Shared/Interfaces/ICollectionQueryService.cs
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICollectionQueryService
{
    Task<List<LocationTileSummary>> GetLocationOverviewsAsync();
}
```

```csharp
// OmniCard.Shared/Interfaces/IMismatchLogService.cs
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IMismatchLogService
{
    Task LogMismatchAsync(CardMatch oldMatch, CardMatch newMatch, ScannedCard scannedCard);
}
```

- [ ] **Step 7: Move ViewModel base classes to OmniCard.Shared**

Move `OmniCard/Views/IViewModel.cs`, `OmniCard/Views/ViewModel.cs`, `OmniCard/Views/IView.cs` to `OmniCard.Shared/Views/`:

```bash
mkdir -p OmniCard.Shared/Views
cp OmniCard/Views/IViewModel.cs OmniCard.Shared/Views/
cp OmniCard/Views/ViewModel.cs OmniCard.Shared/Views/
cp OmniCard/Views/IView.cs OmniCard.Shared/Views/
```

Update namespaces to `OmniCard.Views` (stays the same). Remove originals from `OmniCard/Views/`.

- [ ] **Step 8: Delete standalone interface files from OmniCard/Services/**

```bash
rm OmniCard/Services/ICardGameService.cs
rm OmniCard/Services/IDataPathService.cs
rm OmniCard/Services/IOcrMatchingService.cs
rm OmniCard/Services/IDataMigrationService.cs
```

- [ ] **Step 9: Add `using OmniCard.Interfaces;` to all service files**

Every service implementation file in `OmniCard/Services/` that implements an interface needs `using OmniCard.Interfaces;` added to its using statements.

- [ ] **Step 10: Build and verify**

```bash
dotnet build OmniCard.slnx
```

Expected: BUILD SUCCEEDED. All interfaces now live in Shared, models live in Shared, implementations still in main project.

- [ ] **Step 11: Run tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "refactor: move interfaces and models to OmniCard.Shared"
```

---

### Task 3: Create OmniCard.Data — Move DbContexts and Migration Services

Move all database context classes and migration-related services to OmniCard.Data.

**Files:**
- Move: `OmniCard.Shared/Data/CollectionDbContext.cs` → `OmniCard.Data/CollectionDbContext.cs`
- Move: `OmniCard/Data/ScryfallDbContext.cs` → `OmniCard.Data/ScryfallDbContext.cs`
- Move: `OmniCard/Data/OptcgDbContext.cs` → `OmniCard.Data/OptcgDbContext.cs`
- Move: `OmniCard/Data/SealedProductDbContext.cs` → `OmniCard.Data/SealedProductDbContext.cs`
- Move: `OmniCard/Data/PropertyBuilderExtensions.cs` → `OmniCard.Data/PropertyBuilderExtensions.cs`
- Move: `OmniCard/Services/DataMigrationService.cs` → `OmniCard.Data/DataMigrationService.cs`
- Move: `OmniCard/Services/CollectionMigrationService.cs` → `OmniCard.Data/CollectionMigrationService.cs`
- Move: `OmniCard/Services/DataPathService.cs` → `OmniCard.Data/DataPathService.cs`
- Modify: `OmniCard/OmniCard.csproj` — add reference to `OmniCard.Data`
- Modify: `OmniCard.Web/OmniCard.Web.csproj` — add reference to `OmniCard.Data`

**Interfaces:**
- Consumes: Interfaces from Shared (`IDataPathService`, `IDataMigrationService`), Models from Shared
- Produces: All 4 DbContext classes and migration services accessible via `OmniCard.Data` namespace

- [ ] **Step 1: Move DbContext files**

```bash
# Move from OmniCard.Shared
mv OmniCard.Shared/Data/CollectionDbContext.cs OmniCard.Data/

# Move from OmniCard/Data/
mv OmniCard/Data/ScryfallDbContext.cs OmniCard.Data/
mv OmniCard/Data/OptcgDbContext.cs OmniCard.Data/
mv OmniCard/Data/SealedProductDbContext.cs OmniCard.Data/
mv OmniCard/Data/PropertyBuilderExtensions.cs OmniCard.Data/
```

- [ ] **Step 2: Move migration services**

```bash
mv OmniCard/Services/DataMigrationService.cs OmniCard.Data/
mv OmniCard/Services/CollectionMigrationService.cs OmniCard.Data/
mv OmniCard/Services/DataPathService.cs OmniCard.Data/
```

- [ ] **Step 3: Update namespaces in all moved files**

Change namespace from `OmniCard.Data` / `OmniCard.Services` to `OmniCard.Data` for all moved files. Add `using OmniCard.Models;` and `using OmniCard.Interfaces;` where needed.

CollectionDbContext is already namespace `OmniCard.Data` — no change needed.
ScryfallDbContext, OptcgDbContext, SealedProductDbContext are namespace `OmniCard.Data` — no change needed.
DataMigrationService, CollectionMigrationService, DataPathService: change from `OmniCard.Services` to `OmniCard.Data`.

- [ ] **Step 4: Remove OmniCard.Shared/Data/ directory**

```bash
rmdir OmniCard.Shared/Data
```

Update `OmniCard.Shared.csproj` — remove the `Microsoft.EntityFrameworkCore.Sqlite` package since CollectionDbContext no longer lives here.

- [ ] **Step 5: Update project references**

Add to `OmniCard/OmniCard.csproj`:
```xml
<ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
```

Add to `OmniCard.Web/OmniCard.Web.csproj`:
```xml
<ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
```

- [ ] **Step 6: Update using statements in consuming files**

Files that reference `OmniCard.Data` namespace (DbContexts) need no change — namespace is the same.
Files that referenced `DataMigrationService`, `CollectionMigrationService`, or `DataPathService` via `OmniCard.Services` now need `using OmniCard.Data;` instead. The main consumer is `App.xaml.cs`.

- [ ] **Step 7: Build and verify**

```bash
dotnet build OmniCard.slnx
```

- [ ] **Step 8: Run tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: move DbContexts and migration services to OmniCard.Data"
```

---

### Task 4: Create OmniCard.Imaging — Move Image Processing Services

Move perceptual hashing, OCR matching, and image caching to OmniCard.Imaging.

**Files:**
- Move: `OmniCard/Services/PerceptualHashService.cs` → `OmniCard.Imaging/PerceptualHashService.cs`
- Move: `OmniCard/Services/OcrMatchingService.cs` → `OmniCard.Imaging/OcrMatchingService.cs`
- Move: `OmniCard/Services/CardArtCache.cs` → `OmniCard.Imaging/CardArtCache.cs`
- Move: `OmniCard/Services/ScanImageCache.cs` → `OmniCard.Imaging/ScanImageCache.cs`
- Modify: `OmniCard/OmniCard.csproj` — add reference to `OmniCard.Imaging`

**Interfaces:**
- Consumes: `IPerceptualHashService`, `IOcrMatchingService` from Shared
- Produces: Image processing implementations in `OmniCard.Imaging` namespace

- [ ] **Step 1: Move files**

```bash
mv OmniCard/Services/PerceptualHashService.cs OmniCard.Imaging/
mv OmniCard/Services/OcrMatchingService.cs OmniCard.Imaging/
mv OmniCard/Services/CardArtCache.cs OmniCard.Imaging/
mv OmniCard/Services/ScanImageCache.cs OmniCard.Imaging/
```

- [ ] **Step 2: Update namespaces**

Change all moved files from `namespace OmniCard.Services;` to `namespace OmniCard.Imaging;`.

Add `using OmniCard.Interfaces;` and `using OmniCard.Models;` as needed.

- [ ] **Step 3: Check for WPF dependencies in Imaging**

`CardArtCache` and `ScanImageCache` may use `BitmapImage` or other WPF types. If so, the Imaging project needs:
- Change framework to `net10.0-windows10.0.22621.0` and add `<UseWPF>true</UseWPF>`, OR
- Refactor the cache to return byte arrays instead of BitmapImage (preferred but more work)

Check the actual file contents during implementation. If WPF types are used, add WPF support to the csproj.

- [ ] **Step 4: Update project reference**

Add to `OmniCard/OmniCard.csproj`:
```xml
<ProjectReference Include="..\OmniCard.Imaging\OmniCard.Imaging.csproj" />
```

- [ ] **Step 5: Update using statements in consumers**

Files that reference `PerceptualHashService`, `OcrMatchingService`, `ScanImageCache`, or `CardArtCache` by class name (not interface) need `using OmniCard.Imaging;`. Key consumers:
- `App.xaml.cs` (DI registration)
- `CardSevice.cs` (uses `ScanImageCache` directly)
- Converters in `Views/Root/` (use `ScanImageCache.Instance`, `CardArtCache.Instance`)

- [ ] **Step 6: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: move image processing services to OmniCard.Imaging"
```

---

### Task 5: Create OmniCard.CardMatching — Move Game Services

Move Scryfall and OPTCG game services to OmniCard.CardMatching.

**Files:**
- Move: `OmniCard/Services/ScryfallService.cs` → `OmniCard.CardMatching/ScryfallService.cs`
- Move: `OmniCard/Services/OptcgService.cs` → `OmniCard.CardMatching/OptcgService.cs`
- Move: `OmniCard/Services/ScryfallQueryParser.cs` → `OmniCard.CardMatching/ScryfallQueryParser.cs`
- Move: `OmniCard/Services/CardAttributeExtractor.cs` → `OmniCard.CardMatching/CardAttributeExtractor.cs`
- Modify: `OmniCard/OmniCard.csproj` — add reference to `OmniCard.CardMatching`

**Interfaces:**
- Consumes: `ICardGameService`, `IScryfallService`, `IPerceptualHashService` from Shared; DbContexts from Data; image services from Imaging
- Produces: Game-specific matching implementations in `OmniCard.CardMatching` namespace

- [ ] **Step 1: Move files**

```bash
mv OmniCard/Services/ScryfallService.cs OmniCard.CardMatching/
mv OmniCard/Services/OptcgService.cs OmniCard.CardMatching/
mv OmniCard/Services/ScryfallQueryParser.cs OmniCard.CardMatching/
mv OmniCard/Services/CardAttributeExtractor.cs OmniCard.CardMatching/
```

- [ ] **Step 2: Update namespaces to `OmniCard.CardMatching`**

Add required using statements: `using OmniCard.Interfaces;`, `using OmniCard.Models;`, `using OmniCard.Data;`, `using OmniCard.Imaging;`

- [ ] **Step 3: Update project reference and consuming files**

Add to `OmniCard/OmniCard.csproj`:
```xml
<ProjectReference Include="..\OmniCard.CardMatching\OmniCard.CardMatching.csproj" />
```

Update `App.xaml.cs`: add `using OmniCard.CardMatching;` for `ScryfallService` and `OptcgService` DI registrations.

Update any files that reference `CardAttributeExtractor` directly (e.g., `App.xaml.cs:542` `BackfillColorCardType` method).

- [ ] **Step 4: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: move game matching services to OmniCard.CardMatching"
```

---

### Task 6: Create OmniCard.Collection — Move Collection Management Services

Move CardService (with rename), storage, presets, CSV, and sealed product services.

**Files:**
- Move: `OmniCard/Services/CardSevice.cs` → `OmniCard.Collection/CardService.cs` (rename!)
- Move: `OmniCard/Services/StorageContainerService.cs` → `OmniCard.Collection/StorageContainerService.cs`
- Move: `OmniCard/Services/CollectionPresetService.cs` → `OmniCard.Collection/CollectionPresetService.cs`
- Move: `OmniCard/Services/CsvExportImportService.cs` → `OmniCard.Collection/CsvExportImportService.cs`
- Move: `OmniCard/Services/SealedProductService.cs` → `OmniCard.Collection/SealedProductService.cs`
- Move: `OmniCard/Services/SealedProductArchetypeRegistry.cs` → `OmniCard.Collection/SealedProductArchetypeRegistry.cs`
- Create: `OmniCard.Collection/CollectionQueryService.cs` (NEW)
- Create: `OmniCard.Collection/MismatchLogService.cs` (NEW)
- Modify: `OmniCard/OmniCard.csproj` — add reference to `OmniCard.Collection`

**Interfaces:**
- Consumes: `ICardService`, `IStorageContainerService`, `ICollectionPresetService`, `ICsvExportImportService`, `ISealedProductService`, `ICollectionQueryService`, `IMismatchLogService` from Shared; DbContexts from Data
- Produces: Collection management implementations in `OmniCard.Collection` namespace

- [ ] **Step 1: Move files**

```bash
mv OmniCard/Services/CardSevice.cs OmniCard.Collection/CardService.cs
mv OmniCard/Services/StorageContainerService.cs OmniCard.Collection/
mv OmniCard/Services/CollectionPresetService.cs OmniCard.Collection/
mv OmniCard/Services/CsvExportImportService.cs OmniCard.Collection/
mv OmniCard/Services/SealedProductService.cs OmniCard.Collection/
mv OmniCard/Services/SealedProductArchetypeRegistry.cs OmniCard.Collection/
```

- [ ] **Step 2: Rename class `CardSevice` → `CardService`**

In `OmniCard.Collection/CardService.cs`:
- Change `public sealed class CardSevice : ICardService` → `public sealed class CardService : ICardService`
- Change `ILogger<CardSevice>` → `ILogger<CardService>` throughout
- Update namespace to `OmniCard.Collection`

- [ ] **Step 3: Update namespaces in all moved files**

Change from `namespace OmniCard.Services;` to `namespace OmniCard.Collection;`.
Add: `using OmniCard.Interfaces;`, `using OmniCard.Models;`, `using OmniCard.Data;`

- [ ] **Step 4: Create CollectionQueryService**

Read the `LoadOverview()` method from `CollectionViewModel.cs` (lines 223-320) to extract the exact DB query logic. Create:

```csharp
// OmniCard.Collection/CollectionQueryService.cs
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class CollectionQueryService(
    IDbContextFactory<CollectionDbContext> dbContextFactory,
    IDataPathService dataPathService) : ICollectionQueryService
{
    public async Task<List<LocationTileSummary>> GetLocationOverviewsAsync()
    {
        // Extract the exact query logic from CollectionViewModel.LoadOverview()
        // This includes: grouping by container, counting cards, summing prices,
        // resolving cover images, etc.
        // Read CollectionViewModel.cs:223-320 for the exact implementation.
        using var context = dbContextFactory.CreateDbContext();
        // ... (copy the exact query logic from CollectionViewModel.LoadOverview)
    }
}
```

**Important:** Read `CollectionViewModel.cs:223-320` during implementation to copy the exact query logic.

- [ ] **Step 5: Create MismatchLogService**

Read `RootViewModel.cs:1200-1223` to extract the exact mismatch logging logic:

```csharp
// OmniCard.Collection/MismatchLogService.cs
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class MismatchLogService(
    IDbContextFactory<CollectionDbContext> dbContextFactory) : IMismatchLogService
{
    public async Task LogMismatchAsync(CardMatch oldMatch, CardMatch newMatch, ScannedCard scannedCard)
    {
        // Extract the exact logic from RootViewModel.LogMismatchIfHighConfidence()
        // Read RootViewModel.cs:1200-1223 for the exact implementation.
        using var ctx = dbContextFactory.CreateDbContext();
        // ... (copy logic)
    }
}
```

- [ ] **Step 6: Update DI registration in App.xaml.cs**

Replace `using OmniCard.Services;` references for moved types with `using OmniCard.Collection;`:
```csharp
services.AddSingleton<ICardService, CardService>(); // was CardSevice
services.AddSingleton<ICollectionQueryService, CollectionQueryService>(); // NEW
services.AddSingleton<IMismatchLogService, MismatchLogService>(); // NEW
```

- [ ] **Step 7: Add project reference**

Add to `OmniCard/OmniCard.csproj`:
```xml
<ProjectReference Include="..\OmniCard.Collection\OmniCard.Collection.csproj" />
```

- [ ] **Step 8: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: move collection services to OmniCard.Collection, rename CardSevice → CardService"
```

---

### Task 7: Create OmniCard.eBay — Move eBay Services

Move all eBay integration services.

**Files:**
- Move: `OmniCard/Services/EbayAuthService.cs` → `OmniCard.eBay/EbayAuthService.cs`
- Move: `OmniCard/Services/EbayListingService.cs` → `OmniCard.eBay/EbayListingService.cs`
- Move: `OmniCard/Services/EbayCatalogService.cs` → `OmniCard.eBay/EbayCatalogService.cs`
- Move: `OmniCard/Services/EbaySyncService.cs` → `OmniCard.eBay/EbaySyncService.cs`
- Move: `OmniCard/Services/CredentialStore.cs` → `OmniCard.eBay/CredentialStore.cs`
- Modify: `OmniCard/OmniCard.csproj` — add reference to `OmniCard.eBay`

**Interfaces:**
- Consumes: eBay interfaces from Shared, `ICredentialStore` from Shared, DbContexts from Data
- Produces: eBay implementations in `OmniCard.eBay` namespace

- [ ] **Step 1: Move files**

```bash
mv OmniCard/Services/EbayAuthService.cs OmniCard.eBay/
mv OmniCard/Services/EbayListingService.cs OmniCard.eBay/
mv OmniCard/Services/EbayCatalogService.cs OmniCard.eBay/
mv OmniCard/Services/EbaySyncService.cs OmniCard.eBay/
mv OmniCard/Services/CredentialStore.cs OmniCard.eBay/
```

- [ ] **Step 2: Update namespaces to `OmniCard.eBay`**

Change all files from `namespace OmniCard.Services;` to `namespace OmniCard.eBay;`.
Add: `using OmniCard.Interfaces;`, `using OmniCard.Models;`, `using OmniCard.Data;`

- [ ] **Step 3: Update project reference and DI**

Add to `OmniCard/OmniCard.csproj`:
```xml
<ProjectReference Include="..\OmniCard.eBay\OmniCard.eBay.csproj" />
```

Update `App.xaml.cs`: add `using OmniCard.eBay;` for eBay service DI registrations.

- [ ] **Step 4: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: move eBay integration services to OmniCard.eBay"
```

---

### Task 8: Create OmniCard.Scanner — Move Scanner Service

Move the TWAIN scanner service and scan diagnostics.

**Files:**
- Move: `OmniCard/Services/ScannerService.cs` → `OmniCard.Scanner/ScannerService.cs`
- Move: `OmniCard/Services/ScanDiagnosticService.cs` → `OmniCard.Scanner/ScanDiagnosticService.cs`
- Modify: `OmniCard/OmniCard.csproj` — add reference to `OmniCard.Scanner`

**Interfaces:**
- Consumes: `ICardService`, `IScanDiagnosticService` from Shared
- Produces: Scanner implementation in `OmniCard.Scanner` namespace

- [ ] **Step 1: Move files**

```bash
mv OmniCard/Services/ScannerService.cs OmniCard.Scanner/
mv OmniCard/Services/ScanDiagnosticService.cs OmniCard.Scanner/
```

- [ ] **Step 2: Update namespaces to `OmniCard.Scanner`**

Add: `using OmniCard.Interfaces;`, `using OmniCard.Models;`

- [ ] **Step 3: Update project reference and DI**

Add to `OmniCard/OmniCard.csproj`:
```xml
<ProjectReference Include="..\OmniCard.Scanner\OmniCard.Scanner.csproj" />
```

Update `App.xaml.cs`: add `using OmniCard.Scanner;` for `ScannerService` and `ScanDiagnosticService` registrations.

- [ ] **Step 4: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: move scanner services to OmniCard.Scanner"
```

---

### Task 9: Create OmniCard.Audit — Move Audit Services

Move audit, PDF export, and diagnostic export services.

**Files:**
- Move: `OmniCard/Services/AuditService.cs` → `OmniCard.Audit/AuditService.cs`
- Move: `OmniCard/Services/AuditPdfExporter.cs` → `OmniCard.Audit/AuditPdfExporter.cs`
- Move: `OmniCard/Services/DiagnosticExporter.cs` → `OmniCard.Audit/DiagnosticExporter.cs`
- Modify: `OmniCard/OmniCard.csproj` — add reference to `OmniCard.Audit`

**Interfaces:**
- Consumes: `IAuditService`, `IAuditPdfExporter` from Shared, DbContexts from Data
- Produces: Audit implementations in `OmniCard.Audit` namespace

- [ ] **Step 1: Move files**

```bash
mv OmniCard/Services/AuditService.cs OmniCard.Audit/
mv OmniCard/Services/AuditPdfExporter.cs OmniCard.Audit/
mv OmniCard/Services/DiagnosticExporter.cs OmniCard.Audit/
```

- [ ] **Step 2: Update namespaces to `OmniCard.Audit`**

Add: `using OmniCard.Interfaces;`, `using OmniCard.Models;`, `using OmniCard.Data;`

- [ ] **Step 3: Update project reference and DI**

Add to `OmniCard/OmniCard.csproj`:
```xml
<ProjectReference Include="..\OmniCard.Audit\OmniCard.Audit.csproj" />
```

Update `App.xaml.cs`: add `using OmniCard.Audit;`.

- [ ] **Step 4: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: move audit services to OmniCard.Audit"
```

---

### Task 10: Create OmniCard.Controls — Move Converters, Helpers, and Themes

Move all WPF converters, attached properties, symbol parsing, and theme resources.

**Files:**
- Move: `OmniCard/Helpers/EnumEqualsConverter.cs` → `OmniCard.Controls/Converters/EnumEqualsConverter.cs`
- Move: `OmniCard/Helpers/ListToCommaSeparatedConverter.cs` → `OmniCard.Controls/Converters/ListToCommaSeparatedConverter.cs`
- Move: `OmniCard/Helpers/MtgSymbolParser.cs` → `OmniCard.Controls/MtgSymbolParser.cs`
- Move: `OmniCard/Helpers/MtgTextAttachedProperty.cs` → `OmniCard.Controls/MtgTextAttachedProperty.cs`
- Move: `OmniCard/Helpers/SetSymbolCache.cs` → `OmniCard.Controls/SetSymbolCache.cs`
- Move: `OmniCard/Helpers/SetSymbolConverter.cs` → `OmniCard.Controls/Converters/SetSymbolConverter.cs`
- Move: `OmniCard/Views/Root/Converters.cs` → `OmniCard.Controls/Converters/RootConverters.cs`
- Move: `OmniCard/Views/Root/ScanImageConverter.cs` → `OmniCard.Controls/Converters/ScanImageConverter.cs`
- Move: `OmniCard/Views/Root/MatchedArtConverter.cs` → `OmniCard.Controls/Converters/MatchedArtConverter.cs`
- Move: `OmniCard/Themes/OmniCardTheme.cs` → `OmniCard.Controls/Themes/OmniCardTheme.cs`
- Move: `OmniCard/Themes/AppTheme.xaml` → `OmniCard.Controls/Themes/AppTheme.xaml`
- Move: `OmniCard/Views/Controls/CardSearchControl.xaml(.cs)` → `OmniCard.Controls/CardSearchControl.xaml(.cs)`
- Move: `OmniCard/Views/Controls/CardDetailTemplates.xaml` → `OmniCard.Controls/CardDetailTemplates.xaml` (if it exists as a standalone XAML)
- Modify: `OmniCard/OmniCard.csproj` — add reference to `OmniCard.Controls`

**Interfaces:**
- Consumes: Models from Shared (for converter type references)
- Produces: All WPF converters, controls, and theme resources in `OmniCard.Controls` namespace

- [ ] **Step 1: Create subdirectories**

```bash
mkdir -p OmniCard.Controls/Converters
mkdir -p OmniCard.Controls/Themes
```

- [ ] **Step 2: Move Helpers files**

```bash
mv OmniCard/Helpers/EnumEqualsConverter.cs OmniCard.Controls/Converters/
mv OmniCard/Helpers/ListToCommaSeparatedConverter.cs OmniCard.Controls/Converters/
mv OmniCard/Helpers/MtgSymbolParser.cs OmniCard.Controls/
mv OmniCard/Helpers/MtgTextAttachedProperty.cs OmniCard.Controls/
mv OmniCard/Helpers/SetSymbolCache.cs OmniCard.Controls/
mv OmniCard/Helpers/SetSymbolConverter.cs OmniCard.Controls/Converters/
```

- [ ] **Step 3: Move View converters and controls**

```bash
mv OmniCard/Views/Root/Converters.cs OmniCard.Controls/Converters/RootConverters.cs
mv OmniCard/Views/Root/ScanImageConverter.cs OmniCard.Controls/Converters/
mv OmniCard/Views/Root/MatchedArtConverter.cs OmniCard.Controls/Converters/
```

**Important:** `ScanImageConverter` and `MatchedArtConverter` reference `ScanImageCache.Instance` and `CardArtCache.Instance` (static singletons in OmniCard.Imaging). The Controls project needs a reference to Imaging, OR these converters should use a different pattern. Since the static `Instance` pattern is already used, add a project reference:

Add to `OmniCard.Controls/OmniCard.Controls.csproj`:
```xml
<ProjectReference Include="..\OmniCard.Imaging\OmniCard.Imaging.csproj" />
```

- [ ] **Step 4: Move Theme files**

```bash
mv OmniCard/Themes/OmniCardTheme.cs OmniCard.Controls/Themes/
mv OmniCard/Themes/AppTheme.xaml OmniCard.Controls/Themes/
```

- [ ] **Step 5: Move CardSearchControl**

```bash
mv OmniCard/Views/Controls/CardSearchControl.xaml OmniCard.Controls/
mv OmniCard/Views/Controls/CardSearchControl.xaml.cs OmniCard.Controls/
```

Update the `x:Class` attribute in `CardSearchControl.xaml` to match the new namespace.

- [ ] **Step 6: Update all namespaces**

Change all moved files to appropriate `OmniCard.Controls` or `OmniCard.Controls.Converters` namespace.

`RootConverters.cs` (was `Converters.cs`): change from `namespace OmniCard.Views.Root;` to `namespace OmniCard.Controls.Converters;`

`OmniCardTheme.cs`: change from `namespace OmniCard.Themes;` to `namespace OmniCard.Controls.Themes;`

`SetSymbolCache.cs`: change from `namespace OmniCard.Helpers;` to `namespace OmniCard.Controls;`

- [ ] **Step 7: Update XAML references**

All XAML files that reference converters need updated `xmlns` declarations and converter references. Search for converter usage patterns:
- `xmlns:local="clr-namespace:OmniCard.Views.Root"` → may need `xmlns:conv="clr-namespace:OmniCard.Controls.Converters;assembly=OmniCard.Controls"`
- `xmlns:helpers="clr-namespace:OmniCard.Helpers"` → `xmlns:helpers="clr-namespace:OmniCard.Controls;assembly=OmniCard.Controls"`
- `xmlns:themes="clr-namespace:OmniCard.Themes"` → `xmlns:themes="clr-namespace:OmniCard.Controls.Themes;assembly=OmniCard.Controls"`

**This is the most tedious part.** Search all `.xaml` files for converter references and update them.

- [ ] **Step 8: Add project reference**

Add to `OmniCard/OmniCard.csproj`:
```xml
<ProjectReference Include="..\OmniCard.Controls\OmniCard.Controls.csproj" />
```

- [ ] **Step 9: Remove empty Helpers and Themes directories**

```bash
rmdir OmniCard/Helpers
rmdir OmniCard/Themes
```

- [ ] **Step 10: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "refactor: move converters, controls, and themes to OmniCard.Controls"
```

---

### Task 11: Fix ViewModel Coupling

Remove direct `IDbContextFactory` usage from ViewModels and wire up the new service interfaces.

**Files:**
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs` — replace `IDbContextFactory<CollectionDbContext>` with `ICollectionQueryService`
- Modify: `OmniCard/Views/Root/RootViewModel.cs` — replace `IDbContextFactory<CollectionDbContext>` with `IMismatchLogService`
- Modify: `OmniCard/App.xaml.cs` — register new services

**Interfaces:**
- Consumes: `ICollectionQueryService`, `IMismatchLogService` from Shared (created in Task 6)
- Produces: Clean ViewModels that only depend on service interfaces

- [ ] **Step 1: Update CollectionViewModel**

In `OmniCard/Views/Root/CollectionViewModel.cs`:

1. Remove `IDbContextFactory<CollectionDbContext>` from constructor parameters and field
2. Add `ICollectionQueryService collectionQueryService` to constructor
3. Add `private readonly ICollectionQueryService _collectionQueryService;` field
4. Replace `LoadOverview()` method body:

Before:
```csharp
using var context = _dbContextFactory.CreateDbContext();
// ... complex query ...
```

After:
```csharp
var overviews = await _collectionQueryService.GetLocationOverviewsAsync();
// ... use overviews to populate the UI ...
```

5. Remove `using OmniCard.Data;` if no longer needed
6. Add `using OmniCard.Interfaces;`

- [ ] **Step 2: Update RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`:

1. Remove `IDbContextFactory<CollectionDbContext> collectionDbContextFactory` from primary constructor
2. Add `IMismatchLogService mismatchLogService` parameter
3. Replace `LogMismatchIfHighConfidence()` method:

Before:
```csharp
using var ctx = collectionDbContextFactory.CreateDbContext();
ctx.MismatchLogs.Add(new MismatchLog { ... });
ctx.SaveChanges();
```

After:
```csharp
await mismatchLogService.LogMismatchAsync(oldMatch, newMatch, scannedCard);
```

4. Remove `using OmniCard.Data;` if no longer needed

- [ ] **Step 3: Update DI registrations in App.xaml.cs**

Ensure these are registered:
```csharp
services.AddSingleton<ICollectionQueryService, CollectionQueryService>();
services.AddSingleton<IMismatchLogService, MismatchLogService>();
```

(These may have been added in Task 6 already — verify.)

- [ ] **Step 4: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: remove direct DbContext usage from ViewModels"
```

---

### Task 12: Update App Shell and Clean Up

Update the OmniCard app project: clean up using statements, remove moved files, update csproj to remove packages that moved to other projects.

**Files:**
- Modify: `OmniCard/OmniCard.csproj` — remove NuGet packages that moved, add all project references
- Modify: `OmniCard/App.xaml.cs` — update all using statements
- Delete: `OmniCard/Services/` directory (should be empty after all moves)
- Delete: `OmniCard/Data/` directory (should be empty after all moves)
- Delete: `OmniCard/Models/` directory (should be empty after all moves)
- Delete: `OmniCard/Helpers/` directory (should be empty after all moves)

**Interfaces:**
- Consumes: All project references
- Produces: Clean App shell with only Views, ViewModels, DI root, and resources

- [ ] **Step 1: Verify OmniCard/Services/ is empty**

```bash
ls OmniCard/Services/
```

Only `DialogService.cs` should remain (it's UI-specific and stays in the App project). If any other files remain, they were missed in previous tasks — move them now.

- [ ] **Step 2: Clean up OmniCard.csproj**

Remove NuGet packages that are no longer directly needed (they're in feature projects now):
- Remove `QuestPDF` (moved to Audit)
- Remove `NTwain` (moved to Scanner)
- Remove `CsvHelper` (moved to Collection)
- Remove `AdysTech.CredentialManager` (moved to eBay)

Keep packages the App still directly uses:
- `CommunityToolkit.Mvvm` (ViewModels)
- `MaterialDesignThemes` (XAML themes — unless moved to Controls)
- `Microsoft.EntityFrameworkCore.Design` (for design-time tooling)
- `Microsoft.Extensions.Hosting`
- `Microsoft.Web.WebView2`
- `Serilog.*`
- `SharpVectors.Wpf` (unless moved to Controls)
- `Microsoft.Xaml.Behaviors.Wpf`

Ensure all 8 new project references are present:
```xml
<ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
<ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
<ProjectReference Include="..\OmniCard.Imaging\OmniCard.Imaging.csproj" />
<ProjectReference Include="..\OmniCard.CardMatching\OmniCard.CardMatching.csproj" />
<ProjectReference Include="..\OmniCard.Collection\OmniCard.Collection.csproj" />
<ProjectReference Include="..\OmniCard.eBay\OmniCard.eBay.csproj" />
<ProjectReference Include="..\OmniCard.Scanner\OmniCard.Scanner.csproj" />
<ProjectReference Include="..\OmniCard.Audit\OmniCard.Audit.csproj" />
<ProjectReference Include="..\OmniCard.Controls\OmniCard.Controls.csproj" />
```

- [ ] **Step 3: Update App.xaml.cs using statements**

Replace the old using block with:
```csharp
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.eBay;
using OmniCard.Scanner;
using OmniCard.Audit;
using OmniCard.Controls;
using OmniCard.Controls.Themes;
// ... keep existing View using statements
```

- [ ] **Step 4: Clean up empty directories**

```bash
rmdir OmniCard/Services 2>/dev/null  # may still have DialogService.cs
rmdir OmniCard/Data 2>/dev/null
rmdir OmniCard/Models 2>/dev/null
```

If `OmniCard/Services/` still has `DialogService.cs`, leave it. It's UI-specific and stays.

- [ ] **Step 5: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: clean up app shell — remove moved packages and files"
```

---

### Task 13: Update Test and Web Projects

Update OmniCard.Tests to reference all new projects, and update OmniCard.Web for the Data project.

**Files:**
- Modify: `OmniCard.Tests/OmniCard.Tests.csproj` — add references to feature projects
- Modify: `OmniCard.Web/OmniCard.Web.csproj` — verify references
- Modify: Test files — update using statements for new namespaces

**Interfaces:**
- Consumes: All new projects
- Produces: Compiling and passing test suite, working web companion

- [ ] **Step 1: Update OmniCard.Tests.csproj**

The test project currently references only `OmniCard.csproj` (the app). Since the app references all feature projects transitively, this may still work. However, for explicit testing of individual projects, add direct references:

```xml
<ItemGroup>
  <ProjectReference Include="..\OmniCard\OmniCard.csproj" />
  <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
  <ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
  <ProjectReference Include="..\OmniCard.Imaging\OmniCard.Imaging.csproj" />
  <ProjectReference Include="..\OmniCard.CardMatching\OmniCard.CardMatching.csproj" />
  <ProjectReference Include="..\OmniCard.Collection\OmniCard.Collection.csproj" />
  <ProjectReference Include="..\OmniCard.eBay\OmniCard.eBay.csproj" />
  <ProjectReference Include="..\OmniCard.Scanner\OmniCard.Scanner.csproj" />
  <ProjectReference Include="..\OmniCard.Audit\OmniCard.Audit.csproj" />
  <ProjectReference Include="..\OmniCard.Controls\OmniCard.Controls.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Update test file using statements**

Search all test files for `using OmniCard.Services;` and replace with the appropriate new namespace:
- Service tests that test CardMatching services: add `using OmniCard.CardMatching;`
- Service tests that test Collection services: add `using OmniCard.Collection;`
- Service tests that test eBay services: add `using OmniCard.eBay;`
- Service tests that test Imaging services: add `using OmniCard.Imaging;`
- Service tests that test Audit services: add `using OmniCard.Audit;`
- Data tests: add `using OmniCard.Data;`
- All tests using interfaces: add `using OmniCard.Interfaces;`

- [ ] **Step 3: Update OmniCard.Web references**

Ensure web project references both Shared and Data:
```xml
<ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
<ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
```

Update any using statements in web pages that reference moved types.

- [ ] **Step 4: Add InternalsVisibleTo where needed**

If tests access internal members of feature projects, add `InternalsVisibleTo` attributes. Check existing test patterns — if tests only use public APIs, this isn't needed.

Add to each feature project's csproj if needed:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="OmniCard.Tests" />
</ItemGroup>
```

- [ ] **Step 5: Build and test**

```bash
dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

Expected: ALL tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: update test and web projects for new solution structure"
```

---

### Task 14: Final Verification and Cleanup

Comprehensive verification that everything works end-to-end.

**Files:**
- No new files — verification only
- May fix any remaining build errors or test failures discovered

- [ ] **Step 1: Clean build**

```bash
cd d:/source/repos/OmniCard
dotnet clean OmniCard.slnx
dotnet build OmniCard.slnx
```

Expected: BUILD SUCCEEDED with 0 warnings related to the restructuring.

- [ ] **Step 2: Run all tests**

```bash
dotnet test OmniCard.slnx --verbosity normal
```

Expected: All tests pass.

- [ ] **Step 3: Verify no circular dependencies**

```bash
# If dotnet build succeeds, there are no circular dependencies.
# But also check that feature projects don't reference each other:
grep -r "OmniCard.CardMatching\|OmniCard.Collection\|OmniCard.eBay\|OmniCard.Scanner\|OmniCard.Audit\|OmniCard.Controls" OmniCard.CardMatching/*.csproj OmniCard.Collection/*.csproj OmniCard.eBay/*.csproj OmniCard.Scanner/*.csproj OmniCard.Audit/*.csproj OmniCard.Controls/*.csproj 2>/dev/null
```

Expected: Only references to `OmniCard.Shared`, `OmniCard.Data`, and `OmniCard.Imaging` (from CardMatching and Controls). No cross-feature references.

- [ ] **Step 4: Verify namespace alignment**

```bash
# Check that files in each project use the correct namespace
grep -rn "namespace " OmniCard.Data/*.cs | head -20
grep -rn "namespace " OmniCard.Imaging/*.cs | head -20
grep -rn "namespace " OmniCard.CardMatching/*.cs | head -20
grep -rn "namespace " OmniCard.Collection/*.cs | head -20
grep -rn "namespace " OmniCard.eBay/*.cs | head -20
grep -rn "namespace " OmniCard.Scanner/*.cs | head -20
grep -rn "namespace " OmniCard.Audit/*.cs | head -20
```

Expected: Each file's namespace matches its project name.

- [ ] **Step 5: Verify the app launches**

```bash
dotnet run --project OmniCard/OmniCard.csproj
```

Manually verify: app launches, splash screen shows, main window loads, tabs are accessible.

- [ ] **Step 6: Commit final state**

```bash
git add -A
git commit -m "refactor: complete solution restructuring — verify all builds and tests pass"
```
