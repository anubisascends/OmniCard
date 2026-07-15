# Web Companion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a standalone, read-only, mobile-friendly web app that lets users view the contents of their storage locations, and add QR code link text to the OmniCard desktop app.

**Architecture:** A new `OmniCard.Shared` class library extracts models and DbContext so both the WPF app and a new `OmniCard.Web` ASP.NET Core Razor Pages app can share them. The web app reads the same `collection.db` in read-only mode. The desktop app gets a `WebCompanion.BaseUrl` setting and shows link text per container.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, EF Core SQLite, xUnit

## Global Constraints

- Target framework: `net10.0` for shared/web projects, `net10.0-windows10.0.22621.0` for WPF/tests
- EF Core SQLite version: `10.0.9` (match existing)
- No JavaScript frameworks — server-rendered Razor Pages with plain CSS
- Web app is read-only — no write endpoints
- All new projects added to `OmniCard.slnx`

---

### Task 1: Create OmniCard.Shared and Extract Models

**Files:**
- Create: `OmniCard.Shared/OmniCard.Shared.csproj`
- Create: `OmniCard.Shared/Models/CollectionCard.cs`
- Create: `OmniCard.Shared/Models/StorageContainer.cs`
- Create: `OmniCard.Shared/Models/ContainerType.cs`
- Create: `OmniCard.Shared/Models/CardGame.cs`
- Create: `OmniCard.Shared/Models/MismatchLog.cs`
- Create: `OmniCard.Shared/Models/FlagResolution.cs`
- Create: `OmniCard.Shared/Data/CollectionDbContext.cs`
- Modify: `OmniCard/OmniCard.csproj` — add ProjectReference to Shared, remove EF Core Sqlite package
- Modify: `OmniCard.Tests/OmniCard.Tests.csproj` — keep ProjectReference to OmniCard (transitive)
- Modify: `OmniCard.slnx` — add OmniCard.Shared project
- Delete: `OmniCard/Models/CollectionCard.cs`, `OmniCard/Models/StorageContainer.cs`, `OmniCard/Models/ContainerType.cs`, `OmniCard/Models/CardGame.cs`, `OmniCard/Models/MismatchLog.cs`, `OmniCard/Models/FlagResolution.cs`, `OmniCard/Data/CollectionDbContext.cs`

**Interfaces:**
- Produces: `OmniCard.Shared` NuGet-style project reference providing `OmniCard.Models.*` and `OmniCard.Data.CollectionDbContext`

- [ ] **Step 1: Create the shared project file**

Create `OmniCard.Shared/OmniCard.Shared.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.9" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Move model files to the shared project**

Move (not copy) these files from `OmniCard/Models/` to `OmniCard.Shared/Models/`:
- `CollectionCard.cs`
- `StorageContainer.cs`
- `ContainerType.cs`
- `CardGame.cs`
- `MismatchLog.cs`
- `FlagResolution.cs`

The namespace stays `OmniCard.Models` in each file — no changes to file contents needed. All `using OmniCard.Models;` references throughout the codebase continue to work.

Move `OmniCard/Data/CollectionDbContext.cs` to `OmniCard.Shared/Data/CollectionDbContext.cs`. The namespace stays `OmniCard.Data`.

- [ ] **Step 3: Update OmniCard.csproj to reference OmniCard.Shared**

Add a ProjectReference and remove the EF Core Sqlite package (it comes transitively from Shared):

```xml
<ItemGroup>
  <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
</ItemGroup>
```

Remove from the `<PackageReference>` list:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.9" />
```

Keep `Microsoft.EntityFrameworkCore.Design` — it's needed for tooling in the WPF project.

- [ ] **Step 4: Update OmniCard.slnx**

```xml
<Solution>
  <Project Path="OmniCard.Shared/OmniCard.Shared.csproj" />
  <Project Path="OmniCard.Tests/OmniCard.Tests.csproj" />
  <Project Path="OmniCard/OmniCard.csproj" />
</Solution>
```

- [ ] **Step 5: Build and run tests**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeded, 0 errors

