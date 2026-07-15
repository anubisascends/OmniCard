# Decklist Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import a decklist from Moxfield/Archidekt (or pasted text) and generate a printable PDF report showing which cards the user owns (with physical locations) and which need to be purchased (with market prices).

**Architecture:** A `DecklistService` in `OmniCard.Collection` handles URL parsing, API fetching, text fallback parsing, and collection matching. A `DecklistPdfExporter` in `OmniCard.Audit` generates the QuestPDF report. A new `DecklistCheckView` dialog orchestrates the user flow. The feature is accessed via a "Check Decklist..." menu item under Tools.

**Tech Stack:** C#/.NET 10, WPF, CommunityToolkit.Mvvm, EF Core (SQLite), QuestPDF, System.Text.Json, IHttpClientFactory.

## Global Constraints

- No new NuGet packages -- reuse existing dependencies only.
- Follow existing MVVM patterns: `IView<TViewModel>`, `DataContext = this`, constructor-injected ViewModel.
- Follow existing DI registration patterns in `App.xaml.cs`.
- Follow existing dialog patterns in `DialogService` (SetOwner, ShowDialog).
- QuestPDF uses `LicenseType.Community`.
- HTTP requests use `IHttpClientFactory` with User-Agent `OmniCard/1.0` and 10-second timeout.

---

### Task 1: Models and Service Interface

**Files:**
- Create: `OmniCard.Shared/Models/DecklistEntry.cs`
- Create: `OmniCard.Shared/Models/DecklistCheckResult.cs`
- Create: `OmniCard.Shared/Interfaces/IDecklistService.cs`
- Create: `OmniCard.Shared/Interfaces/IDecklistPdfExporter.cs`
- Test: `OmniCard.Tests/Services/DecklistTextParserTests.cs`

**Interfaces:**
- Consumes: Nothing (foundational task)
- Produces:
  - `DecklistEntry` record: `int Quantity`, `string CardName`, `string? SetCode`, `string? CollectorNumber`
  - `DecklistCardLocation` record: `string ContainerName`, `int? Page`, `int? Slot`, `string? Section`, `string SetCode`, `bool IsFoil`, `bool IsExactSetMatch`
  - `OwnedDecklistEntry` record: `string CardName`, `string? SetCode`, `string? CollectorNumber`, `int QuantityNeeded`, `List<DecklistCardLocation> Locations`
  - `MissingDecklistEntry` record: `string CardName`, `string? SetCode`, `string? CollectorNumber`, `int QuantityNeeded`, `decimal? MarketPrice`
  - `DecklistCheckResult` class: `string DeckName`, `string DeckSource`, `List<OwnedDecklistEntry> OwnedEntries`, `List<MissingDecklistEntry> MissingEntries`, `int TotalOwned`, `int TotalMissing`, `decimal EstimatedCost`
  - `IDecklistService` interface: `Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url)`, `(string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text)`, `DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries)`
  - `IDecklistPdfExporter` interface: `void Export(DecklistCheckResult result, string filePath)`

- [ ] **Step 1: Create `DecklistEntry.cs`**

Create `OmniCard.Shared/Models/DecklistEntry.cs`:

```csharp
namespace OmniCard.Models;

public record DecklistEntry(int Quantity, string CardName, string? SetCode, string? CollectorNumber);
```

- [ ] **Step 2: Create `DecklistCheckResult.cs`**

Create `OmniCard.Shared/Models/DecklistCheckResult.cs`:

```csharp
namespace OmniCard.Models;

public record DecklistCardLocation(
    string ContainerName,
    int? Page,
    int? Slot,
    string? Section,
    string SetCode,
    bool IsFoil,
    bool IsExactSetMatch);

public record OwnedDecklistEntry(
    string CardName,
    string? SetCode,
    string? CollectorNumber,
    int QuantityNeeded,
    List<DecklistCardLocation> Locations);

public record MissingDecklistEntry(
    string CardName,
    string? SetCode,
    string? CollectorNumber,
    int QuantityNeeded,
    decimal? MarketPrice);

public class DecklistCheckResult
{
    public required string DeckName { get; init; }
    public required string DeckSource { get; init; }
    public required List<OwnedDecklistEntry> OwnedEntries { get; init; }
    public required List<MissingDecklistEntry> MissingEntries { get; init; }
    public int TotalOwned => OwnedEntries.Sum(e => e.QuantityNeeded);
    public int TotalMissing => MissingEntries.Sum(e => e.QuantityNeeded);
    public int TotalCards => TotalOwned + TotalMissing;
    public decimal EstimatedCost => MissingEntries
        .Where(e => e.MarketPrice.HasValue)
        .Sum(e => e.MarketPrice!.Value * e.QuantityNeeded);
}
```

- [ ] **Step 3: Create `IDecklistService.cs`**

Create `OmniCard.Shared/Interfaces/IDecklistService.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IDecklistService
{
    Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url);
    (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text);
    DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries);
}
```

- [ ] **Step 4: Create `IDecklistPdfExporter.cs`**

Create `OmniCard.Shared/Interfaces/IDecklistPdfExporter.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IDecklistPdfExporter
{
    void Export(DecklistCheckResult result, string filePath);
}
```

- [ ] **Step 5: Write text parser tests**

