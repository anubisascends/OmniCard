# OmniCard Web

The browser front end for OmniCard: a React/TypeScript single-page app (Vite + MUI) served by the
ASP.NET Core backend in this project. It is LAN-accessible and IIS-hostable, and — unlike the
original read-only companion — it is a full **read/write** app: browse and edit the collection,
manage locations and binders, scan cards with a phone camera (matching runs server-side), track
sealed inventory, run the sales/orders workflow, and list on eBay.

## Architecture

- **SPA** — `ClientApp/` (React + TS + Vite + MUI). Built to `wwwroot/app/` and served at **`/app`**
  with a client-side-routing fallback. The API lives under `/api/*`.
- **Unified store on SQL Server** — collection, inventory, sales (`OmniCardDbContext`) live in SQL
  Server for true multi-user concurrency (shadow `rowversion` tokens on the edited entities). EF
  migrations under `Migrations/` are applied automatically at startup.
- **Per-game catalog DBs stay SQLite** — `scryfall.db`, `optcg.db`, `riftbound.db`, `pokemon.db`,
  `yugioh.db`, `fftcg.db` are opened **read-only** as reference caches (produced by the desktop app).
- **Site-wide passphrase gate** — one shared passphrase (`Auth:Passphrase`); open when unconfigured.
- **Server-side scanning** — image upload → perceptual hash + OCR matching (the same
  `ICardGameService` pipeline the desktop uses), no TWAIN, no desktop round-trip.

## Prerequisites

- **.NET 10 SDK** (build) / **.NET 10 Hosting Bundle** (IIS host).
- **Node 18+ / npm** for the SPA build (developed on Node 24 / npm 9).
- **SQL Server 2019+** (Express is fine). Default connection targets `localhost` with Windows auth.
- A **data directory** the desktop app has populated (default `%LOCALAPPDATA%\OmniCard`) — it holds
  the SQLite catalog DBs, `scans/`, and `symbols/`. Copy or point at it.

## Configuration

Settings come from `appsettings.json` (and the shared `%LOCALAPPDATA%\OmniCard\appsettings.json`,
loaded automatically if present). Key settings:

| Key | Purpose |
|-----|---------|
| `DataDirectory` (or `--db <path>` CLI arg) | Folder holding the catalog SQLite DBs, `scans/`, `symbols/`. |
| `ConnectionStrings:OmniCard` | SQL Server unified store. Default: `Server=localhost;Database=OmniCard;Trusted_Connection=True;TrustServerCertificate=True;` |
| `Auth:Passphrase` | Site-wide passphrase. Leave blank to run open (LAN-trusted only). |
| `Binder:EditPassphrase` | Legacy binder-editor gate (separate from the site gate). |
| `eBay` section | eBay app credentials — see [eBay](#ebay-setup). |

DataProtection keys (used to encrypt stored eBay tokens) are persisted to
`<DataDirectory>/dataprotection-keys` so they survive app-pool recycles.

## First-time data migration (SQLite → SQL Server)

The desktop app writes its unified store to `inventory.db` (SQLite). Copy it into SQL Server once
with the bundled migrator (idempotent — it clears the target first, preserves ids):

```bash
dotnet run --project OmniCard.DbMigrator -- "<dataDir>" ["<sqlserver-connstring>"]
# e.g.
dotnet run --project OmniCard.DbMigrator -- "X:\TCG Card Scanner"
```

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
   - **read** on the data directory's SQLite catalog DBs + `scans/`,
   - **read/write** on `<DataDirectory>/dataprotection-keys`,
   - access to SQL Server (or use a SQL login in the connection string instead of Windows auth).

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