Run: `dotnet test OmniCard.Tests --no-restore`
Expected: All 280 tests pass

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/ OmniCard.slnx OmniCard/OmniCard.csproj
git add -u OmniCard/Models/CollectionCard.cs OmniCard/Models/StorageContainer.cs OmniCard/Models/ContainerType.cs OmniCard/Models/CardGame.cs OmniCard/Models/MismatchLog.cs OmniCard/Models/FlagResolution.cs OmniCard/Data/CollectionDbContext.cs
git commit -m "refactor: extract shared models and DbContext into OmniCard.Shared"
```

---

### Task 2: Create OmniCard.Web Project with Home Page

**Files:**
- Create: `OmniCard.Web/OmniCard.Web.csproj`
- Create: `OmniCard.Web/Program.cs`
- Create: `OmniCard.Web/Pages/Index.cshtml`
- Create: `OmniCard.Web/Pages/Index.cshtml.cs`
- Create: `OmniCard.Web/wwwroot/css/site.css`
- Create: `OmniCard.Web/appsettings.json`
- Modify: `OmniCard.slnx` — add OmniCard.Web project

**Interfaces:**
- Consumes: `OmniCard.Shared` — `CollectionDbContext`, `StorageContainer`, `ContainerType`
- Produces: `GET /` — HTML page listing all storage containers with name, type, card count

- [ ] **Step 1: Create the web project file**

Create `OmniCard.Web/OmniCard.Web.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create appsettings.json**

Create `OmniCard.Web/appsettings.json`:
```json
{
  "DataDirectory": ""
}
```

- [ ] **Step 3: Create Program.cs**

Create `OmniCard.Web/Program.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

var builder = WebApplication.CreateBuilder(args);

// --db command-line argument overrides config
var dataDir = builder.Configuration.GetValue<string>("db")
    ?? builder.Configuration.GetValue<string>("DataDirectory")
    ?? "";

if (string.IsNullOrWhiteSpace(dataDir))
{
    Console.Error.WriteLine("Error: DataDirectory not configured. Use --db <path> or set DataDirectory in appsettings.json.");
    return 1;
}

var dbPath = Path.Combine(dataDir, "collection.db");
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Error: Database not found at {dbPath}");
    return 1;
}

var scansDir = Path.Combine(dataDir, "scans");

builder.Services.AddRazorPages();
builder.Services.AddDbContextFactory<CollectionDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Mode=ReadOnly"));

var app = builder.Build();

app.UseStaticFiles();

// Serve scan images from the data directory
if (Directory.Exists(scansDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(scansDir),
        RequestPath = "/scans"
    });
}

app.MapRazorPages();

Console.WriteLine($"Serving collection from: {dataDir}");
app.Run();
return 0;
```

- [ ] **Step 4: Create the home page Razor Page**

Create `OmniCard.Web/Pages/Index.cshtml.cs`:
```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public IndexModel(IDbContextFactory<CollectionDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public List<ContainerSummary> Containers { get; set; } = [];

    public void OnGet()
    {
        using var db = _dbFactory.CreateDbContext();
        Containers = db.StorageContainers
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new ContainerSummary
            {
                Id = c.Id,
                Name = c.Name,
                ContainerType = c.ContainerType,
                CardCount = c.Cards.Count,
            })
            .ToList();
    }

    public record ContainerSummary
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public ContainerType ContainerType { get; init; }
        public int CardCount { get; init; }

        public string TypeDisplay => ContainerType switch
        {
            ContainerType.Bulk => "Bulk",
            ContainerType.Binder => "Binder",
            ContainerType.Box => "Box",
            ContainerType.DeckBox => "Deck Box",
            ContainerType.DisplayCase => "Display Case",
            _ => ContainerType.ToString(),
        };
    }
}
```

Create `OmniCard.Web/Pages/Index.cshtml`:
```html
@page
@model OmniCard.Web.Pages.IndexModel
@{
    ViewData["Title"] = "OmniCard Collection";
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1"/>
    <title>@ViewData["Title"]</title>
    <link rel="stylesheet" href="/css/site.css"/>
</head>
<body>
    <header>
        <h1>OmniCard Collection</h1>
    </header>
    <main>
        <h2>Storage Locations</h2>
        @if (Model.Containers.Count == 0)
        {
            <p class="empty">No storage locations found.</p>
        }
        else
        {
            <table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Type</th>
                        <th>Cards</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var c in Model.Containers)
                    {
                        <tr>
                            <td><a href="/location/@c.Id">@c.Name</a></td>
                            <td>@c.TypeDisplay</td>
                            <td>@c.CardCount</td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    </main>
</body>
</html>
```

