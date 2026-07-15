# Location Audit Tool — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable users to audit a storage location by scanning its cards, comparing scan results to DB inventory, and producing a report with PDF export.

**Architecture:** A new `AuditService` encapsulates scoped hash matching (using a temporary index built from the location's cards in the Scryfall DB) and report generation with one-to-one `GameCardId` matching. The scanner pipeline branches on `AuditService.IsAuditActive` to use scoped matching instead of the full Scryfall DB. A report dialog displays results with manual card assignment, and QuestPDF generates the PDF export.

**Tech Stack:** C# / .NET 10, WPF (MVVM with CommunityToolkit.Mvvm), EF Core (SQLite), xUnit, QuestPDF (MIT)

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`
- Test project: `OmniCard.Tests` using xUnit + in-memory SQLite
- MVVM: ViewModels use `[ObservableProperty]` / `[RelayCommand]` from CommunityToolkit.Mvvm
- XAML bindings in scanner tab use `{Binding ViewModel.*}` pattern (not `DataContext.ViewModel.*`)
- XAML bindings in collection views use `DataContext.ViewModel.Collection.*` via `RelativeSource AncestorType=Window`
- DI: Services registered as singletons in `App.xaml.cs`, views as transient
- `CardSevice` (note: existing typo in class name — keep it)
- Scoped matching must not modify the `ScryfallService` singleton's `_hashCache` / `_artHashCache`
- Scanner pipeline uses `BeginInvoke` (not `Invoke`) for UI thread work — TWAIN message pump runs on UI thread
- `PerceptualHashService.HammingDistance(ulong, ulong)` is a static method returning `int` (0-64)

---

### Task 1: AuditReport Model + AuditService Core Logic

**Files:**
- Create: `OmniCard/Models/AuditReport.cs`
- Create: `OmniCard/Services/AuditService.cs`
- Test: `OmniCard.Tests/Services/AuditServiceTests.cs`

**Interfaces:**
- Consumes:
  - `IDbContextFactory<CollectionDbContext>` — read location's cards
  - `IDbContextFactory<ScryfallDbContext>` — read Scryfall hashes for scoped index
  - `IStorageContainerService.GetAll()` — resolve container name
  - `PerceptualHashService.HammingDistance(ulong, ulong)` — static, no injection needed
- Produces:
  - `IAuditService` interface (consumed by Tasks 2, 3)
  - `AuditReport` / `AuditReportItem` models (consumed by Tasks 5, 6)

- [ ] **Step 1: Create AuditReport model**

Create `OmniCard/Models/AuditReport.cs`:

```csharp
namespace OmniCard.Models;

public class AuditReport
{
    public required string LocationName { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public int ExpectedCount { get; init; }
    public int ActualCount { get; init; }
    public List<AuditReportItem> Matched { get; init; } = [];
    public List<AuditReportItem> Missing { get; init; } = [];
    public List<AuditReportItem> Extra { get; init; } = [];
}

public class AuditReportItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string? _name;
    private string? _setCode;
    private string? _setName;
    private string? _collectorNumber;
    private string? _imageUri;
    private string? _gameCardId;
    private double? _confidence;
    private bool _isManuallyAssigned;

    public string? Name { get => _name; set => SetProperty(ref _name, value); }
    public string? SetCode { get => _setCode; set => SetProperty(ref _setCode, value); }
    public string? SetName { get => _setName; set => SetProperty(ref _setName, value); }
    public string? CollectorNumber { get => _collectorNumber; set => SetProperty(ref _collectorNumber, value); }
    public string? ImageUri { get => _imageUri; set => SetProperty(ref _imageUri, value); }
    public string? GameCardId { get => _gameCardId; set => SetProperty(ref _gameCardId, value); }
    public double? Confidence { get => _confidence; set => SetProperty(ref _confidence, value); }
    public bool IsManuallyAssigned { get => _isManuallyAssigned; set => SetProperty(ref _isManuallyAssigned, value); }

    /// <summary>Scan temp image path, for Extra items that came from the scanner.</summary>
    public string? ScanImagePath { get; init; }
}
```

Note: `AuditReportItem` extends `ObservableObject` because users can manually assign card identity in the report dialog, and the UI needs to react to those changes.

- [ ] **Step 2: Write failing tests for AuditService**

Create `OmniCard.Tests/Services/AuditServiceTests.cs`:

```csharp
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly SqliteConnection _collectionConn;
    private readonly DbContextOptions<CollectionDbContext> _collectionOptions;
    private readonly SqliteConnection _scryfallConn;
    private readonly DbContextOptions<ScryfallDbContext> _scryfallOptions;

    public AuditServiceTests()
    {
        _collectionConn = new SqliteConnection("Data Source=:memory:");
        _collectionConn.Open();
        _collectionOptions = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_collectionConn)
            .Options;
        using var collCtx = new CollectionDbContext(_collectionOptions);
        collCtx.Database.EnsureCreated();

        _scryfallConn = new SqliteConnection("Data Source=:memory:");
        _scryfallConn.Open();
        _scryfallOptions = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_scryfallConn)
            .Options;
        using var scryfallCtx = new ScryfallDbContext(_scryfallOptions);
        scryfallCtx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _collectionConn.Dispose();
        _scryfallConn.Dispose();
    }

    private IDbContextFactory<CollectionDbContext> CollectionFactory() => new MockCollectionFactory(_collectionOptions);
    private IDbContextFactory<ScryfallDbContext> ScryfallFactory() => new MockScryfallFactory(_scryfallOptions);

    private AuditService CreateService() => new(
        CollectionFactory(),
        ScryfallFactory(),
        new StubContainerService(),
        NullLogger<AuditService>.Instance);

    [Fact]
    public void GenerateReport_MatchesOneToOneByGameCardId()
    {
        // Setup: location has 2x CardA and 1x CardB
        using (var ctx = new CollectionDbContext(_collectionOptions))
        {
            var container = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();

            ctx.Cards.AddRange(
                new CollectionCard { Game = CardGame.Mtg, GameCardId = "card-a", Name = "Card A", SetCode = "SET", ContainerId = container.Id },
                new CollectionCard { Game = CardGame.Mtg, GameCardId = "card-a", Name = "Card A", SetCode = "SET", ContainerId = container.Id },
                new CollectionCard { Game = CardGame.Mtg, GameCardId = "card-b", Name = "Card B", SetCode = "SET", ContainerId = container.Id }
            );
            ctx.SaveChanges();
        }

        var service = CreateService();
        int containerId;
        using (var ctx = new CollectionDbContext(_collectionOptions))
            containerId = ctx.StorageContainers.First().Id;

        service.StartAudit(containerId);

        // Simulate: scanned 1x CardA and 1x CardC (unknown)
        var scans = new List<ScannedCard>
        {
            new() { TempImagePath = "t1.png", Hash = 0, Game = CardGame.Mtg,
                     Match = new CardMatch { GameSpecificId = "card-a", Name = "Card A", SetCode = "SET" } },
            new() { TempImagePath = "t2.png", Hash = 0, Game = CardGame.Mtg,
                     Match = null }, // unmatched scan
        };

        var report = service.GenerateReport(scans);

        Assert.Equal("Binder", report.LocationName);
        Assert.Equal(3, report.ExpectedCount);
        Assert.Equal(2, report.ActualCount);
        Assert.Single(report.Matched);                // 1x CardA matched
        Assert.Equal(2, report.Missing.Count);         // 1x CardA + 1x CardB missing
        Assert.Single(report.Extra);                   // 1x unmatched scan
    }

    [Fact]
    public void StartAudit_SetsActiveState()
    {
        using (var ctx = new CollectionDbContext(_collectionOptions))
        {
            var container = new StorageContainer { Name = "Box", ContainerType = ContainerType.Box };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
        }

        var service = CreateService();
        int containerId;
        using (var ctx = new CollectionDbContext(_collectionOptions))
            containerId = ctx.StorageContainers.First().Id;

        Assert.False(service.IsAuditActive);
        service.StartAudit(containerId);
        Assert.True(service.IsAuditActive);
        Assert.Equal(containerId, service.AuditLocationId);
        Assert.Equal("Box", service.AuditLocationName);

        service.EndAudit();
        Assert.False(service.IsAuditActive);
        Assert.Null(service.AuditLocationId);
    }

    [Fact]
    public void FindScopedMatch_MatchesOnlyLocationCards()
    {
        // Setup: location has CardA (hash 100), Scryfall DB has CardA (hash 100) and CardB (hash 200)
        Guid cardAId, cardBId;
        using (var ctx = new ScryfallDbContext(_scryfallOptions))
        {
            var cardA = new ScryfallCard { Name = "Card A", SetCode = "SET", CollectorNumber = "1", ImageHash = 100 };
            var cardB = new ScryfallCard { Name = "Card B", SetCode = "SET", CollectorNumber = "2", ImageHash = 200 };
            ctx.Cards.AddRange(cardA, cardB);
            ctx.SaveChanges();
            cardAId = cardA.Id;
            cardBId = cardB.Id;
        }

        using (var ctx = new CollectionDbContext(_collectionOptions))
        {
            var container = new StorageContainer { Name = "Binder", ContainerType = ContainerType.Binder };
            ctx.StorageContainers.Add(container);
            ctx.SaveChanges();
            ctx.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg, GameCardId = cardAId.ToString(), Name = "Card A",
                SetCode = "SET", ContainerId = container.Id
            });
            ctx.SaveChanges();
        }

        var service = CreateService();
        int containerId;
        using (var ctx = new CollectionDbContext(_collectionOptions))
            containerId = ctx.StorageContainers.First().Id;

        service.StartAudit(containerId);

        // Hash 100 should match CardA (in location)
        var matchA = service.FindScopedMatch(100, null);
        Assert.NotNull(matchA);
        Assert.Equal("Card A", matchA.Name);

        // Hash 200 is CardB which is NOT in the location — should not match
        var matchB = service.FindScopedMatch(200, null);
        Assert.Null(matchB);
    }

    // --- Stubs ---

    private class MockCollectionFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }

    private class MockScryfallFactory(DbContextOptions<ScryfallDbContext> options) : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }

    private class StubContainerService : IStorageContainerService
    {
        // Minimal stub — StartAudit reads container name from CollectionDbContext directly
        public IReadOnlyList<StorageContainer> GetAll() => [];
        public StorageContainer Create(string name, ContainerType type) => throw new NotImplementedException();
        public void Delete(int id, bool moveCardsToBulk) => throw new NotImplementedException();
        public void SetCoverCard(int containerId, int? cardId) => throw new NotImplementedException();
    }
}
```

Note: The `ScryfallCard` model referenced in the test — you'll need to check its exact properties. It has `Id` (Guid PK), `Name`, `SetCode`, `CollectorNumber`, `ImageHash` (ulong?), `ArtHash` (ulong?). The test creates cards with known hash values and verifies scoped matching only returns cards from the audited location.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~AuditServiceTests" -v minimal`
Expected: Build errors — `AuditService`, `IAuditService` not found

