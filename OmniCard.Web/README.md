# OmniCard Web

OmniCard is a React/TypeScript single-page app (Vite + MUI) served by the ASP.NET Core backend in
this project. It is LAN-accessible and IIS-hostable, and a full **read/write** app: browse and edit
the collection, manage locations and binders, scan cards with a phone camera (matching runs
server-side), track sealed inventory, run the sales/orders workflow, and list on eBay. This is the
whole app — the original WPF desktop app has been retired.

## Architecture

- **SPA** — `ClientApp/` (React + TS + Vite + MUI). Built to `wwwroot/app/` and served at **`/app`**
  with a client-side-routing fallback. The API lives under `/api/*`.
- **Unified store on SQL Server** — collection, inventory, sales (`OmniCardDbContext`) live in SQL
  Server for true multi-user concurrency (shadow `rowversion` tokens on the edited entities). EF
  migrations under `Migrations/` are applied automatically at startup.
- **Per-game catalogs on SQL Server** — one database per game (`OmniCard_Scryfall`, `OmniCard_Optcg`,
  `OmniCard_Riftbound`, `OmniCard_Pokemon`, `OmniCard_Yugioh`, `OmniCard_FinalFantasy`). They're
  disposable reference caches (refresh wipes + reloads), so they use `EnsureCreated` at startup, not
  migrations. Refreshed in-place via the catalog "refresh" operations (Settings → Catalog data).
- **Site-wide passphrase gate** — one shared passphrase (`Auth:Passphrase`); open when unconfigured.
- **Server-side scanning** — image upload → perceptual hash + OCR matching via the per-game
  `ICardGameService` pipeline; no TWAIN, no desktop agent.