- [ ] **Step 5: Create the CSS**

Create `OmniCard.Web/wwwroot/css/site.css`:
```css
:root {
    --bg: #1e1e1e;
    --surface: #2d2d2d;
    --text: #e0e0e0;
    --text-muted: #a0a0a0;
    --accent: #7c4dff;
    --link: #bb86fc;
    --border: #444;
}

* { box-sizing: border-box; margin: 0; padding: 0; }

body {
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    background: var(--bg);
    color: var(--text);
    line-height: 1.5;
    padding: 16px;
    max-width: 800px;
    margin: 0 auto;
}

header h1 {
    font-size: 1.4rem;
    margin-bottom: 16px;
    color: var(--accent);
}

h2 { font-size: 1.1rem; margin-bottom: 12px; }

a { color: var(--link); text-decoration: none; }
a:hover { text-decoration: underline; }

table {
    width: 100%;
    border-collapse: collapse;
}

th, td {
    text-align: left;
    padding: 10px 12px;
    border-bottom: 1px solid var(--border);
}

th {
    background: var(--surface);
    font-size: 0.85rem;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: var(--text-muted);
}

tr:active { background: var(--surface); }

.empty { color: var(--text-muted); font-style: italic; }

.back-link { display: inline-block; margin-bottom: 16px; font-size: 0.9rem; }

.card-image {
    max-width: 300px;
    width: 100%;
    border-radius: 8px;
    margin-bottom: 16px;
}

.detail-table { margin-bottom: 16px; }
.detail-table th { width: 140px; font-size: 0.8rem; }
.detail-table td { font-size: 0.95rem; }

.set-list { list-style: none; margin-bottom: 16px; }
.set-list li { padding: 4px 0; border-bottom: 1px solid var(--border); }
.set-list .count { color: var(--text-muted); font-size: 0.85rem; }

.badge {
    display: inline-block;
    padding: 2px 8px;
    border-radius: 4px;
    font-size: 0.75rem;
    background: var(--surface);
    color: var(--text-muted);
}

.badge.foil { background: #4a148c; color: #e1bee7; }
```

- [ ] **Step 6: Add OmniCard.Web to the solution**

Update `OmniCard.slnx`:
```xml
<Solution>
  <Project Path="OmniCard.Shared/OmniCard.Shared.csproj" />
  <Project Path="OmniCard.Tests/OmniCard.Tests.csproj" />
  <Project Path="OmniCard.Web/OmniCard.Web.csproj" />
  <Project Path="OmniCard/OmniCard.csproj" />
</Solution>
```

- [ ] **Step 7: Build the full solution**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 8: Smoke test manually**

Run: `dotnet run --project OmniCard.Web -- --db "T:\TCG Card Scanner"`
Expected: Server starts, navigate to `http://localhost:5000` to see the container list.
Stop the server with Ctrl+C.

- [ ] **Step 9: Commit**

```bash
git add OmniCard.Web/ OmniCard.slnx
git commit -m "feat: add OmniCard.Web project with home page listing storage locations"
```

---

### Task 3: Location Detail Page

**Files:**
- Create: `OmniCard.Web/Pages/Location.cshtml`
- Create: `OmniCard.Web/Pages/Location.cshtml.cs`

**Interfaces:**
- Consumes: `CollectionDbContext`, `CollectionCard`, `StorageContainer`
- Produces: `GET /location/{id}` — HTML page showing container name, type, card count, set summary, and card list table

- [ ] **Step 1: Create the Location page model**