Create `OmniCard.Tests/Services/DecklistTextParserTests.cs`:

```csharp
using OmniCard.Collection;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class DecklistTextParserTests
{
    [Fact]
    public void ParseDecklistText_SimpleNameOnly()
    {
        var service = CreateService();
        var (name, entries) = service.ParseDecklistText("1 Lightning Bolt\n2 Mountain");

        Assert.Equal("Pasted Decklist", name);
        Assert.Equal(2, entries.Count);
        Assert.Equal(new DecklistEntry(1, "Lightning Bolt", null, null), entries[0]);
        Assert.Equal(new DecklistEntry(2, "Mountain", null, null), entries[1]);
    }

    [Fact]
    public void ParseDecklistText_WithSetAndCollectorNumber()
    {
        var service = CreateService();
        var (_, entries) = service.ParseDecklistText("1 Lightning Bolt (M11) 149");

        var entry = Assert.Single(entries);
        Assert.Equal("Lightning Bolt", entry.CardName);
        Assert.Equal("M11", entry.SetCode);
        Assert.Equal("149", entry.CollectorNumber);
        Assert.Equal(1, entry.Quantity);
    }

    [Fact]
    public void ParseDecklistText_IgnoresCommentsAndBlankLines()
    {
        var service = CreateService();
        var text = "// Creatures\n1 Ragavan, Nimble Pilferer\n\n// Lands\n2 Mountain";
        var (_, entries) = service.ParseDecklistText(text);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Ragavan, Nimble Pilferer", entries[0].CardName);
        Assert.Equal("Mountain", entries[1].CardName);
    }

    [Fact]
    public void ParseDecklistText_AggregatesDuplicateEntries()
    {
        var service = CreateService();
        var text = "2 Lightning Bolt\n1 Lightning Bolt";
        var (_, entries) = service.ParseDecklistText(text);

        var entry = Assert.Single(entries);
        Assert.Equal(3, entry.Quantity);
        Assert.Equal("Lightning Bolt", entry.CardName);
    }

    [Fact]
    public void ParseDecklistText_EmptyInput_ReturnsEmptyList()
    {
        var service = CreateService();
        var (_, entries) = service.ParseDecklistText("");

        Assert.Empty(entries);
    }

    [Fact]
    public void ParseDecklistText_IgnoresSectionHeaders()
    {
        var service = CreateService();
        var text = "Deck\n1 Lightning Bolt\nSideboard\n1 Pyroblast";
        var (_, entries) = service.ParseDecklistText(text);

        Assert.Equal(2, entries.Count);
    }

    private static DecklistService CreateService()
    {
        return new DecklistService(null!, null!, null!);
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "DecklistTextParser" --no-restore`
Expected: Build failure -- `DecklistService` class does not exist yet.

- [ ] **Step 7: Commit models and interfaces**

```bash
git add OmniCard.Shared/Models/DecklistEntry.cs OmniCard.Shared/Models/DecklistCheckResult.cs OmniCard.Shared/Interfaces/IDecklistService.cs OmniCard.Shared/Interfaces/IDecklistPdfExporter.cs OmniCard.Tests/Services/DecklistTextParserTests.cs
git commit -m "feat(decklist): add models, interfaces, and text parser tests for decklist check"
```

---

### Task 2: DecklistService -- Text Parser and URL Fetching

**Files:**
- Create: `OmniCard.Collection/DecklistService.cs`
- Test: `OmniCard.Tests/Services/DecklistTextParserTests.cs` (from Task 1)

**Interfaces:**
- Consumes: `DecklistEntry` record, `IDecklistService` interface (Task 1)
- Produces: `DecklistService` class implementing `IDecklistService.ParseDecklistText` and `IDecklistService.FetchDecklistAsync`

- [ ] **Step 1: Implement `DecklistService` with text parser and API fetching**

Create `OmniCard.Collection/DecklistService.cs`:

