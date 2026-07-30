# Scan Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture complete diagnostic data during card scanning — algorithm decisions, tie zone candidates, OCR results, and user actions — into an event log that can be exported in an AI-readable text format for match analysis.

**Architecture:** One new `ScanDiagnosticEvent` table stores timestamped events (ScanCompleted, UserFlagged, UserConfirmed, UserCorrected, UserUnflagged) with JSON payloads. A `MatchDiagnostics` class captures the algorithm's internal decision during `FindClosestMatch()` via a `LastMatchDiagnostics` property on `ICardGameService`. A new `IScanDiagnosticService` handles logging, export, and clear. The export renders events into a structured plaintext format optimized for AI consumption.

**Tech Stack:** WPF (.NET 10), EF Core 10 + SQLite, CommunityToolkit.Mvvm, System.Text.Json

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`
- MVVM pattern: ViewModels extend `OmniCard.Views.ViewModel`, use `[ObservableProperty]` and `[RelayCommand]`
- DB uses `IDbContextFactory<CollectionDbContext>` — short-lived contexts per operation
- Existing diagnostic tables (`FlagResolution`, `MismatchLog`) remain untouched
- `CardSevice` filename has a typo ("Sevice") — follow existing convention, do not rename
- `ICardGameService` interface is implemented by both `ScryfallService` (MTG) and `OptcgService` (One Piece) — both need `LastMatchDiagnostics`
- Tests use in-memory SQLite via `SqliteConnection("Data Source=:memory:")`
- The `FindClosestMatch` method runs on a scanner background thread; `LastMatchDiagnostics` must be thread-safe
- JSON serialization uses `System.Text.Json.JsonSerializer`

---

### Task 1: Create MatchDiagnostics Model and TieZoneCandidate

**Files:**
- Create: `OmniCard/Models/MatchDiagnostics.cs`

**Interfaces:**
- Produces: `MatchDiagnostics` class and `TieZoneCandidate` class — used by Tasks 2, 3, and 4

- [ ] **Step 1: Create MatchDiagnostics.cs**

Create `OmniCard/Models/MatchDiagnostics.cs`:

```csharp
namespace OmniCard.Models;

/// <summary>
/// Captures the internal decision data from FindClosestMatch for diagnostic logging.
/// </summary>
public class MatchDiagnostics
{
    /// <summary>Which phase decided the match: ExactCorrection, PHashConfident, OcrAssisted, ArtHashFallback, NoMatch.</summary>
    public string DecisionPhase { get; set; } = "NoMatch";

    public int PHashDistance { get; set; }
    public int? ArtHashDistance { get; set; }
    public List<TieZoneCandidate> TieZoneCandidates { get; set; } = [];

    // OCR data (copied from OcrMatchResult for self-contained diagnostics)
    public string? OcrRecognizedName { get; set; }
    public double? OcrNameConfidence { get; set; }
    public List<OcrSetDetection>? OcrDetectedSets { get; set; }

    // Set filter state
    public bool SetFilterActive { get; set; }
    public List<string>? ActiveSets { get; set; }
    public List<string>? PreferredSets { get; set; }
}

public class TieZoneCandidate
{
    public string CardId { get; set; } = "";
    public string Name { get; set; } = "";
    public string SetCode { get; set; } = "";
    public string CollectorNumber { get; set; } = "";
    public int PHashDistance { get; set; }
    public int? ArtHashDistance { get; set; }
    public int SetBonus { get; set; }
    public int FinalScore { get; set; }
    public bool Selected { get; set; }
}

