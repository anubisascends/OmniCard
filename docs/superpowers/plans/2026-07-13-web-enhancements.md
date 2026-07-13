# Web Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add card stacking, game filtering, collection search, and decklist checking to the OmniCard.Web companion app.

**Architecture:** Add project references from OmniCard.Web to OmniCard.Collection and OmniCard.CardMatching. Register game DB contexts and services read-only. Create a lightweight `ICardService` adapter for the web context since the full `CardService` has WPF dependencies. Implement features as modifications to existing Razor pages plus one new Decklist page.

**Tech Stack:** ASP.NET Razor Pages, Entity Framework Core (SQLite read-only), C# 13 / .NET 10

## Global Constraints

- All database access is read-only (SQLite `Mode=ReadOnly`)
- Web app targets `net10.0`
- Follow existing code patterns: Razor Pages with code-behind, no JS frameworks
- Dark theme CSS variables already defined in `site.css`
- The user requested all work on a new branch

---

### Task 1: Create Branch and Add Project References + DI Registration

**Files:**
- Modify: `OmniCard.Web/OmniCard.Web.csproj`
- Modify: `OmniCard.Web/Program.cs`
- Create: `OmniCard.Web/Services/WebCardService.cs`

**Interfaces:**
- Consumes: `ICardService` (from OmniCard.Shared), `ICardGameService` (from OmniCard.Shared), `ScryfallService` (from OmniCard.CardMatching), `OptcgService` (from OmniCard.CardMatching), `DecklistService` (from OmniCard.Collection)
- Produces: `WebCardService` — a lightweight `ICardService` implementation that satisfies `DecklistService`'s dependency. Only `GetGameService()` and `AvailableGames` are functional; all other members throw `NotSupportedException`.

- [ ] **Step 1: Create feature branch**

```bash
cd d:/source/repos/OmniCard
git checkout -b feat/web-enhancements
```

- [ ] **Step 2: Add project references to OmniCard.Web.csproj**

Replace the `<ItemGroup>` containing project references in `OmniCard.Web/OmniCard.Web.csproj`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\OmniCard.Shared\OmniCard.Shared.csproj" />
    <ProjectReference Include="..\OmniCard.Data\OmniCard.Data.csproj" />
    <ProjectReference Include="..\OmniCard.Collection\OmniCard.Collection.csproj" />
    <ProjectReference Include="..\OmniCard.CardMatching\OmniCard.CardMatching.csproj" />
  </ItemGroup>
```

- [ ] **Step 3: Create WebCardService**

Create `OmniCard.Web/Services/WebCardService.cs`. This is a minimal adapter so `DecklistService` can resolve `ICardService.GetGameService()`:

```csharp
using System.Collections.ObjectModel;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>
/// Lightweight ICardService for web context. Only GetGameService() is functional —
/// all scanner/scan-related members throw NotSupportedException.
/// </summary>
public sealed class WebCardService(IEnumerable<ICardGameService> gameServices) : ICardService
{
    private readonly Dictionary<CardGame, ICardGameService> _gameServices =
        gameServices.ToDictionary(s => s.Game);

    public ICardGameService GetGameService(CardGame game) => _gameServices[game];
    public IReadOnlyList<CardGame> AvailableGames => _gameServices.Keys.ToList();
    public ICardGameService ActiveGameService => _gameServices.Values.First();