```csharp
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed partial class DecklistService(
    IDbContextFactory<CollectionDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    ICardService cardService) : IDecklistService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Regex: "1 Card Name" or "1 Card Name (SET) 123" or "1x Card Name"
    [GeneratedRegex(@"^(\d+)x?\s+(.+?)(?:\s+\(([A-Za-z0-9]+)\)\s+(\S+))?$")]
    private static partial Regex DecklistLineRegex();

    // Known section headers to skip (Moxfield/Archidekt text export)
    private static readonly HashSet<string> SectionHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Deck", "Sideboard", "Commander", "Companion", "Maybeboard", "Considering",
        "Main", "Mainboard", "Main Deck", "Tokens", "Attractions", "Stickers", "Contraptions"
    };

    public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text)
    {
        var entries = new Dictionary<string, DecklistEntry>(StringComparer.OrdinalIgnoreCase);
        var regex = DecklistLineRegex();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            if (SectionHeaders.Contains(line))
                continue;

            var match = regex.Match(line);
            if (!match.Success)
                continue;

            var qty = int.Parse(match.Groups[1].Value);
            var name = match.Groups[2].Value.Trim();
            var setCode = match.Groups[3].Success ? match.Groups[3].Value.ToUpperInvariant() : null;
            var collectorNumber = match.Groups[4].Success ? match.Groups[4].Value : null;

            var key = name.ToUpperInvariant();
            if (entries.TryGetValue(key, out var existing))
                entries[key] = existing with { Quantity = existing.Quantity + qty };
            else
                entries[key] = new DecklistEntry(qty, name, setCode, collectorNumber);
        }

        return ("Pasted Decklist", entries.Values.ToList());
    }

    public async Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url)
    {
        var (source, deckId) = ParseUrl(url);
        if (source is null || deckId is null)
            return null;

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");
        client.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            return source switch
            {
                "Moxfield" => await FetchMoxfieldAsync(client, deckId),
                "Archidekt" => await FetchArchidektAsync(client, deckId),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static (string? Source, string? DeckId) ParseUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return (null, null);

        var host = uri.Host.Replace("www.", "");
        var segments = uri.AbsolutePath.Trim('/').Split('/');

        if (host.Contains("moxfield.com") && segments.Length >= 2 && segments[0] == "decks")
            return ("Moxfield", segments[1]);

        if (host.Contains("archidekt.com") && segments.Length >= 2 && segments[0] == "decks")
            return ("Archidekt", segments[1]);

        return (null, null);
    }

    private static async Task<(string DeckName, List<DecklistEntry> Entries)?> FetchMoxfieldAsync(
        HttpClient client, string deckId)
    {
        var response = await client.GetAsync($"https://api2.moxfield.com/v2/decks/all/{deckId}");
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        var deckName = root.GetProperty("name").GetString() ?? "Moxfield Deck";

        var entries = new List<DecklistEntry>();

        // Moxfield stores cards in board objects: mainboard, sideboard, commanders, companions
        foreach (var boardName in new[] { "mainboard", "sideboard", "commanders", "companions" })
        {
            if (!root.TryGetProperty(boardName, out var board) || board.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var cardProp in board.EnumerateObject())
            {
                var cardObj = cardProp.Value;
                var qty = cardObj.GetProperty("quantity").GetInt32();
                var card = cardObj.GetProperty("card");
                var name = card.GetProperty("name").GetString() ?? "";
                var setCode = card.GetProperty("set").GetString()?.ToUpperInvariant();
                var cn = card.GetProperty("cn").GetString();

                entries.Add(new DecklistEntry(qty, name, setCode, cn));
            }
        }

        return (deckName, entries);
    }

    private static async Task<(string DeckName, List<DecklistEntry> Entries)?> FetchArchidektAsync(
        HttpClient client, string deckId)
    {
        var response = await client.GetAsync($"https://archidekt.com/api/decks/{deckId}/");
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        var deckName = root.GetProperty("name").GetString() ?? "Archidekt Deck";

        var entries = new List<DecklistEntry>();

        if (root.TryGetProperty("cards", out var cards) && cards.ValueKind == JsonValueKind.Array)
        {
            foreach (var cardObj in cards.EnumerateArray())
            {
                var qty = cardObj.GetProperty("quantity").GetInt32();
                var card = cardObj.GetProperty("card");
                var edition = card.GetProperty("edition");

                var name = card.GetProperty("oracleCard").GetProperty("name").GetString() ?? "";
                var setCode = edition.GetProperty("editioncode").GetString()?.ToUpperInvariant();
                var cn = card.TryGetProperty("collectorNumber", out var cnProp)
                    ? cnProp.GetString() : null;

                entries.Add(new DecklistEntry(qty, name, setCode, cn));
            }
        }

        return (deckName, entries);
    }

    public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries)
    {
        throw new NotImplementedException(); // Implemented in Task 3
    }
}
```

- [ ] **Step 2: Run text parser tests**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "DecklistTextParser" --no-restore`
Expected: All 6 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add OmniCard.Collection/DecklistService.cs
git commit -m "feat(decklist): implement text parser and Moxfield/Archidekt API fetching"
```

---

### Task 3: DecklistService -- Collection Matching

**Files:**
- Modify: `OmniCard.Collection/DecklistService.cs` (replace `CheckAgainstCollection` stub)
- Test: `OmniCard.Tests/Services/DecklistMatchingTests.cs`

**Interfaces:**
- Consumes: `DecklistEntry`, `DecklistCheckResult`, `OwnedDecklistEntry`, `MissingDecklistEntry`, `DecklistCardLocation` (Task 1), `DecklistService` (Task 2), `IDbContextFactory<CollectionDbContext>`, `ICardService.SearchCards`, `ICardService.GetCurrentPrice`
- Produces: Working `DecklistService.CheckAgainstCollection` method

- [ ] **Step 1: Write collection matching tests**