- [ ] **Step 4: Implement IAuditService and AuditService**

Create `OmniCard/Services/AuditService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IAuditService
{
    bool IsAuditActive { get; }
    int? AuditLocationId { get; }
    string? AuditLocationName { get; }
    void StartAudit(int containerId);
    void EndAudit();
    CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes);
    AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards);
}

public sealed class AuditService : IAuditService
{
    private readonly IDbContextFactory<CollectionDbContext> _collectionDbFactory;
    private readonly IDbContextFactory<ScryfallDbContext> _scryfallDbFactory;
    private readonly IStorageContainerService _containerService;
    private readonly ILogger<AuditService> _logger;

    // Scoped index built on StartAudit
    private List<(Guid Id, ulong Hash, string Name, string SetCode, string CollectorNumber, string GameCardId)>? _scopedHashIndex;
    private List<(Guid Id, ulong ArtHash)>? _scopedArtHashIndex;

    // Expected cards for report generation
    private List<CollectionCard>? _expectedCards;

    public bool IsAuditActive { get; private set; }
    public int? AuditLocationId { get; private set; }
    public string? AuditLocationName { get; private set; }

    public AuditService(
        IDbContextFactory<CollectionDbContext> collectionDbFactory,
        IDbContextFactory<ScryfallDbContext> scryfallDbFactory,
        IStorageContainerService containerService,
        ILogger<AuditService> logger)
    {
        _collectionDbFactory = collectionDbFactory;
        _scryfallDbFactory = scryfallDbFactory;
        _containerService = containerService;
        _logger = logger;
    }

    public void StartAudit(int containerId)
    {
        using var collCtx = _collectionDbFactory.CreateDbContext();
        var container = collCtx.StorageContainers.FirstOrDefault(c => c.Id == containerId);
        if (container is null)
            throw new InvalidOperationException($"Container {containerId} not found");

        AuditLocationId = containerId;
        AuditLocationName = container.Name;

        // Load expected cards from the location
        _expectedCards = collCtx.Cards
            .AsNoTracking()
            .Where(c => c.ContainerId == containerId)
            .ToList();

        // Get distinct GameCardIds to build scoped hash index
        var gameCardIds = _expectedCards
            .Select(c => c.GameCardId)
            .Distinct()
            .ToHashSet();

        // Build scoped hash index from Scryfall DB
        using var scryfallCtx = _scryfallDbFactory.CreateDbContext();
        var scryfallCards = scryfallCtx.Cards
            .AsNoTracking()
            .Where(c => c.ImageHash != null)
            .Select(c => new { c.Id, Hash = c.ImageHash!.Value, c.Name, c.SetCode, c.CollectorNumber, c.ArtHash })
            .AsEnumerable()
            .Where(c => gameCardIds.Contains(c.Id.ToString()))
            .ToList();

        _scopedHashIndex = scryfallCards
            .Select(c => (c.Id, c.Hash, c.Name, c.SetCode, c.CollectorNumber, GameCardId: c.Id.ToString()))
            .ToList();

        _scopedArtHashIndex = scryfallCards
            .Where(c => c.ArtHash.HasValue)
            .Select(c => (c.Id, c.ArtHash!.Value))
            .ToList();

        IsAuditActive = true;
        _logger.LogInformation("Audit started for container {Id} ({Name}): {Expected} expected cards, {Index} hash index entries",
            containerId, container.Name, _expectedCards.Count, _scopedHashIndex.Count);
    }

    public void EndAudit()
    {
        IsAuditActive = false;
        AuditLocationId = null;
        AuditLocationName = null;
        _scopedHashIndex = null;
        _scopedArtHashIndex = null;
        _expectedCards = null;
        _logger.LogInformation("Audit ended");
    }

    public CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes)
    {
        if (_scopedHashIndex is null || _scopedHashIndex.Count == 0)
            return null;

        const int MaxDistance = 14;
        const int TieZone = 2;

        // Phase 1: Find best pHash distance
        int bestDistance = int.MaxValue;
        foreach (var (_, cardHash, _, _, _, _) in _scopedHashIndex)
        {
            var dist = PerceptualHashService.HammingDistance(hash, cardHash);
            if (dist < bestDistance)
                bestDistance = dist;
        }

        if (bestDistance > MaxDistance)
            return null;

        // Phase 2: Collect tie-zone candidates
        var candidates = new List<(Guid Id, int Distance, string Name, string SetCode, string CollectorNumber, string GameCardId)>();
        foreach (var (id, cardHash, name, setCode, collNum, gameCardId) in _scopedHashIndex)
        {
            var dist = PerceptualHashService.HammingDistance(hash, cardHash);
            if (dist <= bestDistance + TieZone)
                candidates.Add((id, dist, name, setCode, collNum, gameCardId));
        }

        if (candidates.Count == 0)
            return null;

        // Phase 3: Art hash disambiguation (if multiple candidates and art hashes available)
        var bestCandidate = candidates.OrderBy(c => c.Distance).First();

        if (artHashes is not null && _scopedArtHashIndex is { Count: > 0 } && candidates.Count > 1)
        {
            var artLookup = new Dictionary<Guid, ulong>();
            foreach (var (id, artHash) in _scopedArtHashIndex)
                artLookup.TryAdd(id, artHash);

            int bestCombined = int.MaxValue;
            foreach (var candidate in candidates)
            {
                var combined = candidate.Distance;
                if (artLookup.TryGetValue(candidate.Id, out var candidateArtHash))
                {
                    var artDist = artHashes.Min(ah => PerceptualHashService.HammingDistance(ah, candidateArtHash));
                    combined += artDist;
                }
                if (combined < bestCombined)
                {
                    bestCombined = combined;
                    bestCandidate = candidate;
                }
            }
        }

        var confidence = Math.Max(0, (1.0 - (double)bestCandidate.Distance / MaxDistance) * 100);

        return new CardMatch
        {
            Name = bestCandidate.Name,
            SetCode = bestCandidate.SetCode,
            CollectorNumber = bestCandidate.CollectorNumber,
            GameSpecificId = bestCandidate.GameCardId,
            Confidence = confidence,
            Source = new object(), // Placeholder — scoped match doesn't need full card source
        };
    }

    public AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards)
    {
        if (_expectedCards is null)
            throw new InvalidOperationException("No audit is active");

        var scans = scannedCards.ToList();

        // Build a bag of expected GameCardIds (with duplicates for quantity)
        var expectedBag = _expectedCards
            .Select(c => (c.GameCardId, c.Name, c.SetCode, c.SetName, CollectorNumber: c.Number))
            .ToList();

        var matched = new List<AuditReportItem>();
        var extra = new List<AuditReportItem>();

        foreach (var scan in scans)
        {
            if (scan.Match is not null)
            {
                // Try to consume one expected card with the same GameCardId
                var idx = expectedBag.FindIndex(e => e.GameCardId == scan.Match.GameSpecificId);
                if (idx >= 0)
                {
                    var consumed = expectedBag[idx];
                    expectedBag.RemoveAt(idx);
                    matched.Add(new AuditReportItem
                    {
                        Name = consumed.Name,
                        SetCode = consumed.SetCode,
                        SetName = consumed.SetName,
                        CollectorNumber = consumed.CollectorNumber,
                        GameCardId = consumed.GameCardId,
                        Confidence = scan.Match.Confidence,
                    });
                }
                else
                {
                    // Matched a card but it's not expected in this location
                    extra.Add(new AuditReportItem
                    {
                        Name = scan.Match.Name,
                        SetCode = scan.Match.SetCode,
                        SetName = scan.Match.SetName,
                        CollectorNumber = scan.Match.CollectorNumber,
                        GameCardId = scan.Match.GameSpecificId,
                        Confidence = scan.Match.Confidence,
                        ScanImagePath = scan.TempImagePath,
                    });
                }
            }
            else
            {
                // Unmatched scan — extra with no card data
                extra.Add(new AuditReportItem
                {
                    ScanImagePath = scan.TempImagePath,
                });
            }
        }

        // Remaining expected cards are missing
        var missing = expectedBag.Select(e => new AuditReportItem
        {
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.CollectorNumber,
            GameCardId = e.GameCardId,
        }).ToList();

        return new AuditReport
        {
            LocationName = AuditLocationName ?? "Unknown",
            ExpectedCount = _expectedCards.Count,
            ActualCount = scans.Count,
            Matched = matched,
            Missing = missing,
            Extra = extra,
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~AuditServiceTests" -v minimal`
Expected: 3 tests pass