public class OcrSetDetection
{
    public string SetCode { get; set; } = "";
    public double Confidence { get; set; }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Clean build.

- [ ] **Step 3: Commit**

```bash
git add OmniCard/Models/MatchDiagnostics.cs
git commit -m "feat: add MatchDiagnostics and TieZoneCandidate models"
```

---

### Task 2: Add LastMatchDiagnostics to ICardGameService and Implementations

**Files:**
- Modify: `OmniCard/Services/ICardGameService.cs`
- Modify: `OmniCard/Services/ScryfallService.cs`
- Modify: `OmniCard/Services/OptcgService.cs`

**Interfaces:**
- Consumes: `MatchDiagnostics` from Task 1
- Produces: `ICardGameService.LastMatchDiagnostics` property — used by Tasks 3 and 4

- [ ] **Step 1: Add property to ICardGameService interface**

In `OmniCard/Services/ICardGameService.cs`, add after line 6 (`CardGame Game { get; }`):

```csharp
    MatchDiagnostics? LastMatchDiagnostics { get; }
```

- [ ] **Step 2: Add backing field and property to ScryfallService**

In `OmniCard/Services/ScryfallService.cs`, add a field near the other cache fields (around line 95):

```csharp
    private MatchDiagnostics? _lastMatchDiagnostics;
    public MatchDiagnostics? LastMatchDiagnostics => _lastMatchDiagnostics;
```

- [ ] **Step 3: Instrument FindClosestMatch in ScryfallService**

This is the most involved step. Inside `FindClosestMatch()` (lines 108-462), add diagnostics capture at key points:

**At the start of the method** (after line 161, after local cache references are captured):

```csharp
        var diagnostics = new MatchDiagnostics
        {
            SetFilterActive = setFilter is not null,
            ActiveSets = setFilter?.ToList(),
            PreferredSets = preferredSets?.ToList(),
        };
```

**Phase 1 — exact correction return** (before line 175 `return correctedCard`):

```csharp
                    diagnostics.DecisionPhase = "ExactCorrection";
                    diagnostics.PHashDistance = 0;
                    diagnostics.TieZoneCandidates = [new TieZoneCandidate
                    {
                        CardId = correctedCard.GameSpecificId,
                        Name = correctedCard.Name,
                        SetCode = correctedCard.SetCode,
                        CollectorNumber = correctedCard.CollectorNumber,
                        PHashDistance = 0,
                        FinalScore = 0,
                        Selected = true,
                    }];
                    _lastMatchDiagnostics = diagnostics;
```

**After tie zone candidates are collected** (after line 216, after the `pHashCandidates` loop). Build the diagnostic tie zone list. This needs card name/set lookups, so use a helper lookup. Add after the pHashCandidates loop and before the preferred set bonus:

```csharp
        // Build diagnostic tie zone snapshot (before bonuses/penalties modify distances)
        var diagnosticCandidateIds = pHashCandidates.Select(c => c.Id).ToHashSet();
```

**After the art hash disambiguation selects a winner** (around line 314 after the else block that selects bestPHashId), capture the tie zone with final scores:

```csharp
        // Capture tie zone diagnostics with final adjusted distances
        foreach (var (id, dist) in pHashCandidates)
        {
            if (!diagnosticCandidateIds.Contains(id)) continue;
            var card = _readContext.Cards.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.Name, c.SetCode, c.CollectorNumber })
                .FirstOrDefault();
            if (card is null) continue;

            // Compute art hash distance for this candidate if available
            int? artDist = null;
            if (artHashes is not null && artHashCache.Count > 0)
            {
                var artLookup2 = artHashCache.FirstOrDefault(a => a.Id == id);
                if (artLookup2 != default)
                {
                    artDist = artHashes.Where(h => h != 0)
                        .Select(h => PerceptualHashService.HammingDistance(h, artLookup2.ArtHash))
                        .DefaultIfEmpty(int.MaxValue)
                        .Min();
                    if (artDist == int.MaxValue) artDist = null;
                }
            }

            var originalDist = hashCache.First(h => h.Id == id).Hash;
            var rawPHash = PerceptualHashService.HammingDistance(imageHash, originalDist);
            diagnostics.TieZoneCandidates.Add(new TieZoneCandidate
            {
                CardId = id.ToString(),
                Name = card.Name,
                SetCode = card.SetCode,
                CollectorNumber = card.CollectorNumber,
                PHashDistance = rawPHash,
                ArtHashDistance = artDist,
                SetBonus = dist - rawPHash,
                FinalScore = dist,
                Selected = id == bestPHashId,
            });
        }
```

**Phase 3 — confident hash return** (before line 368 `return confidentCard`):

```csharp
            diagnostics.DecisionPhase = "PHashConfident";
            diagnostics.PHashDistance = bestPHashDistance;
            _lastMatchDiagnostics = diagnostics;
```

**Phase 4 — OCR-assisted** (after line 418, after OCR scoring selects a winner):

```csharp
        diagnostics.DecisionPhase = "OcrAssisted";
```

**Populate OCR data on diagnostics** (just before the Phase 4 block, around line 371):

```csharp
        // Capture OCR diagnostics regardless of whether OCR phase runs
        if (ocrResult is not null)
        {
            diagnostics.OcrRecognizedName = ocrResult.RecognizedName;
            diagnostics.OcrNameConfidence = ocrResult.NameConfidence;
            if (ocrResult.CandidateSetCodes is { Count: > 0 })
            {
                diagnostics.OcrDetectedSets = ocrResult.CandidateSetCodes
                    .Select(s => new OcrSetDetection { SetCode = s, Confidence = ocrResult.SymbolConfidence })
                    .ToList();
            }
        }
```

**At every null/rejection return** (lines 184, 203, 426, 435): set `_lastMatchDiagnostics = diagnostics;` before each return.

**At the final successful return** (before line 449 `return new CardMatch`):

```csharp
        if (diagnostics.DecisionPhase == "NoMatch")
            diagnostics.DecisionPhase = bestPHashDistance <= maxDistance ? "PHashConfident" : "ArtHashFallback";
        diagnostics.PHashDistance = winnerDistance;
        // Mark the selected candidate
        foreach (var tc in diagnostics.TieZoneCandidates)
            tc.Selected = tc.CardId == bestPHashId.ToString();
        _lastMatchDiagnostics = diagnostics;
```

- [ ] **Step 4: Add stub property to OptcgService**

In `OmniCard/Services/OptcgService.cs`, add the property (OptcgService has a simpler matcher, so diagnostics are minimal):

```csharp
    public MatchDiagnostics? LastMatchDiagnostics { get; private set; }
```

At the start of its `FindClosestMatch` method, add:

```csharp
        LastMatchDiagnostics = new MatchDiagnostics { SetFilterActive = setFilter is not null };
```

And before each return point, set the decision phase appropriately (the OptcgService has a simpler 2-phase matcher).

- [ ] **Step 5: Build and run existing tests**

Run: `dotnet build && dotnet test OmniCard.Tests`
Expected: All existing tests pass. The diagnostics are side-effect only — they don't change any return values.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/ICardGameService.cs OmniCard/Services/ScryfallService.cs OmniCard/Services/OptcgService.cs
git commit -m "feat: capture MatchDiagnostics in FindClosestMatch"
```

---

### Task 3: Create ScanDiagnosticEvent Model and DB Table

**Files:**
- Create: `OmniCard.Shared/Models/ScanDiagnosticEvent.cs`
- Modify: `OmniCard.Shared/Data/CollectionDbContext.cs`
- Modify: `OmniCard/Services/CardSevice.cs` (add table creation SQL in constructor)

**Interfaces:**
- Produces: `ScanDiagnosticEvent` entity, `CollectionDbContext.ScanDiagnosticEvents` DbSet, DB table — used by Task 4

- [ ] **Step 1: Create the model**

Create `OmniCard.Shared/Models/ScanDiagnosticEvent.cs`:

```csharp
namespace OmniCard.Models;

public class ScanDiagnosticEvent
{
    public int Id { get; set; }
    public string SessionId { get; set; } = "";
    public ulong ScanHash { get; set; }
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Payload { get; set; } = "";
}
```

- [ ] **Step 2: Add DbSet to CollectionDbContext**

In `OmniCard.Shared/Data/CollectionDbContext.cs`, after line 11 (`DbSet<FlagResolution> FlagResolutions`), add:

```csharp
    public DbSet<ScanDiagnosticEvent> ScanDiagnosticEvents => Set<ScanDiagnosticEvent>();
```

In `OnModelCreating`, after the `FlagResolution` configuration block (after line 58), add:

```csharp
        modelBuilder.Entity<ScanDiagnosticEvent>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).ValueGeneratedOnAdd();
            e.HasIndex(d => d.ScanHash);
            e.HasIndex(d => d.SessionId);
            e.HasIndex(d => d.EventType);
        });