Create `OmniCard.Tests/Services/DecklistMatchingTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class DecklistMatchingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public DecklistMatchingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbFactory = new TestCollectionDbFactory(options);
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed Bulk container
        ctx.StorageContainers.Add(new StorageContainer
        {
            Name = "Bulk", ContainerType = ContainerType.Bulk,
            IsSystem = true, SortOrder = 0,
        });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private DecklistService CreateService(ICardService? cardService = null)
    {
        return new DecklistService(_dbFactory, null!, cardService ?? new StubCardService());
    }

    private void SeedCard(string name, string setCode, string number, int containerId = 1,
        int? page = null, int? slot = null, string? section = null, bool isFoil = false)
    {
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.Mtg, GameCardId = Guid.NewGuid().ToString(),
            Name = name, SetCode = setCode, Number = number,
            SetName = setCode, Rarity = "common",
            ContainerId = containerId, Page = page, Slot = slot, Section = section,
            IsFoil = isFoil,
        });
        ctx.SaveChanges();
    }

    private int SeedContainer(string name, ContainerType type)
    {
        using var ctx = _dbFactory.CreateDbContext();
        var c = new StorageContainer { Name = name, ContainerType = type, SortOrder = 1 };
        ctx.StorageContainers.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    [Fact]
    public void CheckAgainstCollection_CardOwned_ShowsInOwnedWithLocation()
    {
        var binderId = SeedContainer("Binder A", ContainerType.Binder);
        SeedCard("Lightning Bolt", "M11", "149", binderId, page: 3, slot: 2);

        var service = CreateService();
        var entries = new List<DecklistEntry> { new(1, "Lightning Bolt", "M11", "149") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        Assert.Equal(1, result.TotalOwned);
        Assert.Equal(0, result.TotalMissing);
        var owned = Assert.Single(result.OwnedEntries);
        Assert.Equal("Lightning Bolt", owned.CardName);
        var loc = Assert.Single(owned.Locations);
        Assert.Equal("Binder A", loc.ContainerName);
        Assert.Equal(3, loc.Page);
        Assert.Equal(2, loc.Slot);
        Assert.True(loc.IsExactSetMatch);
    }

    [Fact]
    public void CheckAgainstCollection_CardMissing_ShowsInMissing()
    {
        var service = CreateService();
        var entries = new List<DecklistEntry> { new(1, "Ragavan, Nimble Pilferer", "MH2", "138") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        Assert.Equal(0, result.TotalOwned);
        Assert.Equal(1, result.TotalMissing);
        var missing = Assert.Single(result.MissingEntries);
        Assert.Equal("Ragavan, Nimble Pilferer", missing.CardName);
    }

    [Fact]
    public void CheckAgainstCollection_PartialOwnership_SplitsOwnedAndMissing()
    {
        SeedCard("Lightning Bolt", "M11", "149");

        var service = CreateService();
        var entries = new List<DecklistEntry> { new(3, "Lightning Bolt", "M11", "149") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        // Owned 1 copy, missing 2
        Assert.Equal(1, result.TotalOwned);
        Assert.Equal(2, result.TotalMissing);
    }

    [Fact]
    public void CheckAgainstCollection_DifferentSet_FallbackMatch_NotExactSetMatch()
    {
        SeedCard("Lightning Bolt", "2ED", "162");

        var service = CreateService();
        var entries = new List<DecklistEntry> { new(1, "Lightning Bolt", "M11", "149") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        Assert.Equal(1, result.TotalOwned);
        var loc = Assert.Single(result.OwnedEntries.Single().Locations);
        Assert.False(loc.IsExactSetMatch);
        Assert.Equal("2ED", loc.SetCode);
    }

    [Fact]
    public void CheckAgainstCollection_ExactSetPreferred()
    {
        SeedCard("Lightning Bolt", "2ED", "162");
        SeedCard("Lightning Bolt", "M11", "149");

        var service = CreateService();
        var entries = new List<DecklistEntry> { new(1, "Lightning Bolt", "M11", "149") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        Assert.Equal(1, result.TotalOwned);
        var owned = Assert.Single(result.OwnedEntries);
        // Should show both locations but exact set match first
        Assert.Equal(2, owned.Locations.Count);
        Assert.True(owned.Locations[0].IsExactSetMatch);
    }

    private class StubCardService : ICardService
    {
        public bool DefaultIsFoil { get; set; }
        public decimal? DefaultPurchasePrice { get; set; }
        public IReadOnlyList<CardGame> AvailableGames => [];
        public ICardGameService ActiveGameService => null!;
        public Action<HashStageResult>? OnHashStage { get; set; }
        public ulong LastComputedHash => 0;
        public ICardGameService GetGameService(CardGame game) => new StubGameService();
        public void AddFromStream(Stream stream) { }
        public void ReprocessScans() { }
        public void CommitScans(IEnumerable<ScannedCard> scannedCards) { }
        public void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null) { }
        public void SearchCollection(string query, CardGame? gameFilter, System.Collections.ObjectModel.ObservableCollection<CollectionCard> results) { }
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, System.Collections.ObjectModel.ObservableCollection<CollectionCard> results) { }
        public void StartNewDiagnosticSession() { }
        public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null) => (null, CardGame.Mtg);
        public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter = null) => [];
    }

    private class StubGameService : ICardGameService
    {
        public CardGame Game => CardGame.Mtg;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => new();
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IReadOnlyList<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public object? FindCardById(string gameCardId) => null;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "DecklistMatching" --no-restore`
Expected: FAIL -- `CheckAgainstCollection` throws `NotImplementedException`.

- [ ] **Step 3: Implement `CheckAgainstCollection`**

Replace the `CheckAgainstCollection` method in `OmniCard.Collection/DecklistService.cs`:

```csharp
    public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var allCards = ctx.Cards
            .Include(c => c.Container)
            .AsNoTracking()
            .ToList();

        var ownedEntries = new List<OwnedDecklistEntry>();
        var missingEntries = new List<MissingDecklistEntry>();

        foreach (var entry in entries)
        {
            // Find all owned copies by name (case-insensitive)
            var ownedCopies = allCards
                .Where(c => string.Equals(c.Name, entry.CardName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Sort: exact set matches first, then others
            if (entry.SetCode is not null)
            {
                ownedCopies = ownedCopies
                    .OrderByDescending(c => string.Equals(c.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var locations = ownedCopies.Select(c => new DecklistCardLocation(
                ContainerName: c.Container?.Name ?? "Unknown",
                Page: c.Page,
                Slot: c.Slot,
                Section: c.Section,
                SetCode: c.SetCode,
                IsFoil: c.IsFoil,
                IsExactSetMatch: entry.SetCode is not null &&
                    string.Equals(c.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase)
            )).ToList();

            var ownedCount = Math.Min(ownedCopies.Count, entry.Quantity);
            var missingCount = entry.Quantity - ownedCount;

            if (ownedCount > 0)
            {
                ownedEntries.Add(new OwnedDecklistEntry(
                    entry.CardName, entry.SetCode, entry.CollectorNumber,
                    ownedCount, locations));
            }

            if (missingCount > 0)
            {
                // Look up market price
                decimal? price = null;
                var gameService = cardService.GetGameService(CardGame.Mtg);
                var searchResults = gameService.SearchCards($"name:{entry.CardName}", 1);

                if (searchResults.Count > 0)
                {
                    // Prefer exact set match for price if available
                    var priceCard = entry.SetCode is not null
                        ? searchResults.FirstOrDefault(r =>
                            string.Equals(r.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase))
                          ?? searchResults[0]
                        : searchResults[0];

                    price = gameService.GetCurrentPrice(priceCard.GameSpecificId, false);
                }

                missingEntries.Add(new MissingDecklistEntry(
                    entry.CardName, entry.SetCode, entry.CollectorNumber,
                    missingCount, price));
            }
        }

        return new DecklistCheckResult
        {
            DeckName = deckName,
            DeckSource = deckSource,
            OwnedEntries = ownedEntries.OrderBy(e => e.CardName, StringComparer.OrdinalIgnoreCase).ToList(),
            MissingEntries = missingEntries
                .OrderByDescending(e => (e.MarketPrice ?? 0) * e.QuantityNeeded)
                .ToList(),
        };
    }
```

- [ ] **Step 4: Run all decklist tests**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "Decklist" --no-restore`
Expected: All 11 tests PASS (6 parser + 5 matching).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/DecklistService.cs OmniCard.Tests/Services/DecklistMatchingTests.cs
git commit -m "feat(decklist): implement collection matching with exact-set preference"
```

---

### Task 4: PDF Report Generation

**Files:**
- Create: `OmniCard.Audit/DecklistPdfExporter.cs`
- Test: `OmniCard.Tests/Services/DecklistPdfExporterTests.cs`

**Interfaces:**
- Consumes: `DecklistCheckResult`, `OwnedDecklistEntry`, `MissingDecklistEntry`, `DecklistCardLocation` (Task 1), `IDecklistPdfExporter` (Task 1)
- Produces: `DecklistPdfExporter` class implementing `IDecklistPdfExporter.Export`

- [ ] **Step 1: Write PDF exporter test**

Create `OmniCard.Tests/Services/DecklistPdfExporterTests.cs`:

```csharp
using OmniCard.Audit;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class DecklistPdfExporterTests : IDisposable
{
    private readonly string _tempDir;

    public DecklistPdfExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Export_CreatesValidPdfFile()
    {
        var result = new DecklistCheckResult
        {
            DeckName = "Test Deck",
            DeckSource = "Moxfield",
            OwnedEntries =
            [
                new OwnedDecklistEntry("Lightning Bolt", "M11", "149", 1,
                [
                    new DecklistCardLocation("Binder A", 3, 2, null, "M11", false, true)
                ])
            ],
            MissingEntries =
            [
                new MissingDecklistEntry("Ragavan, Nimble Pilferer", "MH2", "138", 1, 55.00m)
            ],
        };

        var exporter = new DecklistPdfExporter();
        var path = Path.Combine(_tempDir, "test_report.pdf");
        exporter.Export(result, path);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 100);
        // PDF magic bytes
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void Export_EmptyResult_CreatesValidPdf()
    {
        var result = new DecklistCheckResult
        {
            DeckName = "Empty Deck",
            DeckSource = "Text",
            OwnedEntries = [],
            MissingEntries = [],
        };

        var exporter = new DecklistPdfExporter();
        var path = Path.Combine(_tempDir, "empty_report.pdf");
        exporter.Export(result, path);

        Assert.True(File.Exists(path));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "DecklistPdfExporter" --no-restore`
Expected: Build failure -- `DecklistPdfExporter` does not exist.

- [ ] **Step 3: Implement `DecklistPdfExporter`**

Create `OmniCard.Audit/DecklistPdfExporter.cs`:

```csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

public sealed class DecklistPdfExporter : IDecklistPdfExporter
{
    public void Export(DecklistCheckResult result, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Decklist Report").FontSize(18).Bold();
                    col.Item().Text(t =>
                    {
                        t.Span($"{result.DeckName}").Bold();
                        t.Span($" — Imported from {result.DeckSource}").FontColor(Colors.Grey.Medium);
                    });
                    col.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(4).Text(t =>
                    {
                        t.Span($"Owned: {result.TotalOwned}/{result.TotalCards}").Bold();
                        t.Span("  |  ");
                        t.Span($"Missing: {result.TotalMissing}").Bold()
                            .FontColor(result.TotalMissing > 0 ? Colors.Red.Medium : Colors.Green.Medium);
                        t.Span("  |  ");
                        t.Span($"Estimated cost: ${result.EstimatedCost:N2}").Bold();
                    });
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    // Cards You Own
                    if (result.OwnedEntries.Count > 0)
                    {
                        col.Item().Text("Cards You Own").FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2.5f); // Name
                                columns.RelativeColumn(0.7f); // Set
                                columns.RelativeColumn(0.5f); // Qty
                                columns.RelativeColumn(4);    // Location(s)
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Card Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Qty").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Location(s)").Bold();
                            });

                            foreach (var entry in result.OwnedEntries)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(entry.CardName);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(entry.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text($"{entry.QuantityNeeded}");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(FormatLocations(entry.Locations));
                            }
                        });
                    }

                    // Cards to Buy
                    if (result.MissingEntries.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Cards to Buy").FontSize(13).Bold()
                            .FontColor(Colors.Red.Medium);
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);   // Name
                                columns.RelativeColumn(0.7f); // Set
                                columns.RelativeColumn(0.5f); // Qty
                                columns.RelativeColumn(1);   // Market Price
                                columns.RelativeColumn(1);   // Subtotal
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Card Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Qty").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Price").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Subtotal").Bold();
                            });

                            foreach (var entry in result.MissingEntries)
                            {
                                var priceStr = entry.MarketPrice.HasValue ? $"${entry.MarketPrice:N2}" : "N/A";
                                var subtotalStr = entry.MarketPrice.HasValue
                                    ? $"${entry.MarketPrice.Value * entry.QuantityNeeded:N2}" : "N/A";

                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(entry.CardName);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(entry.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text($"{entry.QuantityNeeded}");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(priceStr);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(subtotalStr);
                            }

                            // Total row
                            table.Cell().ColumnSpan(4).Padding(4).AlignRight().Text("Total:").Bold();
                            table.Cell().Padding(4).Text($"${result.EstimatedCost:N2}").Bold();
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }

    private static string FormatLocations(List<DecklistCardLocation> locations)
    {
        return string.Join("; ", locations.Select(loc =>
        {
            var parts = new List<string> { loc.ContainerName };
            if (loc.Page.HasValue) parts.Add($"Page {loc.Page}");
            if (loc.Slot.HasValue) parts.Add($"Slot {loc.Slot}");
            if (loc.Section is not null) parts.Add(loc.Section);

            var locationStr = string.Join(", ", parts);
            var suffix = new List<string>();
            if (loc.SetCode is not null) suffix.Add(loc.SetCode);
            if (loc.IsFoil) suffix.Add("Foil");
            if (loc.IsExactSetMatch) suffix.Add("\u2605"); // star character

            return suffix.Count > 0 ? $"{locationStr} ({string.Join(", ", suffix)})" : locationStr;
        }));
    }
}
```

- [ ] **Step 4: Run PDF tests**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "DecklistPdfExporter" --no-restore`
Expected: Both tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Audit/DecklistPdfExporter.cs OmniCard.Tests/Services/DecklistPdfExporterTests.cs
git commit -m "feat(decklist): implement PDF report generation with QuestPDF"
```

---

### Task 5: Dialog UI, DI Wiring, and Menu Integration

**Files:**
- Create: `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml`
- Create: `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml.cs`
- Create: `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs`
- Modify: `OmniCard.Shared/Interfaces/IDialogService.cs` (add `ShowDecklistCheck` method)
- Modify: `OmniCard/Services/DialogService.cs` (implement `ShowDecklistCheck`)
- Modify: `OmniCard/App.xaml.cs` (register services in DI)
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (add `CheckDecklistCommand`)
- Modify: `OmniCard/Views/Root/RootView.xaml` (add menu item)

**Interfaces:**
- Consumes: `IDecklistService.FetchDecklistAsync`, `IDecklistService.ParseDecklistText`, `IDecklistService.CheckAgainstCollection` (Tasks 1-3), `IDecklistPdfExporter.Export` (Task 4)
- Produces: Complete UI integration -- user can click Tools > Check Decklist, paste a URL or text, see results, and export a PDF.

- [ ] **Step 1: Create ViewModel**