Create `OmniCard.Web/Pages/Location.cshtml.cs`:
```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class LocationModel : PageModel
{
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public LocationModel(IDbContextFactory<CollectionDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public StorageContainer Container { get; set; } = null!;
    public int CardCount { get; set; }
    public List<SetSummary> Sets { get; set; } = [];
    public List<CollectionCard> Cards { get; set; } = [];

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var container = db.StorageContainers
            .AsNoTracking()
            .FirstOrDefault(c => c.Id == id);

        if (container is null)
            return NotFound();

        Container = container;

        Cards = db.Cards
            .AsNoTracking()
            .Where(c => c.ContainerId == id)
            .OrderBy(c => c.Name)
            .ToList();

        CardCount = Cards.Count;

        Sets = Cards
            .GroupBy(c => new { c.SetCode, c.SetName })
            .Select(g => new SetSummary
            {
                SetCode = g.Key.SetCode,
                SetName = g.Key.SetName,
                Count = g.Count(),
            })
            .OrderBy(s => s.SetName)
            .ToList();

        return Page();
    }

    public string TypeDisplay => Container.ContainerType switch
    {
        ContainerType.Bulk => "Bulk",
        ContainerType.Binder => "Binder",
        ContainerType.Box => "Box",
        ContainerType.DeckBox => "Deck Box",
        ContainerType.DisplayCase => "Display Case",
        _ => Container.ContainerType.ToString(),
    };

    public record SetSummary
    {
        public string SetCode { get; init; } = "";
        public string SetName { get; init; } = "";
        public int Count { get; init; }
    }
}
```

- [ ] **Step 2: Create the Location Razor view**

Create `OmniCard.Web/Pages/Location.cshtml`:
```html
@page "/location/{id:int}"
@model OmniCard.Web.Pages.LocationModel
@{
    ViewData["Title"] = Model.Container.Name;
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1"/>
    <title>@ViewData["Title"] — OmniCard</title>
    <link rel="stylesheet" href="/css/site.css"/>
</head>
<body>
    <a href="/" class="back-link">&larr; All Locations</a>
    <header>
        <h1>@Model.Container.Name</h1>
        <p><span class="badge">@Model.TypeDisplay</span> &middot; @Model.CardCount card(s)</p>
    </header>
    <main>
        @if (Model.Sets.Count > 0)
        {
            <h2>Sets</h2>
            <ul class="set-list">
                @foreach (var s in Model.Sets)
                {
                    <li>@s.SetName (@s.SetCode) <span class="count">&times;@s.Count</span></li>
                }
            </ul>
        }

        <h2>Cards</h2>
        @if (Model.Cards.Count == 0)
        {
            <p class="empty">No cards in this location.</p>
        }
        else
        {
            <table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Set</th>
                        <th>#</th>
                        <th>Rarity</th>
                        <th>Cond</th>
                        <th>Foil</th>
                        <th>Color</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var card in Model.Cards)
                    {
                        <tr>
                            <td><a href="/card/@card.Id">@card.Name</a></td>
                            <td>@card.SetCode</td>
                            <td>@card.Number</td>
                            <td>@card.Rarity</td>
                            <td>@card.Condition</td>
                            <td>@(card.IsFoil ? "Yes" : "")</td>
                            <td>@card.Color</td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    </main>
</body>
</html>
```

- [ ] **Step 3: Build and smoke test**

Run: `dotnet build OmniCard.Web`
Expected: Build succeeded

Run: `dotnet run --project OmniCard.Web -- --db "T:\TCG Card Scanner"`
Navigate to `http://localhost:5000/location/1` — should show the container detail.

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Web/Pages/Location.cshtml OmniCard.Web/Pages/Location.cshtml.cs
git commit -m "feat: add location detail page with set summary and card list"
```

---

### Task 4: Card Detail Page

**Files:**
- Create: `OmniCard.Web/Pages/Card.cshtml`
- Create: `OmniCard.Web/Pages/Card.cshtml.cs`

**Interfaces:**
- Consumes: `CollectionDbContext`, `CollectionCard`, `StorageContainer`
- Produces: `GET /card/{id}` — HTML page showing all card fields and image

- [ ] **Step 1: Create the Card page model**

Create `OmniCard.Web/Pages/Card.cshtml.cs`:
```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

namespace OmniCard.Web.Pages;

public class CardModel : PageModel
{
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public CardModel(IDbContextFactory<CollectionDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public OmniCard.Models.CollectionCard Card { get; set; } = null!;

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var card = db.Cards
            .AsNoTracking()
            .Include(c => c.Container)
            .FirstOrDefault(c => c.Id == id);

        if (card is null)
            return NotFound();

        Card = card;
        return Page();
    }