Note: The `ScryfallCard` model and `ScryfallDbContext` schema may require adjustment — check that `ScryfallCard` has the properties used. If the test for `FindScopedMatch` fails because the `ScryfallCard` schema differs, adapt the test setup to match the actual model. The key test is `GenerateReport_MatchesOneToOneByGameCardId` which validates the one-to-one bag-consumption logic.

- [ ] **Step 6: Register AuditService in DI**

In `OmniCard/App.xaml.cs`, add after the `IScanDiagnosticService` registration (around line 106):

```csharp
services.AddSingleton<IAuditService, AuditService>();
```

- [ ] **Step 7: Build and run full test suite**

Run: `dotnet build OmniCard.slnx && dotnet test OmniCard.Tests -v minimal`
Expected: Build succeeds, all tests pass

- [ ] **Step 8: Commit**

```bash
git add OmniCard/Models/AuditReport.cs OmniCard/Services/AuditService.cs OmniCard/App.xaml.cs OmniCard.Tests/Services/AuditServiceTests.cs
git commit -m "feat: add AuditService with scoped hash matching and report generation"
```

---

### Task 2: Wire Audit Mode into Scanner Pipeline

**Files:**
- Modify: `OmniCard/Services/CardSevice.cs` (constructor + `AddFromStream`)