    // -- Everything below is not used in web context --
    public ObservableCollection<ScannedCard> ScannedCards => throw new NotSupportedException();
    public CardGame SelectedGame { get; set; }
    public HashSet<string>? SelectedSetFilter { get; set; }
    public bool DefaultIsFoil { get; set; }
    public decimal? DefaultPurchasePrice { get; set; }
    public Action<HashStageResult>? OnHashStage { get; set; }
    public ulong LastComputedHash => 0;
    public IOcrMatchingService OcrService => throw new NotSupportedException();
    public void AddFromStream(Stream stream) => throw new NotSupportedException();
    public void ReprocessScans() => throw new NotSupportedException();
    public void CommitScans(IEnumerable<ScannedCard> s) => throw new NotSupportedException();
    public void CommitScans(IEnumerable<ScannedCard> s, StorageContainer? c, int? p, int? sl, string? se, IProgress<string>? pr) => throw new NotSupportedException();
    public ulong ComputeHashFromStream(Stream s) => throw new NotSupportedException();
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong h, ulong[]? a = null, OcrMatchResult? o = null, IReadOnlySet<string>? sf = null, IReadOnlySet<string>? ps = null) => throw new NotSupportedException();
    public void SearchCollection(string q, CardGame? g, int? c, ObservableCollection<CollectionCard> r) => throw new NotSupportedException();
    public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame g, IProgress<string>? p = null) => throw new NotSupportedException();
    public List<MissingCard> GetMissingCards(CardGame g, string s) => throw new NotSupportedException();
    public void RemoveTempFile(ScannedCard c) => throw new NotSupportedException();
    public void ClearTempFiles() => throw new NotSupportedException();
    public (int, int, int) ClearDiagnosticLogs() => throw new NotSupportedException();
    public (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? p = null) => throw new NotSupportedException();
    public void StartNewDiagnosticSession() => throw new NotSupportedException();
}
```

Note: The exact method signatures must match `ICardService`. After creating this file, check the `ICardService` interface and fix any missing or mismatched members until it compiles.

- [ ] **Step 4: Update Program.cs to register all services**

Replace `OmniCard.Web/Program.cs` with:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Web.Services;

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
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Collection DB (read-only)
builder.Services.AddDbContextFactory<CollectionDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Mode=ReadOnly"));

// Game databases (read-only)
builder.Services.AddDbContextFactory<ScryfallDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "scryfall.db")};Mode=ReadOnly"));
builder.Services.AddDbContextFactory<OptcgDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "optcg.db")};Mode=ReadOnly"));

// Game services
builder.Services.AddSingleton<ICardGameService, ScryfallService>();
builder.Services.AddSingleton<ICardGameService, OptcgService>();

// Card service adapter for web
builder.Services.AddSingleton<ICardService, WebCardService>();

// Decklist service
builder.Services.AddSingleton<IDecklistService, DecklistService>();

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
app.MapControllers();
app.MapHub<OmniCard.Web.Hubs.ScanHub>("/hubs/scan");

Console.WriteLine($"Serving collection from: {dataDir}");
app.Run();
return 0;
```

- [ ] **Step 5: Build and verify compilation**

```bash
cd d:/source/repos/OmniCard
dotnet build OmniCard.Web/OmniCard.Web.csproj
```

Expected: Build succeeds. Fix any `ICardService` interface mismatches in `WebCardService.cs` until it compiles.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Web/OmniCard.Web.csproj OmniCard.Web/Program.cs OmniCard.Web/Services/WebCardService.cs
git commit -m "feat(web): add project references and DI registration for game services"
```

---

### Task 2: Card Stacking on Location Page

**Files:**
- Modify: `OmniCard.Web/Pages/Location.cshtml.cs`
- Modify: `OmniCard.Web/Pages/Location.cshtml`

**Interfaces:**
- Consumes: `CollectionDbContext.Cards` (from OmniCard.Data)
- Produces: `StackedCard` record with `Id`, `Name`, `SetCode`, `Number`, `Rarity`, `Condition`, `IsFoil`, `Color`, `Quantity` — used by the Location Razor view

- [ ] **Step 1: Update Location.cshtml.cs to group cards**

Replace `OmniCard.Web/Pages/Location.cshtml.cs` with:

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
    public List<StackedCard> Cards { get; set; } = [];

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var container = db.StorageContainers
            .AsNoTracking()
            .FirstOrDefault(c => c.Id == id);

        if (container is null)
            return NotFound();

        Container = container;

        var allCards = db.Cards
            .AsNoTracking()
            .Where(c => c.ContainerId == id)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.SetCode)
            .ToList();

        CardCount = allCards.Count;

        Cards = allCards
            .GroupBy(c => new { c.Name, c.SetCode })
            .Select(g =>
            {
                var first = g.First();
                return new StackedCard
                {
                    Id = first.Id,
                    Name = first.Name,
                    SetCode = first.SetCode,
                    Number = first.Number,
                    Rarity = first.Rarity,
                    Color = first.Color,
                    Quantity = g.Count(),
                };
            })
            .OrderBy(c => c.Name)
            .ThenBy(c => c.SetCode)
            .ToList();

        Sets = allCards
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

    public record StackedCard
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string Number { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string? Color { get; init; }
        public int Quantity { get; init; }
    }
}
```

- [ ] **Step 2: Update Location.cshtml to show Qty column**