```

- [ ] **Step 3: Add table creation SQL in CardSevice constructor**

In `OmniCard/Services/CardSevice.cs`, after line 123 (the FlagResolutions CREATE TABLE), add:

```csharp
        ctx.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ScanDiagnosticEvents" (
                "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "SessionId" TEXT NOT NULL DEFAULT '',
                "ScanHash" INTEGER NOT NULL DEFAULT 0,
                "EventType" TEXT NOT NULL DEFAULT '',
                "Timestamp" TEXT NOT NULL DEFAULT '',
                "Payload" TEXT NOT NULL DEFAULT ''
            );
            """);

        // Add indexes if they don't exist (safe to repeat)
        ctx.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ScanDiagnosticEvents_ScanHash ON ScanDiagnosticEvents(ScanHash)");
        ctx.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ScanDiagnosticEvents_SessionId ON ScanDiagnosticEvents(SessionId)");
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build`
Expected: Clean build.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Shared/Models/ScanDiagnosticEvent.cs OmniCard.Shared/Data/CollectionDbContext.cs OmniCard/Services/CardSevice.cs
git commit -m "feat: add ScanDiagnosticEvent model and DB table"
```

---

### Task 4: Create ScanDiagnosticService

**Files:**
- Create: `OmniCard/Services/ScanDiagnosticService.cs`
- Create: `OmniCard.Tests/Services/ScanDiagnosticServiceTests.cs`

**Interfaces:**
- Consumes: `ScanDiagnosticEvent` from Task 3, `MatchDiagnostics` from Task 1, `CardMatch`, `ScannedCard`, `FlagReason`, `OcrMatchResult` from existing models
- Produces: `IScanDiagnosticService` with `LogScanCompleted()`, `LogUserFlagged()`, `LogUserConfirmed()`, `LogUserCorrected()`, `LogUserUnflagged()`, `ExportDiagnostics()`, `ClearDiagnostics()`, `GetEventCount()` — used by Tasks 5 and 6

- [ ] **Step 1: Write failing tests**

Create `OmniCard.Tests/Services/ScanDiagnosticServiceTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class ScanDiagnosticServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public ScanDiagnosticServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IScanDiagnosticService CreateService() =>
        new ScanDiagnosticService(new MockFactory(_options));

    [Fact]
    public void LogScanCompleted_CreatesEvent()
    {
        var service = CreateService();
        var match = new CardMatch
        {
            Name = "Lightning Bolt",
            SetCode = "m21",
            CollectorNumber = "199",
            GameSpecificId = "abc-123",
            Confidence = 87.5,
        };
        var diagnostics = new MatchDiagnostics
        {
            DecisionPhase = "PHashConfident",
            PHashDistance = 3,
            TieZoneCandidates =
            [
                new TieZoneCandidate { CardId = "abc-123", Name = "Lightning Bolt", SetCode = "m21", CollectorNumber = "199", PHashDistance = 3, FinalScore = -2, Selected = true },
                new TieZoneCandidate { CardId = "def-456", Name = "Lightning Bolt", SetCode = "2xm", CollectorNumber = "141", PHashDistance = 4, FinalScore = 4, Selected = false },
            ],
        };

        service.LogScanCompleted("session-1", 0xA3F7B2C1, match, diagnostics, [12345UL, 67890UL], null, FlagReason.None);

        Assert.Equal(1, service.GetEventCount());

        using var ctx = new CollectionDbContext(_options);
        var evt = ctx.ScanDiagnosticEvents.Single();
        Assert.Equal("ScanCompleted", evt.EventType);
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal(0xA3F7B2C1UL, evt.ScanHash);

        var payload = JsonDocument.Parse(evt.Payload);
        Assert.Equal("Lightning Bolt", payload.RootElement.GetProperty("matchedName").GetString());
        Assert.Equal("PHashConfident", payload.RootElement.GetProperty("decisionPhase").GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("tieZoneCandidates").GetArrayLength());
    }

    [Fact]
    public void LogUserCorrected_SetsWasInTieZone()
    {
        var service = CreateService();

        // First log a scan with a tie zone
        var match = new CardMatch { Name = "Bolt", SetCode = "m21", CollectorNumber = "199", GameSpecificId = "abc-123", Confidence = 80 };
        var diagnostics = new MatchDiagnostics
        {
            DecisionPhase = "PHashConfident",
            PHashDistance = 3,
            TieZoneCandidates =
            [
                new TieZoneCandidate { CardId = "abc-123", Name = "Bolt", SetCode = "m21", Selected = true },
                new TieZoneCandidate { CardId = "def-456", Name = "Bolt", SetCode = "2xm", Selected = false },
            ],
        };
        service.LogScanCompleted("s1", 0xAAAA, match, diagnostics, null, null, FlagReason.None);

        // Now correct to "def-456" which was in the tie zone
        var card = new ScannedCard { TempImagePath = "", Hash = 0xAAAA, Match = match, FlagReason = FlagReason.Manual };
        var newMatch = new CardMatch { Name = "Bolt", SetCode = "2xm", CollectorNumber = "141", GameSpecificId = "def-456", Confidence = 100 };
        service.LogUserCorrected(0xAAAA, card, newMatch);

        using var ctx = new CollectionDbContext(_options);
        var evt = ctx.ScanDiagnosticEvents.Where(e => e.EventType == "UserCorrected").Single();
        var payload = JsonDocument.Parse(evt.Payload);
        Assert.True(payload.RootElement.GetProperty("wasInTieZone").GetBoolean());
    }

    [Fact]
    public void LogUserCorrected_WasInTieZone_FalseWhenNotInTieZone()
    {
        var service = CreateService();

        var match = new CardMatch { Name = "Bolt", SetCode = "m21", GameSpecificId = "abc-123", Confidence = 80 };
        var diagnostics = new MatchDiagnostics
        {
            DecisionPhase = "PHashConfident",
            PHashDistance = 3,
            TieZoneCandidates = [new TieZoneCandidate { CardId = "abc-123", Selected = true }],
        };
        service.LogScanCompleted("s1", 0xBBBB, match, diagnostics, null, null, FlagReason.None);

        var card = new ScannedCard { TempImagePath = "", Hash = 0xBBBB, Match = match, FlagReason = FlagReason.Manual };
        var newMatch = new CardMatch { Name = "Other Card", SetCode = "leg", GameSpecificId = "xyz-999", Confidence = 100 };
        service.LogUserCorrected(0xBBBB, card, newMatch);

        using var ctx = new CollectionDbContext(_options);
        var evt = ctx.ScanDiagnosticEvents.Where(e => e.EventType == "UserCorrected").Single();
        var payload = JsonDocument.Parse(evt.Payload);
        Assert.False(payload.RootElement.GetProperty("wasInTieZone").GetBoolean());
    }

    [Fact]
    public void ClearDiagnostics_RemovesAllEvents()
    {
        var service = CreateService();
        service.LogScanCompleted("s1", 0x1111, null, null, null, null, FlagReason.NoMatch);
        service.LogScanCompleted("s1", 0x2222, null, null, null, null, FlagReason.NoMatch);
        Assert.Equal(2, service.GetEventCount());

        service.ClearDiagnostics();
        Assert.Equal(0, service.GetEventCount());
    }

    [Fact]
    public void LogUserFlagged_CreatesEvent()
    {
        var service = CreateService();
        var card = new ScannedCard
        {
            TempImagePath = "", Hash = 0xCCCC,
            Match = new CardMatch { Name = "Bolt", SetCode = "m21", GameSpecificId = "abc", Confidence = 85 },
        };
        service.LogScanCompleted("s1", 0xCCCC, card.Match, null, null, null, FlagReason.None);
        service.LogUserFlagged(0xCCCC, card);

        using var ctx = new CollectionDbContext(_options);
        var evt = ctx.ScanDiagnosticEvents.Where(e => e.EventType == "UserFlagged").Single();
        var payload = JsonDocument.Parse(evt.Payload);
        Assert.Equal("Manual", payload.RootElement.GetProperty("flagReason").GetString());
    }

    [Fact]
    public void GetEventCount_ReturnsCorrectCount()
    {
        var service = CreateService();
        Assert.Equal(0, service.GetEventCount());

        service.LogScanCompleted("s1", 0x1111, null, null, null, null, FlagReason.NoMatch);
        Assert.Equal(1, service.GetEventCount());
    }

    private class MockFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ScanDiagnosticServiceTests" --no-restore`
Expected: Build failure — `ScanDiagnosticService` does not exist.

- [ ] **Step 3: Implement ScanDiagnosticService**

Create `OmniCard/Services/ScanDiagnosticService.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IScanDiagnosticService
{
    void LogScanCompleted(string sessionId, ulong scanHash, CardMatch? match, MatchDiagnostics? diagnostics, ulong[]? artHashes, OcrMatchResult? ocrResult, FlagReason autoFlagReason);
    void LogUserFlagged(ulong scanHash, ScannedCard card);
    void LogUserConfirmed(ulong scanHash, ScannedCard card);
    void LogUserCorrected(ulong scanHash, ScannedCard card, CardMatch newMatch);
    void LogUserUnflagged(ulong scanHash, ScannedCard card, FlagReason previousReason);
    void ExportDiagnostics(string filePath);
    void ClearDiagnostics();
    int GetEventCount();
}

public class ScanDiagnosticService(IDbContextFactory<CollectionDbContext> dbContextFactory) : IScanDiagnosticService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public void LogScanCompleted(string sessionId, ulong scanHash, CardMatch? match, MatchDiagnostics? diagnostics, ulong[]? artHashes, OcrMatchResult? ocrResult, FlagReason autoFlagReason)
    {
        var payload = new Dictionary<string, object?>
        {
            ["matchedCardId"] = match?.GameSpecificId,
            ["matchedName"] = match?.Name,
            ["matchedSet"] = match?.SetCode,
            ["matchedNumber"] = match?.CollectorNumber,
            ["confidence"] = match?.Confidence,
            ["decisionPhase"] = diagnostics?.DecisionPhase ?? "NoMatch",
            ["pHashDistance"] = diagnostics?.PHashDistance ?? 0,
            ["artHashDistance"] = diagnostics?.ArtHashDistance,
            ["ocrRecognizedName"] = diagnostics?.OcrRecognizedName ?? ocrResult?.RecognizedName,
            ["ocrNameConfidence"] = diagnostics?.OcrNameConfidence ?? ocrResult?.NameConfidence,
            ["ocrDetectedSets"] = diagnostics?.OcrDetectedSets,
            ["ocrSymbolConfidence"] = ocrResult?.SymbolConfidence,
            ["setFilterActive"] = diagnostics?.SetFilterActive ?? false,
            ["activeSets"] = diagnostics?.ActiveSets,
            ["preferredSets"] = diagnostics?.PreferredSets,
            ["tieZoneCandidates"] = diagnostics?.TieZoneCandidates,
            ["artHashes"] = artHashes,
            ["autoFlagReason"] = autoFlagReason.ToString(),
        };

        LogEvent(sessionId, scanHash, "ScanCompleted", payload);
    }

    public void LogUserFlagged(ulong scanHash, ScannedCard card)
    {
        var sessionId = LookupSessionId(scanHash);
        var payload = new Dictionary<string, object?>
        {
            ["currentCardId"] = card.Match?.GameSpecificId,
            ["currentName"] = card.Match?.Name,
            ["currentSet"] = card.Match?.SetCode,
            ["currentConfidence"] = card.Match?.Confidence,
            ["flagReason"] = "Manual",
        };
        LogEvent(sessionId, scanHash, "UserFlagged", payload);
    }

    public void LogUserConfirmed(ulong scanHash, ScannedCard card)
    {
        var sessionId = LookupSessionId(scanHash);
        var payload = new Dictionary<string, object?>
        {
            ["confirmedCardId"] = card.Match?.GameSpecificId,
            ["confirmedName"] = card.Match?.Name,
            ["confirmedSet"] = card.Match?.SetCode,
            ["originalConfidence"] = card.Match?.Confidence,
        };
        LogEvent(sessionId, scanHash, "UserConfirmed", payload);
    }

    public void LogUserCorrected(ulong scanHash, ScannedCard card, CardMatch newMatch)
    {
        var sessionId = LookupSessionId(scanHash);
        var wasInTieZone = CheckWasInTieZone(scanHash, newMatch.GameSpecificId);
        var payload = new Dictionary<string, object?>
        {
            ["originalCardId"] = card.Match?.GameSpecificId,
            ["originalName"] = card.Match?.Name,
            ["originalSet"] = card.Match?.SetCode,
            ["originalConfidence"] = card.Match?.Confidence,
            ["correctedCardId"] = newMatch.GameSpecificId,
            ["correctedName"] = newMatch.Name,
            ["correctedSet"] = newMatch.SetCode,
            ["correctedNumber"] = newMatch.CollectorNumber,
            ["wasInTieZone"] = wasInTieZone,
        };
        LogEvent(sessionId, scanHash, "UserCorrected", payload);
    }

    public void LogUserUnflagged(ulong scanHash, ScannedCard card, FlagReason previousReason)
    {
        var sessionId = LookupSessionId(scanHash);
        var payload = new Dictionary<string, object?>
        {
            ["cardId"] = card.Match?.GameSpecificId,
            ["cardName"] = card.Match?.Name,
            ["previousFlagReason"] = previousReason.ToString(),
        };
        LogEvent(sessionId, scanHash, "UserUnflagged", payload);
    }

    public void ExportDiagnostics(string filePath)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var events = ctx.ScanDiagnosticEvents
            .AsNoTracking()
            .OrderBy(e => e.Timestamp)
            .ToList();

        var exporter = new DiagnosticExporter(events);
        File.WriteAllText(filePath, exporter.Render());
    }

    public void ClearDiagnostics()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.ScanDiagnosticEvents.RemoveRange(ctx.ScanDiagnosticEvents);
        ctx.SaveChanges();
    }

    public int GetEventCount()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.ScanDiagnosticEvents.Count();
    }

    private void LogEvent(string sessionId, ulong scanHash, string eventType, Dictionary<string, object?> payload)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.ScanDiagnosticEvents.Add(new ScanDiagnosticEvent
        {
            SessionId = sessionId,
            ScanHash = scanHash,
            EventType = eventType,
            Timestamp = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload, JsonOpts),
        });
        ctx.SaveChanges();
    }

    private string LookupSessionId(ulong scanHash)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var scanEvent = ctx.ScanDiagnosticEvents
            .AsNoTracking()
            .Where(e => e.ScanHash == (long)(long)scanHash && e.EventType == "ScanCompleted")
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();
        return scanEvent?.SessionId ?? "orphaned";
    }

    private bool CheckWasInTieZone(ulong scanHash, string correctedCardId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var scanEvent = ctx.ScanDiagnosticEvents
            .AsNoTracking()
            .Where(e => e.ScanHash == (long)(long)scanHash && e.EventType == "ScanCompleted")
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();

        if (scanEvent is null) return false;

        try
        {
            var payload = JsonDocument.Parse(scanEvent.Payload);
            if (payload.RootElement.TryGetProperty("tieZoneCandidates", out var candidates))
            {
                foreach (var candidate in candidates.EnumerateArray())
                {
                    if (candidate.TryGetProperty("cardId", out var cardId) &&
                        cardId.GetString() == correctedCardId)
                        return true;
                }
            }
        }
        catch { }

        return false;
    }
}
```

Note: The `ScanHash` column is stored as `INTEGER` (long) in SQLite because SQLite doesn't support unsigned 64-bit integers. The `ulong` value is stored via EF Core's default conversion. The `(long)(long)scanHash` cast in LINQ queries ensures correct comparison. This matches the pattern used by `MismatchLog.ScanHash`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ScanDiagnosticServiceTests" --no-restore`
Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Services/ScanDiagnosticService.cs OmniCard.Tests/Services/ScanDiagnosticServiceTests.cs
git commit -m "feat: implement ScanDiagnosticService with event logging and tests"
```

---

### Task 5: Create DiagnosticExporter (AI-Readable Export Format)

**Files:**
- Create: `OmniCard/Services/DiagnosticExporter.cs`
- Create: `OmniCard.Tests/Services/DiagnosticExportTests.cs`

**Interfaces:**
- Consumes: `ScanDiagnosticEvent` from Task 3
- Produces: `DiagnosticExporter.Render()` returning the full export string — called by `ScanDiagnosticService.ExportDiagnostics()` from Task 4

- [ ] **Step 1: Write failing tests**

Create `OmniCard.Tests/Services/DiagnosticExportTests.cs`:

```csharp
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class DiagnosticExportTests
{
    [Fact]
    public void EmptyExport_HasHeaderAndZeroCounts()
    {
        var exporter = new DiagnosticExporter([]);
        var output = exporter.Render();

        Assert.Contains("=== SCAN DIAGNOSTIC EXPORT ===", output);
        Assert.Contains("Total Sessions: 0", output);
        Assert.Contains("Total Events: 0", output);
        Assert.Contains("Total Scans: 0", output);
    }

    [Fact]
    public void SingleScan_NoUserAction_ShowsOutcome()
    {
        var events = new List<ScanDiagnosticEvent>
        {
            new()
            {
                SessionId = "session-1",
                ScanHash = 0xAABBCCDD,
                EventType = "ScanCompleted",
                Timestamp = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                Payload = """{"matchedName":"Lightning Bolt","matchedSet":"m21","matchedNumber":"199","confidence":87.5,"decisionPhase":"PHashConfident","pHashDistance":3,"autoFlagReason":"None","tieZoneCandidates":[{"cardId":"abc","name":"Lightning Bolt","set":"m21","number":"199","pHashDist":3,"finalScore":-2,"selected":true}]}""",
            }
        };
        var output = new DiagnosticExporter(events).Render();

        Assert.Contains("SESSION: session-1", output);
        Assert.Contains("scan_hash=0x00000000AABBCCDD", output);
        Assert.Contains("Decision: PHashConfident", output);
        Assert.Contains("Lightning Bolt | m21 #199", output);
        Assert.Contains("Confidence: 87.5%", output);
        Assert.Contains("[SELECTED]", output);
        Assert.Contains("OUTCOME: No user action", output);
        Assert.Contains("Auto-Accepted (no user action): 1", output);
    }

    [Fact]
    public void CorrectedScan_ShowsUserActions()
    {
        var events = new List<ScanDiagnosticEvent>
        {
            new()
            {
                SessionId = "s1", ScanHash = 0x1111, EventType = "ScanCompleted",
                Timestamp = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                Payload = """{"matchedName":"Wrong Card","matchedSet":"m21","confidence":90,"decisionPhase":"PHashConfident","pHashDistance":2,"autoFlagReason":"None","tieZoneCandidates":[]}""",
            },
            new()
            {
                SessionId = "s1", ScanHash = 0x1111, EventType = "UserFlagged",
                Timestamp = new DateTime(2026, 7, 6, 10, 5, 0, DateTimeKind.Utc),
                Payload = """{"currentName":"Wrong Card","flagReason":"Manual"}""",
            },
            new()
            {
                SessionId = "s1", ScanHash = 0x1111, EventType = "UserCorrected",
                Timestamp = new DateTime(2026, 7, 6, 10, 5, 30, DateTimeKind.Utc),
                Payload = """{"originalName":"Wrong Card","originalSet":"m21","originalConfidence":90,"correctedName":"Right Card","correctedSet":"2xm","correctedNumber":"42","wasInTieZone":false}""",
            },
        };
        var output = new DiagnosticExporter(events).Render();

        Assert.Contains("USER FLAGGED", output);
        Assert.Contains("USER CORRECTED", output);
        Assert.Contains("Was: Wrong Card", output);
        Assert.Contains("Now: Right Card", output);
        Assert.Contains("Correct card was in tie zone: NO", output);
        Assert.Contains("OUTCOME: Corrected", output);
        Assert.Contains("User Corrected: 1", output);
    }

    [Fact]
    public void SummaryStatistics_CountsCorrectly()
    {
        var events = new List<ScanDiagnosticEvent>
        {
            MakeScanEvent("s1", 0x1111, "Card A", 90, "PHashConfident"),
            MakeScanEvent("s1", 0x2222, "Card B", 45, "OcrAssisted"),
            new() { SessionId = "s1", ScanHash = 0x2222, EventType = "UserConfirmed", Timestamp = DateTime.UtcNow, Payload = """{"confirmedName":"Card B"}""" },
            MakeScanEvent("s1", 0x3333, "Card C", 85, "PHashConfident"),
            new() { SessionId = "s1", ScanHash = 0x3333, EventType = "UserCorrected", Timestamp = DateTime.UtcNow, Payload = """{"originalConfidence":85,"correctedName":"Card D","wasInTieZone":true}""" },
        };
        var output = new DiagnosticExporter(events).Render();

        Assert.Contains("Total Scans: 3", output);
        Assert.Contains("Auto-Accepted (no user action): 1", output);
        Assert.Contains("User Confirmed: 1", output);
        Assert.Contains("User Corrected: 1", output);
        Assert.Contains("PHashConfident: 2", output);
        Assert.Contains("OcrAssisted: 1", output);
    }

    private static ScanDiagnosticEvent MakeScanEvent(string session, ulong hash, string name, double confidence, string phase) =>
        new()
        {
            SessionId = session,
            ScanHash = hash,
            EventType = "ScanCompleted",
            Timestamp = DateTime.UtcNow,
            Payload = $$"""{"matchedName":"{{name}}","confidence":{{confidence}},"decisionPhase":"{{phase}}","pHashDistance":3,"autoFlagReason":"None","tieZoneCandidates":[]}""",
        };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DiagnosticExportTests" --no-restore`
Expected: Build failure — `DiagnosticExporter` does not exist.

- [ ] **Step 3: Implement DiagnosticExporter**

Create `OmniCard/Services/DiagnosticExporter.cs`:

```csharp
using System.Reflection;
using System.Text;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Services;

public class DiagnosticExporter(List<ScanDiagnosticEvent> events)
{
    public string Render()
    {
        var sb = new StringBuilder();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        var sessions = events.GroupBy(e => e.SessionId).OrderBy(g => g.Min(e => e.Timestamp)).ToList();
        var scanEvents = events.Where(e => e.EventType == "ScanCompleted").ToList();

        // Header
        sb.AppendLine("=== SCAN DIAGNOSTIC EXPORT ===");
        sb.AppendLine($"App Version: {version}");
        sb.AppendLine($"Exported: {DateTime.UtcNow:O}");
        sb.AppendLine($"Total Sessions: {sessions.Count}");
        sb.AppendLine($"Total Events: {events.Count}");
        sb.AppendLine();

        // Track stats
        int totalScans = 0, autoAccepted = 0, confirmed = 0, corrected = 0, correctedInTieZone = 0, correctedNotInTieZone = 0, flaggedThenUnflagged = 0;
        double confidenceSum = 0, correctedConfidenceSum = 0;
        var phaseBreakdown = new Dictionary<string, int>();
        var highConfMismatches = new List<string>();

        foreach (var session in sessions)
        {
            var sessionEvents = session.OrderBy(e => e.Timestamp).ToList();
            var scansInSession = sessionEvents.Where(e => e.EventType == "ScanCompleted").ToList();
            var flagsInSession = sessionEvents.Count(e => e.EventType == "UserFlagged");
            var correctionsInSession = sessionEvents.Count(e => e.EventType == "UserCorrected");

            sb.AppendLine("================================================================");
            sb.AppendLine($"SESSION: {session.Key}");
            sb.AppendLine($"Started: {sessionEvents.First().Timestamp:O}");
            sb.AppendLine($"Cards Scanned: {scansInSession.Count}");
            sb.AppendLine($"Flags Raised: {flagsInSession}");
            sb.AppendLine($"Corrections Made: {correctionsInSession}");
            sb.AppendLine("================================================================");
            sb.AppendLine();

            // Group events by scan hash
            var cardGroups = sessionEvents.GroupBy(e => e.ScanHash).ToList();

            foreach (var cardGroup in cardGroups)
            {
                var cardEvents = cardGroup.OrderBy(e => e.Timestamp).ToList();
                var scanEvt = cardEvents.FirstOrDefault(e => e.EventType == "ScanCompleted");
                if (scanEvt is null) continue;

                totalScans++;
                var scanPayload = JsonDocument.Parse(scanEvt.Payload);

                sb.AppendLine($"--- CARD: scan_hash=0x{cardGroup.Key:X16} ---");
                RenderScanResult(sb, scanEvt, scanPayload);

                var confidence = GetDouble(scanPayload, "confidence");
                if (confidence.HasValue) confidenceSum += confidence.Value;

                var phase = GetString(scanPayload, "decisionPhase") ?? "Unknown";
                phaseBreakdown[phase] = phaseBreakdown.GetValueOrDefault(phase) + 1;

                // Render user actions
                var userActions = cardEvents.Where(e => e.EventType != "ScanCompleted").ToList();
                foreach (var action in userActions)
                {
                    var actionPayload = JsonDocument.Parse(action.Payload);
                    RenderUserAction(sb, action, actionPayload);
                }

                // Determine outcome
                var hasConfirm = userActions.Any(e => e.EventType == "UserConfirmed");
                var hasCorrect = userActions.Any(e => e.EventType == "UserCorrected");
                var hasFlagOnly = userActions.Any(e => e.EventType == "UserFlagged") && !hasCorrect && !hasConfirm;
                var hasUnflag = userActions.Any(e => e.EventType == "UserUnflagged");

                string outcome;
                if (hasCorrect)
                {
                    outcome = "Corrected";
                    corrected++;
                    if (confidence.HasValue) correctedConfidenceSum += confidence.Value;

                    var correctEvt = userActions.First(e => e.EventType == "UserCorrected");
                    var correctPayload = JsonDocument.Parse(correctEvt.Payload);
                    var wasInTieZone = GetBool(correctPayload, "wasInTieZone");
                    if (wasInTieZone == true) correctedInTieZone++;
                    else correctedNotInTieZone++;

                    if (confidence is >= 80)
                    {
                        var origName = GetString(scanPayload, "matchedName");
                        var origSet = GetString(scanPayload, "matchedSet");
                        var origNum = GetString(scanPayload, "matchedNumber");
                        var corrName = GetString(correctPayload, "correctedName");
                        var corrSet = GetString(correctPayload, "correctedSet");
                        var corrNum = GetString(correctPayload, "correctedNumber");
                        var tieStr = wasInTieZone == true ? "IN tie zone" : "NOT in tie zone";
                        highConfMismatches.Add($"  {origName} | {origSet} #{origNum} -> {corrName} | {corrSet} #{corrNum} | Was {confidence:F1}% confident | {tieStr}");
                    }
                }
                else if (hasConfirm) { outcome = "Confirmed"; confirmed++; }
                else if (hasFlagOnly && hasUnflag) { outcome = "Flagged then unflagged"; flaggedThenUnflagged++; }
                else { outcome = "No user action"; autoAccepted++; }

                sb.AppendLine($"  OUTCOME: {outcome}");
                sb.AppendLine();
            }
        }

        // Summary
        sb.AppendLine("=== SUMMARY STATISTICS ===");
        sb.AppendLine($"Total Scans: {totalScans}");
        sb.AppendLine($"Auto-Accepted (no user action): {autoAccepted}");
        sb.AppendLine($"User Confirmed: {confirmed}");
        sb.AppendLine($"User Corrected: {corrected}");
        if (corrected > 0)
        {
            var tieZonePct = corrected > 0 ? correctedInTieZone * 100.0 / corrected : 0;
            var notTieZonePct = corrected > 0 ? correctedNotInTieZone * 100.0 / corrected : 0;
            sb.AppendLine($"  Correct card was in tie zone: {correctedInTieZone} ({tieZonePct:F0}%)");
            sb.AppendLine($"  Correct card was NOT in tie zone: {correctedNotInTieZone} ({notTieZonePct:F0}%)");
        }
        sb.AppendLine($"User Flagged then Unflagged: {flaggedThenUnflagged}");
        sb.AppendLine($"Average Confidence (all scans): {(totalScans > 0 ? confidenceSum / totalScans : 0):F1}%");
        sb.AppendLine($"Average Confidence (corrected scans): {(corrected > 0 ? correctedConfidenceSum / corrected : 0):F1}%");
        sb.AppendLine("Decision Phase Breakdown:");
        foreach (var (phase, count) in phaseBreakdown.OrderBy(p => p.Key))
            sb.AppendLine($"  {phase}: {count}");
        sb.AppendLine($"High-Confidence Mismatches (>=80% confidence, user corrected): {highConfMismatches.Count}");
        foreach (var line in highConfMismatches)
            sb.AppendLine(line);

        return sb.ToString();
    }

    private static void RenderScanResult(StringBuilder sb, ScanDiagnosticEvent evt, JsonDocument payload)
    {
        var phase = GetString(payload, "decisionPhase") ?? "Unknown";
        var name = GetString(payload, "matchedName") ?? "(no match)";
        var set = GetString(payload, "matchedSet") ?? "???";
        var number = GetString(payload, "matchedNumber") ?? "?";
        var confidence = GetDouble(payload, "confidence");
        var pHashDist = GetInt(payload, "pHashDistance");
        var artHashDist = GetInt(payload, "artHashDistance");
        var ocrName = GetString(payload, "ocrRecognizedName");
        var ocrConf = GetDouble(payload, "ocrNameConfidence");
        var setFilterActive = GetBool(payload, "setFilterActive");
        var autoFlag = GetString(payload, "autoFlagReason") ?? "None";

        sb.AppendLine($"SCAN RESULT [{evt.Timestamp:O}]");
        sb.AppendLine($"  Decision: {phase}");
        sb.AppendLine($"  Match: {name} | {set} #{number} | Confidence: {confidence?.ToString("F1") ?? "N/A"}%");
        sb.AppendLine($"  pHash Distance: {pHashDist?.ToString() ?? "N/A"} | Art Hash Distance: {artHashDist?.ToString() ?? "N/A"}");

        if (ocrName is not null)
            sb.AppendLine($"  OCR Name: \"{ocrName}\" (confidence: {ocrConf?.ToString("F2") ?? "N/A"})");

        // OCR sets
        if (payload.RootElement.TryGetProperty("ocrDetectedSets", out var ocrSets) && ocrSets.ValueKind == JsonValueKind.Array)
        {
            var setStrs = new List<string>();
            foreach (var s in ocrSets.EnumerateArray())
            {
                var sc = s.TryGetProperty("setCode", out var scv) ? scv.GetString() :
                         s.TryGetProperty("set", out var sv) ? sv.GetString() : null;
                var conf = s.TryGetProperty("confidence", out var cv) ? cv.GetDouble().ToString("F2") : "?";
                if (sc is not null) setStrs.Add($"{sc} ({conf})");
            }
            if (setStrs.Count > 0)
                sb.AppendLine($"  OCR Sets Detected: {string.Join(", ", setStrs)}");
        }

        sb.AppendLine($"  Set Filter: {(setFilterActive == true ? "ON" : "OFF")}");

        if (payload.RootElement.TryGetProperty("activeSets", out var activeSets) && activeSets.ValueKind == JsonValueKind.Array)
        {
            var sets = activeSets.EnumerateArray().Select(s => s.GetString()).Where(s => s is not null);
            sb.AppendLine($"    Active Sets: {string.Join(", ", sets)}");
        }
        if (payload.RootElement.TryGetProperty("preferredSets", out var prefSets) && prefSets.ValueKind == JsonValueKind.Array)
        {
            var sets = prefSets.EnumerateArray().Select(s => s.GetString()).Where(s => s is not null);
            sb.AppendLine($"    Preferred: {string.Join(", ", sets)}");
        }

        sb.AppendLine($"  Auto-Flag: {autoFlag}");

        // Tie zone candidates
        if (payload.RootElement.TryGetProperty("tieZoneCandidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            var candidateList = candidates.EnumerateArray().ToList();
            sb.AppendLine($"  Tie Zone ({candidateList.Count} candidates):");
            foreach (var c in candidateList)
            {
                var cName = c.TryGetProperty("name", out var cn) ? cn.GetString() : "?";
                var cSet = c.TryGetProperty("set", out var cs) ? cs.GetString() :
                           c.TryGetProperty("setCode", out var csc) ? csc.GetString() : "?";
                var cNum = c.TryGetProperty("number", out var cnu) ? cnu.GetString() :
                           c.TryGetProperty("collectorNumber", out var ccn) ? ccn.GetString() : "?";
                var cPHash = c.TryGetProperty("pHashDist", out var cp) ? cp.GetInt32().ToString() :
                             c.TryGetProperty("pHashDistance", out var cpd) ? cpd.GetInt32().ToString() : "?";
                var cArt = c.TryGetProperty("artHashDist", out var ca) ? ca.GetInt32().ToString() :
                           c.TryGetProperty("artHashDistance", out var cad) ? cad.GetInt32().ToString() : "N/A";
                var cBonus = c.TryGetProperty("setBonus", out var cb) ? cb.GetInt32().ToString() : "0";
                var cScore = c.TryGetProperty("finalScore", out var cf) ? cf.GetInt32().ToString() : "?";
                var selected = c.TryGetProperty("selected", out var sel) && sel.GetBoolean();

                var prefix = selected ? "    > [SELECTED] " : "      ";
                sb.AppendLine($"{prefix}{cName} | {cSet} #{cNum} | pHash: {cPHash}, artHash: {cArt}, setBonus: {cBonus}, finalScore: {cScore}");
            }
        }
    }

    private static void RenderUserAction(StringBuilder sb, ScanDiagnosticEvent evt, JsonDocument payload)
    {
        switch (evt.EventType)
        {
            case "UserFlagged":
                var flagReason = GetString(payload, "flagReason") ?? "Manual";
                var flagName = GetString(payload, "currentName") ?? "?";
                var flagConf = GetDouble(payload, "currentConfidence");
                sb.AppendLine($"  USER FLAGGED [{evt.Timestamp:O}]");
                sb.AppendLine($"    Reason: {flagReason} | Card at time: {flagName} | Confidence: {flagConf?.ToString("F1") ?? "N/A"}%");
                break;

            case "UserConfirmed":
                var confName = GetString(payload, "confirmedName") ?? "?";
                var confSet = GetString(payload, "confirmedSet") ?? "?";
                sb.AppendLine($"  USER CONFIRMED [{evt.Timestamp:O}]");
                sb.AppendLine($"    Confirmed: {confName} | {confSet}");
                break;

            case "UserCorrected":
                var origName = GetString(payload, "originalName") ?? "?";
                var origSet = GetString(payload, "originalSet") ?? "?";
                var origConf = GetDouble(payload, "originalConfidence");
                var corrName = GetString(payload, "correctedName") ?? "?";
                var corrSet = GetString(payload, "correctedSet") ?? "?";
                var corrNum = GetString(payload, "correctedNumber") ?? "?";
                var wasInTieZone = GetBool(payload, "wasInTieZone");
                sb.AppendLine($"  USER CORRECTED [{evt.Timestamp:O}]");
                sb.AppendLine($"    Was: {origName} | {origSet} ({origConf?.ToString("F1") ?? "N/A"}%)");
                sb.AppendLine($"    Now: {corrName} | {corrSet} #{corrNum}");
                sb.AppendLine($"    Correct card was in tie zone: {(wasInTieZone == true ? "YES" : "NO")}");
                break;

            case "UserUnflagged":
                var prevReason = GetString(payload, "previousFlagReason") ?? "?";
                sb.AppendLine($"  USER UNFLAGGED [{evt.Timestamp:O}]");
                sb.AppendLine($"    Previous reason: {prevReason}");
                break;
        }
    }

    private static string? GetString(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetDouble(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? GetInt(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static bool? GetBool(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DiagnosticExportTests" --no-restore`
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Services/DiagnosticExporter.cs OmniCard.Tests/Services/DiagnosticExportTests.cs
git commit -m "feat: implement AI-readable diagnostic export format"
```

---

### Task 6: Wire Up Event Logging and UI

**Files:**
- Modify: `OmniCard/App.xaml.cs` (DI registration)
- Modify: `OmniCard/Services/CardSevice.cs` (session tracking + LogScanCompleted)
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (user action logging + export/clear UI)
- Modify: `OmniCard/Views/Root/RootView.xaml` (menu items)

**Interfaces:**
- Consumes: `IScanDiagnosticService` from Task 4, `ICardGameService.LastMatchDiagnostics` from Task 2
- Produces: Fully wired diagnostic system — events logged at all capture points, export/clear accessible from UI

- [ ] **Step 1: Register ScanDiagnosticService in DI**

In `OmniCard/App.xaml.cs`, in the singleton services section (near line 68 where `ICardService` is registered), add:

```csharp
            services.AddSingleton<IScanDiagnosticService, ScanDiagnosticService>();
```

- [ ] **Step 2: Add session tracking and LogScanCompleted to CardSevice**

In `OmniCard/Services/CardSevice.cs`:

Add the diagnostic service to the constructor parameters and field:

```csharp
    private readonly IScanDiagnosticService _diagnosticService;
    private string _currentSessionId = Guid.NewGuid().ToString();
```

Update the constructor to accept `IScanDiagnosticService diagnosticService` and assign `_diagnosticService = diagnosticService;`.

Add a method to generate a new session (called when scanning starts):

```csharp
    public void StartNewDiagnosticSession() => _currentSessionId = Guid.NewGuid().ToString();
```

Add `StartNewDiagnosticSession()` to the `ICardService` interface.

In `AddFromStream()`, after the auto-flag logic (after line 291 where `scannedCard` is created), before the BeginInvoke call, add:

```csharp
        // Log diagnostic event
        try
        {
            var lastDiag = _gameServices.TryGetValue(game, out var gs) ? gs.LastMatchDiagnostics : null;
            _diagnosticService.LogScanCompleted(_currentSessionId, hash, match, lastDiag, artHashes, null, flagReason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log scan diagnostic event");
        }
```

Also inside the `BeginInvoke` callback, after the OCR re-match (around line 334, after the `scannedCard.FlagReason = FlagReason.None;` block), add a second diagnostic log for the OCR-improved match:

```csharp
                        // Update diagnostic with OCR-improved match
                        try
                        {
                            var ocrDiag = _gameServices.TryGetValue(ocrGame, out var gs2) ? gs2.LastMatchDiagnostics : null;
                            _diagnosticService.LogScanCompleted(_currentSessionId, capturedHash, ocrMatch, ocrDiag, scannedCard.ArtHashes, ocrResult, scannedCard.FlagReason);
                        }
                        catch { }
```

- [ ] **Step 3: Add user action logging to RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, add `IScanDiagnosticService` to the constructor and store as a field `_diagnosticService`.

**In ToggleFlag() (line 637):**

After `card.FlagReason = FlagReason.Manual;` (line 655), add:

```csharp
            try { _diagnosticService.LogUserFlagged(card.Hash, card); } catch { }
```

After the unflag block (after line 651 `card.FlagReason = FlagReason.None;`), add:

```csharp
            try { _diagnosticService.LogUserUnflagged(card.Hash, card, card.FlagFix?.OriginalFlagReason ?? FlagReason.Manual); } catch { }
```

**In AssignMatch() (line 953):**

After `card.Match = newMatch;` (line 978), add:

```csharp
                try { _diagnosticService.LogUserCorrected(card.Hash, card, newMatch); } catch { }
```

**In ConfirmMatch() (line 1005):**

After the RecordCorrection call (around line 1013), add:

```csharp
        try { _diagnosticService.LogUserConfirmed(card.Hash, card); } catch { }
```

**In SelectedPrinting setter (line 704):**

After `card.Match = value;` (line 738), add:

```csharp
                    try { _diagnosticService.LogUserCorrected(card.Hash, card, value); } catch { }
```

- [ ] **Step 4: Update ClearDiagnosticLogs to also clear the new table**

In `CardSevice.ClearDiagnosticLogs()` (line 505), add after the existing clear:

```csharp
        var diagnosticCount = context.ScanDiagnosticEvents.Count();
        context.ScanDiagnosticEvents.RemoveRange(context.ScanDiagnosticEvents);
        context.SaveChanges();
```

Update the return type and message to include the diagnostic count. Update the method signature to return `(int FlagResolutions, int MismatchLogs, int DiagnosticEvents)`.

Update `ICardService` interface to match the new return type.

In `RootViewModel.ClearDiagnosticLogs()`, update the message format to include diagnostic event count.

- [ ] **Step 5: Add Export button to RootViewModel and UI**

In `RootViewModel`, add:

```csharp
    [ObservableProperty]
    public partial int DiagnosticEventCount { get; set; }

    [RelayCommand]
    public void ExportDiagnostics()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = $"omnicard-diagnostics-{DateTime.Now:yyyy-MM-dd}",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _diagnosticService.ExportDiagnostics(dialog.FileName);
                Message = $"Diagnostics exported to {Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex)
            {
                Message = $"Export failed: {ex.Message}";
            }
        }
    }

    public void RefreshDiagnosticCount()
    {
        try { DiagnosticEventCount = _diagnosticService.GetEventCount(); } catch { }
    }
```

Call `RefreshDiagnosticCount()` after scan additions and after clear.

In `RootView.xaml`, in the Tools menu (near line 197 where "Clear Diagnostic Logs" is), add:

```xml
<MenuItem Header="Export _Diagnostics..."
          Command="{Binding ViewModel.ExportDiagnosticsCommand}"/>
```

And add a status display somewhere visible (e.g., near the scan stats):

```xml
<TextBlock Text="{Binding ViewModel.DiagnosticEventCount, StringFormat='{}{0} diagnostic events'}"
           FontSize="11" FontStyle="Italic"
           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
```

- [ ] **Step 6: Call StartNewDiagnosticSession when scanning starts**

In `ScannerService` or wherever scan batches are initiated, call `CardService.StartNewDiagnosticSession()`. Search for where `ScannerService.StartScanning()` or equivalent is called and add the session start there.

- [ ] **Step 7: Build and run all tests**

Run: `dotnet build && dotnet test OmniCard.Tests`
Expected: All tests pass. No existing behavior changed.

- [ ] **Step 8: Commit**

```bash
git add OmniCard/App.xaml.cs OmniCard/Services/CardSevice.cs OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/RootView.xaml OmniCard/Services/ScannerService.cs
git commit -m "feat: wire up diagnostic event logging and export UI"
```