**Interfaces:**
- Consumes: `IAuditService.IsAuditActive`, `IAuditService.FindScopedMatch(ulong, ulong[])` from Task 1
- Produces: Modified `AddFromStream` that branches on audit mode (consumed by the existing scanner flow)

- [ ] **Step 1: Inject IAuditService into CardSevice constructor**

In `OmniCard/Services/CardSevice.cs`, add `IAuditService` to the constructor. The constructor is at the top of the `CardSevice` class. Find the constructor and add the parameter:

```csharp
private readonly IAuditService _auditService;
```

Add it as a constructor parameter and assign it. The constructor takes: `IPerceptualHashService hashService, IEnumerable<ICardGameService> gameServices, IDbContextFactory<CollectionDbContext> collectionDbContextFactory, IOcrMatchingService ocrService, ScanImageCache scanImageCache, ILogger<CardSevice> logger, IDataPathService dataPathService, IScanDiagnosticService diagnosticService`

Add `IAuditService auditService` as a parameter and assign `_auditService = auditService;`

- [ ] **Step 2: Add audit mode branch in AddFromStream**

In `AddFromStream` (line ~298), the current matching call is:
```csharp
var (match, matchedGame) = FindBestMatch(hash, artHashes, null, SelectedSetFilter, detectedSets);
game = matchedGame;
```