Create `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.DecklistCheck;

public sealed partial class DecklistCheckViewModel(
    IDecklistService decklistService,
    ILogger<DecklistCheckViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    public partial string Url { get; set; } = "";

    [ObservableProperty]
    public partial string FallbackText { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowFallback { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial DecklistCheckResult? Result { get; set; }

    public Action<DecklistCheckResult>? ExportPdf { get; set; }

    [RelayCommand]
    public async Task Fetch()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            StatusMessage = "Please enter a URL.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Fetching decklist...";
        Result = null;

        try
        {
            var fetched = await decklistService.FetchDecklistAsync(Url.Trim());

            if (fetched is null)
            {
                ShowFallback = true;
                StatusMessage = "Couldn't reach the site. Paste your decklist below instead.";
                logger.LogWarning("Failed to fetch decklist from {Url}", Url);
                return;
            }

            var (deckName, entries) = fetched.Value;
            StatusMessage = $"Fetched \"{deckName}\" ({entries.Count} cards). Checking collection...";

            var source = Url.Contains("moxfield", StringComparison.OrdinalIgnoreCase) ? "Moxfield" : "Archidekt";
            Result = decklistService.CheckAgainstCollection(deckName, source, entries);
            StatusMessage = $"Owned: {Result.TotalOwned}/{Result.TotalCards} | Missing: {Result.TotalMissing} | Cost: ${Result.EstimatedCost:N2}";
            logger.LogInformation("Decklist check complete: {Owned}/{Total} owned, {Missing} missing",
                Result.TotalOwned, Result.TotalCards, Result.TotalMissing);
        }
        catch (Exception ex)
        {
            ShowFallback = true;
            StatusMessage = "Couldn't reach the site. Paste your decklist below instead.";
            logger.LogWarning(ex, "Error fetching decklist from {Url}", Url);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ParseText()
    {
        if (string.IsNullOrWhiteSpace(FallbackText))
        {
            StatusMessage = "Please paste a decklist.";
            return;
        }

        var (deckName, entries) = decklistService.ParseDecklistText(FallbackText);
        if (entries.Count == 0)
        {
            StatusMessage = "No cards found in the pasted text.";
            return;
        }

        StatusMessage = $"Parsed {entries.Count} cards. Checking collection...";
        Result = decklistService.CheckAgainstCollection(deckName, "Text", entries);
        StatusMessage = $"Owned: {Result.TotalOwned}/{Result.TotalCards} | Missing: {Result.TotalMissing} | Cost: ${Result.EstimatedCost:N2}";
    }

    [RelayCommand]
    public void GenerateReport()
    {
        if (Result is null) return;
        ExportPdf?.Invoke(Result);
    }
}
```

- [ ] **Step 2: Create View XAML**

Create `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml`:

```xml
<Window x:Class="OmniCard.Views.DecklistCheck.DecklistCheckView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:conv="clr-namespace:OmniCard.Controls.Converters;assembly=OmniCard.Controls"
        ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        Title="Check Decklist" Height="550" Width="600"
        Background="{DynamicResource MaterialDesign.Brush.Background}"
        TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
        TextElement.FontWeight="Regular"
        TextElement.FontSize="13"
        FontFamily="{StaticResource AppFont}">

    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- URL input -->
        <GroupBox Grid.Row="0" Header="Decklist URL" Padding="8">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Fetch" Padding="16,6" Margin="8,0,0,0"
                        Command="{Binding ViewModel.FetchCommand}"/>
                <TextBox Text="{Binding ViewModel.Url, UpdateSourceTrigger=PropertyChanged}"
                         VerticalContentAlignment="Center"/>
            </DockPanel>
        </GroupBox>

        <!-- Fallback text input -->
        <GroupBox Grid.Row="1" Header="Paste Decklist" Padding="8" Margin="0,8,0,0"
                  Visibility="{Binding ViewModel.ShowFallback, Converter={conv:BoolToVisibilityConverter}}">
            <DockPanel>
                <Button DockPanel.Dock="Bottom" Content="Parse" Padding="16,6" Margin="0,8,0,0"
                        HorizontalAlignment="Left"
                        Command="{Binding ViewModel.ParseTextCommand}"/>
                <TextBox Text="{Binding ViewModel.FallbackText, UpdateSourceTrigger=PropertyChanged}"
                         AcceptsReturn="True" VerticalScrollBarVisibility="Auto"
                         Height="120" TextWrapping="Wrap"/>
            </DockPanel>
        </GroupBox>

        <!-- Results summary -->
        <GroupBox Grid.Row="2" Header="Results" Padding="8" Margin="0,8,0,0"
                  Visibility="{Binding ViewModel.Result, Converter={conv:NullToVisibilityConverter}}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <TextBlock Text="{Binding ViewModel.Result.DeckName}" FontSize="16" FontWeight="Bold"/>

                <ScrollViewer Grid.Row="1" Margin="0,8,0,0" VerticalScrollBarVisibility="Auto">
                    <StackPanel>
                        <!-- Owned cards -->
                        <TextBlock FontWeight="Bold" Margin="0,0,0,4">
                            <Run Text="Cards You Own: "/>
                            <Run Text="{Binding ViewModel.Result.TotalOwned, Mode=OneWay}"
                                 Foreground="{DynamicResource MaterialDesign.Brush.Primary}"/>
                        </TextBlock>
                        <ItemsControl ItemsSource="{Binding ViewModel.Result.OwnedEntries}" Margin="8,0,0,0">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock TextWrapping="Wrap" Margin="0,1">
                                        <Run Text="{Binding QuantityNeeded, StringFormat='{}{0}x ', Mode=OneWay}"/>
                                        <Run Text="{Binding CardName, Mode=OneWay}" FontWeight="SemiBold"/>
                                    </TextBlock>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>

                        <!-- Missing cards -->
                        <TextBlock FontWeight="Bold" Margin="0,12,0,4" Foreground="#E53935">
                            <Run Text="Cards to Buy: "/>
                            <Run Text="{Binding ViewModel.Result.TotalMissing, Mode=OneWay}"/>
                        </TextBlock>
                        <ItemsControl ItemsSource="{Binding ViewModel.Result.MissingEntries}" Margin="8,0,0,0">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock TextWrapping="Wrap" Margin="0,1">
                                        <Run Text="{Binding QuantityNeeded, StringFormat='{}{0}x ', Mode=OneWay}"/>
                                        <Run Text="{Binding CardName, Mode=OneWay}" FontWeight="SemiBold"/>
                                        <Run Text="{Binding MarketPrice, StringFormat=' (${0:N2})', Mode=OneWay, TargetNullValue=''}"
                                             Foreground="#E53935"/>
                                    </TextBlock>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </ScrollViewer>
            </Grid>
        </GroupBox>

        <!-- Status bar and buttons -->
        <DockPanel Grid.Row="3" Margin="0,12,0,0">
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="Generate Report" Padding="16,6" Margin="0,0,8,0"
                        Command="{Binding ViewModel.GenerateReportCommand}"
                        IsEnabled="{Binding ViewModel.Result, Converter={conv:NullToBoolConverter}}"/>
                <Button Content="Close" Padding="16,6" Click="OnClose"/>
            </StackPanel>
            <TextBlock Text="{Binding ViewModel.StatusMessage}"
                       VerticalAlignment="Center" TextWrapping="Wrap"
                       Foreground="{DynamicResource MaterialDesign.Brush.Primary}"/>
        </DockPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: Create View code-behind**

Create `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml.cs`:

```csharp
using System.Windows;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.DecklistCheck;

