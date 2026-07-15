# OmniCard Web Companion

A read-only, mobile-friendly web app that lets users view the contents of their storage locations by scanning a QR code. Runs as a standalone ASP.NET Core server alongside the OmniCard desktop app, reading from the same SQLite database.

## Architecture

### New Projects

**OmniCard.Shared** (Class Library, `net10.0`)
- Extracts shared code from OmniCard WPF so both apps can reference it
- Contains: `CollectionCard`, `StorageContainer`, `ContainerType`, `CardGame`, `CollectionDbContext`
- No WPF dependencies

**OmniCard.Web** (ASP.NET Core, `net10.0`)
- Minimal API + Razor Pages
- References `OmniCard.Shared`
- Read-only access to `collection.db`
- Serves scan images from the scans directory as static files
- Dark-themed responsive CSS (no JS framework)

**OmniCard** (WPF, modified)
- References `OmniCard.Shared` instead of defining models directly
- New `WebCompanionSettings` for base URL configuration
- Storage Manager UI displays QR code link text per container

### Data Flow

```
Phone (browser) --> OmniCard.Web (Kestrel) --> collection.db (SQLite, read-only)
                                           --> scans/ directory (static file serving)

OmniCard (WPF) --> collection.db (SQLite, read-write)
               --> generates QR link text using BaseUrl + ContainerId
```

Both apps share models and DbContext via `OmniCard.Shared`. The web app opens the database in read-only mode to avoid locking conflicts with the desktop app.

## Web App Configuration

Command-line usage:
```
OmniCard.Web --db "T:\TCG Card Scanner" --urls "http://0.0.0.0:5000"
```

`appsettings.json`:
```json
{
  "DataDirectory": "T:\\TCG Card Scanner"
}
```

The `--db` argument overrides `DataDirectory` from config. The app derives paths from it:
- Database: `{DataDirectory}/collection.db`
- Scans: `{DataDirectory}/scans/`

## Pages & Routes

### `GET /` - Home

Lists all storage containers. Each entry shows:
- Container name
- Container type (Binder, Box, etc.)
- Card count

Each container links to `/location/{id}`.

### `GET /location/{id}` - Location Detail

Header section:
- Container name and type
- Total card count
- List of distinct sets with per-set card counts

Card list table (sortable by column):
- Name, Set, Collector Number, Rarity, Condition, Foil, Color

Each card row links to `/card/{id}`.

### `GET /card/{id}` - Card Detail

Displays all card fields:
- **Image**: Scan image (served from `/scans/{filename}`) if available, otherwise Scryfall art URL
- **Identity**: Name, Set Name, Set Code, Collector Number
- **Attributes**: Rarity, Color, Card Type, Foil
- **Collection**: Condition, Purchase Price, Date Added
- **Position**: Page, Slot, Section (within the container)
- **Back link** to the location page

### `GET /scans/{filename}` - Scan Image

Static file serving from the scans directory. Used by the card detail page to display the user's scan image.

## Desktop Integration

### Settings

New section in `appsettings.json`:
```json
"WebCompanion": {
  "BaseUrl": "http://192.168.1.50:5000"
}
```

New model `WebCompanionSettings`:
```csharp
public class WebCompanionSettings
{
    public string BaseUrl { get; set; } = "";
}
```

Registered via `services.Configure<WebCompanionSettings>(...)` in `App.xaml.cs`.

### QR Code Link Text

The Storage Manager view displays a read-only text field per container showing:
```
{BaseUrl}/location/{containerId}
```

Example: `http://192.168.1.50:5000/location/3`

Only visible when `BaseUrl` is configured (non-empty). The user copies this text into external QR code software.

## Styling

- Dark theme matching OmniCard desktop aesthetic
- Responsive CSS (mobile-first, no framework)
- Card images sized appropriately for phone screens
- Clean table layout with adequate touch targets for row links

## Project Structure

```
OmniCard.Shared/
  OmniCard.Shared.csproj
  Models/
    CollectionCard.cs
    StorageContainer.cs
    ContainerType.cs
    CardGame.cs
  Data/
    CollectionDbContext.cs

OmniCard.Web/
  OmniCard.Web.csproj
  Program.cs
  Pages/
    Index.cshtml / Index.cshtml.cs
    Location.cshtml / Location.cshtml.cs
    Card.cshtml / Card.cshtml.cs
  wwwroot/
    css/
      site.css
  appsettings.json
```

## Non-Goals

- No authentication (local network use only)
- No editing/write operations from the web app
- No QR code image generation (user uses external software)
- No real-time sync or push notifications
- No JavaScript SPA framework
