# Background Price Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refresh card price data for all sets of every game in the background on startup (non-blocking, throttled) and on a manual command, with splash + status-bar progress, updating the open collection's prices when done.

**Architecture:** A new price-only refresh method on each game service (MTG applies only `Prices` from its bulk stream; One Piece loops all sets, prices only), orchestrated by a singleton `PriceUpdateService` that throttles per game, exposes bindable progress, and raises a `PricesUpdated` event. Startup fires it non-blocking after `Host.Start()`; a menu command forces it. The collection re-pulls displayed prices on completion via `INotifyPropertyChanged` on `CollectionCard.MarketPrice`.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, EF Core (SQLite, per-game `DbContextFactory`), xUnit + Moq.

## Global Constraints

- Do not change any project's TFM or LangVersion.
- Do not alter the existing full bulk download behavior (`DownloadBulkDataAsync`) or card-matching logic; only add price-only paths.
- All sets, all games — no owned-only/per-set scoping (uniform behavior).
- Automatic startup refresh must not block app load; it runs after `Host.Start()` as fire-and-forget and its exceptions are swallowed+logged inside the service.
- Startup respects a 24h per-game throttle; the manual command bypasses it.
- Price refresh throttle state persists to `price-refresh-timestamps.json` — SEPARATE from the existing bulk-data `refresh-timestamps.json`.
- Progress/state mutations that back UI bindings must be marshaled to the UI thread; but the service must remain unit-testable without a running WPF app (guard `Application.Current?.Dispatcher`, run inline when null).
- Tests: xUnit in `OmniCard.Tests`; `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`. Build: `dotnet build OmniCard/OmniCard.csproj`.
- MTG price refresh streams the Scryfall bulk file and applies ONLY `Prices` to existing cards (no inserts, no image-hash recompute).

---

## File Structure

- `OmniCard.Shared/Models/PriceUpdateProgress.cs` — new progress record.
- `OmniCard.Shared/Interfaces/ICardGameService.cs` — add `UpdatePricesAsync`.
- `OmniCard.CardMatching/ScryfallService.cs` — implement price-only refresh (refactor a shared private import runner).
- `OmniCard.CardMatching/OptcgService.cs` — implement price-only refresh (extract a shared per-set fetch helper).
- `OmniCard/Helpers/PriceRefreshCooldownHelper.cs` — new 24h per-game throttle (own file).
- `OmniCard/Services/PriceUpdateService.cs` — new orchestrator singleton.
- `OmniCard/App.xaml.cs` — DI registration + startup hook.
- `OmniCard/Views/Root/RootViewModel.cs` — expose service, `RefreshPrices` command, wire `PricesUpdated`.
- `OmniCard/Views/Root/RootView.xaml` — menu item + status-bar indicator.
- `OmniCard.Shared/Models/CollectionCard.cs` — `INotifyPropertyChanged` for `MarketPrice`.
- `OmniCard/Views/Root/CollectionViewModel.cs` — `RefreshVisiblePrices()`.
- Tests: `OmniCard.Tests/Services/PriceRefreshCooldownHelperTests.cs`, `OmniCard.Tests/Services/PriceUpdateServiceTests.cs`.

---

## Task 1: `PriceUpdateProgress` + `PriceRefreshCooldownHelper`

**Files:**
- Create: `OmniCard.Shared/Models/PriceUpdateProgress.cs`
- Create: `OmniCard/Helpers/PriceRefreshCooldownHelper.cs`
- Test: `OmniCard.Tests/Services/PriceRefreshCooldownHelperTests.cs`

**Interfaces:**
- Produces: `public sealed record PriceUpdateProgress(CardGame Game, string? SetCode, int Completed, int Total, string Message)` in namespace `OmniCard.Models`.
- Produces: `PriceRefreshCooldownHelper` (static) with `DateTime? GetLastRefresh(string dataDir, CardGame)`, `void RecordRefresh(string dataDir, CardGame)`, `bool IsCooldownActive(string dataDir, CardGame, out DateTime nextAvailable)`; 24h window; file `price-refresh-timestamps.json`.

- [ ] **Step 1: Create the progress record**

`OmniCard.Shared/Models/PriceUpdateProgress.cs`:
```csharp
namespace OmniCard.Models;

/// <summary>Progress for a background price refresh. SetCode/Total are populated for
/// per-set sources (One Piece) and may be null/0 for bulk sources (MTG).</summary>
public sealed record PriceUpdateProgress(
    CardGame Game,
    string? SetCode,
    int Completed,
    int Total,
    string Message);
```

- [ ] **Step 2: Write the failing cooldown-helper tests**

