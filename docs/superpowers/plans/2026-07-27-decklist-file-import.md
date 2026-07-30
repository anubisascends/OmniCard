# Decklist File Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import a plain-text decklist file (`qty name (SET) collector#` per line) into a chosen List or Location, resolving each line to an exact printing against the active game's catalog.

**Architecture:** A new `ParseDecklistPrintings` parser produces printing-level `DecklistEntry` records; a pure `DecklistPrintingResolver` maps each entry to a `CardMatch` via a 4-rung ladder; a new `DecklistImportViewModel` + `DecklistImportView` modal presents a resolution preview and a List-or-Location target picker, then commits resolved cards via `IListService.AddPrinting` (List) or `ICardService.AddCardToCollection` (Location). Launched from a top-level **Import ▸ Decklist file…** menu command that defaults the target to the current Location.

**Tech Stack:** C# / .NET, WPF + MaterialDesignInXAML, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]` source generators), EF Core, xUnit with hand-written test doubles.

## Global Constraints

- Resolution runs against `cardService.ActiveGameService` only (the active game). No cross-game inference.
- Every imported card is **non-foil**; text after the collector number (e.g. `*E*`, `*F*`) is ignored.
- Location imports use condition `"Near Mint"` and `purchasePrice: null`.
- When a line provides both set code and collector number but no exact catalog match exists, the line is **unresolved** — never guessed.
- Follow the WPF dark-theme rule: set explicit `Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"` on every `TextBlock`/text control (implicit styles lose to MaterialDesign).
- Follow existing repo conventions: `[ObservableProperty] public partial` properties, `[RelayCommand]` methods, constructor-injected primary constructors, xUnit `[Fact]`/`[Theory]` with hand-written fakes (no mocking library).

---

## File Structure

**Create:**
- `OmniCard.Shared/Models/DecklistImportSummary.cs` — result record returned from the dialog.
- `OmniCard.Collection/DecklistPrintingResolver.cs` — pure set/collector-aware resolver.
- `OmniCard/Views/DecklistImport/DecklistImportRow.cs` — one preview row (parsed entry + resolution).
- `OmniCard/Views/DecklistImport/DecklistImportViewModel.cs` — dialog VM (load/resolve/target/commit).
- `OmniCard/Views/DecklistImport/DecklistImportView.xaml` (+ `.xaml.cs`) — the modal window.
- `OmniCard.Tests/Fakes/ImportFakes.cs` — shared test doubles for the tasks below.
- `OmniCard.Tests/Services/DecklistPrintingResolverTests.cs`
- `OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs`

**Modify:**
- `OmniCard.Shared/Models/CardList.cs:3` — add `File` to `ListItemSource`.
- `OmniCard.Shared/Interfaces/IDecklistService.cs` — add `ParseDecklistPrintings`.
- `OmniCard.Collection/DecklistService.cs:17` — widen shared regex; add `ParseDecklistPrintings`.
- `OmniCard.Shared/Interfaces/IDialogService.cs` — add `ShowDecklistImport`.
- `OmniCard/Services/DialogService.cs` — implement `ShowDecklistImport`.
- `OmniCard/App.xaml.cs:199-200` (area) — register `DecklistImportView` + `DecklistImportViewModel`.
- `OmniCard/Views/Root/RootViewModel.cs` — add `ImportDecklistFile` command.
- `OmniCard/Views/Root/RootView.xaml:140` (area) — add the menu item.
- `OmniCard.Tests/Services/DecklistServiceTests.cs` (create if absent) — parser tests.

---

## Shared Test Doubles

Create `OmniCard.Tests/Fakes/ImportFakes.cs`. Tasks reference these classes by name.

