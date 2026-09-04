# OmniCard

A desktop scanner and collection manager for trading card games. Scan physical cards with a TWAIN scanner or phone camera, automatically identify them via perceptual hashing and OCR, and organize your collection across storage locations.

Supports **Magic: The Gathering** (via Scryfall) and **One Piece TCG**.

## Features

- **Bulk scanning** with automatic card identification using perceptual image hashing
- **OCR-based matching** for One Piece TCG collector numbers
- **Manual search** by card name or set/collector number (e.g. `TMT-002`, `OP15-041`)
- **Collection management** with storage locations (binders, boxes, deck boxes, bulk)
- **Set completion tracking** with missing card reports
- **Decklist checking** against your collection (Moxfield and Archidekt)
- **CSV import/export** (Manabox, Moxfield, TCGPlayer, app-native formats)
- **eBay listing integration** for selling cards
- **Inventory tracking** for sealed product (booster boxes, packs, bundles, etc.) with lots and valuation
- **Web app** for browsing *and managing* your collection from any device — edit cards, manage locations/binders/inventory/sales, and scan with your phone camera (matching runs server-side). See [OmniCard.Web/README.md](OmniCard.Web/README.md).
- **Location auditing** with PDF reports

## Requirements

- **OS:** Windows 10 22H2 (build 22621) or later
- **Runtime:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for building from source)
- **Scanner:** Any TWAIN-compatible scanner (optional -- you can also import images or scan with a phone)

## Download

Grab the latest release from the [Releases](../../releases) page. Each release includes two downloads:

| Asset | What it is |
|-------|------------|
| `OmniCard-v{VERSION}-win-x64.zip` | The desktop app -- a single self-contained `OmniCard.exe` plus a handful of native scanner/OCR support files |
| `OmniCard-Web-v{VERSION}-iis.zip` | The web companion, packaged for installation on IIS |

### Installing the Desktop App

Download `OmniCard-v{VERSION}-win-x64.zip`, extract it anywhere, and run `OmniCard.exe`.

No installation required -- just extract and run.

### Installing the Web Companion on IIS

The web app is a normal ASP.NET Core app (a React SPA served by the backend). It is a full
read/write app backed by **SQL Server** for the unified collection/inventory/sales store, with the
per-game catalog databases opened read-only as reference caches. People on your network can browse,
edit, and scan with their phones.

> The steps below are a quick outline. For the full deployment guide — SPA build, SQL Server setup,
> the one-time SQLite→SQL Server data migration, configuration keys, and eBay setup — see
> **[OmniCard.Web/README.md](OmniCard.Web/README.md)**.

**Prerequisites (on the IIS server):**

- IIS with the **ASP.NET Core Module V2** -- install the
  [.NET 10 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) (not just the SDK/runtime), then restart the server (or at least run `net stop was /y` followed by `net start w3svc`).
- **SQL Server 2019+** (Express is fine) for the unified store, plus a one-time data migration from
  the desktop's `inventory.db` (see the deployment guide).
- The desktop app must have run at least once on a reachable path, so the per-game catalog databases
  exist to point at.

**Steps:**

