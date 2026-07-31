# Mobile Web Camera Card Scanner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the loop on the existing phone-camera scan pipeline so the phone shows the matched card (or a correction search) instead of a bare "Sent!" status.

**Architecture:** `POST /api/scan` becomes a synchronous request/response: `ScanController` uses a SignalR "client result" (`IHubContext<T>.Clients.Client(id).InvokeAsync<TResult>`) to invoke a method on the desktop app's existing hub connection and awaits the match. The desktop's `CardService.AddFromStream` gains an optional per-call game override (driven by a new phone-side game picker) and now returns the `ScannedCard` it produces. A new read-only `/api/scan/search` endpoint reuses `ICardGameService.SearchCards` directly from Web; a `/api/scan/correct` endpoint relays the picked correction back to desktop (writes must go through desktop since Web's DBs are read-only).

**Tech Stack:** ASP.NET Core Razor Pages + SignalR (OmniCard.Web), WPF desktop app (OmniCard), OmniCard.Collection/OmniCard.Shared, xUnit + Moq (OmniCard.Tests), vanilla JS/CSS (no new client libraries).

## Global Constraints

- No new client-side JS libraries — Scan.cshtml stays plain JS/CSS (PRD §4, "lightweight implementation").
- No DB schema changes; a phone scan lands in the desktop's existing `ScannedCards` review queue exactly like a TWAIN scan, never auto-committed to inventory (confirmed with user).
- `OmniCard.Web`'s DB contexts are opened `Mode=ReadOnly` — any write (`RecordCorrection`) must be relayed to and executed by the desktop process, never attempted directly from Web.
- Matching stays single-game only — never falls back across games (existing `CardService.FindBestMatch` behavior, unchanged).
- `ulong` scan hashes must be carried through JSON as strings, not numbers — JS `Number` only safely represents 53 bits and pHash values use the full 64-bit range (silent precision loss would break the hash-based correction lookup).

---

## Task 1: Shared DTOs

**Files:**
- Create: `OmniCard.Shared/Models/ScanResultDto.cs`
- Create: `OmniCard.Shared/Models/ScanCorrectionDto.cs`

**Interfaces:**
- Produces: `OmniCard.Models.ScanResultDto` and `OmniCard.Models.ScanCorrectionDto`, consumed by Task 4 (`ScanController`) and Task 5 (`WebScannerService`).

Both projects (`OmniCard.Web` and `OmniCard`) already reference `OmniCard.Shared`, so these plain data classes are visible to both without new project references.

- [ ] **Step 1: Create `ScanResultDto`**

```csharp
// OmniCard.Shared/Models/ScanResultDto.cs
namespace OmniCard.Models;

public class ScanResultDto
{
    public bool Matched { get; init; }
    public string? Name { get; init; }
    public string? SetName { get; init; }
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public string? Rarity { get; init; }
    public string? ImageUri { get; init; }
    public double? Confidence { get; init; }

    /// <summary>Decimal string of the scan's 64-bit pHash — kept as a string so it round-trips
    /// through JSON/JS without losing precision (JS numbers only safely hold 53 bits).</summary>
    public string ScanHash { get; init; } = "";

    public CardGame Game { get; init; }

    /// <summary>Set when the desktop app isn't connected or the round trip failed/timed out.
    /// The phone always has a screen to show, never a hung spinner.</summary>
    public string? Error { get; init; }
}
```

- [ ] **Step 2: Create `ScanCorrectionDto`**

```csharp
// OmniCard.Shared/Models/ScanCorrectionDto.cs
namespace OmniCard.Models;

public class ScanCorrectionDto
{
    public string ScanHash { get; init; } = "";
    public CardGame Game { get; init; }
    public string GameSpecificId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string CollectorNumber { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string? ImageUri { get; init; }
}
```

- [ ] **Step 3: Build to confirm both projects compile**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeded, no errors.

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Shared/Models/ScanResultDto.cs OmniCard.Shared/Models/ScanCorrectionDto.cs
git commit -m "feat(web-scanner): add ScanResultDto and ScanCorrectionDto"
```

---

## Task 2: `CardService` per-call game override

**Files:**
- Modify: `OmniCard.Shared/Interfaces/ICardService.cs`
- Modify: `OmniCard.Collection/CardService.cs`
- Modify: `OmniCard.Web/Services/WebCardService.cs`
- Modify: `OmniCard.Tests/Fakes/ImportFakes.cs`
- Modify: `OmniCard.Tests/Services/DecklistMatchingTests.cs`
- Modify: `OmniCard.Tests/Services/ListServiceTests.cs`
- Test: `OmniCard.Tests/Services/FallbackMatchingTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ICardService.FindBestMatch(..., CardGame? gameOverride = null)` and `ICardService.AddFromStream(Stream stream, CardGame? gameOverride = null) : ScannedCard` — consumed by Task 5 (`WebScannerService`).

The phone gets its own game picker (confirmed with user) independent of the desktop's shared `SelectedGame` toolbar toggle, so a single scan call must be able to target a specific game without mutating that shared, singleton-scoped property (a concurrent TWAIN scan could be relying on it). `FindBestMatch` is the seam where `SelectedGame` is read today; threading an optional override through it (and through `AddFromStream`, which calls it) keeps every other call site backward-compatible via the added parameter's default value.

- [ ] **Step 1: Write the failing test for the override routing**

Add to `OmniCard.Tests/Services/FallbackMatchingTests.cs`, after the existing `FindBestMatch_PassesScanEdgeHashThrough` test (before the `CreateCardService` helper method):

```csharp
[Fact]
public void FindBestMatch_GameOverride_RoutesToOverriddenGame_IgnoringSelectedGame()
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

    var mtgService = new StubGameService(CardGame.Mtg, match: mtgMatch);
    var opService = new StubGameService(CardGame.OnePiece, match: null);

    var service = CreateCardService([mtgService, opService]);
    service.SelectedGame = CardGame.OnePiece; // desktop toolbar left on a different game

    var (match, game) = service.FindBestMatch(0xDEADBEEF, gameOverride: CardGame.Mtg);

    Assert.NotNull(match);
    Assert.Equal("Bolt", match.Name);
    Assert.Equal(CardGame.Mtg, game);
    Assert.Equal(CardGame.OnePiece, service.SelectedGame); // override must not mutate the shared toggle
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~FallbackMatchingTests.FindBestMatch_GameOverride_RoutesToOverriddenGame_IgnoringSelectedGame"`
Expected: FAIL — `FindBestMatch` has no `gameOverride` parameter (compile error at this stage, since the test project won't build until the signature exists).

- [ ] **Step 3: Update `ICardService`**

In `OmniCard.Shared/Interfaces/ICardService.cs`, change:

```csharp
    void AddFromStream(Stream stream);
```
to:
```csharp
    ScannedCard AddFromStream(Stream stream, CardGame? gameOverride = null);
```

and change:
```csharp
    (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null);
```
to:
```csharp
    (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null, CardGame? gameOverride = null);
```

- [ ] **Step 4: Update `CardService.FindBestMatch`**

In `OmniCard.Collection/CardService.cs`, change the method signature (currently line 89):

```csharp
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null)
    {
        // Normalize empty set filter to null
        if (setFilter is { Count: 0 })
            setFilter = null;

        // Match only within the selected game — never fall back to other games
        if (_gameServices.TryGetValue(SelectedGame, out var primaryService))
        {
            var primaryMatch = primaryService.FindClosestMatch(hash, artHashes, ocrResult, setFilter, preferredSets, scanEdgeHash: scanEdgeHash);
            if (primaryMatch is not null)
                return (primaryMatch, SelectedGame);
        }

        return (null, SelectedGame);
    }
```

to:

```csharp
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null, CardGame? gameOverride = null)
    {
        // Normalize empty set filter to null
        if (setFilter is { Count: 0 })
            setFilter = null;

        var effectiveGame = gameOverride ?? SelectedGame;

        // Match only within the selected game — never fall back to other games
        if (_gameServices.TryGetValue(effectiveGame, out var primaryService))
        {
            var primaryMatch = primaryService.FindClosestMatch(hash, artHashes, ocrResult, setFilter, preferredSets, scanEdgeHash: scanEdgeHash);
            if (primaryMatch is not null)
                return (primaryMatch, effectiveGame);
        }

        return (null, effectiveGame);
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~FallbackMatchingTests"`
Expected: All `FallbackMatchingTests` PASS, including the new one. (The project won't fully build yet — `AddFromStream` callers/implementers still need updating in the next steps — so this may still show compile errors until Step 6 is done. If so, proceed to Step 6 then re-run.)

- [ ] **Step 6: Update `CardService.AddFromStream` to accept the override and return the `ScannedCard`**

In `OmniCard.Collection/CardService.cs`, change the method declaration (currently line 140):

```csharp
    public void AddFromStream(Stream stream)
```
to:
```csharp
    public ScannedCard AddFromStream(Stream stream, CardGame? gameOverride = null)
```

Change the game-resolution line (currently line 150):
```csharp
        var game = SelectedGame;
```
to:
```csharp
        var game = gameOverride ?? SelectedGame;
```

Change the MTG art-hash branch (currently line 170):
```csharp
        if (SelectedGame == CardGame.Mtg)
```
to:
```csharp
        if (game == CardGame.Mtg)
```
(this is the branch guarding `artHashes = _hashService.ComputeArtHash(...)`, a few lines above the foil edge-hash check)

Change the foil edge-hash condition (currently lines 183-184):
```csharp
        if (DefaultIsFoil && (SelectedGame == CardGame.OnePiece || SelectedGame == CardGame.Riftbound
            || SelectedGame == CardGame.Pokemon || SelectedGame == CardGame.YuGiOh || SelectedGame == CardGame.FinalFantasy))
```
to:
```csharp
        if (DefaultIsFoil && (game == CardGame.OnePiece || game == CardGame.Riftbound
            || game == CardGame.Pokemon || game == CardGame.YuGiOh || game == CardGame.FinalFantasy))
```

Change the symbol-detection branch (currently line 216, the *second* `if (SelectedGame == CardGame.Mtg)` in the method — the one guarding `_ocrService.DetectSetSymbol`):
```csharp
        if (SelectedGame == CardGame.Mtg)
```
to:
```csharp
        if (game == CardGame.Mtg)
```

Change the initial synchronous match call (currently line 236):
```csharp
            var (bestMatch, matchedGame) = FindBestMatch(hash, artHashes, null, SelectedSetFilter, detectedSets, scanEdgeHash);
```
to:
```csharp
            var (bestMatch, matchedGame) = FindBestMatch(hash, artHashes, null, SelectedSetFilter, detectedSets, scanEdgeHash, gameOverride: game);
```

Inside the `Dispatcher.BeginInvoke` closure, add `gameOverride: game` to each of the five OCR-refinement `FindBestMatch` calls (they already close over the outer `game` local, so this keeps the async OCR re-match scoped to the same overridden game rather than re-reading a possibly-changed `SelectedGame`):

- OPTCG branch (currently line 298):
  ```csharp
  var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, null, scannedCard.ScanEdgeHash);
  ```
  →
  ```csharp
  var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, null, scannedCard.ScanEdgeHash, gameOverride: game);
  ```
- Riftbound branch (currently line 316): same replacement.
- Pokemon/YuGiOh/FinalFantasy branch (currently line 339): same replacement.
- MTG branch (currently line 367):
  ```csharp
  var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, mergedPreferredSets);
  ```
  →
  ```csharp
  var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, mergedPreferredSets, gameOverride: game);
  ```
- Rotation retry (currently line 440):
  ```csharp
  var (rotatedMatch, rotatedGame) = FindBestMatch(rotatedHash, null, rotatedOcr, capturedSetFilter, null, rotatedEdgeHash);
  ```
  →
  ```csharp
  var (rotatedMatch, rotatedGame) = FindBestMatch(rotatedHash, null, rotatedOcr, capturedSetFilter, null, rotatedEdgeHash, gameOverride: game);
  ```

Finally, return the `scannedCard` at the end of the method — change (currently lines 484-486):
```csharp
        sw.Stop();
        _logger.LogInformation("Card scan processed in {ElapsedMs}ms (total scanned: {Count})", sw.ElapsedMilliseconds, ScannedCards.Count);
    }
```
to:
```csharp
        sw.Stop();
        _logger.LogInformation("Card scan processed in {ElapsedMs}ms (total scanned: {Count})", sw.ElapsedMilliseconds, ScannedCards.Count);

        return scannedCard;
    }
```

Note: the returned `ScannedCard` reflects only the synchronous pHash/art-hash/edge-hash match — the async OCR refinement that follows (inside `Dispatcher.BeginInvoke`) may still improve `scannedCard.Match` afterward, same as it already does for TWAIN scans reviewed later in the desktop queue. This is an accepted limitation for the phone's immediate result screen (per the approved design doc).

- [ ] **Step 7: Update `WebCardService` stub**

In `OmniCard.Web/Services/WebCardService.cs`, change:
```csharp
    public void AddFromStream(Stream stream) => throw new NotSupportedException();
```
to:
```csharp
    public ScannedCard AddFromStream(Stream stream, CardGame? gameOverride = null) => throw new NotSupportedException();
```

and change:
```csharp
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => throw new NotSupportedException();
```
to:
```csharp
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null, CardGame? gameOverride = null) => throw new NotSupportedException();
```

- [ ] **Step 8: Update the three `ICardService` test fakes**

In `OmniCard.Tests/Fakes/ImportFakes.cs`, change:
```csharp
    public void AddFromStream(Stream stream) => throw new NotImplementedException();
```
to:
```csharp
    public ScannedCard AddFromStream(Stream stream, CardGame? gameOverride = null) => throw new NotImplementedException();
```
and change:
```csharp
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => throw new NotImplementedException();
```
to:
```csharp
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null, CardGame? gameOverride = null) => throw new NotImplementedException();
```

In `OmniCard.Tests/Services/DecklistMatchingTests.cs`, change:
```csharp
        public void AddFromStream(Stream stream) { }
```
to:
```csharp
        public ScannedCard AddFromStream(Stream stream, CardGame? gameOverride = null) => null!;
```
and change:
```csharp
        public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => (null, CardGame.Mtg);
```
to:
```csharp
        public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null, CardGame? gameOverride = null) => (null, CardGame.Mtg);
```

In `OmniCard.Tests/Services/ListServiceTests.cs`, apply the identical two replacements (same current text as `DecklistMatchingTests.cs` above, at lines 272 and 303).

- [ ] **Step 9: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: Build succeeds; all tests PASS, including the new `FindBestMatch_GameOverride_RoutesToOverriddenGame_IgnoringSelectedGame`.

- [ ] **Step 10: Commit**

```bash
git add OmniCard.Shared/Interfaces/ICardService.cs OmniCard.Collection/CardService.cs OmniCard.Web/Services/WebCardService.cs OmniCard.Tests/Fakes/ImportFakes.cs OmniCard.Tests/Services/DecklistMatchingTests.cs OmniCard.Tests/Services/ListServiceTests.cs OmniCard.Tests/Services/FallbackMatchingTests.cs
git commit -m "feat(web-scanner): add per-call game override to CardService.AddFromStream/FindBestMatch"
```

---

## Task 3: Track the connected desktop client

**Files:**
- Create: `OmniCard.Web/Hubs/DesktopConnectionTracker.cs`
- Modify: `OmniCard.Web/Hubs/ScanHub.cs`
- Modify: `OmniCard.Web/Program.cs`
- Test: `OmniCard.Tests/Web/DesktopConnectionTrackerTests.cs`

**Interfaces:**
- Produces: `DesktopConnectionTracker` with `string? CurrentConnectionId`, `void Set(string connectionId)`, `void Clear(string connectionId)` — consumed by Task 4 (`ScanController`).

`Clients.Client(connectionId).InvokeAsync<T>` (used in Task 4) needs to know *which* connection is the desktop app. `ScanHub` already logs connect/disconnect; this task turns that into state a controller can read. `Clear` only evicts if the id passed still matches the currently tracked one, so a stale disconnect notification arriving after a newer reconnect can't evict the live connection.

- [ ] **Step 1: Write the failing tests**

```csharp
// OmniCard.Tests/Web/DesktopConnectionTrackerTests.cs
using OmniCard.Web.Hubs;

namespace OmniCard.Tests.Web;

public class DesktopConnectionTrackerTests
{
    [Fact]
    public void CurrentConnectionId_IsNull_BeforeAnyConnection()
    {
        var tracker = new DesktopConnectionTracker();
        Assert.Null(tracker.CurrentConnectionId);
    }

    [Fact]
    public void Set_UpdatesCurrentConnectionId()
    {
        var tracker = new DesktopConnectionTracker();
        tracker.Set("conn-1");
        Assert.Equal("conn-1", tracker.CurrentConnectionId);
    }

    [Fact]
    public void Clear_RemovesMatchingConnectionId()
    {
        var tracker = new DesktopConnectionTracker();
        tracker.Set("conn-1");
        tracker.Clear("conn-1");
        Assert.Null(tracker.CurrentConnectionId);
    }

    [Fact]
    public void Clear_IgnoresStaleConnectionId()
    {
        var tracker = new DesktopConnectionTracker();
        tracker.Set("conn-1");
        tracker.Set("conn-2");
        tracker.Clear("conn-1");
        Assert.Equal("conn-2", tracker.CurrentConnectionId);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~DesktopConnectionTrackerTests"`
Expected: FAIL to build — `DesktopConnectionTracker` doesn't exist yet.

- [ ] **Step 3: Create `DesktopConnectionTracker`**

```csharp
// OmniCard.Web/Hubs/DesktopConnectionTracker.cs
namespace OmniCard.Web.Hubs;

public class DesktopConnectionTracker
{
    private volatile string? _connectionId;

    public string? CurrentConnectionId => _connectionId;

    public void Set(string connectionId) => _connectionId = connectionId;

    public void Clear(string connectionId)
    {
        if (_connectionId == connectionId)
            _connectionId = null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~DesktopConnectionTrackerTests"`
Expected: All 4 tests PASS.

- [ ] **Step 5: Wire the tracker into `ScanHub`**

In `OmniCard.Web/Hubs/ScanHub.cs`, replace the whole file:

```csharp
// OmniCard.Web/Hubs/ScanHub.cs
using Microsoft.AspNetCore.SignalR;

namespace OmniCard.Web.Hubs;

public class ScanHub : Hub
{
    private readonly ILogger<ScanHub> _logger;
    private readonly DesktopConnectionTracker _tracker;

    public ScanHub(ILogger<ScanHub> logger, DesktopConnectionTracker tracker)
    {
        _logger = logger;
        _tracker = tracker;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Desktop client connected: {ConnectionId}", Context.ConnectionId);
        _tracker.Set(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Desktop client disconnected: {ConnectionId}", Context.ConnectionId);
        _tracker.Clear(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
```

- [ ] **Step 6: Register the tracker as a singleton**

In `OmniCard.Web/Program.cs`, add this line near the other infrastructure singleton registrations (after `builder.Services.AddSingleton<SetSymbolCache>();`):

```csharp
builder.Services.AddSingleton<OmniCard.Web.Hubs.DesktopConnectionTracker>();
```

- [ ] **Step 7: Build and run the full test suite**

Run: `dotnet build OmniCard.slnx && dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: Build succeeds; all tests PASS.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Web/Hubs/DesktopConnectionTracker.cs OmniCard.Web/Hubs/ScanHub.cs OmniCard.Web/Program.cs OmniCard.Tests/Web/DesktopConnectionTrackerTests.cs
git commit -m "feat(web-scanner): track the connected desktop SignalR connection id"
```

---

## Task 4: `ScanController` request/response upload, search, and correction

**Files:**
- Modify: `OmniCard.Web/Api/ScanController.cs`
- Modify: `OmniCard.Web/Program.cs`
- Test: `OmniCard.Tests/Web/ScanControllerTests.cs`

**Interfaces:**
- Consumes: `DesktopConnectionTracker.CurrentConnectionId` (Task 3), `ScanResultDto`/`ScanCorrectionDto` (Task 1), `ICardService.GetGameService(CardGame)` (existing), `ICardGameService.SearchCards(string, int)` (existing).
- Produces: `POST /api/scan` now returns a `ScanResultDto` body (200) instead of `{status, size}`. `GET /api/scan/search?game=&q=` returns `List<CardMatch>`. `POST /api/scan/correct` (JSON body `ScanCorrectionDto`) returns 200 on success, 400 if no desktop connected. These three endpoints are consumed by Task 6 (Scan.cshtml).

The old `Upload` broadcast to `Clients.All` (fire-and-forget) is replaced with a single-client `InvokeAsync<ScanResultDto>` round trip that awaits the desktop's answer, with a 15s timeout. `CardGame` needs to serialize as its name (not a raw int) in JSON request/response bodies for this to be usable from JS without a lookup table — `Search`'s `game` query param already binds by name for free (ASP.NET Core's built-in enum model binder), but `ScanCorrectionDto.Game` arrives via `[FromBody]` JSON, which needs `JsonStringEnumConverter` registered to accept `"Mtg"` instead of only `0`.

- [ ] **Step 1: Write the failing tests**

Replace the entire contents of `OmniCard.Tests/Web/ScanControllerTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Api;
using OmniCard.Web.Hubs;

namespace OmniCard.Tests.Web;

public class ScanControllerTests
{
    private readonly Mock<ISingleClientProxy> _mockClientProxy;
    private readonly Mock<IHubClients> _mockClients;
    private readonly DesktopConnectionTracker _tracker;
    private readonly Mock<ICardService> _mockCardService;
    private readonly ScanController _controller;

    public ScanControllerTests()
    {
        _mockClientProxy = new Mock<ISingleClientProxy>();
        _mockClients = new Mock<IHubClients>();
        _mockClients.Setup(c => c.Client(It.IsAny<string>())).Returns(_mockClientProxy.Object);

        var mockHubContext = new Mock<IHubContext<ScanHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);

        _tracker = new DesktopConnectionTracker();
        _mockCardService = new Mock<ICardService>();

        _controller = new ScanController(
            mockHubContext.Object,
            _tracker,
            _mockCardService.Object,
            NullLogger<ScanController>.Instance);
    }

    private static IFormFile CreateFormFile(
        byte[]? content = null,
        string contentType = "image/jpeg",
        string fileName = "test.jpg",
        long? overrideLength = null)
    {
        content ??= [0xFF, 0xD8, 0xFF, 0xE0]; // JPEG magic bytes
        var stream = new MemoryStream(content);
        var file = new FormFile(stream, 0, overrideLength ?? content.Length, "image", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
        return file;
    }

    [Fact]
    public async Task Upload_DesktopConnected_ReturnsDesktopResult()
    {
        _tracker.Set("conn-1");
        var expected = new ScanResultDto { Matched = true, Name = "Lightning Bolt", Game = CardGame.Mtg, ScanHash = "123" };
        _mockClientProxy
            .Setup(p => p.InvokeCoreAsync<ScanResultDto>("ProcessScan", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var file = CreateFormFile(new byte[1024], "image/jpeg");
        var result = await _controller.Upload(file, CardGame.Mtg, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ScanResultDto>(ok.Value);
        Assert.True(dto.Matched);
        Assert.Equal("Lightning Bolt", dto.Name);
    }

    [Fact]
    public async Task Upload_DesktopNotConnected_ReturnsUnmatchedWithError()
    {
        var file = CreateFormFile(new byte[1024], "image/jpeg");
        var result = await _controller.Upload(file, CardGame.Mtg, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ScanResultDto>(ok.Value);
        Assert.False(dto.Matched);
        Assert.Equal("Desktop app not connected", dto.Error);
    }

    [Fact]
    public async Task Upload_DesktopTimesOut_ReturnsUnmatchedWithError()
    {
        _tracker.Set("conn-1");
        _mockClientProxy
            .Setup(p => p.InvokeCoreAsync<ScanResultDto>("ProcessScan", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var file = CreateFormFile(new byte[1024], "image/jpeg");
        var result = await _controller.Upload(file, CardGame.Mtg, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ScanResultDto>(ok.Value);
        Assert.False(dto.Matched);
        Assert.Equal("Desktop app timed out", dto.Error);
    }

    [Fact]
    public async Task Upload_NoFile_Returns400()
    {
        var result = await _controller.Upload(null!, CardGame.Mtg, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public async Task Upload_WrongContentType_Returns400()
    {
        var file = CreateFormFile(contentType: "text/plain", fileName: "test.txt");

        var result = await _controller.Upload(file, CardGame.Mtg, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public async Task Upload_OversizedFile_Returns400()
    {
        var file = CreateFormFile(content: new byte[1], overrideLength: 11 * 1024 * 1024);

        var result = await _controller.Upload(file, CardGame.Mtg, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public void Search_ReturnsGameServiceSearchResults()
    {
        var mockGameService = new Mock<ICardGameService>();
        mockGameService.Setup(s => s.SearchCards("bolt", 20)).Returns([
            new CardMatch { Name = "Lightning Bolt", GameSpecificId = "abc" }
        ]);
        _mockCardService.Setup(c => c.GetGameService(CardGame.Mtg)).Returns(mockGameService.Object);

        var result = _controller.Search(CardGame.Mtg, "bolt");

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<List<CardMatch>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Lightning Bolt", list[0].Name);
    }

    [Fact]
    public void Search_BlankQuery_ReturnsEmptyListWithoutCallingGameService()
    {
        var result = _controller.Search(CardGame.Mtg, "  ");

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<List<CardMatch>>(ok.Value);
        Assert.Empty(list);
        _mockCardService.Verify(c => c.GetGameService(It.IsAny<CardGame>()), Times.Never);
    }

    [Fact]
    public async Task Correct_DesktopConnected_RelaysToHubAndReturns200()
    {
        _tracker.Set("conn-1");
        _mockClientProxy
            .Setup(p => p.InvokeCoreAsync<bool>("RecordCorrection", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new ScanCorrectionDto
        {
            ScanHash = "123",
            Game = CardGame.Mtg,
            GameSpecificId = "abc",
            Name = "Bolt",
            SetCode = "lea",
            SetName = "Alpha",
            CollectorNumber = "1",
            Rarity = "common",
        };

        var result = await _controller.Correct(request, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        _mockClientProxy.Verify(
            p => p.InvokeCoreAsync<bool>("RecordCorrection", It.Is<object?[]>(a => a.Length == 1 && a[0] == request), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Correct_DesktopNotConnected_Returns400()
    {
        var request = new ScanCorrectionDto { ScanHash = "123", Game = CardGame.Mtg, GameSpecificId = "abc" };

        var result = await _controller.Correct(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ScanControllerTests"`
Expected: FAIL to build — `ScanController`'s constructor and `Upload` signature don't match yet, `Search`/`Correct` don't exist.

- [ ] **Step 3: Rewrite `ScanController`**

Replace the entire contents of `OmniCard.Web/Api/ScanController.cs`:

```csharp
// OmniCard.Web/Api/ScanController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Hubs;

namespace OmniCard.Web.Api;

[ApiController]
[Route("api/[controller]")]
public class ScanController : ControllerBase
{
    private readonly IHubContext<ScanHub> _hubContext;
    private readonly DesktopConnectionTracker _tracker;
    private readonly ICardService _cardService;
    private readonly ILogger<ScanController> _logger;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
    };

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(15);

    public ScanController(
        IHubContext<ScanHub> hubContext,
        DesktopConnectionTracker tracker,
        ICardService cardService,
        ILogger<ScanController> logger)
    {
        _hubContext = hubContext;
        _tracker = tracker;
        _cardService = cardService;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Upload([FromForm] IFormFile image, [FromForm] CardGame game, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "No image provided" });

        if (!AllowedContentTypes.Contains(image.ContentType))
            return BadRequest(new { error = "Only JPEG and PNG images are accepted" });

        if (image.Length > MaxFileSize)
            return BadRequest(new { error = "Image exceeds 10 MB limit" });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);
        var imageData = ms.ToArray();

        _logger.LogInformation("Received scan image: {Size} bytes, {ContentType}, game {Game}", imageData.Length, image.ContentType, game);

        var connectionId = _tracker.CurrentConnectionId;
        if (connectionId is null)
            return Ok(new ScanResultDto { Matched = false, Game = game, Error = "Desktop app not connected" });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ScanTimeout);
        try
        {
            var result = await _hubContext.Clients.Client(connectionId)
                .InvokeAsync<ScanResultDto>("ProcessScan", imageData, game, timeoutCts.Token);
            return Ok(result);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Desktop app timed out processing scan");
            return Ok(new ScanResultDto { Matched = false, Game = game, Error = "Desktop app timed out" });
        }
    }

    [HttpGet("search")]
    public IActionResult Search([FromQuery] CardGame game, [FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new List<CardMatch>());

        var results = _cardService.GetGameService(game).SearchCards(q, 20);
        return Ok(results);
    }

    [HttpPost("correct")]
    public async Task<IActionResult> Correct([FromBody] ScanCorrectionDto request, CancellationToken ct)
    {
        var connectionId = _tracker.CurrentConnectionId;
        if (connectionId is null)
            return BadRequest(new { error = "Desktop app not connected" });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ScanTimeout);
        try
        {
            await _hubContext.Clients.Client(connectionId)
                .InvokeAsync<bool>("RecordCorrection", request, timeoutCts.Token);
            return Ok();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { error = "Desktop app timed out" });
        }
    }
}
```

- [ ] **Step 4: Register `JsonStringEnumConverter` so `CardGame` serializes as its name in JSON bodies**

In `OmniCard.Web/Program.cs`, change:
```csharp
builder.Services.AddControllers();
```
to:
```csharp
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ScanControllerTests"`
Expected: All tests PASS.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Web/Api/ScanController.cs OmniCard.Web/Program.cs OmniCard.Tests/Web/ScanControllerTests.cs
git commit -m "feat(web-scanner): synchronous scan upload, catalog search, and correction relay endpoints"
```

---

## Task 5: Desktop side — respond to scans and corrections over the hub connection

**Files:**
- Modify: `OmniCard/Services/WebScannerService.cs`
- Test: `OmniCard.Tests/Services/WebScannerServiceMappingTests.cs`

**Interfaces:**
- Consumes: `ICardService.AddFromStream(Stream, CardGame?) : ScannedCard` (Task 2), `ScanResultDto`/`ScanCorrectionDto` (Task 1), `ICardGameService.RecordCorrection(ulong, string, ulong?)` (existing).
- Produces: the desktop now answers `"ProcessScan"` and `"RecordCorrection"` client-result invocations from Web (Task 4), replacing the old fire-and-forget `"ImageReceived"` handler.

The mapping from a `ScannedCard`/`CardMatch` to the wire DTO is pulled out into a small pure `internal static` method so it can be unit tested without touching SignalR or the WPF `Dispatcher` (`CardService.AddFromStream` itself stays untested at the unit level here, consistent with the rest of this class — see Task 7's manual smoke test for full round-trip verification).

- [ ] **Step 1: Write the failing test for the mapping function**

```csharp
// OmniCard.Tests/Services/WebScannerServiceMappingTests.cs
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class WebScannerServiceMappingTests
{
    [Fact]
    public void MapToDto_MatchedCard_PopulatesFieldsFromMatch()
    {
        var match = new CardMatch
        {
            Name = "Lightning Bolt",
            SetName = "Alpha",
            SetCode = "lea",
            CollectorNumber = "1",
            Rarity = "common",
            ImageUri = "https://example.com/bolt.jpg",
            Confidence = 92.5,
            GameSpecificId = "abc",
            Source = new object(),
        };
        var scanned = new ScannedCard { TempImagePath = "temp.png", Hash = 0x123456789ABCDEF0UL, Match = match };

        var dto = WebScannerService.MapToDto(scanned, CardGame.Mtg);

        Assert.True(dto.Matched);
        Assert.Equal("Lightning Bolt", dto.Name);
        Assert.Equal("Alpha", dto.SetName);
        Assert.Equal("lea", dto.SetCode);
        Assert.Equal("1", dto.CollectorNumber);
        Assert.Equal("common", dto.Rarity);
        Assert.Equal("https://example.com/bolt.jpg", dto.ImageUri);
        Assert.Equal(92.5, dto.Confidence);
        Assert.Equal("1311768467294899696", dto.ScanHash); // 0x123456789ABCDEF0 as decimal string
        Assert.Equal(CardGame.Mtg, dto.Game);
        Assert.Null(dto.Error);
    }

    [Fact]
    public void MapToDto_UnmatchedCard_ReturnsMatchedFalseWithNullFields()
    {
        var scanned = new ScannedCard { TempImagePath = "temp.png", Hash = 42UL, Match = null };

        var dto = WebScannerService.MapToDto(scanned, CardGame.OnePiece);

        Assert.False(dto.Matched);
        Assert.Null(dto.Name);
        Assert.Equal("42", dto.ScanHash);
        Assert.Equal(CardGame.OnePiece, dto.Game);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~WebScannerServiceMappingTests"`
Expected: FAIL to build — `WebScannerService.MapToDto` doesn't exist yet.

- [ ] **Step 3: Rewrite `WebScannerService`**

Replace the entire contents of `OmniCard/Services/WebScannerService.cs`:

```csharp
// OmniCard/Services/WebScannerService.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;
using System.IO;
using System.Linq;

namespace OmniCard.Services;

public sealed class WebScannerService : IAsyncDisposable
{
    private readonly ICardService _cardService;
    private readonly ILogger<WebScannerService> _logger;
    private readonly IOptionsMonitor<WebCompanionSettings> _settings;
    private HubConnection? _hubConnection;
    private IDisposable? _settingsChangeToken;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public WebScannerService(
        ICardService cardService,
        ILogger<WebScannerService> logger,
        IOptionsMonitor<WebCompanionSettings> settings)
    {
        _cardService = cardService;
        _logger = logger;
        _settings = settings;
    }

    public async Task StartAsync()
    {
        var baseUrl = _settings.CurrentValue.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogInformation("WebCompanion BaseUrl not configured — phone scanner disabled");
            return;
        }

        await ConnectAsync(baseUrl);

        // Reconnect if settings change
        _settingsChangeToken = _settings.OnChange(async newSettings =>
        {
            var newUrl = newSettings.BaseUrl;
            if (string.IsNullOrWhiteSpace(newUrl))
            {
                await DisconnectAsync();
                return;
            }

            // Reconnect if URL changed
            if (_hubConnection is null || _hubConnection.State == HubConnectionState.Disconnected)
                await ConnectAsync(newUrl);
        });
    }

    private async Task ConnectAsync(string baseUrl)
    {
        await DisconnectAsync();

        var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/scan";
        _logger.LogInformation("Connecting to phone scanner hub at {Url}", hubUrl);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<byte[], CardGame, ScanResultDto>("ProcessScan", OnProcessScan);
        _hubConnection.On<ScanCorrectionDto, bool>("RecordCorrection", OnRecordCorrection);

        _hubConnection.Reconnecting += _ =>
        {
            _logger.LogWarning("Phone scanner connection lost, reconnecting...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += _ =>
        {
            _logger.LogInformation("Phone scanner reconnected");
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync();
            _logger.LogInformation("Connected to phone scanner hub");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to phone scanner hub at {Url} — phone scanning unavailable", hubUrl);
        }
    }

    private Task<ScanResultDto> OnProcessScan(byte[] imageData, CardGame game)
    {
        _logger.LogInformation("Received scan image from phone: {Size} bytes, game {Game}", imageData.Length, game);
        try
        {
            using var stream = new MemoryStream(imageData);
            var scanned = _cardService.AddFromStream(stream, game);
            return Task.FromResult(MapToDto(scanned, game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process phone scan image");
            return Task.FromResult(new ScanResultDto { Matched = false, Game = game, Error = "Desktop failed to process the image" });
        }
    }

    private Task<bool> OnRecordCorrection(ScanCorrectionDto request)
    {
        try
        {
            var hash = ulong.Parse(request.ScanHash);
            _cardService.GetGameService(request.Game).RecordCorrection(hash, request.GameSpecificId);

            var scan = _cardService.ScannedCards.FirstOrDefault(s => s.Hash == hash);
            if (scan is not null)
            {
                scan.Match = new CardMatch
                {
                    Name = request.Name,
                    SetCode = request.SetCode,
                    SetName = request.SetName,
                    CollectorNumber = request.CollectorNumber,
                    Rarity = request.Rarity,
                    ImageUri = request.ImageUri,
                    GameSpecificId = request.GameSpecificId,
                    Source = null!,
                };
                scan.Game = request.Game;
                scan.FlagReason = FlagReason.None;
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record scan correction");
            return Task.FromResult(false);
        }
    }

    internal static ScanResultDto MapToDto(ScannedCard scanned, CardGame game)
    {
        var match = scanned.Match;
        return new ScanResultDto
        {
            Matched = match is not null,
            Name = match?.Name,
            SetName = match?.SetName,
            SetCode = match?.SetCode,
            CollectorNumber = match?.CollectorNumber,
            Rarity = match?.Rarity,
            ImageUri = match?.ImageUri,
            Confidence = match?.Confidence,
            ScanHash = scanned.Hash.ToString(),
            Game = game,
        };
    }

    private async Task DisconnectAsync()
    {
        if (_hubConnection is not null)
        {
            try
            {
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting from phone scanner hub");
            }
            _hubConnection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _settingsChangeToken?.Dispose();
        await DisconnectAsync();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~WebScannerServiceMappingTests"`
Expected: Both tests PASS.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/WebScannerService.cs OmniCard.Tests/Services/WebScannerServiceMappingTests.cs
git commit -m "feat(web-scanner): answer ProcessScan/RecordCorrection client-result invocations from Web"
```

---

## Task 6: Phone UI — game picker, result screen, correction search

**Files:**
- Modify: `OmniCard.Web/Pages/Scan.cshtml`

**Interfaces:**
- Consumes: `POST /api/scan` (now returns `ScanResultDto` JSON, camelCase: `matched`, `name`, `setName`, `setCode`, `collectorNumber`, `rarity`, `imageUri`, `confidence`, `scanHash`, `game`, `error`), `GET /api/scan/search?game=&q=` (returns `CardMatch[]` JSON: `name`, `setCode`, `setName`, `collectorNumber`, `rarity`, `imageUri`, `gameSpecificId`), `POST /api/scan/correct` (JSON body matching `ScanCorrectionDto`) — all from Task 4.

Plain JS/CSS only, no new libraries (per PRD §4). The existing viewfinder/crop/pinch-zoom/fallback-mode code is untouched; this task adds a game picker bar, a result screen, and a search/correction screen, and rewires `capture()`'s and the fallback `change` handler's post-upload logic to route into them instead of a one-line status message.

- [ ] **Step 1: Add the game picker**

In `OmniCard.Web/Pages/Scan.cshtml`, add this CSS inside the existing `<style>` block, after the `.status.uploading { color: #ffeb3b; }` rule:

```css
        /* Game picker */
        .game-picker {
            padding: 8px 16px; background: rgba(0,0,0,0.9);
            display: flex; justify-content: center;
        }
        .game-picker select {
            background: #222; color: #fff; border: 1px solid #444; border-radius: 6px;
            padding: 8px 12px; font-size: 14px; width: 100%; max-width: 320px;
            -webkit-appearance: none; appearance: none;
        }
```

Add this markup right after `<body>` and before `<div class="container" id="app">`:

```html
    <div class="game-picker" id="gamePicker">
        <select id="gameSelect">
            <option value="Mtg">Magic: The Gathering</option>
            <option value="OnePiece">One Piece TCG</option>
            <option value="Riftbound">Riftbound</option>
            <option value="Pokemon">Pokémon</option>
            <option value="YuGiOh">Yu-Gi-Oh!</option>
            <option value="FinalFantasy">Final Fantasy TCG</option>
        </select>
    </div>
```

Add this JS near the top of the `<script>` block, right after the existing `let zoomIndicatorTimeout = null;` declaration:

```javascript
        // ── Game selection ──
        const gameSelect = document.getElementById('gameSelect');
        const GAME_STORAGE_KEY = 'omnicard-scan-game';
        gameSelect.value = localStorage.getItem(GAME_STORAGE_KEY) || 'Mtg';
        gameSelect.addEventListener('change', () => localStorage.setItem(GAME_STORAGE_KEY, gameSelect.value));
        function getSelectedGame() { return gameSelect.value; }
```

- [ ] **Step 2: Add the result and search screens' markup and CSS**

Add this CSS inside the existing `<style>` block, after the `.error-screen p { margin-top: 12px; color: #aaa; font-size: 14px; }` rule:

```css
        /* Result screen */
        .result-card {
            flex: 1; display: flex; flex-direction: column; align-items: center;
            justify-content: center; padding: 24px; gap: 12px; overflow-y: auto;
        }
        .result-image { max-width: 70%; max-height: 50vh; border-radius: 8px; }
        .result-title { font-size: 20px; font-weight: 600; text-align: center; }
        .result-subtitle { font-size: 14px; color: #aaa; text-align: center; }
        .result-actions { display: flex; gap: 12px; padding: 20px; background: rgba(0,0,0,0.8); }
        .result-btn {
            flex: 1; padding: 16px; border: none; border-radius: 8px;
            font-size: 16px; font-weight: 600; cursor: pointer;
            -webkit-tap-highlight-color: transparent;
        }
        .result-btn.confirm { background: #4caf50; color: #fff; }
        .result-btn.reject { background: #333; color: #fff; }

        /* Search / correction screen */
        .search-header { padding: 16px; background: rgba(0,0,0,0.8); }
        .search-message { font-size: 14px; color: #aaa; margin-bottom: 10px; text-align: center; }
        .search-input {
            width: 100%; padding: 12px; border-radius: 8px; border: 1px solid #444;
            background: #222; color: #fff; font-size: 16px;
        }
        .search-results { flex: 1; overflow-y: auto; }
        .search-result-item {
            display: flex; align-items: center; gap: 12px; padding: 12px 16px;
            border-bottom: 1px solid #222; cursor: pointer;
        }
        .search-result-item img { width: 40px; height: 56px; object-fit: cover; border-radius: 4px; background: #222; }
        .search-result-item .name { font-size: 15px; font-weight: 600; }
        .search-result-item .meta { font-size: 12px; color: #aaa; }
        .search-footer { padding: 16px; background: rgba(0,0,0,0.8); }
```

Add this markup right after the `errorScreen` container div (before the closing `<script>` tag's preceding content):

```html
    <div class="container" id="resultScreen" style="display:none">
        <div class="result-card">
            <img id="resultImage" class="result-image" alt="" />
            <div id="resultTitle" class="result-title"></div>
            <div id="resultSubtitle" class="result-subtitle"></div>
        </div>
        <div class="result-actions">
            <button class="result-btn confirm" id="confirmBtn">Confirm</button>
            <button class="result-btn reject" id="rejectBtn">Not this card</button>
        </div>
    </div>

    <div class="container" id="searchScreen" style="display:none">
        <div class="search-header">
            <div id="searchMessage" class="search-message"></div>
            <input type="text" id="searchInput" class="search-input" placeholder="Search card name..." />
        </div>
        <div id="searchResults" class="search-results"></div>
        <div class="search-footer">
            <button class="fallback-btn" id="searchBackBtn">Back to Camera</button>
        </div>
    </div>
```

- [ ] **Step 3: Add the screen-management and search JS**

Add this JS block right before the `// ── Init ──` comment near the end of the `<script>` block:

```javascript
        // ── Result / search screens ──

        let lastScanHash = null;
        let lastScanGame = null;
        let searchDebounceTimer = null;

        function hideAllScreens() {
            document.getElementById('app').style.display = 'none';
            document.getElementById('fallbackScreen').style.display = 'none';
            document.getElementById('resultScreen').style.display = 'none';
            document.getElementById('searchScreen').style.display = 'none';
        }

        function showViewfinder() {
            hideAllScreens();
            if (canUseLiveCamera) {
                document.getElementById('app').style.display = '';
            } else {
                document.getElementById('fallbackScreen').style.display = '';
            }
        }

        function showResultScreen(dto) {
            hideAllScreens();
            document.getElementById('resultScreen').style.display = '';
            document.getElementById('resultImage').src = dto.imageUri || '';
            document.getElementById('resultTitle').textContent = dto.name || '';
            const parts = [dto.setName, dto.collectorNumber ? ('#' + dto.collectorNumber) : null, dto.rarity].filter(Boolean);
            document.getElementById('resultSubtitle').textContent = parts.join(' — ');
        }

        function showSearchScreen(message) {
            hideAllScreens();
            document.getElementById('searchScreen').style.display = '';
            document.getElementById('searchMessage').textContent = message || '';
            document.getElementById('searchInput').value = '';
            document.getElementById('searchResults').innerHTML = '';
            document.getElementById('searchInput').focus();
        }

        function handleScanResult(dto) {
            lastScanHash = dto.scanHash;
            lastScanGame = dto.game;
            if (dto.error) {
                showSearchScreen(dto.error);
            } else if (dto.matched) {
                showResultScreen(dto);
            } else {
                showSearchScreen('No match found — search for the card');
            }
        }

        async function runSearch(query) {
            const resultsEl = document.getElementById('searchResults');
            if (!query || query.trim().length === 0) {
                resultsEl.innerHTML = '';
                return;
            }
            try {
                const response = await fetch(`/api/scan/search?game=${encodeURIComponent(lastScanGame || getSelectedGame())}&q=${encodeURIComponent(query)}`);
                const cards = await response.json();
                renderSearchResults(cards);
            } catch (_) {
                resultsEl.innerHTML = '';
            }
        }

        function renderSearchResults(cards) {
            const resultsEl = document.getElementById('searchResults');
            resultsEl.innerHTML = '';
            for (const card of cards) {
                const item = document.createElement('div');
                item.className = 'search-result-item';
                item.innerHTML = `
                    <img src="${card.imageUri || ''}" alt="" />
                    <div>
                        <div class="name">${card.name}</div>
                        <div class="meta">${card.setName || ''} ${card.collectorNumber ? '#' + card.collectorNumber : ''} ${card.rarity || ''}</div>
                    </div>`;
                item.addEventListener('click', () => selectSearchResult(card));
                resultsEl.appendChild(item);
            }
        }

        async function selectSearchResult(card) {
            const request = {
                scanHash: lastScanHash,
                game: lastScanGame || getSelectedGame(),
                gameSpecificId: card.gameSpecificId,
                name: card.name,
                setCode: card.setCode,
                setName: card.setName,
                collectorNumber: card.collectorNumber,
                rarity: card.rarity,
                imageUri: card.imageUri,
            };
            try {
                await fetch('/api/scan/correct', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(request)
                });
            } catch (_) { /* best effort — desktop queue can be reconciled manually if this fails */ }

            showResultScreen({
                imageUri: card.imageUri, name: card.name, setName: card.setName,
                collectorNumber: card.collectorNumber, rarity: card.rarity
            });
        }

        document.getElementById('searchInput').addEventListener('input', (e) => {
            clearTimeout(searchDebounceTimer);
            const query = e.target.value;
            searchDebounceTimer = setTimeout(() => runSearch(query), 300);
        });

        document.getElementById('confirmBtn').addEventListener('click', showViewfinder);
        document.getElementById('rejectBtn').addEventListener('click', () => showSearchScreen('Search for the correct card'));
        document.getElementById('searchBackBtn').addEventListener('click', showViewfinder);
```

- [ ] **Step 4: Send the selected game with every upload and route the response to the new screens**

In `uploadImage()`, change:
```javascript
        async function uploadImage(blob) {
            const formData = new FormData();
            formData.append('image', blob, 'scan.jpg');

            const response = await fetch('/api/scan', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                const err = await response.json().catch(() => ({}));
                throw new Error(err.error || 'Upload failed');
            }
            return await response.json();
        }
```
to:
```javascript
        async function uploadImage(blob) {
            const formData = new FormData();
            formData.append('image', blob, 'scan.jpg');
            formData.append('game', getSelectedGame());

            const response = await fetch('/api/scan', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                const err = await response.json().catch(() => ({}));
                throw new Error(err.error || 'Upload failed');
            }
            return await response.json();
        }
```

In `capture()`, change the tail:
```javascript
            setStatus('Uploading...', 'uploading');

            try {
                const blob = await new Promise(resolve =>
                    outputCanvas.toBlob(resolve, 'image/jpeg', 0.92)
                );
                await uploadImage(blob);
                setStatus(detectedCard ? 'Sent (cropped)! Ready for next.' : 'Sent! Ready for next card.', 'success');
            } catch (err) {
                setStatus(err.message || 'Network error', 'error');
            }

            isUploading = false;
            captureBtn.disabled = false;
        }
```
to:
```javascript
            setStatus('Matching...', 'uploading');

            try {
                const blob = await new Promise(resolve =>
                    outputCanvas.toBlob(resolve, 'image/jpeg', 0.92)
                );
                const dto = await uploadImage(blob);
                handleScanResult(dto);
            } catch (err) {
                setStatus(err.message || 'Network error', 'error');
            }

            isUploading = false;
            captureBtn.disabled = false;
        }
```

In `initFallback()`'s file-input `change` handler, change:
```javascript
                setFallbackStatus('Uploading...', 'uploading');

                try {
                    await uploadImage(file);
                    setFallbackStatus('Sent! Tap to scan another.', 'success');
                } catch (err) {
                    setFallbackStatus(err.message || 'Network error', 'error');
                }

                // Reset input so the same file can be re-selected
                fileInput.value = '';
```
to:
```javascript
                setFallbackStatus('Matching...', 'uploading');

                try {
                    const dto = await uploadImage(file);
                    handleScanResult(dto);
                } catch (err) {
                    setFallbackStatus(err.message || 'Network error', 'error');
                }

                // Reset input so the same file can be re-selected
                fileInput.value = '';
```

- [ ] **Step 5: Manual verification (no automated test — plain page JS, per project convention)**

Run: `dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "%LOCALAPPDATA%\OmniCard"`

Open `http://localhost:<port>/Scan` in a desktop browser (mobile emulation via devtools is fine for this step — full camera round trip is verified in Task 7 on a real phone):
- Confirm the game picker renders above the viewfinder/fallback screen, defaults to "Magic: The Gathering", and the choice persists across a page reload (check `localStorage['omnicard-scan-game']` in devtools).
- Confirm there are no JS console errors on load.
- Open the Network tab, and — since the desktop app isn't necessarily connected right now — trigger a capture/upload and confirm the request body is `multipart/form-data` containing both `image` and `game` fields, and that the response renders the search screen with the "Desktop app not connected" message (this exercises the real `/api/scan` → `ScanController` → "no tracked connection" path end-to-end, even without a desktop client attached).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Web/Pages/Scan.cshtml
git commit -m "feat(web-scanner): add game picker, match result screen, and correction search to the phone scanner"
```

---

## Task 7: Full-suite verification and end-to-end manual smoke test

**Files:** none (verification only).

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: All tests PASS (this now includes the new tests from Tasks 2, 3, 4, and 5).

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build OmniCard.slnx`
Expected: Build succeeded, no errors or new warnings.

- [ ] **Step 3: Manual end-to-end smoke test (desktop + phone, real devices)**

This exercises the full round trip that no automated test can reach (SignalR client-result invocation across two live processes, a real phone camera, and WPF's `Dispatcher`):

1. Set `WebCompanion:BaseUrl` in the desktop app's settings to the machine's LAN URL, then launch the desktop app (`dotnet run --project OmniCard/OmniCard.csproj`) and confirm its log shows "Connected to phone scanner hub".
2. Launch the web companion (`dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "%LOCALAPPDATA%\OmniCard"`) on the same machine.
3. From a phone on the same network, open `http://<machine-ip>:<port>/Scan`.
4. Pick a game in the picker, point the camera at a real card of that game, and capture.
5. Confirm the phone shows the result screen with the correct card's art and name within a few seconds (not a timeout/error).
6. Confirm the same card now appears in the desktop app's scan review queue (`ScannedCards`) with the same match.
7. Tap "Not this card", search by name, pick a different printing, and confirm: (a) the phone shows that printing on the result screen, and (b) the desktop's queued entry for that scan now reflects the corrected card.
8. Stop the desktop app, then capture again from the phone — confirm the phone shows "Desktop app not connected" rather than hanging.
9. Note in the PR description (or to whoever reviews this) that this step was performed and what was observed — this project's convention (per recent web-scanner-adjacent work) is that GUI/hardware-dependent flows are manually smoke-tested, not automated.

- [ ] **Step 4: No commit for this task** — it's verification only. If Step 3 surfaces a bug, fix it within the relevant task above (amend that task's files, add/adjust a test if the bug was in testable logic) and re-run Steps 1–3.