public partial class DecklistCheckView : Window
{
    public DecklistCheckViewModel ViewModel { get; }

    public DecklistCheckView(DecklistCheckViewModel viewModel, IDecklistPdfExporter pdfExporter)
    {
        ViewModel = viewModel;
        DataContext = this;

        viewModel.ExportPdf = result =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                FileName = $"Decklist_{result.DeckName}_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                pdfExporter.Export(result, dlg.FileName);
                MessageBox.Show("PDF exported successfully.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Check if `NullToVisibilityConverter` and `NullToBoolConverter` exist**

Search for these converters in the existing codebase. If `NullToVisibilityConverter` does not exist, add it to `OmniCard.Controls/Converters/RootConverters.cs` following the existing pattern:

```csharp
public class NullToVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class NullToBoolConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
```

- [ ] **Step 5: Add `ShowDecklistCheck` to `IDialogService`**

Add to `OmniCard.Shared/Interfaces/IDialogService.cs`:

```csharp
    void ShowDecklistCheck();
```

- [ ] **Step 6: Implement `ShowDecklistCheck` in `DialogService`**

Add to `OmniCard/Services/DialogService.cs`:

```csharp
    public void ShowDecklistCheck()
    {
        var wnd = Services.GetRequiredService<DecklistCheckView>();
        SetOwner(wnd);
        wnd.ShowDialog();
    }
```

Add `using OmniCard.Views.DecklistCheck;` to the top of the file.

- [ ] **Step 7: Register in DI**

Add to `OmniCard/App.xaml.cs` in the singleton services block (after `IAuditPdfExporter` registration around line 125):

```csharp
            services.AddSingleton<IDecklistService, DecklistService>();
            services.AddSingleton<IDecklistPdfExporter, DecklistPdfExporter>();
```

Add to the transient views block (after `AuditReportViewModel` registration around line 168):

```csharp
            services.AddTransient<DecklistCheckView>();
            services.AddTransient<DecklistCheckViewModel>();
```

Add `using OmniCard.Views.DecklistCheck;` to the top of the file.

- [ ] **Step 8: Add command to `RootViewModel`**

Add to `OmniCard/Views/Root/RootViewModel.cs`, near the other dialog commands (around line 1445):

```csharp
    [RelayCommand]
    public void CheckDecklist() => DialogService.ShowDecklistCheck();
```

- [ ] **Step 9: Add menu item to `RootView.xaml`**

Add inside the `_Tools` MenuItem, before the closing `</MenuItem>` tag (around line 204):

```xml
                <Separator/>
                <MenuItem Header="Check _Decklist..."
                          Command="{Binding ViewModel.CheckDecklistCommand}"/>
```

- [ ] **Step 10: Build and verify**

Run: `dotnet build OmniCard/OmniCard.csproj --no-restore`
Expected: Build succeeds with 0 errors.

- [ ] **Step 11: Run all decklist tests**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "Decklist" --no-restore`
Expected: All 13 tests PASS.

- [ ] **Step 12: Commit**

```bash
git add OmniCard/Views/DecklistCheck/ OmniCard/Services/DialogService.cs OmniCard.Shared/Interfaces/IDialogService.cs OmniCard/App.xaml.cs OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/RootView.xaml OmniCard.Controls/Converters/RootConverters.cs
git commit -m "feat(decklist): add dialog UI, DI wiring, and Tools menu integration"
```
