# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

OmniCard is a Windows desktop app (WPF, .NET 10) for scanning and managing trading card collections. It identifies physical cards scanned via TWAIN scanner or phone camera using perceptual image hashing + OCR, then tracks them across storage locations, sealed-product inventory, and sales/fulfillment (including eBay listing). A read-only ASP.NET Core web companion lets you browse the collection and scan with a phone from any device on the network.

Supported games: Magic: The Gathering (Scryfall), One Piece TCG, Riftbound, Pokémon, Yu-Gi-Oh!, Final Fantasy TCG (the last three via TCGCSV).

## Commands

```bash
# Build everything
dotnet build OmniCard.slnx

# Run the desktop app
dotnet run --project OmniCard/OmniCard.csproj

# Run the web companion (point --db at the desktop app's data dir; opens DBs read-only)
dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "%LOCALAPPDATA%\OmniCard"

# Run all tests
dotnet test OmniCard.Tests/OmniCard.Tests.csproj

# Run a single test / fixture (standard dotnet test filter syntax)
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests"
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests.CreateProduct_RoundTrips"

# Publish a release build (win-x64, framework-dependent)
dotnet publish OmniCard/OmniCard.csproj -c Release -r win-x64
```

CI (`.github/workflows/ci.yml`) runs `dotnet build` + `dotnet test` with coverage on `windows-latest` for every push to `master` and every PR — there is no Linux/macOS target, and platform-specific (WPF/TWAIN) code is expected. Releases (`.github/workflows/release.yml`) publish a zipped win-x64 build and attach it to the GitHub Release when a release is published.

Requires the .NET 10 SDK and Windows 10 22H2+ (target framework `net10.0-windows10.0.22621.0`). On first launch the app creates `%LOCALAPPDATA%\OmniCard` and writes a default `appsettings.json` there — see `App.xaml.cs` `InitSettingsDirectory()`.

## Solution layout

Projects are wired together as dependencies flow downward; `OmniCard.Shared` is the common core everything else references for interfaces/models.

```
OmniCard.Shared/       Interfaces (I*Service) and models shared across every project — no implementation logic
OmniCard.Data/         EF Core DbContexts (SQLite) — one per game DB, plus the unified OmniCardDbContext
OmniCard.Imaging/      Perceptual hashing (pHash, foil-aware edge hash), OCR, image caching
OmniCard.CardMatching/ Per-game ICardGameService implementations (Scryfall, OPTCG, Riftbound, TCGCSV-backed games)
OmniCard.Collection/   Business logic: collection queries, CSV import/export, decklists, inventory, sales/fulfillment
OmniCard.eBay/         eBay OAuth, catalog lookup, listing create/sync, seller setup
OmniCard.Scanner/      TWAIN scanner coordination (talks to OmniCard.ScannerHost over IPC)
OmniCard.ScannerHost/  Out-of-process TWAIN bridge — kept separate because TWAIN drivers are often 32-bit/unstable
OmniCard.Audit/        Location auditing + PDF export (QuestPDF)
OmniCard.Controls/     Reusable WPF controls, converters, themes (MaterialDesignThemes)
OmniCard/              WPF desktop app: Views/ViewModels (MVVM), DI composition root (App.xaml.cs)
OmniCard.Web/          ASP.NET Core Razor Pages + SignalR web companion (reads the same SQLite DBs read-only)
OmniCard.Tests/        xUnit tests for everything above
```

## Architecture

### DI composition root
`OmniCard/App.xaml.cs` builds a single `IHost` with every service registered as `Singleton` (services, caches, one `IDbContextFactory<T>` per game DB) or `Transient` (dialog Views/ViewModels created per-open). `RootView` is registered as an `IHostedService` and drives the WPF window lifecycle. Startup (`OnStartup`) runs DB migrations and cache warmup on a background thread behind a splash screen before showing the main window — read this method before touching startup/migration behavior.

### Per-game card matching (`ICardGameService`)
Every supported card game implements `ICardGameService` (`OmniCard.Shared/Interfaces/ICardGameService.cs`) and is registered in DI as one of several `IEnumerable<ICardGameService>` — consumers resolve the game they need by `.Game` (a `CardGame` enum value) rather than by concrete type. `ScryfallService` and `OptcgService` are bespoke per-source implementations; Pokémon, Yu-Gi-Oh!, Final Fantasy TCG, and Riftbound all share one abstract base, `TcgCsvGameService<TContext>` (`OmniCard.CardMatching/TcgCsvGameService.cs`), which implements catalog download, price refresh, image hashing, and matching once against the TCGCSV API — subclasses only supply a category id, extended-data field mapping, and price sub-type mapping. When adding a new TCGCSV-backed game, subclass `TcgCsvGameService<T>`, not `ICardGameService` directly.

Card matching combines perceptual image hash (pHash) distance, an edge hash for foils (color shift breaks plain pHash — see `OmniCard.Imaging`), and OCR (used for OPTCG collector numbers) into a combined confidence score; corrections a user makes are persisted per-game (`RecordCorrection`) and boost future matches for that hash.

### Unified inventory/sales model
`OmniCardDbContext` (`OmniCard.Data/OmniCardDbContext.cs`) is the newer, game-agnostic store for `Product`/`InventoryLot`/`Listing`/`Order`/`Customer` etc., used by `IInventoryService`, `IListingService`, `IOrderService`, and the rest of the sales/fulfillment stack in `OmniCard.Collection`. It superseded per-game collection data; `UnifiedMigrationService` (`OmniCard.Data/UnifiedMigrationService.cs`) one-time-migrates legacy `collection.db` singles into it on first launch of a build that has it, guarded by a `MigrationState` DB marker so it only runs once and is safe to retry on failure. Don't assume `collection.db` is authoritative for new work — check whether the unified store already covers it.

### Data storage
Each game and subsystem gets its own SQLite file under `%LOCALAPPDATA%\OmniCard` (configurable): `scryfall.db`, `optcg.db`, `riftbound.db`, `pokemon.db`, `yugioh.db`, `fftcg.db`, `inventory.db` (the unified `OmniCardDbContext`), plus `scans/` and `logs/` (14-day rolling Serilog retention). `OmniCard.Web` opens the same files read-only via `--db <path>`.

### MVVM conventions (WPF app)
Views/ViewModels live paired under `OmniCard/Views/<Feature>/`. ViewModels use `CommunityToolkit.Mvvm` (`ObservableObject`, `[RelayCommand]`, `[ObservableProperty]`). Page-level ViewModels tied to the main window (Collection, Inventory, Dashboard, Sales, Settings, etc.) are DI singletons; dialog/editor ViewModels are transient and constructed per-open. Async work fired from a ViewModel without being awaited by the caller (fire-and-forget, `Task.Run`) needs an explicit completion signal for tests to await — see `async-vm-test-determinism` pattern used across the ViewModel test suite (tests otherwise flake against `Task.Yield`-based mocks).

### Tests
xUnit, one test class per service/feature, mirroring the `OmniCard.Tests/<Area>/` folder to the project under test. DB-backed tests typically spin up an in-memory SQLite connection (`Data Source=:memory:`, kept open for the test's lifetime) against the real `DbContext` and call `EnsureCreated()` — see `OmniCard.Tests/Services/InventoryServiceTests.cs` for the pattern. `OmniCard.Tests/Tools/SyncTestData` is a standalone console tool (not a test suite) for syncing test fixture data.