Replace with:
```csharp
CardMatch? match;
if (_auditService.IsAuditActive)
{
    match = _auditService.FindScopedMatch(hash, artHashes);
    // Skip set symbol detection and OCR re-matching in audit mode
}
else
{
    var (bestMatch, matchedGame) = FindBestMatch(hash, artHashes, null, SelectedSetFilter, detectedSets);
    match = bestMatch;
    game = matchedGame;
}
```

Also, in the `BeginInvoke` async block (line ~338), wrap the OCR re-matching section in a `if (!_auditService.IsAuditActive)` guard so OCR is skipped in audit mode:

```csharp
Application.Current.Dispatcher.BeginInvoke(async () =>
{
    ScannedCards.Add(scannedCard);

    if (!_auditService.IsAuditActive)
    {
        // Existing OCR code unchanged...
    }
});
```

- [ ] **Step 3: Update test stubs to account for new constructor parameter**

In all existing test files that construct `CardSevice` directly, add a stub `IAuditService`. The simplest approach: create a `NullAuditService` stub in each test file (or a shared one):

```csharp
private class NullAuditService : IAuditService
{
    public bool IsAuditActive => false;
    public int? AuditLocationId => null;
    public string? AuditLocationName => null;
    public void StartAudit(int containerId) { }
    public void EndAudit() { }
    public CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes) => null;
    public AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards) => throw new NotImplementedException();
}
```

Add `new NullAuditService()` to the constructor calls in:
- `OmniCard.Tests/Services/CardServiceCollectionTests.cs`
- `OmniCard.Tests/Services/CollectionSortFilterTests.cs`
- `OmniCard.Tests/Services/CollectionCardCrudTests.cs`
- `OmniCard.Tests/Services/FallbackMatchingTests.cs`
- Any other test file that instantiates `CardSevice`

- [ ] **Step 4: Build and run full test suite**

Run: `dotnet build OmniCard.slnx && dotnet test OmniCard.Tests -v minimal`
Expected: Build succeeds, all tests pass

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Services/CardSevice.cs OmniCard.Tests/Services/CardServiceCollectionTests.cs OmniCard.Tests/Services/CollectionSortFilterTests.cs OmniCard.Tests/Services/CollectionCardCrudTests.cs OmniCard.Tests/Services/FallbackMatchingTests.cs
git commit -m "feat: wire audit mode into scanner pipeline"
```

---

### Task 3: RootViewModel Audit Commands

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs`

**Interfaces:**
- Consumes:
  - `IAuditService` from Task 1 (injected via constructor)
  - `IDialogService.ShowAuditReport(AuditReport)` — defined in Task 5 but called here; add interface method now, implement later
- Produces:
  - `RootViewModel.IsAuditMode` (bool, observable) — true when audit is active
  - `RootViewModel.AuditLocationName` (string, computed) — delegates to audit service
  - `RootViewModel.StartAuditCommand` (ICommand, takes int containerId)
  - `RootViewModel.EndAuditCommand` (ICommand)
  - `RootViewModel.GenerateAuditReportCommand` (ICommand)

- [ ] **Step 1: Inject IAuditService into RootViewModel**

`RootViewModel` uses a primary constructor (line 25). Add `IAuditService auditService` parameter. The existing constructor already has many parameters — add it after `IScanDiagnosticService diagnosticService`.

- [ ] **Step 2: Add audit mode properties and commands**

Add after the existing scanning-related properties:

```csharp
// --- Audit Mode ---

[ObservableProperty]
public partial bool IsAuditMode { get; set; }

public string AuditLocationName => auditService.AuditLocationName ?? "";

[RelayCommand]
public void StartAudit(int containerId)
{
    if (IsAuditMode) return;

    // Clear any existing scans
    CardService.ScannedCards.Clear();

    auditService.StartAudit(containerId);
    IsAuditMode = true;
    OnPropertyChanged(nameof(AuditLocationName));

    // Switch to scanner tab
    SelectedTabIndex = 1; // Scanner tab index — verify this matches the actual tab order
    _logger.LogInformation("Audit mode started for location {Id}", containerId);
}

[RelayCommand]
public void EndAudit()
{
    if (!IsAuditMode) return;

    CardService.ScannedCards.Clear();
    auditService.EndAudit();
    IsAuditMode = false;
    OnPropertyChanged(nameof(AuditLocationName));
    _logger.LogInformation("Audit mode ended");
}

[RelayCommand]
public void GenerateAuditReport()
{
    if (!IsAuditMode) return;

    var report = auditService.GenerateReport(CardService.ScannedCards);
    dialogService.ShowAuditReport(report);
}
```

- [ ] **Step 3: Add ShowAuditReport to IDialogService (stub implementation)**

In `OmniCard/Services/DialogService.cs`, add to the interface:

```csharp
void ShowAuditReport(AuditReport report);
```

Add a temporary stub implementation in `DialogService`:

```csharp
public void ShowAuditReport(AuditReport report)
{
    MessageBox.Show($"Audit Report: {report.Matched.Count} matched, {report.Missing.Count} missing, {report.Extra.Count} extra",
        $"Audit — {report.LocationName}");
}
```

This will be replaced with the real dialog in Task 5.

- [ ] **Step 4: Guard Scan() and CommitScans() for audit mode**