```csharp
using System.Collections.ObjectModel;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Fakes;

/// <summary>ICardGameService whose lookup methods are set per-test via delegates.</summary>
public sealed class ConfigurableGameService : ICardGameService
{
    public Func<string, int, List<CardMatch>> OnSearchCards = (_, _) => [];
    public Func<string, List<CardMatch>> OnGetPrintings = _ => [];
    public Func<IEnumerable<string>, bool, Dictionary<string, decimal>> OnGetCurrentPrices = (_, _) => new();

    public CardGame Game => CardGame.Mtg;
    public List<CardMatch> SearchCards(string query, int maxResults = 20) => OnSearchCards(query, maxResults);
    public List<CardMatch> GetPrintings(string cardName) => OnGetPrintings(cardName);
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => OnGetCurrentPrices(gameCardIds, isFoil);

    // Unused members
    public MatchDiagnostics? LastMatchDiagnostics => null;
    public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
    public IReadOnlyList<SetInfo> GetAvailableSets() => [];
    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
    public object? FindCardById(string gameCardId) => null;
}

/// <summary>ICardService that exposes a single active game service and records AddCardToCollection calls.</summary>
public sealed class RecordingCardService(ICardGameService active) : ICardService
{
    public sealed record AddCall(CardMatch Match, CardGame Game, string Condition, bool IsFoil, decimal? PurchasePrice, int Quantity, StorageContainer? Container);
    public List<AddCall> Added { get; } = [];

    public ICardGameService ActiveGameService => active;
    public ICardGameService GetGameService(CardGame game) => active;
    public void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section)
        => Added.Add(new AddCall(match, game, condition, isFoil, purchasePrice, quantity, container));

    // Unused members
    public ObservableCollection<ScannedCard> ScannedCards { get; } = [];
    public CardGame SelectedGame { get; set; }
    public HashSet<string>? SelectedSetFilter { get; set; }
    public bool DefaultIsFoil { get; set; }
    public decimal? DefaultPurchasePrice { get; set; }
    public IReadOnlyList<CardGame> AvailableGames => [];
    public Action<HashStageResult>? OnHashStage { get; set; }
    public ulong LastComputedHash => 0;
    public IOcrMatchingService OcrService => null!;
    public void AddFromStream(Stream stream) => throw new NotImplementedException();
    public void ReprocessScans() => throw new NotImplementedException();
    public void CommitScans(IEnumerable<ScannedCard> scannedCards) => throw new NotImplementedException();
    public void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null) => throw new NotImplementedException();
    public void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, int skip, int take, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
    public int GetSearchCount(string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset, bool stacked) => throw new NotImplementedException();
    public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter = null) => throw new NotImplementedException();
    public void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null) => throw new NotImplementedException();
    public void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update) => throw new NotImplementedException();
    public List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds) => throw new NotImplementedException();
    public void UpdateCollectionCard(CollectionCard card) => throw new NotImplementedException();
    public void DeleteCollectionCard(int id) => throw new NotImplementedException();
    public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null) => throw new NotImplementedException();
    public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame? game, IProgress<string>? progress = null) => throw new NotImplementedException();
    public IReadOnlyDictionary<string, decimal> GetCurrentPrices(CardGame game, IEnumerable<string> gameCardIds, bool foil) => throw new NotImplementedException();
    public List<string> GetDistinctFieldValues(string field, CardGame game) => throw new NotImplementedException();
    public List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode) => throw new NotImplementedException();
    public void RemoveTempFile(ScannedCard card) => throw new NotImplementedException();
    public void ClearTempFiles() => throw new NotImplementedException();
    public void StartNewDiagnosticSession() => throw new NotImplementedException();
    public (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs() => throw new NotImplementedException();
    public (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null) => throw new NotImplementedException();
    public int ImportCollectionCards(IEnumerable<CollectionCard> cards, bool skipDuplicates) => throw new NotImplementedException();
    public ulong ComputeHashFromStream(Stream stream) => throw new NotImplementedException();
    public ulong ComputeEdgeHashFromStream(Stream stream) => throw new NotImplementedException();
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => throw new NotImplementedException();
}

/// <summary>IListService that records AddPrinting/CreateList calls.</summary>
public sealed class RecordingListService : IListService
{
    public sealed record AddPrintingCall(int ListId, CardMatch Printing, bool IsFoil, int Quantity, ListItemSource Source);
    public List<AddPrintingCall> Printings { get; } = [];
    public List<CardList> Lists { get; } = [];
    private int _nextId = 500;

    public IReadOnlyList<CardList> GetLists(CardGame game) => Lists.Where(l => l.Game == game).ToList();
    public CardList CreateList(string name, CardGame game)
    {
        var l = new CardList { Id = _nextId++, Name = name, Game = game };
        Lists.Add(l);
        return l;
    }
    public CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, int quantity, ListItemSource source)
    {
        Printings.Add(new AddPrintingCall(listId, printing, isFoil, quantity, source));
        return new CardListItem { CardListId = listId, GameCardId = printing.GameSpecificId, Quantity = quantity, Source = source };
    }

    // Unused members
    public void RenameList(int listId, string name) => throw new NotImplementedException();
    public void DeleteList(int listId) => throw new NotImplementedException();
    public IReadOnlyList<CardListItem> GetItems(int listId) => throw new NotImplementedException();
    public void RemoveItem(int itemId) => throw new NotImplementedException();
    public void SetQuantity(int itemId, int quantity) => throw new NotImplementedException();
    public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries) => throw new NotImplementedException();
    public void RefreshPrices(int listId) => throw new NotImplementedException();
    public List<DecklistEntry> ToDecklistEntries(int listId) => throw new NotImplementedException();
}

/// <summary>IStorageContainerService that records Create and serves a seeded list.</summary>
public sealed class RecordingContainerService : IStorageContainerService
{
    public List<StorageContainer> Containers { get; } = [];
    public StorageContainer Bulk { get; set; } = new() { Id = 1, Name = "Bulk", IsSystem = true };
    public List<(string Name, ContainerType Type)> Created { get; } = [];
    private int _nextId = 900;

    public List<StorageContainer> GetAll() => Containers;
    public StorageContainer GetBulk() => Bulk;
    public StorageContainer Create(string name, ContainerType type)
    {
        Created.Add((name, type));
        var c = new StorageContainer { Id = _nextId++, Name = name, ContainerType = type };
        Containers.Add(c);
        return c;
    }

    // Unused members
    public void Rename(int id, string newName) => throw new NotImplementedException();
    public void Delete(int id, bool moveCardsToBulk = true) => throw new NotImplementedException();
    public int GetCardCount(int containerId) => throw new NotImplementedException();
    public void SetCoverCard(int containerId, int? cardId) => throw new NotImplementedException();
    public List<CollectionCard> GetCardsInContainer(int containerId) => throw new NotImplementedException();
    public void SetExcludeFromDeckCheck(int containerId, bool exclude) => throw new NotImplementedException();
}

/// <summary>IDecklistService that returns canned printing entries.</summary>
public sealed class FakeDecklistParseService : IDecklistService
{
    public List<DecklistEntry> Printings { get; set; } = [];
    public List<DecklistEntry> ParseDecklistPrintings(string text) => Printings;

    public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text) => throw new NotImplementedException();
    public Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url) => throw new NotImplementedException();
    public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game) => throw new NotImplementedException();
}
```

> Note: `ParseDecklistPrintings` (Task 1) and `ShowDecklistImport` (Task 6) are added to their interfaces in their own tasks; if you implement out of order, the fakes above won't compile until those interface members exist. Implement tasks in order.

---

### Task 1: Printing-level parser + shared regex fix

**Files:**
- Modify: `OmniCard.Shared/Interfaces/IDecklistService.cs`
- Modify: `OmniCard.Collection/DecklistService.cs:17` (regex) and add method
- Test: `OmniCard.Tests/Services/DecklistServiceTests.cs` (create)

