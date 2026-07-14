# Missing from Database Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow users to clear incorrect matches, mark scanned cards as "missing from database", persist that flag through commit, and filter the collection to find them — plus fix a cross-game matching bug.

**Architecture:** Five independent changes: (1) remove cross-game fallback in `FindBestMatch`, (2) add "Clear Match" command in RootViewModel + button in detail panel, (3) add `FlagReason` property to `CollectionCard` with SQLite migration, (4) copy `FlagReason` during commit, (5) add `is:missingdb` filter support. Each change is small and well-bounded.

**Tech Stack:** .NET 10, WPF (CommunityToolkit.Mvvm), EF Core (SQLite), xUnit

## Global Constraints

- Target framework: net10.0-windows10.0.22621.0
- All models in `OmniCard.Shared` namespace `OmniCard.Models`
- SQLite schema changes use try/catch `ALTER TABLE ADD COLUMN` pattern (not EF migrations)
- `ScannedCard` uses `[ObservableProperty]` source generators from CommunityToolkit.Mvvm
- Existing `FlagReason` enum: `None`, `NoMatch`, `VeryLowConfidence`, `Manual`, `MissingFromDatabase`
- Test helpers (`StubGameService`, `StubHashService`, `StubOcrService`, `NullScanDiagnosticService`, `NullAuditService`, `MockCollectionDbContextFactory`) are defined in `FallbackMatchingTests.cs` — reuse the same pattern in new test files

---

### Task 1: Remove Cross-Game Matching Fallback

**Files:**
- Modify: `OmniCard.Collection/CardService.cs:148-181` — `FindBestMatch()` method
- Modify: `OmniCard.Tests/Services/FallbackMatchingTests.cs` — update existing tests

**Interfaces:**
- Consumes: `ICardGameService.FindClosestMatch()` (unchanged)
- Produces: `CardService.FindBestMatch()` — now returns `(null, SelectedGame)` when primary game has no match, never tries other games

- [ ] **Step 1: Update test — cross-game fallback should no longer return a match**

In `OmniCard.Tests/Services/FallbackMatchingTests.cs`, modify the test `FindBestMatch_ReturnsMatchFromOtherGame_WhenPrimaryFails` to expect `null` instead of a cross-game match. Rename it to reflect the new behavior:

```csharp
[Fact]
public void FindBestMatch_ReturnsNull_WhenPrimaryGameHasNoMatch()
{
    var otherGameMatch = new CardMatch
    {
        Name = "Lightning Bolt",
        SetCode = "lea",
        SetName = "Alpha",
        CollectorNumber = "1",
        Rarity = "common",
        GameSpecificId = Guid.NewGuid().ToString(),
        Source = new Card { Id = Guid.NewGuid(), Name = "Lightning Bolt" },
    };

    var noMatchService = new StubGameService(CardGame.OnePiece, match: null);
    var matchService = new StubGameService(CardGame.Mtg, match: otherGameMatch);

    var service = CreateCardService([noMatchService, matchService]);
    service.SelectedGame = CardGame.OnePiece;

    var (match, game) = service.FindBestMatch(0xDEADBEEF);

    Assert.Null(match);
    Assert.Equal(CardGame.OnePiece, game);
}
```

Also update `ReprocessScans_MatchesOnlyUnmatchedCards` — the unmatched OnePiece card should NOT get a cross-game MTG match anymore:

```csharp
[Fact]
public void ReprocessScans_MatchesOnlyUnmatchedCards()
{
    var mtgMatch = new CardMatch
    {
        Name = "Bolt",
        SetCode = "lea",
        SetName = "Alpha",
        CollectorNumber = "1",
        Rarity = "common",
        GameSpecificId = Guid.NewGuid().ToString(),
        Source = new Card { Id = Guid.NewGuid(), Name = "Bolt" },
    };

    var existingMatch = new CardMatch
    {
        Name = "Zoro",
        SetCode = "OP01",
        SetName = "Romance Dawn",
        CollectorNumber = "OP01-001",
        Rarity = "SR",
        GameSpecificId = "OP01-001",
        Source = new OptcgCard { CardSetId = "OP01-001", CardName = "Zoro" },
    };

    // MTG service matches hash 0xAABB, OnePiece matches nothing
    var mtgService = new StubGameService(CardGame.Mtg, match: mtgMatch);
    var opService = new StubGameService(CardGame.OnePiece, match: null);

    var service = CreateCardService([mtgService, opService]);
    service.SelectedGame = CardGame.OnePiece;

    var unmatched = CreateScannedCard(CardGame.OnePiece, hash: 0xAABB, match: null);
    var matched = CreateScannedCard(CardGame.OnePiece, hash: 0xCCDD, match: existingMatch);
    service.ScannedCards.Add(unmatched);
    service.ScannedCards.Add(matched);

    service.ReprocessScans();

    // Unmatched card should remain unmatched — no cross-game fallback
    Assert.Null(unmatched.Match);
    Assert.Equal(CardGame.OnePiece, unmatched.Game);

    // Already-matched card should be unchanged
    Assert.Equal("Zoro", matched.Match!.Name);
    Assert.Equal(CardGame.OnePiece, matched.Game);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~FallbackMatchingTests" -v minimal`

Expected: `FindBestMatch_ReturnsNull_WhenPrimaryGameHasNoMatch` fails (currently returns a match). `ReprocessScans_MatchesOnlyUnmatchedCards` fails (currently assigns cross-game match).

- [ ] **Step 3: Remove the cross-game fallback loop**

In `OmniCard.Collection/CardService.cs`, replace the `FindBestMatch` method body (lines 148-181) with:

```csharp
public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null)
{
    // Normalize empty set filter to null
    if (setFilter is { Count: 0 })
        setFilter = null;

    // Match only within the selected game — never fall back to other games
    if (_gameServices.TryGetValue(SelectedGame, out var primaryService))
    {
        var primaryMatch = primaryService.FindClosestMatch(hash, artHashes, ocrResult, setFilter, preferredSets);
        if (primaryMatch is not null)
            return (primaryMatch, SelectedGame);
    }

    return (null, SelectedGame);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~FallbackMatchingTests" -v minimal`

Expected: All tests PASS. The `FindBestMatch_WithSetFilter_SkipsCrossGameFallback` test should also still pass (the set filter guard is now redundant but harmless — it was removed along with the fallback).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/CardService.cs OmniCard.Tests/Services/FallbackMatchingTests.cs
git commit -m "fix: remove cross-game matching fallback from FindBestMatch

Scans now only match within the selected game. Previously, when the
primary game had no match, FindBestMatch tried all other game services
as a fallback, which could return a Magic match for a One Piece scan."
```

---

### Task 2: Add "Clear Match" Command and Button

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs` — add `ClearMatchCommand`
- Modify: `OmniCard/Views/Root/ScannerDetailPanelView.xaml` — add "Clear Match" button
- Create: `OmniCard.Tests/Services/ClearMatchCommandTests.cs` — unit tests

**Interfaces:**
- Consumes: `ScannedCard.Match`, `ScannedCard.FlagReason`, `ScannedCard.FlagFix`, `ScanFlagFix`, `SerializeMatchData()` (all existing)
- Produces: `RootViewModel.ClearMatchCommand` — sets `Match = null`, `FlagReason = MissingFromDatabase`, creates `ScanFlagFix`

- [ ] **Step 1: Write tests for ClearMatch behavior**