`OmniCard.Tests/Services/PriceRefreshCooldownHelperTests.cs`:
```csharp
using OmniCard.Helpers;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class PriceRefreshCooldownHelperTests : IDisposable
{
    private readonly string _dir;

    public PriceRefreshCooldownHelperTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"pricecd-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void GetLastRefresh_NoFile_ReturnsNull()
    {
        Assert.Null(PriceRefreshCooldownHelper.GetLastRefresh(_dir, CardGame.Mtg));
    }

    [Fact]
    public void RecordThenIsCooldownActive_IsTrueImmediately()
    {
        PriceRefreshCooldownHelper.RecordRefresh(_dir, CardGame.Mtg);
        Assert.True(PriceRefreshCooldownHelper.IsCooldownActive(_dir, CardGame.Mtg, out var next));
        Assert.True(next > DateTime.UtcNow);
    }

    [Fact]
    public void IsCooldownActive_NoRecord_IsFalse()
    {
        Assert.False(PriceRefreshCooldownHelper.IsCooldownActive(_dir, CardGame.Mtg, out _));
    }

    [Fact]
    public void Record_IsPerGame()
    {
        PriceRefreshCooldownHelper.RecordRefresh(_dir, CardGame.Mtg);
        Assert.True(PriceRefreshCooldownHelper.IsCooldownActive(_dir, CardGame.Mtg, out _));
        Assert.False(PriceRefreshCooldownHelper.IsCooldownActive(_dir, CardGame.OnePiece, out _));
    }

    [Fact]
    public void UsesDedicatedPriceFile_NotBulkDataFile()
    {
        PriceRefreshCooldownHelper.RecordRefresh(_dir, CardGame.Mtg);
        Assert.True(File.Exists(Path.Combine(_dir, "price-refresh-timestamps.json")));
    }
}
```
Note: confirm the enum member name for One Piece in `OmniCard.Shared/Models/CardGame.cs` (the collection uses `CardGame.Mtg`; use the actual One Piece member — do NOT guess; read the enum and use the real name).

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~PriceRefreshCooldownHelperTests"`
Expected: FAIL to compile — `PriceRefreshCooldownHelper` does not exist.

- [ ] **Step 4: Implement the helper**

`OmniCard/Helpers/PriceRefreshCooldownHelper.cs` (mirror of `RefreshCooldownHelper` with a dedicated file name):
```csharp
using System.IO;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Helpers;

/// <summary>Per-game 24h throttle for background price refreshes. Persists to its own file so
/// it is independent of the bulk-data refresh cooldown (RefreshCooldownHelper).</summary>
public static class PriceRefreshCooldownHelper
{
    private const string FileName = "price-refresh-timestamps.json";
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromHours(24);

    public static DateTime? GetLastRefresh(string dataDirectory, CardGame game)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path));
            return data?.TryGetValue(game.ToString(), out var ts) == true ? ts : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void RecordRefresh(string dataDirectory, CardGame game)
    {
        var path = Path.Combine(dataDirectory, FileName);
        Dictionary<string, DateTime> data;
        try
        {
            data = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            data = new();
        }

        data[game.ToString()] = DateTime.UtcNow;
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsCooldownActive(string dataDirectory, CardGame game, out DateTime nextAvailable)
    {
        var last = GetLastRefresh(dataDirectory, game);
        if (last is null)
        {
            nextAvailable = default;
            return false;
        }

        nextAvailable = last.Value + CooldownPeriod;
        return DateTime.UtcNow < nextAvailable;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~PriceRefreshCooldownHelperTests"`
Expected: PASS (5 facts).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/PriceUpdateProgress.cs OmniCard/Helpers/PriceRefreshCooldownHelper.cs OmniCard.Tests/Services/PriceRefreshCooldownHelperTests.cs
git commit -m "feat: add PriceUpdateProgress and per-game price-refresh cooldown helper"
```

---

## Task 2: `ICardGameService.UpdatePricesAsync` + Scryfall implementation

**Files:**
- Modify: `OmniCard.Shared/Interfaces/ICardGameService.cs`
- Modify: `OmniCard.CardMatching/ScryfallService.cs`

**Interfaces:**
- Consumes: `PriceUpdateProgress` (Task 1).
- Produces: `Task ICardGameService.UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)`. Every `ICardGameService` implementer must define it (ScryfallService here; OptcgService in Task 3 — the solution will not build until both exist, so build verification for this task is scoped to the OmniCard.CardMatching project compiling the interface + Scryfall, expecting the OptcgService error until Task 3).

- [ ] **Step 1: Add the interface method**

In `OmniCard.Shared/Interfaces/ICardGameService.cs`, after `DownloadBulkDataAsync` (line 9), add:
```csharp
    Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default);