1. Download `OmniCard-Web-v{VERSION}-iis.zip` and extract it to a folder, e.g. `C:\inetpub\OmniCardWeb`.
2. In IIS Manager, create an **Application Pool** for the site with **.NET CLR version** set to
   **No Managed Code** (the ASP.NET Core Module hosts the runtime itself, IIS doesn't need to).
3. Create a **Site** (or **Application** under an existing site) with its physical path pointing at
   the folder from step 1, using the app pool from step 2.
4. Point the app at your data directory. Open the generated `web.config` in that folder and add an
   `environmentVariables` entry inside the `<aspNetCore>` element:
   ```xml
   <aspNetCore processPath="dotnet" arguments=".\OmniCard.Web.dll" ...>
     <environmentVariables>
       <environmentVariable name="DataDirectory" value="C:\Users\<you>\AppData\Local\OmniCard" />
     </environmentVariables>
   </aspNetCore>
   ```
   Use the same data directory the desktop app uses (default `%LOCALAPPDATA%\OmniCard`).
5. Grant the app pool identity (`IIS AppPool\<your app pool name>`) **read** access to the catalog
   SQLite databases + `scans/`, **read/write** on `<DataDirectory>/dataprotection-keys`, and access
   to SQL Server (via Windows auth or a SQL login in the connection string).
6. Browse to the site (the SPA lives under `/app`). Scanning works from any device on the same
   network once you're browsing over the LAN.

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (win-x64)
- Windows 10 22H2 or later

### Build

```bash
git clone https://github.com/anubisascends/OmniCard.git
cd OmniCard
dotnet build OmniCard.slnx
```

### Run

```bash
dotnet run --project OmniCard/OmniCard.csproj
```

On first launch, the app creates its data directory at `%LOCALAPPDATA%\OmniCard` and will prompt you to download card data.

### Run Tests

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

### Publish (Release Build)

```bash
dotnet publish OmniCard/OmniCard.csproj -c Release -r win-x64
```

Output goes to `OmniCard/bin/Release/net10.0-windows10.0.22621.0/win-x64/publish/`.

## Web Companion

The web companion lets you browse your collection from any device on your network and scan cards using your phone's camera.

### Running the Web Companion

```bash
dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "C:\path\to\your\data"
```

Point `--db` at the same data directory the desktop app uses (default: `%LOCALAPPDATA%\OmniCard`). The web app opens the databases in read-only mode.

### Web Companion Pages

| Page | Description |
|------|-------------|
| `/` | Collection browser with search, game filter, and storage location overview |
| `/location/{id}` | Cards in a specific storage location |
| `/card/{id}` | Card detail with scan image |
| `/scan` | Phone scanner -- capture cards with your phone camera |
| `/decklist` | Check a Moxfield or Archidekt decklist against your collection |

## Tech Stack

| Component | Technology |
|-----------|------------|
| Desktop App | WPF (.NET 10), CommunityToolkit.Mvvm, MaterialDesignThemes |
| Web Companion | ASP.NET Core Razor Pages, SignalR |
| Database | SQLite via Entity Framework Core |
| Card Identification | Perceptual hashing (pHash), OCR |
| Scanner Integration | NTwain (TWAIN protocol) |
| MTG Card Data | Scryfall API |
| PDF Reports | QuestPDF |
| eBay Integration | eBay REST API + OAuth |
| SVG Rendering | SharpVectors |
| Logging | Serilog |

## Project Structure

```
OmniCard/                  Main WPF desktop application
OmniCard.Web/              ASP.NET Core web companion
OmniCard.Shared/           Shared models and interfaces
OmniCard.Data/             EF Core database contexts (SQLite)
OmniCard.CardMatching/     Scryfall + OPTCG game services, hash matching
OmniCard.Collection/       Collection management, CSV, decklist service
OmniCard.Imaging/          Perceptual hashing, OCR, image caching
OmniCard.Scanner/          TWAIN scanner coordination
OmniCard.ScannerHost/      Out-of-process TWAIN bridge
OmniCard.Controls/         Reusable WPF controls and themes
OmniCard.eBay/             eBay OAuth, catalog, and listing services
OmniCard.Audit/            Location auditing and PDF export
OmniCard.Tests/            Unit and integration tests (xUnit)
```

## Data Storage

Card data and scans are stored locally in `%LOCALAPPDATA%\OmniCard` by default (configurable via the app's Data Location settings):

- `collection.db` -- your scanned cards and storage locations
- `scryfall.db` -- MTG card reference data (downloaded from Scryfall)
- `optcg.db` -- One Piece TCG reference data
- `inventory.db` -- sealed product inventory (products, lots, valuation)
- `scans/` -- saved scan images
- `logs/` -- application logs (14-day rolling retention)

## License

[MIT](LICENSE.txt)