In `Scan()` (line ~879), the existing guard is `if (IsAuditComplete) return;`. The audit mode doesn't block scanning — it just uses scoped matching. No change needed here.

In `CommitScans()`, add a guard at the top:
```csharp
if (IsAuditMode) return; // Cannot commit in audit mode
```

- [ ] **Step 5: Verify tab index**

Check `RootView.xaml` or equivalent to confirm the scanner tab index. If it's a `TabControl`, look for the tab order. Set `SelectedTabIndex` to the correct value. If there's already a `SelectedTabIndex` property, use it. If not, add one.

- [ ] **Step 6: Build to verify**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeds

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs OmniCard/Services/DialogService.cs
git commit -m "feat: add audit mode commands to RootViewModel"
```

---

### Task 4: Scanner Tab UI — Audit Mode Controls

**Files:**
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml`

**Interfaces:**
- Consumes:
  - `RootViewModel.IsAuditMode` (bool) from Task 3
  - `RootViewModel.AuditLocationName` (string) from Task 3
  - `RootViewModel.EndAuditCommand` from Task 3
  - `RootViewModel.GenerateAuditReportCommand` from Task 3
- Produces: Updated scanner UI with audit mode visual changes

- [ ] **Step 1: Add audit banner at top of scanner tab**

In `ScannerTabView.xaml`, add a new row at the top of the main Grid (before the ToolBarPanel). Insert a new `RowDefinition Height="Auto"` at position 0, shift existing rows down.

Add the banner:

```xml
<!-- Audit mode banner -->
<Border Grid.Row="0"
        Background="#FF1565C0"
        Padding="12,8"
        Visibility="{Binding ViewModel.IsAuditMode, Converter={StaticResource BoolToVis}}">
    <DockPanel>
        <Button DockPanel.Dock="Right"
                Content="Cancel Audit"
                Command="{Binding ViewModel.EndAuditCommand}"
                Margin="12,0,0,0"
                Padding="12,4"/>
        <TextBlock Foreground="White" FontWeight="SemiBold" FontSize="14" VerticalAlignment="Center">
            <Run Text="Auditing: "/>
            <Run Text="{Binding ViewModel.AuditLocationName, Mode=OneWay}"/>
        </TextBlock>
    </DockPanel>
</Border>
```

Update the ToolBarPanel to `Grid.Row="1"` and the content grid to `Grid.Row="2"`.

- [ ] **Step 2: Hide commit-related controls in audit mode**

For each of the following controls, add an `InverseBoolConverter` visibility binding on `IsAuditMode` to hide them during audit. Use the existing `InverseBoolConverter`:

Controls to hide when `IsAuditMode == true`:
- Location ComboBox and its label (lines 49-70)
- Binder position fields: Page, Slot labels and textboxes (lines 72-90)
- Box section field (lines 92-101)
- Is Foil checkbox and label (lines 104-110)
- Purchase Price textbox and label (lines 112-118)
- "Scans Verified" / "Undo Verification" button (lines 144-163)
- "Commit Scans to Collection" button (lines 165-182)

Add to each element:
```xml
Visibility="{Binding ViewModel.IsAuditMode, Converter={local:InverseBoolToVisibilityConverter}}"
```

Note: Some elements already have Visibility bindings (binder/box fields). For those, wrap them in a panel or use a `MultiDataTrigger` to combine both conditions. The simplest approach: wrap the location/binder/box section in a `StackPanel` with the audit visibility, so the inner conditional visibility still works.

- [ ] **Step 3: Add "Generate Audit Report" button**

After the commit button area (line ~182), add the audit report button, visible only in audit mode:

```xml
<Button Command="{Binding ViewModel.GenerateAuditReportCommand}"
        Content="Generate Audit Report"
        Visibility="{Binding ViewModel.IsAuditMode, Converter={StaticResource BoolToVis}}"/>
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/ScannerTabView.xaml
git commit -m "feat: add audit mode UI controls to scanner tab"
```

---

### Task 5: Entry Points — Context Menu + Card List Toolbar

**Files:**
- Modify: `OmniCard/Views/Root/LocationOverviewView.xaml` (context menu)
- Modify: `OmniCard/Views/Root/LocationOverviewView.xaml.cs` (click handler)
- Modify: `OmniCard/Views/Root/CollectionTabView.xaml` (toolbar button)

**Interfaces:**
- Consumes: `RootViewModel.StartAuditCommand` from Task 3
- Produces: Two UI entry points for starting an audit

- [ ] **Step 1: Add context menu item to location tiles**

In `OmniCard/Views/Root/LocationOverviewView.xaml`, in the `ContextMenu` (after the "Delete Location..." MenuItem), add:

```xml
<Separator/>
<MenuItem Header="Audit Location..."
          Click="AuditLocation_Click"/>
```

- [ ] **Step 2: Add click handler in code-behind**

In `OmniCard/Views/Root/LocationOverviewView.xaml.cs`, add:

```csharp
private void AuditLocation_Click(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem) return;
    if (menuItem.DataContext is not LocationTileSummary summary) return;

    var rootView = (RootView)Window.GetWindow(this)!;
    rootView.ViewModel.StartAudit(summary.Container.Id);
}
```

- [ ] **Step 3: Add "Audit" button to card list toolbar**

In `OmniCard/Views/Root/CollectionTabView.xaml`, in the ToolBar, add an "Audit" button. It should be visible only when viewing a single location (not "Browse All"). Add after the location name TextBlock area:

```xml
<Button Content="Audit"
        Command="{Binding DataContext.ViewModel.StartAuditCommand,
            RelativeSource={RelativeSource AncestorType=Window}}"
        CommandParameter="{Binding DataContext.ViewModel.Collection.CurrentLocationId,
            RelativeSource={RelativeSource AncestorType=Window}}"
        Padding="8,4"
        Margin="4,0,0,0">
    <Button.Style>
        <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Visibility" Value="{Binding DataContext.ViewModel.Collection.ShowCardList,
                RelativeSource={RelativeSource AncestorType=Window},
                Converter={local:BoolToVisibilityConverter}}"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding DataContext.ViewModel.Collection.ShowAllCards,
                    RelativeSource={RelativeSource AncestorType=Window}}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>
```

This button is visible when `ShowCardList == true && ShowAllCards == false` (i.e., viewing a single location).

- [ ] **Step 4: Build to verify**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/LocationOverviewView.xaml OmniCard/Views/Root/LocationOverviewView.xaml.cs OmniCard/Views/Root/CollectionTabView.xaml
git commit -m "feat: add audit entry points to collection context menu and toolbar"
```

---

### Task 6: Audit Report Dialog

**Files:**
- Create: `OmniCard/Views/AuditReport/AuditReportView.xaml`
- Create: `OmniCard/Views/AuditReport/AuditReportView.xaml.cs`
- Create: `OmniCard/Views/AuditReport/AuditReportViewModel.cs`
- Modify: `OmniCard/Services/DialogService.cs` (replace stub with real dialog)
- Modify: `OmniCard/App.xaml.cs` (register view + viewmodel)

**Interfaces:**
- Consumes:
  - `AuditReport` model from Task 1
  - `ICardGameService.SearchCards(string query, int maxResults)` — for manual card assignment
  - `ICardService.ActiveGameService` — to get the game service for search
- Produces:
  - `AuditReportView` — modal dialog showing audit results
  - `AuditReportViewModel` — manages report display and manual assignment
  - `IDialogService.ShowAuditReport` — real implementation

- [ ] **Step 1: Create AuditReportViewModel**

Create `OmniCard/Views/AuditReport/AuditReportViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.AuditReport;

public sealed partial class AuditReportViewModel(ICardService cardService) : ObservableObject
{
    [ObservableProperty]
    public partial AuditReport? Report { get; set; }

    [ObservableProperty]
    public partial string ManualSearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial List<CardMatch>? SearchResults { get; set; }

    [ObservableProperty]
    public partial AuditReportItem? SelectedItemForAssignment { get; set; }

    public void Load(AuditReport report) => Report = report;

    [RelayCommand]
    public void SearchForAssignment()
    {
        if (string.IsNullOrWhiteSpace(ManualSearchQuery) || SelectedItemForAssignment is null)
            return;

        var results = cardService.ActiveGameService.SearchCards(ManualSearchQuery, 20);
        SearchResults = results;
    }

    [RelayCommand]
    public void AssignCard(CardMatch match)
    {
        if (SelectedItemForAssignment is null) return;

        SelectedItemForAssignment.Name = match.Name;
        SelectedItemForAssignment.SetCode = match.SetCode;
        SelectedItemForAssignment.SetName = match.SetName;
        SelectedItemForAssignment.CollectorNumber = match.CollectorNumber;
        SelectedItemForAssignment.GameCardId = match.GameSpecificId;
        SelectedItemForAssignment.ImageUri = match.ImageUri;
        SelectedItemForAssignment.IsManuallyAssigned = true;

        // Clear search state
        SelectedItemForAssignment = null;
        ManualSearchQuery = "";
        SearchResults = null;
    }

    [RelayCommand]
    public void BeginAssignment(AuditReportItem item)
    {
        SelectedItemForAssignment = item;
        ManualSearchQuery = "";
        SearchResults = null;
    }

    [RelayCommand]
    public void CancelAssignment()
    {
        SelectedItemForAssignment = null;
        ManualSearchQuery = "";
        SearchResults = null;
    }

    /// <summary>Called by the PDF export button.</summary>
    public Action? ExportPdf { get; set; }

    [RelayCommand]
    public void ExportToPdf() => ExportPdf?.Invoke();
}
```

- [ ] **Step 2: Create AuditReportView XAML**

Create `OmniCard/Views/AuditReport/AuditReportView.xaml` — a `Window` with:

1. **Summary header**: Location name, date, expected/actual/matched/missing/extra counts with match rate percentage
2. **Three Expanders** (Matched, Missing, Extra) each containing a `ListView` of `AuditReportItem` rows showing Name, Set, Collector Number. Missing and Extra sections include an "Assign..." button per row.
3. **Assignment panel** (visible when `SelectedItemForAssignment` is not null): search TextBox + Go button + results list with "Select" buttons
4. **Footer**: "Export to PDF" button + "Close" button

The XAML follows existing dialog patterns (Window with `Owner`, sized appropriately). Use `MaterialDesign` styles consistent with the rest of the app. The Expanders should show count badges in their headers (e.g., "Missing (3)").

Key bindings:
- `{Binding Report.LocationName}`, `{Binding Report.ExpectedCount}`, etc. for summary
- `{Binding Report.Matched}`, `{Binding Report.Missing}`, `{Binding Report.Extra}` for lists
- `{Binding BeginAssignmentCommand}` with `CommandParameter` binding to the list item
- `{Binding SearchForAssignmentCommand}` on the search Go button
- `{Binding AssignCardCommand}` with `CommandParameter` on search result items
- `{Binding ExportToPdfCommand}` on the export button

Create `OmniCard/Views/AuditReport/AuditReportView.xaml.cs`:

```csharp
using System.Windows;
using Microsoft.Win32;
using OmniCard.Services;