- **Server-hosted artwork** — card images cached under `{dataDir}/card-images`, served at
  `/card-images` (see [Catalog data](#catalog-data)).

## Prerequisites

- **.NET 10 SDK** (build) / **.NET 10 Hosting Bundle** (IIS host).
- **Node 18+ / npm** for the SPA build (developed on Node 24 / npm 9).
- **SQL Server 2019+** (Express is fine). Default connection targets `localhost` with Windows auth.
- A **data directory** (default `%LOCALAPPDATA%\OmniCard`, or `--db <path>`) for `scans/`,
  `card-images/`, `symbols/`, and `dataprotection-keys/`. A fresh server builds the SQL Server DBs
  itself; to bring existing data over, run the one-time migration below.

## Configuration

Settings come from `appsettings.json` (and the shared `%LOCALAPPDATA%\OmniCard\appsettings.json`,
loaded automatically if present). Key settings:

| Key | Purpose |
|-----|---------|
| `DataDirectory` (or `--db <path>` CLI arg) | Folder holding `scans/`, `card-images/`, `symbols/`, `dataprotection-keys/`. |
| `ConnectionStrings:OmniCard` | SQL Server unified store. Default: `Server=localhost;Database=OmniCard;Trusted_Connection=True;TrustServerCertificate=True;` The per-game catalog DBs reuse this with the database name swapped (or set `ConnectionStrings:OmniCard_<Game>` explicitly). |
| `Auth:Passphrase` | Site-wide passphrase. Leave blank to run open (LAN-trusted only). |
| `Binder:EditPassphrase` | Legacy binder-editor gate (separate from the site gate). |
| `eBay` section | eBay app credentials — see [eBay](#ebay-setup). |

DataProtection keys (used to encrypt stored eBay tokens) are persisted to
`<DataDirectory>/dataprotection-keys` so they survive app-pool recycles.

## First-time data migration (SQLite → SQL Server)

If you have existing data from the old SQLite files (`inventory.db` + the per-game catalogs
`scryfall.db`, `optcg.db`, …) in a data directory, copy it all into SQL Server once with the bundled
migrator. It's idempotent (clears each target first, preserves ids), copies the unified store **and**
every per-game catalog (creating the `OmniCard` + `OmniCard_<Game>` databases as needed), and streams
in batches so large catalogs (Scryfall is 100k+ cards) don't blow memory. Expect it to take a while.

```bash
dotnet run --project OmniCard.DbMigrator -c Release -- "<dataDir>" ["<sqlserver-connstring>"]
# e.g.
dotnet run --project OmniCard.DbMigrator -c Release -- "X:\TCG Card Scanner"
```

A fresh install with no prior data can skip this — the web app creates the databases on startup, and
you populate the catalogs via **Settings → Catalog data** (Download catalog / Update prices).

On startup the web app applies any pending EF migrations (creating the `OmniCard` database if
absent). To add a schema migration later (local `dotnet-ef` tool 10.0.9 is pinned in
`dotnet-tools.json`):

```bash
dotnet dotnet-ef migrations add <Name> -p OmniCard.Web -s OmniCard.Web -c OmniCardDbContext -o Migrations
```

## Build

```bash
# 1. Build the SPA into wwwroot/app
cd OmniCard.Web/ClientApp
npm install
npm run build

# 2. Build (or publish) the backend
cd ../..
dotnet build OmniCard.Web/OmniCard.Web.csproj
```

`ClientApp/**` is excluded from the .NET globs, so `node_modules` never ends up in a publish; the
built `wwwroot/app` output does.

## Run (development)

Two terminals — the backend serves the API, Vite serves the SPA with HMR and proxies `/api`, `/hubs`,
`/scans`, `/openapi` to the backend:

```bash
# terminal 1 — backend on :5000
dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "X:\TCG Card Scanner"

# terminal 2 — Vite dev server on :5173
cd OmniCard.Web/ClientApp && npm run dev
```

Open http://localhost:5173. For a production-like check, `npm run build` then browse
http://localhost:5000/app/.

> Gotcha: `dotnet run` spawns `OmniCard.Web.exe`, which lingers and locks the DLLs. Run
> `taskkill /F /IM OmniCard.Web.exe` before rebuilding.

## Publish & IIS

```bash
# Build the SPA first (populates wwwroot/app), then:
dotnet publish OmniCard.Web/OmniCard.Web.csproj -c Release
```

Deploy the published output to IIS:

1. Install the **.NET 10 Hosting Bundle** on the server (provides the ASP.NET Core Module V2).
2. Create an **Application Pool** with **No Managed Code**.
3. Create a **Site/Application** pointing at the published folder, using that app pool.
4. Set config via `web.config` `environmentVariables` (or `appsettings.json`): `DataDirectory`,
   `ConnectionStrings__OmniCard`, `Auth__Passphrase`, and the `eBay__*` keys as needed.
5. Grant the app-pool identity (`IIS AppPool\<name>`):
   - **read/write** on the data directory (`scans/`, `card-images/`, `dataprotection-keys/`),
   - access to SQL Server (or use a SQL login in the connection string instead of Windows auth).

## Catalog data

The per-game card catalogs, prices, and image hashes are refreshed **server-side** — the web app no
longer needs the desktop to keep them current. In **Settings → Catalog data**, pick a game and run:

- **Update prices** — refresh market prices.
- **Download catalog** — pull the latest card data (bulk).
- **Recompute hashes** — rebuild perceptual hashes used for scan matching.

- **Download artwork** — cache every printing's image to `{dataDir}/card-images` (served locally).

One job runs at a time; progress is shown live. The per-game catalog databases (and their schemas)
are created on first run, so a fresh server with an empty SQL Server instance can build them from
scratch.

## eBay setup

The full desktop eBay stack runs server-side; the OAuth flow is a normal web redirect.

1. Fill the `eBay` section (from the eBay developer portal):
   ```json
   "eBay": {
     "AppId": "…", "CertId": "…", "DevId": "…",
     "RuName": "…", "AcceptUrl": "https://<host>/api/ebay/callback",
     "Environment": "sandbox"
   }
   ```
2. In the eBay dev portal, set the RuName's **Auth accepted URL** to `https://<host>/api/ebay/callback`,
   matching `Environment` (sandbox vs production hosts differ).
3. In the app, go to **Settings → eBay → Connect to eBay**, approve consent, then **Run seller setup**.

Until configured, `GET /api/ebay/status` reports what's missing and all listing operations no-op
(so order status changes keep working without a live connection). Tokens are stored encrypted in
`<DataDirectory>/web-credentials.dat`.

## Tests

Web-facing tests live under `OmniCard.Tests/Web/` (controllers, services). Run the whole suite:

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```