```

- [ ] **Step 2: Refactor Scryfall bulk import into a shared runner with a prices-only mode**

In `OmniCard.CardMatching/ScryfallService.cs`:

(a) Rename the body of `DownloadBulkDataAsync` into a private method and make the public method delegate. Replace the signature line `public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)` with:
```csharp
    public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        => RunBulkImportAsync(pricesOnly: false, progress, ct);

    private async Task RunBulkImportAsync(bool pricesOnly, IProgress<string>? progress, CancellationToken ct)
```
(the rest of the existing method body stays as-is except steps (b) and (c)).

(b) Thread `pricesOnly` into the batch upserts. Change both `UpsertBatchAsync(importContext, batch, existingIds, ct)` calls to `UpsertBatchAsync(importContext, batch, existingIds, pricesOnly, ct)`, and update `UpsertBatchAsync`'s signature and body:
```csharp
    private async Task<(int Inserted, int Updated)> UpsertBatchAsync(
        ScryfallDbContext context, List<Card> batch, HashSet<Guid> existingIds, bool pricesOnly, CancellationToken ct)
    {
        var newCards = new List<Card>();
        var existingCardIds = new List<Guid>();

        foreach (var card in batch)
        {
            if (existingIds.Contains(card.Id))
                existingCardIds.Add(card.Id);
            else
                newCards.Add(card);
        }

        // Update prices for existing cards
        if (existingCardIds.Count > 0)
        {
            var tracked = await context.Cards
                .Where(c => existingCardIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

            foreach (var card in batch)
            {
                if (tracked.TryGetValue(card.Id, out var existing))
                    existing.Prices = card.Prices;
            }

            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        // Prices-only refresh does not insert new cards (that is the full download's job).
        if (!pricesOnly && newCards.Count > 0)
        {
            foreach (var card in newCards)
                MapAllParts(card);

            context.Cards.AddRange(newCards);
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();

            foreach (var card in newCards)
                existingIds.Add(card.Id);
        }

        return (pricesOnly ? 0 : newCards.Count, existingCardIds.Count);
    }
```
Because `pricesOnly` yields `inserted == 0`, the existing tail guards `if (inserted > 0) { ...ComputeImageHashesAsync... }` (line ~1001) already skip hashing — leave them unchanged.

(c) Skip correction re-linking on a price-only run: change `RelinkOrphanedCorrections();` (line ~998) to:
```csharp
        if (!pricesOnly)
            RelinkOrphanedCorrections();
```

- [ ] **Step 3: Implement `UpdatePricesAsync` on ScryfallService**

Add (near `DownloadBulkDataAsync`):
```csharp
    public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        // MTG prices come as one daily bulk file; refresh applies only price fields to existing
        // cards (no inserts, no hashing). Bridges the string progress into PriceUpdateProgress.
        var bridge = progress is null
            ? null
            : new Progress<string>(msg => progress.Report(new PriceUpdateProgress(CardGame.Mtg, null, 0, 0, msg)));
        await RunBulkImportAsync(pricesOnly: true, bridge, ct);
    }
```
Confirm `using OmniCard.Models;` (or the namespace holding `PriceUpdateProgress`/`CardGame`) is present — it already is (the file uses `CardGame`).

- [ ] **Step 4: Build the CardMatching project (expect only the OptcgService missing-member error)**

Run: `dotnet build OmniCard.CardMatching/OmniCard.CardMatching.csproj`
Expected: FAIL with exactly one error class — `OptcgService does not implement interface member 'ICardGameService.UpdatePricesAsync'`. No other errors (confirms Scryfall + interface compile). Task 3 resolves this.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Shared/Interfaces/ICardGameService.cs OmniCard.CardMatching/ScryfallService.cs
git commit -m "feat: add price-only refresh (UpdatePricesAsync) to ICardGameService + Scryfall"
```

---

## Task 3: OptcgService price-only refresh

**Files:**
- Modify: `OmniCard.CardMatching/OptcgService.cs`

**Interfaces:**
- Consumes: `PriceUpdateProgress`, `ICardGameService.UpdatePricesAsync` (Task 2).
- Produces: `OptcgService.UpdatePricesAsync` — loops all sets, updates only `MarketPrice`/`InventoryPrice` on existing rows, per-set progress.

- [ ] **Step 1: Extract a shared per-set variant fetch helper**

In `OmniCard.CardMatching/OptcgService.cs`, extract the set-list + parallel per-set fetch (currently inline in `DownloadBulkDataAsync`, lines ~127-172) into a private helper, and call it from `DownloadBulkDataAsync`. Add:
```csharp
    // Fetches the full set list and, for each set, its card variants (mapped). Invokes
    // onSetCompleted(done, total, setCode) after each set finishes. Shared by the full
    // download and the price-only refresh.
    private async Task<List<OptcgCard>> FetchAllVariantsAsync(
        HttpClient client, JsonSerializerOptions jsonOptions,
        Action<int, int, string>? onSetCompleted, CancellationToken ct)
    {
        var setList = await client.GetFromJsonAsync<OptcgSetListResponse>(
            $"{ApiBaseUrl}/v1/sets", jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to fetch set list from poneglyph API.");

        _logger.LogInformation("Discovered {Count} OPTCG sets", setList.Data.Count);

        var allCards = new List<OptcgCard>();
        var cardsLock = new object();
        var fetchedSets = 0;

        await Parallel.ForEachAsync(setList.Data, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        }, async (set, token) =>
        {
            try
            {
                var detail = await client.GetFromJsonAsync<OptcgSetDetailResponse>(
                    $"{ApiBaseUrl}/v1/sets/{set.Code}", jsonOptions, token);
                if (detail is null)
                {
                    _logger.LogWarning("Set {SetCode} returned no detail; skipping", set.Code);
                    return;
                }

                var rows = detail.Data.Cards
                    .SelectMany(card => card.Variants.Select(v => MapVariant(card, v)))
                    .ToList();

                lock (cardsLock)
                    allCards.AddRange(rows);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch OPTCG set {SetCode}; skipping", set.Code);
            }
            finally
            {
                var done = Interlocked.Increment(ref fetchedSets);
                onSetCompleted?.Invoke(done, setList.Data.Count, set.Code);
            }
        });

        return allCards;
    }
```
Then in `DownloadBulkDataAsync`, replace the inline set-list fetch + `Parallel.ForEachAsync` block (lines ~127-172) with:
```csharp
        var allCards = await FetchAllVariantsAsync(client, jsonOptions,
            (done, total, _) => progress?.Report($"Fetched {done}/{total} sets..."), ct);
```
Leave the rest of `DownloadBulkDataAsync` (dedupe, upsert, hashing) unchanged.

- [ ] **Step 2: Implement `UpdatePricesAsync`**

Add:
```csharp
    public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting OPTCG price-only refresh");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var allCards = await FetchAllVariantsAsync(client, jsonOptions,
            (done, total, setCode) => progress?.Report(
                new PriceUpdateProgress(CardGame.OnePiece, setCode, done, total,
                    $"One Piece prices: {done}/{total} sets")), ct);

        var deduped = allCards
            .GroupBy(c => c.CardSetId)
            .Select(g => g.Last())
            .ToList();

        await using var context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();

        var updated = 0;
        foreach (var batch in deduped.Chunk(500))
        {
            var ids = batch.Select(c => c.CardSetId).ToList();
            var tracked = await context.Cards
                .Where(c => ids.Contains(c.CardSetId))
                .ToDictionaryAsync(c => c.CardSetId, ct);

            foreach (var card in batch)
            {
                if (tracked.TryGetValue(card.CardSetId, out var existing))
                {
                    existing.MarketPrice = card.MarketPrice;
                    existing.InventoryPrice = card.InventoryPrice;
                    updated++;
                }
            }

            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        // Swap the read context so reads see the refreshed prices.
        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        oldContext.Dispose();

        _logger.LogInformation("OPTCG price refresh complete: {Updated} cards updated", updated);
        progress?.Report(new PriceUpdateProgress(CardGame.OnePiece, null, 0, 0,
            $"One Piece prices updated ({updated} cards)"));
    }
```
Use the correct One Piece `CardGame` enum member (read `CardGame.cs`; do not guess). Confirm `_readContext`'s cache fields (`_hashCache`, etc.) do NOT need clearing here — prices don't affect hashes, so only the read context swap is needed (matching the price-only intent). If reads use `_readContext` for prices, the swap suffices; if `GetCurrentPrices` opens its own context (it does — `using var ctx = _dbContextFactory.CreateDbContext()` per the current code), the swap is harmless and optional but kept for consistency.

- [ ] **Step 3: Build the solution**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded, 0 errors (Scryfall + OPTCG now both implement the interface).

- [ ] **Step 4: Commit**

```bash
git add OmniCard.CardMatching/OptcgService.cs
git commit -m "feat: add price-only per-set refresh to OptcgService"
```

---

## Task 4: `PriceUpdateService` orchestrator + DI

**Files:**
- Create: `OmniCard/Services/PriceUpdateService.cs`
- Modify: `OmniCard/App.xaml.cs` (registration only)
- Test: `OmniCard.Tests/Services/PriceUpdateServiceTests.cs`

**Interfaces:**
- Consumes: `IEnumerable<ICardGameService>`, `IDataPathService`, `ILogger<PriceUpdateService>`, `PriceRefreshCooldownHelper`.
- Produces: `PriceUpdateService` (singleton, `INotifyPropertyChanged`): `Task RunAsync(bool force, CancellationToken ct = default)`; properties `bool IsRunning`, `string StatusText`, `int Completed`, `int Total`; `event EventHandler PricesUpdated`. Single-run guarded; per-game cooldown honored unless `force`; per-game failures isolated; timestamp recorded only on a game's success; `PricesUpdated` raised once per run if ≥1 game refreshed.

- [ ] **Step 1: Write the failing orchestrator tests**

`OmniCard.Tests/Services/PriceUpdateServiceTests.cs`:
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;
using Xunit;

namespace OmniCard.Tests.Services;

public class PriceUpdateServiceTests : IDisposable
{
    private readonly string _dir;

    public PriceUpdateServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"priceupd-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class FakePathService : IDataPathService
    {
        public FakePathService(string dir) => DataDirectory = dir;
        public string DataDirectory { get; }
        // Implement the rest of IDataPathService as needed by returning paths under DataDirectory
        // or throwing NotImplementedException for members unused by PriceUpdateService.
    }

    private sealed class FakeGameService : ICardGameService
    {
        public FakeGameService(CardGame game) => Game = game;
        public CardGame Game { get; }
        public int UpdateCalls { get; private set; }
        public bool ShouldThrow { get; set; }
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
        {
            UpdateCalls++;
            if (ShouldThrow) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }
        // All other ICardGameService members: throw new NotImplementedException();
    }

    private PriceUpdateService Create(params FakeGameService[] games) =>
        new(games, new FakePathService(_dir), NullLogger<PriceUpdateService>.Instance);

    [Fact]
    public async Task RunAsync_Force_InvokesEveryGame_AndRaisesPricesUpdated()
    {
        var mtg = new FakeGameService(CardGame.Mtg);
        var op = new FakeGameService(CardGame.OnePiece);
        var svc = Create(mtg, op);
        var raised = 0;
        svc.PricesUpdated += (_, _) => raised++;

        await svc.RunAsync(force: true);

        Assert.Equal(1, mtg.UpdateCalls);
        Assert.Equal(1, op.UpdateCalls);
        Assert.Equal(1, raised);
        Assert.False(svc.IsRunning);
    }

    [Fact]
    public async Task RunAsync_RespectsCooldown_WhenNotForced()
    {
        var mtg = new FakeGameService(CardGame.Mtg);
        var svc = Create(mtg);
        await svc.RunAsync(force: true);   // records timestamp
        await svc.RunAsync(force: false);  // within cooldown -> skipped

        Assert.Equal(1, mtg.UpdateCalls);
    }

    [Fact]
    public async Task RunAsync_ForceBypassesCooldown()
    {
        var mtg = new FakeGameService(CardGame.Mtg);
        var svc = Create(mtg);
        await svc.RunAsync(force: true);
        await svc.RunAsync(force: true);

        Assert.Equal(2, mtg.UpdateCalls);
    }

    [Fact]
    public async Task RunAsync_IsolatesPerGameFailure()
    {
        var mtg = new FakeGameService(CardGame.Mtg) { ShouldThrow = true };
        var op = new FakeGameService(CardGame.OnePiece);
        var svc = Create(mtg, op);

        await svc.RunAsync(force: true);   // must not throw

        Assert.Equal(1, op.UpdateCalls);   // second game still ran
    }

    [Fact]
    public async Task RunAsync_FailedGame_NotMarkedCooldown_SoRetryRuns()
    {
        var mtg = new FakeGameService(CardGame.Mtg) { ShouldThrow = true };
        var svc = Create(mtg);
        await svc.RunAsync(force: false);  // fails, no timestamp recorded
        await svc.RunAsync(force: false);  // not in cooldown -> runs again

        Assert.Equal(2, mtg.UpdateCalls);
    }
}
```
Note: implement `FakePathService`/`FakeGameService` unused members as `throw new NotImplementedException();` (or minimal). Read `IDataPathService` to satisfy its members compilably.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~PriceUpdateServiceTests"`
Expected: FAIL to compile — `PriceUpdateService` does not exist.

- [ ] **Step 3: Implement `PriceUpdateService`**

`OmniCard/Services/PriceUpdateService.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using OmniCard.Helpers;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Services;

/// <summary>Orchestrates background price refreshes across all game services. Throttled per
/// game (unless forced), single-run guarded, and surfaces bindable progress for the status bar.</summary>
public sealed class PriceUpdateService : INotifyPropertyChanged
{
    private readonly IEnumerable<ICardGameService> _gameServices;
    private readonly IDataPathService _dataPath;
    private readonly ILogger<PriceUpdateService> _logger;
    private readonly object _gate = new();
    private Task? _current;

    public PriceUpdateService(
        IEnumerable<ICardGameService> gameServices,
        IDataPathService dataPath,
        ILogger<PriceUpdateService> logger)
    {
        _gameServices = gameServices;
        _dataPath = dataPath;
        _logger = logger;
    }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private int _completed;
    public int Completed { get => _completed; private set => Set(ref _completed, value); }

    private int _total;
    public int Total { get => _total; private set => Set(ref _total, value); }

    public event EventHandler? PricesUpdated;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task RunAsync(bool force, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_current is { IsCompleted: false })
                return _current;              // single-run guard
            _current = RunCoreAsync(force, ct);
            return _current;
        }
    }

    private async Task RunCoreAsync(bool force, CancellationToken ct)
    {
        IsRunning = true;
        var anyUpdated = false;
        try
        {
            foreach (var svc in _gameServices)
            {
                ct.ThrowIfCancellationRequested();

                if (!force && PriceRefreshCooldownHelper.IsCooldownActive(_dataPath.DataDirectory, svc.Game, out _))
                {
                    _logger.LogInformation("Price refresh skipped for {Game} (within 24h cooldown)", svc.Game);
                    continue;
                }

                try
                {
                    StatusText = $"Updating {svc.Game} prices...";
                    var progress = new Progress<PriceUpdateProgress>(OnProgress);
                    await svc.UpdatePricesAsync(progress, ct);
                    PriceRefreshCooldownHelper.RecordRefresh(_dataPath.DataDirectory, svc.Game);
                    anyUpdated = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Price refresh failed for {Game}", svc.Game);
                    StatusText = $"{svc.Game} price update failed";
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Price refresh cancelled");
        }
        finally
        {
            IsRunning = false;
            Completed = 0;
            Total = 0;
            if (anyUpdated)
            {
                StatusText = "Prices updated";
                RaisePricesUpdated();
            }
        }
    }

    private void OnProgress(PriceUpdateProgress p)
    {
        Completed = p.Completed;
        Total = p.Total;
        StatusText = p.Message;
    }

    private void RaisePricesUpdated() => RunOnUi(() => PricesUpdated?.Invoke(this, EventArgs.Empty));

    // Marshal to the UI thread when a WPF app is running; run inline under unit tests.
    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        RunOnUi(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
    }
}
```
Note: `new Progress<PriceUpdateProgress>(OnProgress)` created on whatever thread RunCoreAsync runs; `OnProgress` sets properties which marshal via `Set`→`RunOnUi`, so binding notifications are UI-safe regardless. If `Application.Current` is null (tests), everything runs inline.

- [ ] **Step 4: Register in DI**

In `OmniCard/App.xaml.cs`, in the main `ConfigureServices` block (after the game-service registrations, near line 106), add:
```csharp
            services.AddSingleton<Services.PriceUpdateService>();
```
(Confirm/add `using OmniCard.Services;` or use the fully-qualified name as shown.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~PriceUpdateServiceTests"`
Expected: PASS (5 facts).

- [ ] **Step 6: Build the app**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Services/PriceUpdateService.cs OmniCard/App.xaml.cs OmniCard.Tests/Services/PriceUpdateServiceTests.cs
git commit -m "feat: add PriceUpdateService orchestrator with per-game throttle"
```

---

## Task 5: Startup hook

**Files:**
- Modify: `OmniCard/App.xaml.cs` (`OnStartup`)

**Interfaces:**
- Consumes: `PriceUpdateService` (Task 4).

- [ ] **Step 1: Fire the background refresh from `OnStartup`**

In `OmniCard/App.xaml.cs`, in `OnStartup`, replace the final block (lines ~303-314) — from `splash.SetStatus("Starting application...");` through `splash.Close();` — with:
```csharp
        splash.SetStatus("Starting application...");
        Host.Start();

        // Start phone scanner connection (non-blocking)
        var webScanner = Host.Services.GetRequiredService<WebScannerService>();
        _ = webScanner.StartAsync();

        // Initialize set symbol converter with cached service
        var setSymbolCache = Host.Services.GetRequiredService<SetSymbolCache>();
        SetSymbol.Initialize(setSymbolCache);

        // Kick off background price refresh (non-blocking; throttled per game; continues after splash closes)
        splash.SetStatus("Updating card prices in background...");
        var priceUpdater = Host.Services.GetRequiredService<Services.PriceUpdateService>();
        _ = priceUpdater.RunAsync(force: false);

        splash.Close();
```

- [ ] **Step 2: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Manual verification (human)**

Launch the app. Confirm: the splash briefly shows "Updating card prices in background...", the app opens without waiting for the refresh, and (with network) a refresh runs in the background. Report as PENDING human verification (do not launch the GUI from an automated agent).

- [ ] **Step 4: Commit**

```bash
git add OmniCard/App.xaml.cs
git commit -m "feat: kick off background price refresh on startup (non-blocking)"
```

---

## Task 6: Status-bar indicator + manual refresh command + menu

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs`
- Modify: `OmniCard/Views/Root/RootView.xaml`

**Interfaces:**
- Consumes: `PriceUpdateService` (Task 4).
- Produces: `RootViewModel.PriceUpdates` (the `PriceUpdateService` instance, for binding) and `RootViewModel.RefreshPricesCommand`.

- [ ] **Step 1: Inject and expose the service on RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, add `PriceUpdateService priceUpdateService` to the primary constructor parameter list (RootViewModel uses CommunityToolkit primary-constructor DI like the other services — match the existing style; see how `setSymbolCache`/`dataPathService` are declared). Expose it for binding:
```csharp
    public PriceUpdateService PriceUpdates => priceUpdateService;
```
Add the required `using OmniCard.Services;` if not present.

- [ ] **Step 2: Add the manual refresh command**

In `OmniCard/Views/Root/RootViewModel.cs`, near `RefreshCardData` (line ~1309), add:
```csharp
    [RelayCommand]
    public async Task RefreshPrices()
    {
        _logger.LogInformation("User initiated manual price refresh (all games)");
        await priceUpdateService.RunAsync(force: true);
    }
```

- [ ] **Step 3: Add the menu item**

In `OmniCard/Views/Root/RootView.xaml`, in the `_Collection` menu right after the "Refresh Card _Data..." item (line ~169-170), add:
```xml
                <MenuItem Header="Refresh _Prices"
                          Command="{Binding ViewModel.RefreshPricesCommand}"/>
```

- [ ] **Step 4: Add the status-bar indicator**

In `OmniCard/Views/Root/RootView.xaml`, in the `StatusBar` (line ~254), add a new `StatusBarItem` after the `Message` item (line ~256):
```xml
            <StatusBarItem Content="{Binding ViewModel.PriceUpdates.StatusText}"
                           Visibility="{Binding ViewModel.PriceUpdates.IsRunning, Converter={conv:BoolToVisibilityConverter}}"/>
```
(`conv:BoolToVisibilityConverter` is already used in this file / project. Confirm the `conv` xmlns is declared in RootView.xaml; if not, reuse the existing converter namespace prefix already declared there.)

- [ ] **Step 5: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Manual verification (human)**

Launch. While a refresh runs, the status bar shows "Updating … prices…"; use Collection ▸ Refresh Prices to force a run (bypasses the 24h throttle). Report as PENDING human verification.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/RootView.xaml
git commit -m "feat: manual Refresh Prices command + status-bar price-update indicator"
```

---

## Task 7: Reflect updated prices in the open collection

**Files:**
- Modify: `OmniCard.Shared/Models/CollectionCard.cs`
- Modify: `OmniCard/Views/Root/CollectionViewModel.cs`
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (wire event)

**Interfaces:**
- Consumes: `PriceUpdateService.PricesUpdated` (Task 4).
- Produces: `CollectionCard : INotifyPropertyChanged` (MarketPrice raises change); `CollectionViewModel.RefreshVisiblePrices()`.

- [ ] **Step 1: Make `CollectionCard.MarketPrice` observable**

In `OmniCard.Shared/Models/CollectionCard.cs`, make the class implement `INotifyPropertyChanged` and convert `MarketPrice` to a notifying property (leave all other members unchanged):
```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace OmniCard.Models;

public class CollectionCard : INotifyPropertyChanged
{
    // ... all existing members unchanged ...

    /// <summary>Cached market price for display and sorting. Not persisted.</summary>
    [NotMapped]
    public decimal MarketPrice
    {
        get => _marketPrice;
        set
        {
            if (_marketPrice == value) return;
            _marketPrice = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarketPrice)));
        }
    }
    private decimal _marketPrice;

    public event PropertyChangedEventHandler? PropertyChanged;
}
```
Keep the existing `[NotMapped] Quantity` and everything else as-is. (EF Core is unaffected — `MarketPrice` is `[NotMapped]`, and INPC classes work with EF.)

- [ ] **Step 2: Add `RefreshVisiblePrices` to CollectionViewModel**

In `OmniCard/Views/Root/CollectionViewModel.cs`, add:
```csharp
    /// <summary>Re-pull prices for the currently displayed cards (no DB re-search) after a
    /// background price refresh. Prices are read off the UI thread, then applied on it so the
    /// observable MarketPrice change updates tiles in place.</summary>
    public void RefreshVisiblePrices()
    {
        if (!ShowCardList) return;
        var results = CollectionSearchResults;
        if (results.Count == 0) return;

        _ = Task.Run(() =>
        {
            var prices = FetchBatchPrices(results); // reads refreshed DB prices; also sets card.MarketPrice
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MarketPrices = prices;
                OnPropertyChanged(nameof(FilteredMarketValue));
            });
        });
    }