Replace the `<table>` section in `OmniCard.Web/Pages/Location.cshtml` (the Cards table, starting at `<table>` inside the `else` block) with:

```html
            <table>
                <thead>
                    <tr>
                        <th>Qty</th>
                        <th>Name</th>
                        <th>Set</th>
                        <th>#</th>
                        <th>Rarity</th>
                        <th>Color</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var card in Model.Cards)
                    {
                        <tr>
                            <td>@card.Quantity</td>
                            <td><a href="/card/@card.Id">@card.Name</a></td>
                            <td>@card.SetCode</td>
                            <td>@card.Number</td>
                            <td>@card.Rarity</td>
                            <td>@card.Color</td>
                        </tr>
                    }
                </tbody>
            </table>
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build OmniCard.Web/OmniCard.Web.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Web/Pages/Location.cshtml OmniCard.Web/Pages/Location.cshtml.cs
git commit -m "feat(web): stack cards by name+set on location page with qty column"
```

---

### Task 3: Game Filter and Collection Search on Index Page

**Files:**
- Modify: `OmniCard.Web/Pages/Index.cshtml.cs`
- Modify: `OmniCard.Web/Pages/Index.cshtml`
- Modify: `OmniCard.Web/wwwroot/css/site.css`

**Interfaces:**
- Consumes: `CollectionDbContext.Cards`, `CollectionDbContext.StorageContainers`
- Produces: Updated Index page model with `GameFilter`, `SearchQuery`, `SearchResults` properties; `CardSearchResult` record

- [ ] **Step 1: Update Index.cshtml.cs with search and game filter logic**

Replace `OmniCard.Web/Pages/Index.cshtml.cs` with:

```csharp
using Microsoft.AspNetCore.Mvc;
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

    [BindProperty(SupportsGet = true)]
    public string? Game { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<ContainerSummary> Containers { get; set; } = [];
    public List<CardSearchResult> SearchResults { get; set; } = [];
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(Q);

    public void OnGet()
    {
        using var db = _dbFactory.CreateDbContext();
        CardGame? gameFilter = ParseGameFilter();

        if (IsSearchActive)
        {
            SearchResults = ExecuteSearch(db, Q!, gameFilter);
        }
        else
        {
            Containers = LoadContainers(db, gameFilter);
        }
    }

    private CardGame? ParseGameFilter()
    {
        return Game?.ToLowerInvariant() switch
        {
            "mtg" => CardGame.Mtg,
            "optcg" => CardGame.OnePiece,
            _ => null,
        };
    }

    private static List<ContainerSummary> LoadContainers(
        CollectionDbContext db, CardGame? gameFilter)
    {
        var query = db.StorageContainers
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name);

        return query
            .Select(c => new ContainerSummary
            {
                Id = c.Id,
                Name = c.Name,
                ContainerType = c.ContainerType,
                CardCount = gameFilter.HasValue
                    ? c.Cards.Count(card => card.Game == gameFilter.Value)
                    : c.Cards.Count,
            })
            .Where(c => c.CardCount > 0 || !gameFilter.HasValue)
            .ToList();
    }

    private static List<CardSearchResult> ExecuteSearch(
        CollectionDbContext db, string query, CardGame? gameFilter)
    {
        IQueryable<CollectionCard> cards = db.Cards.AsNoTracking();

        if (gameFilter.HasValue)
            cards = cards.Where(c => c.Game == gameFilter.Value);

        // Parse search terms
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            var t = term;
            if (t.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[4..];
                cards = cards.Where(c => EF.Functions.Like(c.SetCode, $"%{val}%")
                                      || EF.Functions.Like(c.SetName, $"%{val}%"));
            }
            else if (t.StartsWith("cn:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[3..];
                cards = cards.Where(c => EF.Functions.Like(c.Number, $"%{val}%"));
            }
            else if (t.StartsWith("rarity:", StringComparison.OrdinalIgnoreCase)
                  || t.StartsWith("r:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t.Contains(':') ? t[(t.IndexOf(':') + 1)..] : t;
                cards = cards.Where(c => EF.Functions.Like(c.Rarity, $"%{val}%"));
            }
            else if (t.StartsWith("color:", StringComparison.OrdinalIgnoreCase)
                  || t.StartsWith("c:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t.Contains(':') ? t[(t.IndexOf(':') + 1)..] : t;
                cards = cards.Where(c => c.Color != null
                                      && EF.Functions.Like(c.Color, $"%{val}%"));
            }
            else if (t.StartsWith("type:", StringComparison.OrdinalIgnoreCase)
                  || t.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t.Contains(':') ? t[(t.IndexOf(':') + 1)..] : t;
                cards = cards.Where(c => c.CardType != null
                                      && EF.Functions.Like(c.CardType, $"%{val}%"));
            }
            else
            {
                cards = cards.Where(c => EF.Functions.Like(c.Name, $"%{t}%"));
            }
        }

        return cards
            .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
            .ToList()
            .GroupBy(c => new { c.Name, c.SetCode })
            .Select(g =>
            {
                var first = g.First();
                return new CardSearchResult
                {
                    Id = first.Id,
                    Name = first.Name,
                    SetCode = first.SetCode,
                    Number = first.Number,
                    Rarity = first.Rarity,
                    Color = first.Color,
                    Quantity = g.Count(),
                };
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

    public record CardSearchResult
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string Number { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string? Color { get; init; }
        public int Quantity { get; init; }
    }
}
```

