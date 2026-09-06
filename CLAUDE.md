# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

OmniCard is a web app (ASP.NET Core backend + React/TypeScript SPA, .NET 10) for scanning and managing trading card collections. It identifies physical cards from uploaded images / phone-camera photos using perceptual image hashing + OCR, then tracks them across storage locations, sealed-product inventory, and sales/fulfillment (including eBay listing). It is LAN-accessible and IIS-hostable; matching runs server-side (no scanner drivers, no desktop agent).

> The original WPF desktop app and the TWAIN scanner projects were **retired** once the web app reached parity (see `docs/superpowers/plans/` for the migration history). Everything now runs through `OmniCard.Web`.

Supported games: Magic: The Gathering (Scryfall), One Piece TCG, Riftbound, Pokémon, Yu-Gi-Oh!, Final Fantasy TCG (the last three via TCGCSV).

## Commands

```bash
# Build everything
dotnet build OmniCard.slnx

# Run the web app (backend on :5000; point --db at the data directory)
dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "%LOCALAPPDATA%\OmniCard"

# Run the SPA dev server (Vite, HMR, proxies /api → :5000) — separate terminal
cd OmniCard.Web/ClientApp && npm install && npm run dev   # http://localhost:5173

# Build the SPA into wwwroot/app (required before publish / prod-ish serving at :5000/app)
cd OmniCard.Web/ClientApp && npm run build

# One-time data copy: desktop SQLite (inventory.db + per-game catalogs) → SQL Server
dotnet run --project OmniCard.DbMigrator -- "X:\TCG Card Scanner"

# Run all tests
dotnet test OmniCard.Tests/OmniCard.Tests.csproj

# Run a single test / fixture (standard dotnet test filter syntax)
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests"

# Publish for IIS (framework-dependent; build the SPA first so wwwroot/app is populated)
dotnet publish OmniCard.Web/OmniCard.Web.csproj -c Release -r win-x64 --self-contained false
```

See **`OmniCard.Web/README.md`** for the full deployment/operations guide (SQL Server setup, data migration, config keys, eBay, artwork).

CI (`.github/workflows/ci.yml`) runs `dotnet build` + `dotnet test` with coverage on `windows-latest` for every push to `master` and every PR — Windows-only (the imaging/OCR stack uses `System.Drawing`). Releases (`.github/workflows/release.yml`) build the SPA, publish a framework-dependent win-x64 web build, and attach the IIS zip to the GitHub Release.

Requires the .NET 10 SDK, Node 18+ (SPA), SQL Server 2019+ (unified store + catalogs), and Windows (target framework `net10.0-windows10.0.22621.0`; the imaging code is Windows-only). Gotcha: `dotnet run` spawns `OmniCard.Web.exe` that lingers and locks DLLs — `taskkill //F //IM OmniCard.Web.exe` before rebuilding.

## Solution layout

Dependencies flow downward; `OmniCard.Shared` is the common core everything references for interfaces/models.

```
OmniCard.Shared/       Interfaces (I*Service) and models shared across every project — no implementation logic
OmniCard.Data/         EF Core DbContexts — per-game catalog contexts + the unified OmniCardDbContext (multi-provider: SQLite + SQL Server)
OmniCard.Imaging/      Perceptual hashing (pHash, foil-aware edge hash), OCR, image caching
OmniCard.CardMatching/ Per-game ICardGameService implementations (Scryfall, OPTCG, Riftbound, TCGCSV-backed games)
OmniCard.Collection/   Business logic: collection queries, CSV import/export, decklists, inventory, sales/fulfillment
OmniCard.eBay/         eBay OAuth, catalog lookup, listing create/sync, seller setup
OmniCard.Audit/        Location auditing + PDF export (QuestPDF)
OmniCard.Api.Contracts/ Pure DTO records — the SPA's request/response contract (never serialize EF/domain types)
OmniCard.Web/          ASP.NET Core API + React/TS SPA (ClientApp/, built to wwwroot/app). The app.
OmniCard.DbMigrator/   Console tool: one-time SQLite → SQL Server copy (unified store + per-game catalogs)
OmniCard.Tests/        xUnit tests for everything above
```

## Architecture