Create `OmniCard.Tests/Services/ClearMatchCommandTests.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class ClearMatchCommandTests
{
    [Fact]
    public void ClearMatch_SetsMatchToNull()
    {
        var card = CreateCardWithMatch();
        ClearMatch(card);
        Assert.Null(card.Match);
    }

    [Fact]
    public void ClearMatch_SetsFlagReasonToMissingFromDatabase()
    {
        var card = CreateCardWithMatch();
        ClearMatch(card);
        Assert.Equal(FlagReason.MissingFromDatabase, card.FlagReason);
    }

    [Fact]
    public void ClearMatch_CreatesFlagFix_WithOriginalMatchData()
    {
        var card = CreateCardWithMatch();
        ClearMatch(card);

        Assert.NotNull(card.FlagFix);
        Assert.Equal("ClearMatch", card.FlagFix.FixType);
        Assert.Equal(FlagReason.None, card.FlagFix.OriginalFlagReason);
        Assert.Contains("Lightning Bolt", card.FlagFix.OriginalData);
        Assert.Equal("", card.FlagFix.ResolvedData);
    }

    [Fact]
    public void ClearMatch_PreservesOriginalFlagReason_WhenAlreadyFlagged()
    {
        var card = CreateCardWithMatch();
        card.FlagReason = FlagReason.VeryLowConfidence;
        ClearMatch(card);

        Assert.NotNull(card.FlagFix);
        Assert.Equal(FlagReason.VeryLowConfidence, card.FlagFix.OriginalFlagReason);
        Assert.Equal(FlagReason.MissingFromDatabase, card.FlagReason);
    }

    [Fact]
    public void ClearMatch_NoOp_WhenMatchIsNull()
    {
        var card = new ScannedCard { Hash = 0x1234, Game = CardGame.OnePiece };
        ClearMatch(card);

        Assert.Null(card.FlagFix);
        Assert.Equal(FlagReason.None, card.FlagReason);
    }

    /// <summary>
    /// Mirrors the logic that will be in RootViewModel.ClearMatch().
    /// Extracted here so we can test the logic without the full ViewModel.
    /// </summary>
    private static void ClearMatch(ScannedCard card)
    {
        if (card.Match is null) return;

        var originalFlagReason = card.FlagReason;
        var originalMatchData = System.Text.Json.JsonSerializer.Serialize(new
        {
            name = card.Match.Name,
            setCode = card.Match.SetCode,
            setName = card.Match.SetName,
            collectorNumber = card.Match.CollectorNumber,
            rarity = card.Match.Rarity,
            gameSpecificId = card.Match.GameSpecificId,
            confidence = card.Match.Confidence,
        });

        card.FlagFix = new ScanFlagFix
        {
            FixType = "ClearMatch",
            OriginalFlagReason = originalFlagReason,
            OriginalData = originalMatchData,
            ResolvedData = "",
        };

        card.Match = null;
        card.FlagReason = FlagReason.MissingFromDatabase;
    }

    private static ScannedCard CreateCardWithMatch()
    {
        return new ScannedCard
        {
            Hash = 0xDEADBEEF,
            Game = CardGame.Mtg,
            Match = new CardMatch
            {
                Name = "Lightning Bolt",
                SetCode = "lea",
                SetName = "Alpha",
                CollectorNumber = "1",
                Rarity = "common",
                GameSpecificId = "abc-123",
                Confidence = 85,
            },
        };
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ClearMatchCommandTests" -v minimal`

Expected: All 5 tests PASS — these test the pure logic, not the ViewModel wiring.

- [ ] **Step 3: Add ClearMatch command to RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, add the following method near the existing `ToggleFlag` method (around line 825):

```csharp
[RelayCommand(CanExecute = nameof(CanClearMatch))]
public void ClearMatch(ScannedCard card)
{
    if (card.Match is null) return;

    var originalFlagReason = card.FlagReason;

    card.FlagFix = new ScanFlagFix
    {
        FixType = "ClearMatch",
        OriginalFlagReason = originalFlagReason,
        OriginalData = SerializeMatchData(card.Match),
        ResolvedData = "",
    };

    card.Match = null;
    card.FlagReason = FlagReason.MissingFromDatabase;

    try { _diagnosticService.LogUserFlagged(card.Hash, card); } catch { }

    ApplyScanView();
    Message = "Match cleared — marked as missing from database.";
}

public bool CanClearMatch(ScannedCard card) => card?.Match is not null;
```

Also, in the `SelectedScannedCard` property change handler (or wherever `SelectedScannedCard` changes), notify `ClearMatchCommand` so the button enables/disables. Find where `ConfirmMatchCommand` is notified and add `ClearMatchCommand` alongside it. Look for calls like `ConfirmMatchCommand.NotifyCanExecuteChanged()` and add `ClearMatchCommand.NotifyCanExecuteChanged()` next to them.

- [ ] **Step 4: Add "Clear Match" button to ScannerDetailPanelView.xaml**

In `OmniCard/Views/Root/ScannerDetailPanelView.xaml`, add a "Clear Match" button after the "No match message" TextBlock (after line 153) and before the single-select editable fields section. This button is only visible for single selection when a match exists:

```xml
<!-- Clear Match button (single selection with match) -->
<Button Content="Clear Match (Missing from DB)"
        Command="{Binding ViewModel.ClearMatchCommand}"
        CommandParameter="{Binding ViewModel.SelectedScannedCard}"
        Padding="12,4"
        Margin="0,0,0,8"
        Foreground="White"
        Background="#E65100"
        Style="{StaticResource MaterialDesignFlatMidBgButton}"
        Visibility="{Binding ViewModel.HasSingleSelection, Converter={StaticResource BoolToVis}}"/>
```

The `CanExecute` binding on the command will handle hiding/disabling when there's no match.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test OmniCard.Tests -v minimal`

Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/ScannerDetailPanelView.xaml OmniCard.Tests/Services/ClearMatchCommandTests.cs
git commit -m "feat: add Clear Match button to scan review detail panel

Adds a 'Clear Match (Missing from DB)' button that clears the
app-selected match and sets FlagReason to MissingFromDatabase.
Creates a ScanFlagFix record capturing the original match data."
```

---

### Task 3: Persist FlagReason on CollectionCard

**Files:**
- Modify: `OmniCard.Shared/Models/CollectionCard.cs` — add `FlagReason?` property
- Modify: `OmniCard.Data/CollectionDbContext.cs` — configure enum conversion
- Modify: `OmniCard.Collection/CardService.cs` — schema migration + copy on commit
- Create: `OmniCard.Tests/Data/FlagReasonMigrationTests.cs` — migration test

**Interfaces:**
- Consumes: `FlagReason` enum, `ScannedCard.FlagReason`, `CollectionDbContext`
- Produces: `CollectionCard.FlagReason` — nullable `FlagReason` persisted in SQLite

- [ ] **Step 1: Write migration test**

Create `OmniCard.Tests/Data/FlagReasonMigrationTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class FlagReasonMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public FlagReasonMigrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void FlagReason_ColumnCreatedByEnsureCreated()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "1",
            Name = "Test",
            FlagReason = FlagReason.MissingFromDatabase,
        });
        ctx.SaveChanges();

        var card = ctx.Cards.First();
        Assert.Equal(FlagReason.MissingFromDatabase, card.FlagReason);
    }

    [Fact]
    public void FlagReason_NullByDefault()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "2",
            Name = "Normal Card",
        });
        ctx.SaveChanges();

        var card = ctx.Cards.First();
        Assert.Null(card.FlagReason);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~FlagReasonMigrationTests" -v minimal`

Expected: FAIL — `FlagReason` property doesn't exist on `CollectionCard` yet.

- [ ] **Step 3: Add FlagReason property to CollectionCard**

In `OmniCard.Shared/Models/CollectionCard.cs`, add after line 29 (`public bool IsMissing { get; set; }`):

```csharp
public FlagReason? FlagReason { get; set; }
```

- [ ] **Step 4: Configure enum conversion in CollectionDbContext**

In `OmniCard.Data/CollectionDbContext.cs`, add inside `OnModelCreating` after line 28 (`card.Property(c => c.Game).HasConversion<string>();`):

```csharp
card.Property(c => c.FlagReason).HasConversion<string?>();
```

- [ ] **Step 5: Add schema migration in CardService constructor**

In `OmniCard.Collection/CardService.cs`, add after the `IsMissing` migration block (after line 126):

```csharp
// Add FlagReason column for cards marked as missing from database
try
{
    ctx.Database.ExecuteSqlRaw("ALTER TABLE Cards ADD COLUMN FlagReason TEXT");
}
catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column name"))
{
    // Column already exists
}
```

- [ ] **Step 6: Copy FlagReason during commit**

In `OmniCard.Collection/CardService.cs`, in the `CommitScans` method, add `FlagReason` assignment to both the missing-card branch (after line 529, `IsMissing = true,`) and the matched-card branch (after line 548, `ContainerId = container?.Id,`).

For the missing-card branch (around line 530), add:
```csharp
FlagReason = FlagReason.MissingFromDatabase,
```

For the matched-card branch (around line 549), add:
```csharp
FlagReason = scan.FlagReason != FlagReason.None ? scan.FlagReason : null,
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~FlagReasonMigrationTests" -v minimal`

Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/CollectionCard.cs OmniCard.Data/CollectionDbContext.cs OmniCard.Collection/CardService.cs OmniCard.Tests/Data/FlagReasonMigrationTests.cs
git commit -m "feat: persist FlagReason on CollectionCard through commit