namespace OmniCard.Views.AuditReport;

public partial class AuditReportView : Window
{
    public AuditReportViewModel ViewModel { get; }

    public AuditReportView(AuditReportViewModel viewModel, IAuditPdfExporter pdfExporter)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        viewModel.ExportPdf = () =>
        {
            if (viewModel.Report is null) return;
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                FileName = $"Audit_{viewModel.Report.LocationName}_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                pdfExporter.Export(viewModel.Report, dlg.FileName);
                MessageBox.Show("PDF exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        InitializeComponent();
    }
}
```

- [ ] **Step 3: Update DialogService with real implementation**

In `OmniCard/Services/DialogService.cs`, replace the stub:

```csharp
public void ShowAuditReport(AuditReport report)
{
    var wnd = Services.GetRequiredService<AuditReportView>();
    wnd.Owner = Application.Current.MainWindow;
    wnd.ViewModel.Load(report);
    wnd.ShowDialog();
}
```

- [ ] **Step 4: Register in DI**

In `OmniCard/App.xaml.cs`, add to the transient services section:

```csharp
services.AddTransient<AuditReportView>();
services.AddTransient<AuditReportViewModel>();
```

Add the using:
```csharp
using OmniCard.Views.AuditReport;
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeds (the `IAuditPdfExporter` interface will be created in Task 7 — for now, create a placeholder interface to satisfy compilation)

Add a placeholder in `AuditService.cs` or a new file:
```csharp
public interface IAuditPdfExporter
{
    void Export(AuditReport report, string filePath);
}
```

Register a stub: `services.AddSingleton<IAuditPdfExporter, StubAuditPdfExporter>();` where `StubAuditPdfExporter` is a no-op.

- [ ] **Step 6: Run all tests**

Run: `dotnet test OmniCard.Tests -v minimal`
Expected: All tests pass

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/AuditReport/ OmniCard/Services/DialogService.cs OmniCard/App.xaml.cs OmniCard/Services/AuditService.cs
git commit -m "feat: add audit report dialog with manual card assignment"
```

---

### Task 7: PDF Export with QuestPDF

**Files:**
- Create: `OmniCard/Services/AuditPdfExporter.cs`
- Modify: `OmniCard/OmniCard.csproj` (add QuestPDF NuGet)
- Modify: `OmniCard/App.xaml.cs` (replace stub registration)

**Interfaces:**
- Consumes: `AuditReport` model from Task 1
- Produces: `IAuditPdfExporter.Export(AuditReport, string)` — real implementation replacing the stub from Task 6

- [ ] **Step 1: Add QuestPDF NuGet package**

Run: `dotnet add OmniCard/OmniCard.csproj package QuestPDF`

Also, QuestPDF requires a license setting. For community/open-source use, add to the app startup or the exporter:
```csharp
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

- [ ] **Step 2: Implement AuditPdfExporter**

Create `OmniCard/Services/AuditPdfExporter.cs`:

```csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IAuditPdfExporter
{
    void Export(AuditReport report, string filePath);
}

public sealed class AuditPdfExporter : IAuditPdfExporter
{
    public void Export(AuditReport report, string filePath)
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
                    col.Item().Text($"Audit Report — {report.LocationName}")
                        .FontSize(18).Bold();
                    col.Item().Text($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    // Summary
                    col.Item().PaddingBottom(12).Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Expected: ").Bold();
                            t.Span($"{report.ExpectedCount}");
                        });
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Scanned: ").Bold();
                            t.Span($"{report.ActualCount}");
                        });
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Matched: ").Bold();
                            t.Span($"{report.Matched.Count}");
                        });
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Missing: ").Bold();
                            t.Span($"{report.Missing.Count}").FontColor(Colors.Red.Medium);
                        });
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Extra: ").Bold();
                            t.Span($"{report.Extra.Count}").FontColor(Colors.Orange.Medium);
                        });
                    });

                    // Missing cards table
                    if (report.Missing.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("Missing from Scan").FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Name
                                columns.RelativeColumn(1); // Set
                                columns.RelativeColumn(1); // Number
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold();
                            });

                            foreach (var item in report.Missing)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.Name ?? "Unknown");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.CollectorNumber ?? "");
                            }
                        });
                    }

                    // Extra cards table
                    if (report.Extra.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Not in Location (Extra)").FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold();
                            });

                            foreach (var item in report.Extra)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.Name ?? "Unidentified");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.CollectorNumber ?? "");
                            }
                        });
                    }

                    // Matched cards table
                    if (report.Matched.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Matched").FontSize(13).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Name").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Set").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold();
                            });

                            foreach (var item in report.Matched)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.Name ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.SetCode ?? "");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                    .Text(item.CollectorNumber ?? "");
                            }
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
}
```

- [ ] **Step 3: Replace stub DI registration**

In `OmniCard/App.xaml.cs`, replace the stub `IAuditPdfExporter` registration with:

```csharp
services.AddSingleton<IAuditPdfExporter, AuditPdfExporter>();
```

Remove the stub class if it was defined inline.

- [ ] **Step 4: Build to verify**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeds

- [ ] **Step 5: Run all tests**

Run: `dotnet test OmniCard.Tests -v minimal`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/AuditPdfExporter.cs OmniCard/OmniCard.csproj OmniCard/App.xaml.cs
git commit -m "feat: add QuestPDF audit report export"
```