```
Note: `FetchBatchPrices` (existing) both returns the price dict and assigns `card.MarketPrice` per card. With MarketPrice now observable, assigning it raises PropertyChanged; the assignment happens on the `Task.Run` thread, but WPF data binding marshals bound-property updates and this is the same cross-thread pattern the codebase already tolerates for `MarketPrice` being set during search. If the reviewer flags cross-thread INPC as a risk, adjust so the per-card assignment happens inside the `Dispatcher.Invoke` (compute the dict off-thread, apply `card.MarketPrice` on the UI thread). Prefer the UI-thread-apply variant:
```csharp
        _ = Task.Run(() =>
        {
            var prices = new Dictionary<int, decimal>();
            foreach (var g in results.GroupBy(c => c.Game))
            {
                var gs = _cardService.GetGameService(g.Key);
                foreach (var fg in g.GroupBy(c => c.IsFoil))
                {
                    var batch = gs.GetCurrentPrices(fg.Select(c => c.GameCardId), fg.Key);
                    foreach (var c in fg)
                        prices[c.Id] = batch.GetValueOrDefault(c.GameCardId);
                }
            }
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var c in results)
                    if (prices.TryGetValue(c.Id, out var p)) c.MarketPrice = p;
                MarketPrices = prices;
                OnPropertyChanged(nameof(FilteredMarketValue));
            });
        });