    public string? ImageUrl
    {
        get
        {
            if (Card.ScanImagePath is not null)
            {
                // ScanImagePath is "scans/123.jpg" — serve from /scans/123.jpg
                var filename = Path.GetFileName(Card.ScanImagePath);
                return $"/scans/{filename}";
            }
            return Card.ImageUri;
        }
    }
}
```

- [ ] **Step 2: Create the Card Razor view**

Create `OmniCard.Web/Pages/Card.cshtml`:
```html
@page "/card/{id:int}"
@model OmniCard.Web.Pages.CardModel
@{
    ViewData["Title"] = Model.Card.Name;
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1"/>
    <title>@ViewData["Title"] — OmniCard</title>
    <link rel="stylesheet" href="/css/site.css"/>
</head>
<body>
    @if (Model.Card.ContainerId.HasValue)
    {
        <a href="/location/@Model.Card.ContainerId" class="back-link">&larr; Back to @(Model.Card.Container?.Name ?? "Location")</a>
    }
    else
    {
        <a href="/" class="back-link">&larr; Home</a>
    }

    <header>
        <h1>@Model.Card.Name</h1>
    </header>

    <main>
        @if (Model.ImageUrl is not null)
        {
            <img src="@Model.ImageUrl" alt="@Model.Card.Name" class="card-image"/>
        }

        <table class="detail-table">
            <tr><th>Set</th><td>@Model.Card.SetName (@Model.Card.SetCode)</td></tr>
            <tr><th>Collector #</th><td>@Model.Card.Number</td></tr>
            <tr><th>Rarity</th><td>@Model.Card.Rarity</td></tr>
            <tr><th>Color</th><td>@(Model.Card.Color ?? "—")</td></tr>
            <tr><th>Type</th><td>@(Model.Card.CardType ?? "—")</td></tr>
            <tr><th>Foil</th><td>@(Model.Card.IsFoil ? "Yes" : "No")</td></tr>
            <tr><th>Condition</th><td>@Model.Card.Condition</td></tr>
            <tr><th>Purchase Price</th><td>@(Model.Card.PurchasePrice.HasValue ? Model.Card.PurchasePrice.Value.ToString("C") : "—")</td></tr>
            <tr><th>Date Added</th><td>@Model.Card.DateAdded.ToString("yyyy-MM-dd")</td></tr>
            @if (Model.Card.Container is not null)
            {
                <tr><th>Location</th><td>@Model.Card.Container.Name</td></tr>
            }
            @if (Model.Card.Page.HasValue)
            {
                <tr><th>Page</th><td>@Model.Card.Page</td></tr>
            }
            @if (Model.Card.Slot.HasValue)
            {
                <tr><th>Slot</th><td>@Model.Card.Slot</td></tr>
            }
            @if (Model.Card.Section is not null)
            {
                <tr><th>Section</th><td>@Model.Card.Section</td></tr>
            }
        </table>
    </main>
</body>
</html>
```

- [ ] **Step 3: Build and smoke test**

Run: `dotnet build OmniCard.Web`
Expected: Build succeeded

Run: `dotnet run --project OmniCard.Web -- --db "T:\TCG Card Scanner"`
Navigate to `http://localhost:5000/card/1` — should show card details with image.

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Web/Pages/Card.cshtml OmniCard.Web/Pages/Card.cshtml.cs
git commit -m "feat: add card detail page with image and all fields"
```

---

### Task 5: Desktop Integration — WebCompanion Settings and QR Link Text

**Files:**
- Create: `OmniCard/Models/WebCompanionSettings.cs`
- Modify: `OmniCard/appsettings.json` — add WebCompanion section
- Modify: `OmniCard/App.xaml.cs:55-57` — register WebCompanionSettings
- Modify: `OmniCard/Views/StorageManager/StorageManagerViewModel.cs` — add QR link text
- Modify: `OmniCard/Views/StorageManager/StorageManagerView.xaml` — display QR link text

**Interfaces:**
- Consumes: `IOptions<WebCompanionSettings>`, `ContainerDisplayItem.Id`
- Produces: QR code link text displayed per container in the Storage Manager view

- [ ] **Step 1: Create WebCompanionSettings model**

Create `OmniCard/Models/WebCompanionSettings.cs`:
```csharp
namespace OmniCard.Models;

public class WebCompanionSettings
{
    public string BaseUrl { get; set; } = "";
}
```

- [ ] **Step 2: Add WebCompanion section to appsettings.json**

Add after the `"Scryfall"` section in `OmniCard/appsettings.json`:
```json
"WebCompanion": {
  "BaseUrl": ""
}
```

- [ ] **Step 3: Register WebCompanionSettings in App.xaml.cs**

In `OmniCard/App.xaml.cs`, add after line 57 (`services.Configure<ScryfallSettings>(...)`):
```csharp
services.Configure<WebCompanionSettings>(context.Configuration.GetSection("WebCompanion"));
```

- [ ] **Step 4: Update StorageManagerViewModel to accept settings and expose link text**

In `OmniCard/Views/StorageManager/StorageManagerViewModel.cs`, change the constructor and add a computed property:

Replace the class declaration (line 9):
```csharp
public sealed partial class StorageManagerViewModel(IStorageContainerService containerService) : ViewModel
```
with:
```csharp
public sealed partial class StorageManagerViewModel(
    IStorageContainerService containerService,
    IOptions<WebCompanionSettings> webCompanionSettings) : ViewModel
```

Add the using at the top of the file:
```csharp
using Microsoft.Extensions.Options;
```

Add a computed property after the `CanDelete` property (after line 40):
```csharp
public string? QrLinkText
{
    get
    {
        var baseUrl = webCompanionSettings.Value.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl) || SelectedContainer is null)
            return null;
        return $"{baseUrl.TrimEnd('/')}/location/{SelectedContainer.Id}";
    }
}
```

Update `OnSelectedContainerChanged` (line 33-37) to also notify QrLinkText:
```csharp
partial void OnSelectedContainerChanged(ContainerDisplayItem? value)
{
    OnPropertyChanged(nameof(CanEdit));
    OnPropertyChanged(nameof(CanDelete));
    OnPropertyChanged(nameof(QrLinkText));
}
```

- [ ] **Step 5: Update StorageManagerView.xaml to display QR link text**

In `OmniCard/Views/StorageManager/StorageManagerView.xaml`, add after the action buttons StackPanel (after line 52, before the Add panel Border):
```xml
<!-- QR link text -->
<StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,4,0,4"
            Visibility="{Binding ViewModel.SelectedContainer, Converter={StaticResource BoolToVis}}">
    <TextBlock Text="QR Link:" VerticalAlignment="Center" Margin="0,0,8,0"
               Visibility="{Binding ViewModel.QrLinkText, Converter={StaticResource NullToCollapsed}}"/>
    <TextBox Text="{Binding ViewModel.QrLinkText, Mode=OneWay}" IsReadOnly="True"
             Width="300" VerticalAlignment="Center" FontSize="11"
             Visibility="{Binding ViewModel.QrLinkText, Converter={StaticResource NullToCollapsed}}"/>