### DI composition root
`OmniCard.Web/Program.cs` builds the app: services registered `Singleton` (game services, caches, one `IDbContextFactory<T>` per DB) and the API controllers under `Api/`. On startup it `Migrate()`s the unified `OmniCardDbContext` (SQL Server) and `EnsureCreated()`s the per-game catalog DBs, then serves the SPA at `/app`. Read `Program.cs` before touching startup/DI/serving behavior.

### Databases (all SQL Server)
- **Unified store** — `OmniCardDbContext` (`OmniCard.Data/OmniCardDbContext.cs`): the game-agnostic store for `Product`/`InventoryLot`/`Listing`/`Order`/`Customer`/`StorageContainer`/`CardList`/`Trade` etc. On SQL Server it carries shadow `rowversion` concurrency tokens (see `ConcurrencyTrackedEntities`); EF **migrations** live in `OmniCard.Web/Migrations`. `ConcurrencyExceptionFilter` maps `DbUpdateConcurrencyException` → HTTP 409. **Gotcha:** the desktop services' detached `Update()` fails the rowversion check on SQL Server — web edits **load-then-patch** through the DB factory (see `WebBinderCardService`, and the load-patch update paths in `CustomersController`/`InventoryController`).
- **Per-game catalogs** — one SQL Server DB per game (`OmniCard_Scryfall`, `OmniCard_Optcg`, …), via `SqlServerDb.CatalogConnectionString`. They're disposable reference caches (refresh wipes + reloads), so they use `EnsureCreated`, not migrations. `ulong` hashes map to `decimal(20,0)` (matching is done in memory, so no perf cost). The DbContexts are **multi-provider**: SQLite-specific schema code (`PRAGMA user_version`, `ALTER TABLE`, file-path bootstrap) is guarded by `Database.IsSqlite()` and no-ops on SQL Server.
- **Config:** `ConnectionStrings:OmniCard` (base; per-game DBs swap the database name), `DataDirectory`/`--db` (holds `scans/`, `card-images/`, `symbols/`, `dataprotection-keys/`), `Auth:Passphrase` (site gate), `eBay` section.

### Per-game card matching (`ICardGameService`)
Every game implements `ICardGameService` (`OmniCard.Shared/Interfaces/ICardGameService.cs`), registered as `IEnumerable<ICardGameService>` — resolve by `.Game` (a `CardGame` enum value), not concrete type. `ScryfallService` and `OptcgService` are bespoke; Pokémon, Yu-Gi-Oh!, Final Fantasy TCG, and Riftbound share the abstract base `TcgCsvGameService<TContext>` (subclass it, not `ICardGameService`, for a new TCGCSV game). Matching combines pHash distance, a foil edge hash (color shift breaks plain pHash — see `OmniCard.Imaging`), and OCR into a combined confidence; user corrections persist per-game (`RecordCorrection`) and boost future matches.

Server-side scanning lives in `OmniCard.Web/Services/WebScanMatchingService` (a WPF-free port of the old desktop `CardService.AddFromStream`), driven by `CardScanController` (`/api/scan/*`).

### Artwork
`CardImageCacheService` caches card images on the server filesystem under `{dataDir}/card-images` (keyed by game + card id), served at `/card-images`; the collection prefers the local URL, falling back to the CDN. The catalog-refresh "images" op (`CatalogController` / Settings → Catalog data) bulk-downloads a game's art.

### Tests
xUnit, one test class per service/feature, mirroring `OmniCard.Tests/<Area>/` to the project under test. DB-backed tests spin up an in-memory SQLite connection (`Data Source=:memory:`, kept open for the fixture's lifetime) against the real `DbContext` and call `EnsureCreated()` — see `OmniCard.Tests/Services/InventoryServiceTests.cs` and the web-controller tests under `OmniCard.Tests/Web/`. Note the desktop path is still SQLite in tests; the SQL-Server-specific behavior (rowversion, per-game catalog DBs) is verified against a live SQL Server manually, not in the unit suite.

## Keeping docs current

- **User-facing web features:** there is no bundled in-app help site anymore (that was the retired desktop app). Keep **`OmniCard.Web/README.md`** (deployment/ops) accurate when you change setup, config, or add a major surface.
- **NuGet / third-party assets:** when you add, remove, or upgrade a package, update `THIRD-PARTY-NOTICES.txt` at the repo root (name, version, license, homepage; flag special terms like QuestPDF's Community/Professional threshold). Bump `<Version>`/`<InformationalVersion>` in `Directory.Build.props` when cutting a release.
