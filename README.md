# OmniCard

A web app for scanning and managing trading card collections. Upload card images (or snap them with your phone) and OmniCard identifies them via perceptual hashing + OCR, then tracks them across storage locations, sealed-product inventory, and sales/fulfillment — all from any device on your network.

Supports **Magic: The Gathering** (Scryfall), **One Piece TCG**, **Riftbound**, **Pokémon**, **Yu-Gi-Oh!**, and **Final Fantasy TCG** (the last three via TCGCSV).

> OmniCard started as a WPF desktop app with a read-only web companion. It has since been rebuilt as a full read/write web app (ASP.NET Core API + React/TypeScript SPA) backed by SQL Server, and the desktop app has been retired — everything now runs through `OmniCard.Web`.

## Features

- **Image scanning** — upload card photos/scans (or use the phone camera); matching runs **server-side** (pHash + foil edge hash + OCR), with a review-and-commit queue
- **Collection management** across storage locations (binders, boxes, deck boxes, bulk), with a visual binder editor
- **Set completion tracking** + printable want-lists
- **Decklist checking** against your collection (Moxfield / Archidekt)
- **CSV import/export** (Manabox, Moxfield, TCGplayer, app-native)
- **Sealed inventory** (booster boxes, packs, cases…) with lots and valuation
- **Sales & fulfillment** — orders kanban, listings, customers, pick-list & receipt PDFs
- **eBay listing integration** (server-side OAuth)
- **Card lists** and **trade history**
- **Location auditing** with PDF reports
- **Server-hosted artwork** — card images cached on the server and served locally

## Requirements

- **.NET 10 SDK** — <https://dotnet.microsoft.com/download/dotnet/10.0>
- **Node 18+** (to build the SPA)
- **SQL Server 2019+** (Express is fine) — the unified store and per-game catalogs
- **Windows** — the imaging/OCR stack uses `System.Drawing` (target framework `net10.0-windows`)

## Download

Grab the latest release from the [Releases](../../releases) page:

| Asset | What it is |
|-------|------------|
| `OmniCard-Web-v{VERSION}-iis.zip` | The web app, packaged for installation on IIS |

## Installing on IIS

1. Install the **.NET 10 Hosting Bundle** (provides the ASP.NET Core Module V2) and **SQL Server**.
2. Create an **Application Pool** with **No Managed Code**, and a **Site/Application** pointing at the extracted zip.
3. Configure `DataDirectory`, `ConnectionStrings__OmniCard`, `Auth__Passphrase`, and (optionally) the `eBay__*` keys via `web.config` `environmentVariables` or `appsettings.json`.
4. Run the one-time data migration and grant the app-pool identity the needed permissions.

**Full step-by-step deployment/operations guide — SQL Server setup, data migration, config keys, artwork, and eBay — is in [OmniCard.Web/README.md](OmniCard.Web/README.md).**

## Building from Source

```bash
git clone https://github.com/anubisascends/OmniCard.git
cd OmniCard

# Build the SPA (into OmniCard.Web/wwwroot/app)
cd OmniCard.Web/ClientApp && npm install && npm run build && cd ../..

# Build the backend
dotnet build OmniCard.slnx
```

### Run (development)

Two terminals — the backend serves the API, Vite serves the SPA with hot-reload and proxies `/api`:

```bash
# terminal 1 — backend on :5000
dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "C:\path\to\your\data"

# terminal 2 — SPA dev server on :5173
cd OmniCard.Web/ClientApp && npm run dev
```

Open <http://localhost:5173>. For a production-like check, `npm run build` then browse <http://localhost:5000/app/>.

### Tests

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

### Publish (for IIS)

```bash
cd OmniCard.Web/ClientApp && npm run build && cd ../..
dotnet publish OmniCard.Web/OmniCard.Web.csproj -c Release -r win-x64 --self-contained false
```

## SPA pages

The React app is served at `/app`:

| Route | Description |
|-------|-------------|
| `/` | Dashboard — holdings + realized P&L |
| `/scan` | Upload/scan cards; server-side match, review, and commit |
| `/collection` | Searchable collection grid with edit drawer |
| `/locations`, `/location/:id`, `/binder/:id` | Storage locations + visual binder |
| `/sets` | Set-completion checklist + want-list PDF |
| `/inventory` | Sealed product + lots + valuation |
| `/lists`, `/trades` | Saved card lists; trade history |
| `/import` | CSV import/export + decklist check |
| `/sales` | Orders kanban, listings, customers |
| `/settings` | eBay connection, catalog refresh, artwork download |

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | ASP.NET Core (.NET 10), EF Core |
| Frontend | React + TypeScript + Vite + MUI, TanStack Query |
| Database | SQL Server (unified store + per-game catalogs) |
| Card identification | Perceptual hashing (pHash), OCR |
| Card data | Scryfall API, TCGCSV, poneglyph (One Piece) |
| PDF reports | QuestPDF |
| eBay integration | eBay REST API + OAuth |
| Logging | Serilog |

## Project Structure

```
OmniCard.Web/           ASP.NET Core API + React/TS SPA (ClientApp/) — the app
OmniCard.Api.Contracts/ Pure DTO records (the SPA's request/response contract)
OmniCard.Shared/        Shared models and interfaces
OmniCard.Data/          EF Core DbContexts (unified store + per-game catalogs)
OmniCard.CardMatching/  Per-game card services + hash/OCR matching
OmniCard.Collection/    Collection, inventory, sales, decklist, lists, trades
OmniCard.Imaging/       Perceptual hashing, OCR, image caching
OmniCard.eBay/          eBay OAuth, catalog, and listing services
OmniCard.Audit/         Location auditing and PDF export
OmniCard.DbMigrator/    One-time SQLite → SQL Server data copy
OmniCard.Tests/         Unit and integration tests (xUnit)
```

## Data Storage

- **SQL Server** — the unified store (`OmniCard`: collection, inventory, sales) and one catalog DB per game (`OmniCard_Scryfall`, `OmniCard_Pokemon`, …).
- **Data directory** (`--db` / `DataDirectory`, default `%LOCALAPPDATA%\OmniCard`) — `scans/`, `card-images/` (server-hosted artwork), `symbols/`, `dataprotection-keys/`, and `logs/` (14-day rolling retention).

## License

[MIT](LICENSE.txt)