Adds nullable FlagReason property to CollectionCard, stored as a
string in SQLite. FlagReason is copied from ScannedCard during
commit so missing-from-database cards can be filtered later."
```

---

### Task 4: Add `is:missingdb` Collection Filter

**Files:**
- Modify: `OmniCard.Collection/CardService.cs:1253-1265` — `BuildIsExpression()` method
- Modify: `OmniCard.Tests/Services/CollectionSortFilterTests.cs` — add filter test

**Interfaces:**
- Consumes: `CollectionCard.FlagReason` (from Task 3)
- Produces: `is:missingdb` filter syntax in `ApplyScryfallFilter()` pipeline

- [ ] **Step 1: Write test for `is:missingdb` filter**

In `OmniCard.Tests/Services/CollectionSortFilterTests.cs`, add a test method. First, update `SeedCards` to include a card with `FlagReason = FlagReason.MissingFromDatabase`. Add this card to the seed data:

```csharp
new CollectionCard { Game = CardGame.Mtg, GameCardId = "8", Name = "Unknown Card", SetCode = "", SetName = "", Rarity = "", FlagReason = FlagReason.MissingFromDatabase }
```

Then add the test:

```csharp
[Fact]
public void SearchCollection_IsMissingDb_FiltersToMissingFromDatabase()
{
    var results = new ObservableCollection<CollectionCard>();
    var filter = new FilterPreset { Name = "Missing", Game = CardGame.Mtg, Query = "is:missingdb" };
    CreateService().SearchCollection("", CardGame.Mtg, null, null, filter, results);

    Assert.Single(results);
    Assert.Equal("Unknown Card", results[0].Name);
    Assert.Equal(FlagReason.MissingFromDatabase, results[0].FlagReason);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~CollectionSortFilterTests.SearchCollection_IsMissingDb" -v minimal`

Expected: FAIL — `is:missingdb` is not recognized, falls through to default `true` case, returns all cards.

- [ ] **Step 3: Add `missingdb` case to `BuildIsExpression`**

In `OmniCard.Collection/CardService.cs`, in the `BuildIsExpression` method (around line 1253), add a new case to the switch expression before the default:

```csharp
private static LinqExpression BuildIsExpression(System.Linq.Expressions.ParameterExpression param, string value)
{
    return value.ToLowerInvariant() switch
    {
        "foil" => LinqExpression.Equal(
            LinqExpression.Property(param, nameof(CollectionCard.IsFoil)),
            LinqExpression.Constant(true)),
        "missing" => LinqExpression.Equal(
            LinqExpression.Property(param, nameof(CollectionCard.IsMissing)),
            LinqExpression.Constant(true)),
        "missingdb" => LinqExpression.Equal(
            LinqExpression.Property(param, nameof(CollectionCard.FlagReason)),
            LinqExpression.Constant((FlagReason?)FlagReason.MissingFromDatabase, typeof(FlagReason?))),
        _ => LinqExpression.Constant(true),
    };
}
```

Note: The `FlagReason` property is `FlagReason?` (nullable), so the constant must be cast to `FlagReason?` with the explicit type parameter.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~CollectionSortFilterTests" -v minimal`

Expected: All tests PASS (both existing and new).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/CardService.cs OmniCard.Tests/Services/CollectionSortFilterTests.cs
git commit -m "feat: add is:missingdb filter for collection search

Adds 'missingdb' to the is: filter in ApplyScryfallFilter so users
can find cards flagged as missing from the reference database.
Usage: is:missingdb in search or as a filter preset query."
```

---

### Task 5: Run Full Test Suite and Final Verification

**Files:**
- No new files — verification only

**Interfaces:**
- Consumes: All changes from Tasks 1-4

- [ ] **Step 1: Run the complete test suite**

Run: `dotnet test OmniCard.Tests -v minimal`

Expected: All tests PASS with no regressions.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build OmniCard.sln`

Expected: Build succeeds with no errors or warnings related to the changes.

- [ ] **Step 3: Commit (if any fixes were needed)**

Only if Steps 1-2 required fixes. Otherwise skip.
