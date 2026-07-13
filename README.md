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
- **Sealed product tracking** (booster boxes, bundles, etc.)
- **Web companion** for browsing your collection from any device and scanning cards with your phone camera
- **Location auditing** with PDF reports

## Requirements

- **OS:** Windows 10 22H2 (build 22621) or later
- **Runtime:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for building from source)
- **Scanner:** Any TWAIN-compatible scanner (optional -- you can also import images or scan with a phone)

## Download

Grab the latest release from the [Releases](../../releases) page. Download the `OmniCard-v{VERSION}-win-x64.zip`, extract it, and run `OmniCard.exe`.

No installation required -- just extract and run.

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
- `sealed_products.db` -- sealed product inventory
- `scans/` -- saved scan images
- `logs/` -- application logs (14-day rolling retention)

## License

[MIT](LICENSE.txt)