**Interfaces:**
- Produces: `List<DecklistEntry> IDecklistService.ParseDecklistPrintings(string text)` — one entry per distinct `(name, setCode, collectorNumber)`, quantities summed for exact duplicates; `SetCode`/`CollectorNumber` null when absent; trailing text after the collector number ignored.

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/DecklistServiceTests.cs`. `ParseDecklistPrintings` is pure text handling, but `DecklistService`'s constructor needs infra. Test through a thin construction using `null!` for the unused dependencies (the method touches none of them):

```csharp
using OmniCard.Collection;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class DecklistServiceTests
{
    private static DecklistService NewService() => new(null!, null!, null!);

    private const string Sample = """
        // a comment
        Deck
        1 Isperia, Supreme Judge (SCD) 4 *E*
        4 Island (SCD) 337
        4 Island (SCD) 338
        1 Island (SCD) 338
        1 Sol Ring (SCD) 276
        1 Lightning Bolt
        """;

    [Fact]
    public void ParseDecklistPrintings_ParsesSetAndCollector_IgnoringTrailingText()
    {
        var entries = NewService().ParseDecklistPrintings(Sample);

        var isperia = Assert.Single(entries, e => e.CardName == "Isperia, Supreme Judge");
        Assert.Equal("SCD", isperia.SetCode);
        Assert.Equal("4", isperia.CollectorNumber);   // *E* dropped
        Assert.Equal(1, isperia.Quantity);
    }

    [Fact]
    public void ParseDecklistPrintings_KeepsDistinctPrintings_AndSumsExactDuplicates()
    {
        var entries = NewService().ParseDecklistPrintings(Sample);

        var islands = entries.Where(e => e.CardName == "Island").ToList();
        Assert.Equal(2, islands.Count);                                  // 337 and 338 distinct
        Assert.Equal(4, islands.Single(e => e.CollectorNumber == "337").Quantity);
        Assert.Equal(5, islands.Single(e => e.CollectorNumber == "338").Quantity); // 4 + 1 summed
    }

    [Fact]
    public void ParseDecklistPrintings_SkipsCommentsAndHeaders_AndAllowsNameOnly()
    {
        var entries = NewService().ParseDecklistPrintings(Sample);

        Assert.DoesNotContain(entries, e => e.CardName.StartsWith("//"));
        Assert.DoesNotContain(entries, e => e.CardName == "Deck");
        var bolt = Assert.Single(entries, e => e.CardName == "Lightning Bolt");
        Assert.Null(bolt.SetCode);
        Assert.Null(bolt.CollectorNumber);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistServiceTests" -v minimal`
Expected: FAIL to compile — `ParseDecklistPrintings` not defined.

- [ ] **Step 3: Widen the shared regex**

In `OmniCard.Collection/DecklistService.cs:17`, change the regex so trailing text after the collector number is tolerated **without** weakening set/collector capture (the trailer is nested inside the optional group):

```csharp
    // Regex: "1 Card Name" | "1x Card Name" | "1 Card Name (SET) 123" | "1 Card Name (SET) 123 *E*"
    [GeneratedRegex(@"^(\d+)x?\s+(.+?)(?:\s+\(([A-Za-z0-9]+)\)\s+(\S+)(?:\s+.*)?)?$")]
    private static partial Regex DecklistLineRegex();
```

- [ ] **Step 4: Add `ParseDecklistPrintings`**

Add to `OmniCard.Collection/DecklistService.cs` (below `ParseDecklistText`):

```csharp
    public List<DecklistEntry> ParseDecklistPrintings(string text)
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

            var key = $"{name.ToUpperInvariant()}|{setCode}|{collectorNumber}";
            if (entries.TryGetValue(key, out var existing))
                entries[key] = existing with { Quantity = existing.Quantity + qty };
            else
                entries[key] = new DecklistEntry(qty, name, setCode, collectorNumber);
        }

        return entries.Values.ToList();
    }
```

Add to `OmniCard.Shared/Interfaces/IDecklistService.cs`:

```csharp
    List<DecklistEntry> ParseDecklistPrintings(string text);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistServiceTests" -v minimal`
Expected: PASS (3 tests). Also run the existing decklist tests to confirm the regex change didn't regress paste import:
Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~Decklist" -v minimal` — Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/IDecklistService.cs OmniCard.Collection/DecklistService.cs OmniCard.Tests/Services/DecklistServiceTests.cs
git commit -m "feat(lists): printing-level decklist parser + tolerate trailing tokens"
```

---

### Task 2: Set/collector-aware resolver

**Files:**
- Create: `OmniCard.Collection/DecklistPrintingResolver.cs`
- Test: `OmniCard.Tests/Services/DecklistPrintingResolverTests.cs`

**Interfaces:**
- Consumes: `ICardGameService.SearchCards`, `GetPrintings`, `GetCurrentPrices`; `DecklistEntry`; `CardMatch`.
- Produces: `static CardMatch? DecklistPrintingResolver.Resolve(ICardGameService gs, DecklistEntry entry)` — the 4-rung ladder; `null` means unresolved.

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/DecklistPrintingResolverTests.cs`:

```csharp
using OmniCard.Collection;
using OmniCard.Models;
using OmniCard.Tests.Fakes;
using Xunit;

namespace OmniCard.Tests.Services;

public class DecklistPrintingResolverTests
{
    private static CardMatch Match(string id, string set, string cn) =>
        new() { GameSpecificId = id, Name = "Island", SetCode = set, CollectorNumber = cn };

    [Fact]
    public void SetAndCollector_ExactHit_Returned()
    {
        var gs = new ConfigurableGameService
        {
            OnSearchCards = (q, _) => [Match("a", "SCD", "337"), Match("b", "SCD", "999")],
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(4, "Island", "SCD", "337"));
        Assert.Equal("a", result!.GameSpecificId);
    }

    [Fact]
    public void SetAndCollector_NoExactMatch_Unresolved()
    {
        var gs = new ConfigurableGameService { OnSearchCards = (_, _) => [Match("b", "SCD", "999")] };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", "SCD", "4"));
        Assert.Null(result);
    }

    [Fact]
    public void SetOnly_PicksCheapestInSet()
    {
        var gs = new ConfigurableGameService
        {
            OnGetPrintings = _ => [Match("a", "SCD", "337"), Match("b", "SCD", "338"), Match("c", "OTHER", "1")],
            OnGetCurrentPrices = (_, _) => new() { ["a"] = 2m, ["b"] = 1m, ["c"] = 0.1m },
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", "SCD", null));
        Assert.Equal("b", result!.GameSpecificId);   // cheapest within SCD, not the cheaper OTHER
    }

    [Fact]
    public void CollectorOnly_PicksCheapestAcrossSetsWithThatNumber()
    {
        var gs = new ConfigurableGameService
        {
            OnGetPrintings = _ => [Match("a", "SCD", "5"), Match("b", "XYZ", "5"), Match("c", "SCD", "6")],
            OnGetCurrentPrices = (_, _) => new() { ["a"] = 3m, ["b"] = 1m },
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", null, "5"));
        Assert.Equal("b", result!.GameSpecificId);
    }

    [Fact]
    public void NameOnly_PicksCheapestPrinting()
    {
        var gs = new ConfigurableGameService
        {
            OnGetPrintings = _ => [Match("a", "SCD", "1"), Match("b", "XYZ", "2")],
            OnGetCurrentPrices = (_, _) => new() { ["a"] = 5m, ["b"] = 2m },
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", null, null));
        Assert.Equal("b", result!.GameSpecificId);
    }

    [Fact]
    public void NameOnly_NoPrintings_Unresolved()
    {
        var gs = new ConfigurableGameService { OnGetPrintings = _ => [] };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Nonesuch", null, null));
        Assert.Null(result);
    }

    [Fact]
    public void NameOnly_NoPrices_FallsBackToFirstPrinting()
    {
        var gs = new ConfigurableGameService
        {
            OnGetPrintings = _ => [Match("a", "SCD", "1"), Match("b", "XYZ", "2")],
            OnGetCurrentPrices = (_, _) => new(),   // nothing priced
        };
        var result = DecklistPrintingResolver.Resolve(gs, new DecklistEntry(1, "Island", null, null));
        Assert.Equal("a", result!.GameSpecificId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistPrintingResolverTests" -v minimal`
Expected: FAIL to compile — `DecklistPrintingResolver` not defined.

- [ ] **Step 3: Implement the resolver**

Create `OmniCard.Collection/DecklistPrintingResolver.cs`:

```csharp
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Resolves a decklist entry to a specific printing using a graduated ladder based on
/// which fields the line provides. Returns null when the entry cannot be resolved.
/// </summary>
public static class DecklistPrintingResolver
{
    public static CardMatch? Resolve(ICardGameService gs, DecklistEntry entry)
    {
        var set = string.IsNullOrWhiteSpace(entry.SetCode) ? null : entry.SetCode.Trim();
        var cn = string.IsNullOrWhiteSpace(entry.CollectorNumber) ? null : entry.CollectorNumber.Trim();

        // Rung 1: exact set + collector — trust the line; no fallback if it misses.
        if (set is not null && cn is not null)
        {
            var hits = gs.SearchCards($"set:{set} cn:{cn}", maxResults: 50);
            return hits.FirstOrDefault(r =>
                string.Equals(r.SetCode, set, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.CollectorNumber, cn, StringComparison.OrdinalIgnoreCase));
        }

        // Rungs 2-4 operate over all printings of the name.
        var printings = gs.GetPrintings(entry.CardName);
        if (printings.Count == 0)
            return null;

        IEnumerable<CardMatch> candidates = printings;
        if (set is not null)
            candidates = candidates.Where(p => string.Equals(p.SetCode, set, StringComparison.OrdinalIgnoreCase));
        else if (cn is not null)
            candidates = candidates.Where(p => string.Equals(p.CollectorNumber, cn, StringComparison.OrdinalIgnoreCase));

        var list = candidates.ToList();
        if (list.Count == 0)
            return null;

        return Cheapest(gs, list);
    }

    private static CardMatch Cheapest(ICardGameService gs, List<CardMatch> printings)
    {
        var prices = gs.GetCurrentPrices(printings.Select(p => p.GameSpecificId), isFoil: false);
        var priced = printings
            .Where(p => prices.ContainsKey(p.GameSpecificId))
            .OrderBy(p => prices[p.GameSpecificId])
            .ToList();
        return priced.Count > 0 ? priced[0] : printings[0];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistPrintingResolverTests" -v minimal`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/DecklistPrintingResolver.cs OmniCard.Tests/Services/DecklistPrintingResolverTests.cs OmniCard.Tests/Fakes/ImportFakes.cs
git commit -m "feat(lists): set/collector-aware decklist printing resolver"
```

---

### Task 3: `ListItemSource.File` + `DecklistImportSummary` + preview row

**Files:**
- Modify: `OmniCard.Shared/Models/CardList.cs:3`
- Create: `OmniCard.Shared/Models/DecklistImportSummary.cs`
- Create: `OmniCard/Views/DecklistImport/DecklistImportRow.cs`

**Interfaces:**
- Produces: `ListItemSource.File`; `record DecklistImportSummary(int Added, int Unresolved, string TargetName)`; `class DecklistImportRow` with `Quantity`, `Name`, `SetCode?`, `CollectorNumber?`, `Match` (`CardMatch?`), and computed `IsResolved`, `ResolvedSet`, `ResolvedNumber`, `Status`.

This task has no standalone test (pure data types exercised by Tasks 4-5). Its deliverable is verified by the build.

- [ ] **Step 1: Add the enum value**

`OmniCard.Shared/Models/CardList.cs:3`:

```csharp
public enum ListItemSource { Manual, Url, Paste, File }
```

- [ ] **Step 2: Add the summary record**

Create `OmniCard.Shared/Models/DecklistImportSummary.cs`:

```csharp
namespace OmniCard.Models;

public record DecklistImportSummary(int Added, int Unresolved, string TargetName);
```

- [ ] **Step 3: Add the preview row**

Create `OmniCard/Views/DecklistImport/DecklistImportRow.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Views.DecklistImport;

/// <summary>One parsed decklist line plus its resolution result, shown in the preview grid.</summary>
public sealed class DecklistImportRow
{
    public required int Quantity { get; init; }
    public required string Name { get; init; }
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public CardMatch? Match { get; init; }

    public bool IsResolved => Match is not null;
    public string Status => Match is null ? "Unresolved" : "Resolved";
    public string ResolvedSet => Match?.SetCode ?? "";
    public string ResolvedNumber => Match?.CollectorNumber ?? "";
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -v minimal`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Shared/Models/CardList.cs OmniCard.Shared/Models/DecklistImportSummary.cs OmniCard/Views/DecklistImport/DecklistImportRow.cs
git commit -m "feat(lists): add File list source, import summary, preview row types"
```

---

### Task 4: `DecklistImportViewModel` — load, resolve, target selection

**Files:**
- Create: `OmniCard/Views/DecklistImport/DecklistImportViewModel.cs`
- Test: `OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs`

**Interfaces:**
- Consumes: `IDecklistService.ParseDecklistPrintings`, `ICardService.ActiveGameService`, `IListService.GetLists`, `IStorageContainerService.GetAll/GetBulk`, `DecklistPrintingResolver.Resolve`.
- Produces: `DecklistImportViewModel` with:
  - `void Load(string sourceName, string fileText, int? defaultContainerId)`
  - `ObservableCollection<DecklistImportRow> Rows`, `ObservableCollection<CardList> AvailableLists`, `ObservableCollection<StorageContainer> AvailableLocations`
  - `string SourceName`, `string SummaryLabel`, `int ResolvedCount`, `int UnresolvedCount`
  - `bool TargetIsList` (false = Location), `bool TargetIsLocation` (read-only), `bool TargetIsLocationEditable` (two-way, for the Location radio), `CardList? SelectedList`, `StorageContainer? SelectedLocation`
  - `bool CreateNew`, `bool UseExistingTarget` (read-only, `!CreateNew`), `string NewName`, `ContainerType NewLocationType`, `IReadOnlyList<ContainerType> LocationTypes`
  - `bool CanImport`

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Models;
using OmniCard.Tests.Fakes;
using OmniCard.Views.DecklistImport;
using Xunit;

namespace OmniCard.Tests.ViewModels;

public class DecklistImportViewModelTests
{
    private static CardMatch M(string id, string set, string cn) =>
        new() { GameSpecificId = id, Name = "Island", SetCode = set, CollectorNumber = cn };

    private static (DecklistImportViewModel vm, ConfigurableGameService gs, RecordingListService lists,
        RecordingContainerService containers, RecordingCardService cards, FakeDecklistParseService decks) Build()
    {
        var gs = new ConfigurableGameService();
        var cards = new RecordingCardService(gs);
        var lists = new RecordingListService();
        var containers = new RecordingContainerService();
        var decks = new FakeDecklistParseService();
        var vm = new DecklistImportViewModel(decks, cards, lists, containers, NullLogger<DecklistImportViewModel>.Instance);
        return (vm, gs, lists, containers, cards, decks);
    }

    [Fact]
    public void Load_ResolvesRows_AndCountsResolvedVsUnresolved()
    {
        var (vm, gs, _, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings =
        [
            new DecklistEntry(4, "Island", "SCD", "337"),
            new DecklistEntry(1, "Nonesuch", "SCD", "999"),
        ];
        gs.OnSearchCards = (q, _) => q.Contains("337") ? [M("a", "SCD", "337")] : [];

        vm.Load("deck.txt", "ignored", defaultContainerId: null);

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(1, vm.ResolvedCount);
        Assert.Equal(1, vm.UnresolvedCount);
        Assert.True(vm.Rows[0].IsResolved);
        Assert.False(vm.Rows[1].IsResolved);
    }

    [Fact]
    public void Load_DefaultsToGivenLocation_WhenProvided()
    {
        var (vm, _, _, containers, _, decks) = Build();
        containers.Containers.Add(new StorageContainer { Id = 7, Name = "Deck Box" });
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };

        vm.Load("deck.txt", "ignored", defaultContainerId: 7);

        Assert.False(vm.TargetIsList);
        Assert.Equal(7, vm.SelectedLocation!.Id);
    }

    [Fact]
    public void Load_DefaultsToBulk_WhenNoLocationProvided()
    {
        var (vm, _, _, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        containers.Containers.Add(containers.Bulk);

        vm.Load("deck.txt", "ignored", defaultContainerId: null);

        Assert.False(vm.TargetIsList);
        Assert.Equal(1, vm.SelectedLocation!.Id);
    }

    [Fact]
    public void CanImport_False_WhenNoResolvedRows()
    {
        var (vm, gs, _, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(1, "Nonesuch", "SCD", "999")];
        gs.OnSearchCards = (_, _) => [];

        vm.Load("deck.txt", "ignored", null);

        Assert.False(vm.CanImport);
    }

    [Fact]
    public void CanImport_False_WhenCreateNewButNameBlank()
    {
        var (vm, gs, _, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(1, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a", "SCD", "337")];
        vm.Load("deck.txt", "ignored", null);

        vm.CreateNew = true;
        vm.NewName = "   ";

        Assert.False(vm.CanImport);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistImportViewModelTests" -v minimal`
Expected: FAIL to compile — `DecklistImportViewModel` not defined.

- [ ] **Step 3: Implement the view model (load/resolve/target)**

Create `OmniCard/Views/DecklistImport/DecklistImportViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.DecklistImport;

public sealed partial class DecklistImportViewModel(
    IDecklistService decklistService,
    ICardService cardService,
    IListService listService,
    IStorageContainerService containerService,
    ILogger<DecklistImportViewModel> logger) : ViewModel
{
    public ObservableCollection<DecklistImportRow> Rows { get; } = [];
    public ObservableCollection<CardList> AvailableLists { get; } = [];
    public ObservableCollection<StorageContainer> AvailableLocations { get; } = [];
    public IReadOnlyList<ContainerType> LocationTypes { get; } = Enum.GetValues<ContainerType>();

    [ObservableProperty] public partial string SourceName { get; set; } = "";
    [ObservableProperty] public partial string SummaryLabel { get; set; } = "";
    public int ResolvedCount { get; private set; }
    public int UnresolvedCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetIsLocation))]
    [NotifyPropertyChangedFor(nameof(TargetIsLocationEditable))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool TargetIsList { get; set; }

    public bool TargetIsLocation => !TargetIsList;

    /// <summary>Two-way alias so the "Location" radio can bind directly (radios need a settable source).</summary>
    public bool TargetIsLocationEditable
    {
        get => !TargetIsList;
        set => TargetIsList = !value;
    }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))] public partial CardList? SelectedList { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))] public partial StorageContainer? SelectedLocation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseExistingTarget))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool CreateNew { get; set; }

    public bool UseExistingTarget => !CreateNew;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))] public partial string NewName { get; set; } = "";
    [ObservableProperty] public partial ContainerType NewLocationType { get; set; } = ContainerType.Box;

    public DecklistImportSummary? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    public bool CanImport
    {
        get
        {
            if (ResolvedCount == 0) return false;
            if (CreateNew) return !string.IsNullOrWhiteSpace(NewName);
            return TargetIsList ? SelectedList is not null : SelectedLocation is not null;
        }
    }

    public void Load(string sourceName, string fileText, int? defaultContainerId)
    {
        SourceName = sourceName;
        var gs = cardService.ActiveGameService;
        var game = gs.Game;

        Rows.Clear();
        var entries = decklistService.ParseDecklistPrintings(fileText);
        foreach (var e in entries)
        {
            CardMatch? match;
            try
            {
                match = DecklistPrintingResolver.Resolve(gs, e);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve decklist entry {Name}", e.CardName);
                match = null;
            }
            Rows.Add(new DecklistImportRow
            {
                Quantity = e.Quantity,
                Name = e.CardName,
                SetCode = e.SetCode,
                CollectorNumber = e.CollectorNumber,
                Match = match,
            });
        }

        ResolvedCount = Rows.Count(r => r.IsResolved);
        UnresolvedCount = Rows.Count - ResolvedCount;
        SummaryLabel = $"{Rows.Count} lines · {ResolvedCount} resolved · {UnresolvedCount} unresolved";

        AvailableLists.Clear();
        foreach (var l in listService.GetLists(game))
            AvailableLists.Add(l);

        AvailableLocations.Clear();
        foreach (var c in containerService.GetAll())
            AvailableLocations.Add(c);

        // Default target: current Location if provided, else the Bulk container.
        TargetIsList = false;
        var bulk = containerService.GetBulk();
        SelectedLocation = defaultContainerId is int id
            ? AvailableLocations.FirstOrDefault(c => c.Id == id) ?? bulk
            : AvailableLocations.FirstOrDefault(c => c.Id == bulk.Id) ?? bulk;

        OnPropertyChanged(nameof(CanImport));
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
```

> The `Import` command is added in Task 5 (kept separate so its commit routing can be tested independently). `ViewModel` is the repo's MVVM base type used by `CsvImportViewModel`/`ListsViewModel`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistImportViewModelTests" -v minimal`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/DecklistImport/DecklistImportViewModel.cs OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs
git commit -m "feat(lists): decklist import view model — load, resolve, target selection"
```

---

### Task 5: `DecklistImportViewModel` — commit routing

**Files:**
- Modify: `OmniCard/Views/DecklistImport/DecklistImportViewModel.cs`
- Test: `OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs`

**Interfaces:**
- Consumes: `IListService.CreateList/AddPrinting`, `IStorageContainerService.Create`, `ICardService.AddCardToCollection`.
- Produces: `ImportCommand` (`[RelayCommand] Import()`) that commits resolved rows to the selected target and sets `Result` (`DecklistImportSummary`), then closes the dialog.

- [ ] **Step 1: Write the failing tests**

Append to `OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs`:

```csharp
    [Fact]
    public void Import_ToExistingList_CallsAddPrinting_WithFileSource_NonFoil()
    {
        var (vm, gs, lists, containers, cards, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        lists.Lists.Add(new CardList { Id = 42, Name = "My Deck", Game = CardGame.Mtg });
        decks.Printings =
        [
            new DecklistEntry(4, "Island", "SCD", "337"),
            new DecklistEntry(1, "Nonesuch", "SCD", "999"),
        ];
        gs.OnSearchCards = (q, _) => q.Contains("337") ? [M("a", "SCD", "337")] : [];
        vm.Load("deck.txt", "ignored", null);

        vm.TargetIsList = true;
        vm.SelectedList = vm.AvailableLists.Single(l => l.Id == 42);
        vm.ImportCommand.Execute(null);

        var call = Assert.Single(lists.Printings);
        Assert.Equal(42, call.ListId);
        Assert.Equal("a", call.Printing.GameSpecificId);
        Assert.Equal(4, call.Quantity);
        Assert.False(call.IsFoil);
        Assert.Equal(ListItemSource.File, call.Source);
        Assert.Equal(4, vm.Result!.Added);          // resolved quantity
        Assert.Equal(1, vm.Result.Unresolved);       // one line unresolved
        Assert.Equal("My Deck", vm.Result.TargetName);
    }

    [Fact]
    public void Import_ToExistingLocation_CallsAddCardToCollection_NearMint_NonFoil_NoPrice()
    {
        var (vm, gs, _, containers, cards, decks) = Build();
        var box = new StorageContainer { Id = 7, Name = "Deck Box" };
        containers.Containers.Add(box);
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(3, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a", "SCD", "337")];
        vm.Load("deck.txt", "ignored", defaultContainerId: 7);

        vm.ImportCommand.Execute(null);

        var call = Assert.Single(cards.Added);
        Assert.Equal("a", call.Match.GameSpecificId);
        Assert.Equal("Near Mint", call.Condition);
        Assert.False(call.IsFoil);
        Assert.Null(call.PurchasePrice);
        Assert.Equal(3, call.Quantity);
        Assert.Equal(7, call.Container!.Id);
        Assert.Equal(3, vm.Result!.Added);
        Assert.Equal("Deck Box", vm.Result.TargetName);
    }

    [Fact]
    public void Import_CreateNewList_CreatesThenPopulates()
    {
        var (vm, gs, lists, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(1, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a", "SCD", "337")];
        vm.Load("deck.txt", "ignored", null);

        vm.TargetIsList = true;
        vm.CreateNew = true;
        vm.NewName = "Fresh List";
        vm.ImportCommand.Execute(null);

        var created = Assert.Single(lists.Lists);
        Assert.Equal("Fresh List", created.Name);
        var call = Assert.Single(lists.Printings);
        Assert.Equal(created.Id, call.ListId);
        Assert.Equal("Fresh List", vm.Result!.TargetName);
    }

    [Fact]
    public void Import_CreateNewLocation_CreatesWithTypeThenPopulates()
    {
        var (vm, gs, _, containers, cards, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(2, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a", "SCD", "337")];
        vm.Load("deck.txt", "ignored", null);

        vm.TargetIsList = false;
        vm.CreateNew = true;
        vm.NewName = "New Binder";
        vm.NewLocationType = ContainerType.Binder;
        vm.ImportCommand.Execute(null);

        Assert.Contains(("New Binder", ContainerType.Binder), containers.Created);
        var call = Assert.Single(cards.Added);
        Assert.Equal("New Binder", call.Container!.Name);
        Assert.Equal("New Binder", vm.Result!.TargetName);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistImportViewModelTests" -v minimal`
Expected: FAIL to compile — `ImportCommand` not defined.

- [ ] **Step 3: Implement the `Import` command**

Add to `DecklistImportViewModel` (in `OmniCard/Views/DecklistImport/DecklistImportViewModel.cs`):

```csharp
    [RelayCommand]
    public void Import()
    {
        var resolved = Rows.Where(r => r.IsResolved).ToList();
        var addedQty = 0;
        var unresolved = Rows.Count - resolved.Count;
        var game = cardService.ActiveGameService.Game;
        string targetName;

        if (TargetIsList)
        {
            var listId = CreateNew
                ? listService.CreateList(NewName.Trim(), game).Id
                : SelectedList!.Id;
            targetName = CreateNew ? NewName.Trim() : SelectedList!.Name;

            foreach (var row in resolved)
            {
                listService.AddPrinting(listId, row.Match!, isFoil: false, row.Quantity, ListItemSource.File);
                addedQty += row.Quantity;
            }
        }
        else
        {
            var container = CreateNew
                ? containerService.Create(NewName.Trim(), NewLocationType)
                : SelectedLocation!;
            targetName = container.Name;

            foreach (var row in resolved)
            {
                cardService.AddCardToCollection(row.Match!, game, condition: "Near Mint", isFoil: false,
                    purchasePrice: null, quantity: row.Quantity, container, page: null, slot: null, section: null);
                addedQty += row.Quantity;
            }
        }

        Result = new DecklistImportSummary(addedQty, unresolved, targetName);
        CloseDialog?.Invoke(true);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistImportViewModelTests" -v minimal`
Expected: PASS (9 tests total).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/DecklistImport/DecklistImportViewModel.cs OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs
git commit -m "feat(lists): decklist import commit routing to List or Location"
```

---

### Task 6: Dialog window + `IDialogService` + DI

**Files:**
- Create: `OmniCard/Views/DecklistImport/DecklistImportView.xaml` (+ `.xaml.cs`)
- Modify: `OmniCard.Shared/Interfaces/IDialogService.cs`
- Modify: `OmniCard/Services/DialogService.cs`
- Modify: `OmniCard/App.xaml.cs` (near line 199-200)

**Interfaces:**
- Consumes: `DecklistImportViewModel.Load/Result/CloseDialog`.
- Produces: `DecklistImportSummary? IDialogService.ShowDecklistImport(string sourceName, string fileText, int? defaultContainerId)`.

This task is UI wiring — verified by build + manual smoke, not unit tests (WPF views are not unit-tested in this repo).

- [ ] **Step 1: Create the view code-behind**

Create `OmniCard/Views/DecklistImport/DecklistImportView.xaml.cs`:

```csharp
using System.Windows;

namespace OmniCard.Views.DecklistImport;

public partial class DecklistImportView : Window
{
    public DecklistImportViewModel ViewModel { get; }

    public DecklistImportView(DecklistImportViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = this;
        ViewModel.CloseDialog = result => { DialogResult = result; Close(); };
    }
}
```

- [ ] **Step 2: Create the XAML**

Create `OmniCard/Views/DecklistImport/DecklistImportView.xaml` (follows `CsvImportView.xaml`; explicit `Foreground` per the dark-theme rule):

```xml
<Window x:Class="OmniCard.Views.DecklistImport.DecklistImportView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="clr-namespace:OmniCard.Views.DecklistImport"
        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
        mc:Ignorable="d"
        Title="Import Decklist File" Height="560" Width="680"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False" ResizeMode="NoResize"
        d:DataContext="{d:DesignInstance {x:Type local:DecklistImportView}}"
        Background="{DynamicResource MaterialDesign.Brush.Background}"
        TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
        TextElement.FontWeight="Regular"
        TextElement.FontSize="13"
        FontFamily="{StaticResource AppFont}">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
    </Window.Resources>
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <StackPanel Margin="0,0,0,8">
            <TextBlock Text="{Binding ViewModel.SourceName}" FontWeight="SemiBold" FontSize="14"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
            <TextBlock Text="{Binding ViewModel.SummaryLabel}" Margin="0,2,0,0"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
        </StackPanel>

        <!-- Target -->
        <GroupBox Grid.Row="1" Header="Import into" Margin="0,0,0,8" Padding="6">
            <StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                    <RadioButton Content="Location" IsChecked="{Binding ViewModel.TargetIsLocationEditable}"
                                 Foreground="{DynamicResource MaterialDesign.Brush.Foreground}" Margin="0,0,16,0"/>
                    <RadioButton Content="List" IsChecked="{Binding ViewModel.TargetIsList}"
                                 Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                </StackPanel>

                <!-- Location picker -->
                <StackPanel Orientation="Horizontal"
                            Visibility="{Binding ViewModel.TargetIsLocation, Converter={StaticResource BoolToVis}}">
                    <ComboBox Width="260" DisplayMemberPath="Name"
                              ItemsSource="{Binding ViewModel.AvailableLocations}"
                              SelectedItem="{Binding ViewModel.SelectedLocation}"
                              IsEnabled="{Binding ViewModel.UseExistingTarget}"
                              Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                </StackPanel>

                <!-- List picker -->
                <StackPanel Orientation="Horizontal"
                            Visibility="{Binding ViewModel.TargetIsList, Converter={StaticResource BoolToVis}}">
                    <ComboBox Width="260" DisplayMemberPath="Name"
                              ItemsSource="{Binding ViewModel.AvailableLists}"
                              SelectedItem="{Binding ViewModel.SelectedList}"
                              IsEnabled="{Binding ViewModel.UseExistingTarget}"
                              Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                </StackPanel>

                <!-- Create new -->
                <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                    <CheckBox Content="Create new" IsChecked="{Binding ViewModel.CreateNew}"
                              VerticalAlignment="Center" Margin="0,0,8,0"
                              Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                    <TextBox Width="200" Margin="0,0,8,0"
                             Text="{Binding ViewModel.NewName, UpdateSourceTrigger=PropertyChanged}"
                             IsEnabled="{Binding ViewModel.CreateNew}"
                             Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                    <ComboBox Width="120"
                              Visibility="{Binding ViewModel.TargetIsLocation, Converter={StaticResource BoolToVis}}"
                              SelectedItem="{Binding ViewModel.NewLocationType}"
                              IsEnabled="{Binding ViewModel.CreateNew}"
                              Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
                              ItemsSource="{Binding ViewModel.LocationTypes}"/>
                </StackPanel>
            </StackPanel>
        </GroupBox>

        <!-- Preview -->
        <DataGrid Grid.Row="2" ItemsSource="{Binding ViewModel.Rows}"
                  AutoGenerateColumns="False" IsReadOnly="True" Margin="0,0,0,8">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Qty" Binding="{Binding Quantity}" Width="40"/>
                <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="*"/>
                <DataGridTextColumn Header="Set" Binding="{Binding ResolvedSet}" Width="70"/>
                <DataGridTextColumn Header="#" Binding="{Binding ResolvedNumber}" Width="60"/>
                <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="90"/>
            </DataGrid.Columns>
        </DataGrid>

        <!-- Buttons -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Import" Command="{Binding ViewModel.ImportCommand}"
                    IsEnabled="{Binding ViewModel.CanImport}" Padding="16,4" Margin="0,0,8,0"/>
            <Button Content="Cancel" Command="{Binding ViewModel.CancelCommand}" Padding="16,4" IsCancel="True"/>
        </StackPanel>
    </Grid>
</Window>
```

> All VM members the XAML binds to — `TargetIsLocationEditable`, `TargetIsLocation`, `UseExistingTarget`, `LocationTypes` — are defined in Task 4/5. No further VM changes are needed here.

- [ ] **Step 3: Add the `IDialogService` method + implementation**

`OmniCard.Shared/Interfaces/IDialogService.cs` — add:

```csharp
    DecklistImportSummary? ShowDecklistImport(string sourceName, string fileText, int? defaultContainerId);
```

`OmniCard/Services/DialogService.cs` — add (mirroring `ShowImportPreview`), with the matching `using OmniCard.Views.DecklistImport;`:

```csharp
    public DecklistImportSummary? ShowDecklistImport(string sourceName, string fileText, int? defaultContainerId)
    {
        var wnd = Services.GetRequiredService<DecklistImportView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(sourceName, fileText, defaultContainerId);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }
```

- [ ] **Step 5: Register in DI**

`OmniCard/App.xaml.cs` (near the `CsvImportView`/`CsvImportViewModel` registrations, ~line 199):

```csharp
            services.AddTransient<DecklistImportView>();
            services.AddTransient<DecklistImportViewModel>();
```

Add `using OmniCard.Views.DecklistImport;` at the top of `App.xaml.cs` if not already resolvable.

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -v minimal`
Expected: Build succeeded. Fix any XAML/binding compile errors before continuing.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/DecklistImport/DecklistImportView.xaml OmniCard/Views/DecklistImport/DecklistImportView.xaml.cs OmniCard.Shared/Interfaces/IDialogService.cs OmniCard/Services/DialogService.cs OmniCard/App.xaml.cs OmniCard/Views/DecklistImport/DecklistImportViewModel.cs
git commit -m "feat(lists): decklist import dialog window, dialog service, DI"
```

---

### Task 7: Top-level "Import ▸ Decklist file…" command + menu

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (near `ImportCollection`, ~line 2239)
- Modify: `OmniCard/Views/Root/RootView.xaml` (near line 140, the File menu)

**Interfaces:**
- Consumes: `IDialogService.ShowDecklistImport`; `Collection.CurrentLocationId`.
- Produces: `ImportDecklistFileCommand` on `RootViewModel`.

UI wiring — verified by build + manual smoke.

- [ ] **Step 1: Add the command**

In `OmniCard/Views/Root/RootViewModel.cs`, add next to `ImportCollection` (note `dialogService`, `logger`, and `Collection` are already available on this VM):

```csharp
    [RelayCommand]
    public void ImportDecklistFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Decklist files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Import Decklist File",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var text = System.IO.File.ReadAllText(dialog.FileName);
            var summary = dialogService.ShowDecklistImport(
                System.IO.Path.GetFileName(dialog.FileName), text, Collection.CurrentLocationId);
            if (summary is not null)
            {
                Message = summary.Unresolved > 0
                    ? $"Imported {summary.Added} cards to {summary.TargetName}. {summary.Unresolved} unresolved."
                    : $"Imported {summary.Added} cards to {summary.TargetName}.";
                _ = Collection.SearchCollection();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import decklist file");
            System.Windows.MessageBox.Show($"Failed to import: {ex.Message}", "Import Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
```

> Verify `Collection` exposes `CurrentLocationId` (it does: `CollectionViewModel.CurrentLocationId`, an `int?`). `SearchCollection()` refreshes the grid so a Location import shows immediately; if that method name/overload differs on `CollectionViewModel`, use the same call `ImportCollection` uses to refresh.

- [ ] **Step 2: Add the menu item**

In `OmniCard/Views/Root/RootView.xaml`, immediately after the existing `_Import...` `MenuItem` (line ~140-142), add:

```xml
                <MenuItem Header="Import _Decklist File..."
                          Command="{Binding ViewModel.ImportDecklistFileCommand}"/>
```

- [ ] **Step 3: Build**

Run: `dotnet build OmniCard/OmniCard.csproj -v minimal`
Expected: Build succeeded.

- [ ] **Step 4: Manual smoke test (verify skill)**

Use the `verify` (or `run`) skill to launch the app and drive the flow with the reference file `first-flight-starter-commander-precon-decklist-20260624-140703.txt`:
1. File ▸ Import Decklist File… → pick the file.
2. Confirm the preview shows ~74 rows, the `Isperia … *E*` line resolved (SCD 4), and the four Islands (337-340) as distinct resolved rows.
3. Confirm the target defaults to your current Location when one is open, else Bulk.
4. Import into a new List and into a Location; confirm the summary message and that the cards appear (List tab / collection grid).
5. Confirm unresolved lines (if any) are reported and skipped.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/RootView.xaml
git commit -m "feat(lists): top-level Import Decklist File menu command"
```

---

## Full-suite check

- [ ] Run the complete test suite: `dotnet test OmniCard.Tests -v minimal` — Expected: all green, including pre-existing tests (no regressions from the shared-regex change).
- [ ] Run `dotnet build -v minimal` on the solution to confirm no broken references.