- [ ] **Step 2: Update Index.cshtml with search bar, game filter, and conditional results**

Replace `OmniCard.Web/Pages/Index.cshtml` with:

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
        <nav class="nav-links">
            <a href="/decklist">Decklist Check</a>
        </nav>

        <form method="get" class="search-bar">
            <input type="text" name="q" value="@Model.Q"
                   placeholder="Search cards... (e.g. lightning, set:tmt, cn:002)"
                   autocomplete="off"/>
            <select name="game">
                <option value="">All Games</option>
                <option value="mtg" selected="@(Model.Game == "mtg" ? "selected" : null)">Magic: The Gathering</option>
                <option value="optcg" selected="@(Model.Game == "optcg" ? "selected" : null)">One Piece</option>
            </select>
            <button type="submit">Search</button>
            @if (Model.IsSearchActive || !string.IsNullOrEmpty(Model.Game))
            {
                <a href="/" class="clear-link">Clear</a>
            }
        </form>

        @if (Model.IsSearchActive)
        {
            <h2>Search Results (@Model.SearchResults.Sum(r => r.Quantity) cards)</h2>
            @if (Model.SearchResults.Count == 0)
            {
                <p class="empty">No cards found matching your search.</p>
            }
            else
            {
                <table>
                    <thead>
                        <tr>
                            <th>Qty</th>
                            <th>Name</th>
                            <th>Set</th>
                            <th>#</th>
                            <th>Rarity</th>
                            <th>Color</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var card in Model.SearchResults)
                        {
                            <tr>
                                <td>@card.Quantity</td>
                                <td><a href="/card/@card.Id">@card.Name</a></td>
                                <td>@card.SetCode</td>
                                <td>@card.Number</td>
                                <td>@card.Rarity</td>
                                <td>@card.Color</td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        }
        else
        {
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
        }
    </main>
</body>
</html>
```

- [ ] **Step 3: Add CSS for search bar, game filter, and nav links**

Append to `OmniCard.Web/wwwroot/css/site.css`:

```css

.nav-links { margin-bottom: 16px; }
.nav-links a { margin-right: 16px; font-size: 0.9rem; }

.search-bar {
    display: flex;
    gap: 8px;
    margin-bottom: 20px;
    flex-wrap: wrap;
}

.search-bar input[type="text"] {
    flex: 1;
    min-width: 200px;
    padding: 8px 12px;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 4px;
    color: var(--text);
    font-size: 0.95rem;
}

.search-bar select {
    padding: 8px 12px;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 4px;
    color: var(--text);
    font-size: 0.95rem;
}

.search-bar button {
    padding: 8px 16px;
    background: var(--accent);
    border: none;
    border-radius: 4px;
    color: white;
    cursor: pointer;
    font-size: 0.95rem;
}

.search-bar button:hover { opacity: 0.85; }

.clear-link {
    align-self: center;
    font-size: 0.85rem;
}
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build OmniCard.Web/OmniCard.Web.csproj
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Web/Pages/Index.cshtml OmniCard.Web/Pages/Index.cshtml.cs OmniCard.Web/wwwroot/css/site.css
git commit -m "feat(web): add game filter and collection search on index page"
```

---

### Task 4: Decklist Check Page

**Files:**
- Create: `OmniCard.Web/Pages/Decklist.cshtml`
- Create: `OmniCard.Web/Pages/Decklist.cshtml.cs`
- Modify: `OmniCard.Web/wwwroot/css/site.css`

**Interfaces:**
- Consumes: `IDecklistService.FetchDecklistAsync(string url)`, `IDecklistService.CheckAgainstCollection(string, string, List<DecklistEntry>)`, `DecklistCheckResult`, `OwnedDecklistEntry`, `MissingDecklistEntry`, `DecklistService.TypeCategoryOrder`, `DecklistService.GetTypeCategory(string?)`
- Produces: `/decklist` page — form input + HTML report

- [ ] **Step 1: Create Decklist.cshtml.cs**

Create `OmniCard.Web/Pages/Decklist.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class DecklistModel : PageModel
{
    private readonly IDecklistService _decklistService;

    public DecklistModel(IDecklistService decklistService)
    {
        _decklistService = decklistService;
    }

    [BindProperty]
    public string? DeckUrl { get; set; }

    public DecklistCheckResult? Result { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(DeckUrl))
        {
            ErrorMessage = "Please enter a deck URL.";
            return Page();
        }

        try
        {
            var fetched = await _decklistService.FetchDecklistAsync(DeckUrl.Trim());
            if (fetched is null)
            {
                ErrorMessage = "Could not parse URL. Supported sites: Moxfield, Archidekt.";
                return Page();
            }

            var (deckName, entries) = fetched.Value;
            if (entries.Count == 0)
            {
                ErrorMessage = "No cards found in decklist.";
                return Page();
            }

            var source = DeckUrl.Contains("moxfield", StringComparison.OrdinalIgnoreCase)
                ? "Moxfield" : "Archidekt";
            Result = _decklistService.CheckAgainstCollection(deckName, source, entries);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Failed to fetch decklist. Check the URL and try again.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }

        return Page();
    }

    public IEnumerable<IGrouping<string, OwnedDecklistEntry>> OwnedByType =>
        (Result?.OwnedEntries ?? [])
            .GroupBy(e => e.TypeCategory ?? "Other")
            .OrderBy(g => Array.IndexOf(DecklistService.TypeCategoryOrder, g.Key));

    public IEnumerable<IGrouping<string, MissingDecklistEntry>> MissingByType =>
        (Result?.MissingEntries ?? [])
            .GroupBy(e => e.TypeCategory ?? "Other")
            .OrderBy(g => Array.IndexOf(DecklistService.TypeCategoryOrder, g.Key));
}
```

- [ ] **Step 2: Create Decklist.cshtml**

Create `OmniCard.Web/Pages/Decklist.cshtml`:

```html
@page
@model OmniCard.Web.Pages.DecklistModel
@{
    ViewData["Title"] = "Decklist Check";
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
    <a href="/" class="back-link">&larr; Home</a>
    <header>
        <h1>Decklist Check</h1>
    </header>
    <main>
        <form method="post" class="decklist-form">
            <input type="text" asp-for="DeckUrl"
                   placeholder="Paste Moxfield or Archidekt deck URL..."
                   autocomplete="off"/>
            <button type="submit">Check</button>
        </form>

        @if (Model.ErrorMessage is not null)
        {
            <p class="error-message">@Model.ErrorMessage</p>
        }

        @if (Model.Result is not null)
        {
            var r = Model.Result;
            <div class="decklist-summary">
                <h2>@r.DeckName <span class="badge">@r.DeckSource</span></h2>
                <div class="stats-row">
                    <span>Total: <strong>@r.TotalCards</strong></span>
                    <span class="stat-owned">Owned: <strong>@r.TotalOwned</strong></span>
                    <span class="stat-missing">Missing: <strong>@r.TotalMissing</strong></span>
                    @if (r.EstimatedCost > 0)
                    {
                        <span>Est. Cost: <strong>@r.EstimatedCost.ToString("C")</strong></span>
                    }
                </div>
            </div>

            @if (r.TotalOwned > 0)
            {
                <h2>Owned (@r.TotalOwned)</h2>
                @foreach (var group in Model.OwnedByType)
                {
                    <h3 class="type-header">@group.Key (@group.Sum(e => e.QuantityNeeded))</h3>
                    <table>
                        <thead>
                            <tr>
                                <th>Qty</th>
                                <th>Card</th>
                                <th>Set</th>
                                <th>Location(s)</th>
                            </tr>
                        </thead>
                        <tbody>
                            @foreach (var entry in group)
                            {
                                <tr>
                                    <td>@entry.QuantityNeeded</td>
                                    <td>@entry.CardName</td>
                                    <td>@(entry.SetCode ?? "")</td>
                                    <td>
                                        @foreach (var loc in entry.Locations.Take(3))
                                        {
                                            <span class="location-tag@(loc.IsExactSetMatch ? " exact" : "")">
                                                @loc.ContainerName@(loc.Page.HasValue ? $" p{loc.Page}" : "")@(loc.Slot.HasValue ? $" s{loc.Slot}" : "")
                                            </span>
                                        }
                                        @if (entry.Locations.Count > 3)
                                        {
                                            <span class="location-tag">+@(entry.Locations.Count - 3) more</span>
                                        }
                                    </td>
                                </tr>
                            }
                        </tbody>
                    </table>
                }
            }

            @if (r.TotalMissing > 0)
            {
                <h2>Missing (@r.TotalMissing)</h2>
                @foreach (var group in Model.MissingByType)
                {
                    <h3 class="type-header">@group.Key (@group.Sum(e => e.QuantityNeeded))</h3>
                    <table>
                        <thead>
                            <tr>
                                <th>Qty</th>
                                <th>Card</th>
                                <th>Set</th>
                                <th>Price</th>
                                <th>Total</th>
                            </tr>
                        </thead>
                        <tbody>
                            @foreach (var entry in group)
                            {
                                <tr>
                                    <td>@entry.QuantityNeeded</td>
                                    <td>@entry.CardName</td>
                                    <td>@(entry.SetCode ?? "")</td>
                                    <td>@(entry.MarketPrice.HasValue ? entry.MarketPrice.Value.ToString("C") : "—")</td>
                                    <td>@(entry.MarketPrice.HasValue ? (entry.MarketPrice.Value * entry.QuantityNeeded).ToString("C") : "—")</td>
                                </tr>
                            }
                        </tbody>
                    </table>
                }
            }
        }
    </main>
</body>
</html>
```

- [ ] **Step 3: Add CSS for decklist page**

Append to `OmniCard.Web/wwwroot/css/site.css`:

```css

.decklist-form {
    display: flex;
    gap: 8px;
    margin-bottom: 20px;
}

.decklist-form input[type="text"] {
    flex: 1;
    padding: 8px 12px;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 4px;
    color: var(--text);
    font-size: 0.95rem;
}

.decklist-form button {
    padding: 8px 16px;
    background: var(--accent);
    border: none;
    border-radius: 4px;
    color: white;
    cursor: pointer;
    font-size: 0.95rem;
}

.decklist-form button:hover { opacity: 0.85; }

.error-message {
    color: #ef5350;
    background: #2d1f1f;
    padding: 8px 12px;
    border-radius: 4px;
    margin-bottom: 16px;
}

.decklist-summary {
    background: var(--surface);
    padding: 16px;
    border-radius: 8px;
    margin-bottom: 20px;
}

.stats-row {
    display: flex;
    gap: 24px;
    margin-top: 8px;
    flex-wrap: wrap;
}

.stat-owned { color: #66bb6a; }
.stat-missing { color: #ef5350; }

.type-header {
    font-size: 0.95rem;
    margin: 16px 0 8px;
    color: var(--accent);
}

.location-tag {
    display: inline-block;
    padding: 2px 6px;
    margin: 1px 2px;
    border-radius: 3px;
    font-size: 0.8rem;
    background: var(--surface);
    color: var(--text-muted);
}

.location-tag.exact {
    background: #1b5e20;
    color: #a5d6a7;
}
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build OmniCard.Web/OmniCard.Web.csproj
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Web/Pages/Decklist.cshtml OmniCard.Web/Pages/Decklist.cshtml.cs OmniCard.Web/wwwroot/css/site.css
git commit -m "feat(web): add decklist check page with Moxfield/Archidekt support"
```

---

### Task 5: Final Build Verification

**Files:** None new — verify full solution builds.

- [ ] **Step 1: Build full solution**

```bash
cd d:/source/repos/OmniCard
dotnet build OmniCard.slnx
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Run existing tests to verify no regressions**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 3: Commit any fixes if needed**

Only if steps 1 or 2 required fixes.