```
Use this UI-thread-apply variant (it avoids cross-thread INPC).

- [ ] **Step 3: Wire the event in RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, subscribe to the service after construction (in `Initialize()` or the constructor body — match where RootViewModel does its other wiring). Add:
```csharp
        priceUpdateService.PricesUpdated += (_, _) =>
            Application.Current?.Dispatcher.Invoke(() => collectionViewModel.RefreshVisiblePrices());
```
Use the RootViewModel's existing reference to the `CollectionViewModel` (it already composes it — find the field/param name and use it). `PriceUpdateService` already marshals `PricesUpdated` to the UI thread, so the extra `Dispatcher.Invoke` is defensive.

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Run the full suite (regression)**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS (existing tests + Task 1 + Task 4 tests).

- [ ] **Step 6: Manual verification (human)**

With the collection open and prices visible, trigger a manual refresh; on completion the visible tiles' prices update in place (no re-search, no scroll jump). Report as PENDING human verification.

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Models/CollectionCard.cs OmniCard/Views/Root/CollectionViewModel.cs OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat: refresh open collection prices in place when a price update completes"
```

---

## Task 8: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: Full build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded, 0 errors, no new warnings from this work.

- [ ] **Step 2: Full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: All PASS.

- [ ] **Step 3: End-to-end manual scenario (human)**