</StackPanel>
```

This requires a `NullToCollapsed` converter. Add it to the Window.Resources section (after line 21):
```xml
<local:NullToVisibilityConverter x:Key="NullToCollapsed"/>
```

Create the converter within the `StorageManagerView.xaml.cs` file (or the existing Converters file). Add to `OmniCard/Views/StorageManager/StorageManagerView.xaml.cs` after the existing class:
```csharp
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
```

Add the needed usings to the code-behind if not already present:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;
```

Adjust the Grid.RowDefinitions to add a row for the QR link text. The current rows are `*, Auto, Auto, Auto`. Add a row after the buttons row. The QR link and buttons can share the same row by placing the QR text in a separate row. Update the Grid:
```xml
<Grid.RowDefinitions>
    <RowDefinition Height="*"/>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>
</Grid.RowDefinitions>
```

Set the QR link StackPanel to `Grid.Row="2"`, move the Add panel Border to `Grid.Row="3"`, the Edit panel Border to `Grid.Row="3"`, and the Close button to `Grid.Row="4"`.

- [ ] **Step 6: Build and run tests**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeded, 0 errors

Run: `dotnet test OmniCard.Tests --no-restore`
Expected: All tests pass

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Models/WebCompanionSettings.cs OmniCard/appsettings.json OmniCard/App.xaml.cs
git add OmniCard/Views/StorageManager/
git commit -m "feat: add WebCompanion base URL setting and QR link text in Storage Manager"
```