Use the `verify` skill. Cold start (delete `price-refresh-timestamps.json` first): splash notes the update, app loads without waiting, status bar shows progress then "Prices updated", open-collection prices update. Restart within 24h: startup refresh is skipped (log line). Collection ▸ Refresh Prices: forces a run regardless of cooldown. One Piece shows per-set progress; MTG shows an overall message.

- [ ] **Step 4: Final commit (if verification fixes were needed)**

```bash
git add -A
git commit -m "test: verify background price update end-to-end"
```

---

## Self-Review Notes

- **Spec coverage:** §1 UpdatePricesAsync → Tasks 2–3. §2 PriceUpdateService → Task 4. §3 startup hook → Task 5. §4 status indicator → Task 6. §5 throttle → Task 1 (+ used in Task 4). §6 manual command → Task 6. §7 reflect-in-view → Task 7.
- **Placeholder scan:** none — every code step has full code. Two "read the enum / read IDataPathService" notes are explicit instructions, not placeholders.
- **Type consistency:** `UpdatePricesAsync(IProgress<PriceUpdateProgress>?, CancellationToken)` identical across interface (Task 2), Scryfall (Task 2), OPTCG (Task 3), and the fake (Task 4). `PriceUpdateService.RunAsync(bool, CancellationToken)` / `PricesUpdated` / `IsRunning` / `StatusText` consistent across Tasks 4/5/6/7.
- **Not unit-testable, verified manually (by design):** service network implementations (Tasks 2–3), XAML/startup (Tasks 5–6), in-place UI refresh (Task 7). Pure logic (cooldown helper, orchestration with fakes) is unit-tested.
- **Known assumptions flagged for implementers:** exact One Piece `CardGame` enum member and `IDataPathService` surface must be read, not guessed; RootViewModel's DI/constructor style and its `CollectionViewModel` reference name must be matched to existing code.
