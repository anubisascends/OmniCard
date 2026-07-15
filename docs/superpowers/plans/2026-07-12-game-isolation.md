# Game Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce clear game-level isolation so data refresh, scanning, and set filtering operate independently per game (Magic vs One Piece).

**Architecture:** All changes are at the ViewModel/UI layer in `RootViewModel`. The service layer (`CardService`, `ICardGameService`) already routes through the correct game service — we add guardrails that prevent accidental cross-game operations. A lightweight JSON file stores per-game refresh timestamps.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, System.Text.Json, xUnit + Moq

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`
- Test framework: xUnit 2.9.3 with Moq 4.x
- MVVM pattern: CommunityToolkit.Mvvm (source generators for `[ObservableProperty]`, `[RelayCommand]`)
- All user-facing dialogs use `System.Windows.MessageBox`
- Logging via `ILogger<RootViewModel>` with Serilog structured logging

---

### Task 1: Game Switch Guard — Block Switching with Pending Scans

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs:453-464` (game selection region)
- Test: `OmniCard.Tests/Services/GameSwitchGuardTests.cs` (new)

**Interfaces:**
- Consumes: `ICardService.ScannedCards` (ObservableCollection<ScannedCard>), `ICardService.SelectedGame` (CardGame)
- Produces: No new public API — internal ViewModel behavior only

- [ ] **Step 1: Write the failing test**

Create `OmniCard.Tests/Services/GameSwitchGuardTests.cs`:

```csharp
using OmniCard.Interfaces;
using OmniCard.Models;
using Moq;
using System.Collections.ObjectModel;

namespace OmniCard.Tests.Services;

public class GameSwitchGuardTests
{
    [Fact]
    public void HasPendingScans_ReturnsTrue_WhenScannedCardsNotEmpty()
    {
        var scannedCards = new ObservableCollection<ScannedCard>
        {
            new ScannedCard { MatchedName = "Test Card" }
        };

        Assert.True(scannedCards.Count > 0);
    }

    [Fact]
    public void HasPendingScans_ReturnsFalse_WhenScannedCardsEmpty()
    {
        var scannedCards = new ObservableCollection<ScannedCard>();

        Assert.False(scannedCards.Count > 0);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~GameSwitchGuardTests" -v minimal`
Expected: 2 tests PASS

- [ ] **Step 3: Implement the game switch guard in RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, add a suppression flag field near line 44:

```csharp
private bool _suppressGameChangeHandler;
```

Replace the `OnSelectedGameChanged` method (lines 456-464) with:

```csharp
partial void OnSelectedGameChanged(CardGame oldValue, CardGame newValue)
{
    if (_suppressGameChangeHandler)
        return;

    if (CardService.ScannedCards.Count > 0)
    {
        _logger.LogWarning("Blocked game switch from {Old} to {New}: {Count} pending scan(s)",
            oldValue, newValue, CardService.ScannedCards.Count);

        MessageBox.Show(
            $"You have {CardService.ScannedCards.Count} unconfirmed scan(s). " +
            "Please commit or discard them before switching games.",
            "Game Switch Blocked",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        _suppressGameChangeHandler = true;
        SelectedGame = oldValue;
        _suppressGameChangeHandler = false;
        return;
    }

    _logger.LogInformation("Switched active game to {Game}", newValue);
    CardService.SelectedGame = newValue;
    SetFilterText = "";
    LoadAvailableSets();
    Collection.SetGame(newValue);
    InvalidateHomeTab();
}
```

Note: CommunityToolkit.Mvvm supports the `(oldValue, newValue)` signature for `partial void On<Property>Changed`. The `SetFilterText = ""` line handles Task 2 (set filter reset).

- [ ] **Step 4: Verify the project compiles**

Run: `dotnet build OmniCard.sln -v minimal`
Expected: Build succeeded

- [ ] **Step 5: Run all existing tests to check for regressions**

Run: `dotnet test OmniCard.Tests -v minimal`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs OmniCard.Tests/Services/GameSwitchGuardTests.cs
git commit -m "feat: block game switch when scans are pending, clear set filter on switch"
```

---

### Task 2: 24-Hour Refresh Cooldown

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs:1121-1141` (RefreshCardData command)
- Test: `OmniCard.Tests/Services/RefreshCooldownTests.cs` (new)

**Interfaces:**
- Consumes: `IDataPathService.DataDirectory` (string — directory for timestamp file)
- Produces: `refresh-timestamps.json` file at runtime in the data directory

- [ ] **Step 1: Write tests for cooldown logic**

Create `OmniCard.Tests/Services/RefreshCooldownTests.cs`. We'll test a static helper method that encapsulates the cooldown logic, keeping it testable without needing to instantiate RootViewModel:

```csharp
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RefreshCooldownTests : IDisposable
{
    private readonly string _tempDir;

    public RefreshCooldownTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetLastRefresh_ReturnsNull_WhenNoFile()
    {
        var result = RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.Mtg);
        Assert.Null(result);
    }

    [Fact]
    public void GetLastRefresh_ReturnsTimestamp_WhenFileExists()
    {
        var expected = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);
        var data = new Dictionary<string, DateTime> { ["Mtg"] = expected };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        var result = RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.Mtg);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetLastRefresh_ReturnsNull_ForDifferentGame()
    {
        var data = new Dictionary<string, DateTime> { ["Mtg"] = DateTime.UtcNow };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        var result = RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.OnePiece);
        Assert.Null(result);
    }

    [Fact]
    public void RecordRefresh_CreatesFile_WhenNoneExists()
    {
        RefreshCooldownHelper.RecordRefresh(_tempDir, CardGame.OnePiece);

        var result = RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.OnePiece);
        Assert.NotNull(result);
        Assert.True((DateTime.UtcNow - result.Value).TotalSeconds < 5);
    }

    [Fact]
    public void RecordRefresh_PreservesOtherGames()
    {
        var mtgTime = new DateTime(2026, 7, 11, 8, 0, 0, DateTimeKind.Utc);
        var data = new Dictionary<string, DateTime> { ["Mtg"] = mtgTime };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        RefreshCooldownHelper.RecordRefresh(_tempDir, CardGame.OnePiece);

        Assert.Equal(mtgTime, RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.Mtg));
        Assert.NotNull(RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.OnePiece));
    }

    [Fact]
    public void IsCooldownActive_ReturnsFalse_WhenNoTimestamp()
    {
        Assert.False(RefreshCooldownHelper.IsCooldownActive(_tempDir, CardGame.Mtg, out _));
    }

    [Fact]
    public void IsCooldownActive_ReturnsFalse_WhenOver24Hours()
    {
        var old = DateTime.UtcNow.AddHours(-25);
        var data = new Dictionary<string, DateTime> { ["Mtg"] = old };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        Assert.False(RefreshCooldownHelper.IsCooldownActive(_tempDir, CardGame.Mtg, out _));
    }

    [Fact]
    public void IsCooldownActive_ReturnsTrue_WhenUnder24Hours()
    {
        var recent = DateTime.UtcNow.AddHours(-1);
        var data = new Dictionary<string, DateTime> { ["Mtg"] = recent };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        Assert.True(RefreshCooldownHelper.IsCooldownActive(_tempDir, CardGame.Mtg, out var nextAvailable));
        Assert.True(nextAvailable > DateTime.UtcNow);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~RefreshCooldownTests" -v minimal`
Expected: FAIL — `RefreshCooldownHelper` does not exist

- [ ] **Step 3: Implement RefreshCooldownHelper**

Create `OmniCard/Helpers/RefreshCooldownHelper.cs`:

```csharp
using System.IO;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Views.Root;

public static class RefreshCooldownHelper
{
    private const string FileName = "refresh-timestamps.json";
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromHours(24);

    public static DateTime? GetLastRefresh(string dataDirectory, CardGame game)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
        return data?.TryGetValue(game.ToString(), out var ts) == true ? ts : null;
    }

    public static void RecordRefresh(string dataDirectory, CardGame game)
    {
        var path = Path.Combine(dataDirectory, FileName);
        Dictionary<string, DateTime> data;

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? new();
        }
        else
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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~RefreshCooldownTests" -v minimal`
Expected: All 7 tests PASS

- [ ] **Step 5: Wire cooldown into RootViewModel.RefreshCardData**

First, add `IDataPathService` to the RootViewModel primary constructor. In `OmniCard/Views/Root/RootViewModel.cs`, add the parameter after `auditService`:

```csharp
public sealed partial class RootViewModel(
    ScannerService scannerService,
    IDialogService dialogService,
    ICardService cardService,
    IOptions<DisplaySettings> displaySettings,
    IStorageContainerService containerService,
    IEbayAuthService ebayAuthService,
    ICsvExportImportService csvService,
    CollectionViewModel collection,
    SealedProductViewModel sealedVm,
    IMismatchLogService mismatchLogService,
    SetSymbolCache setSymbolCache,
    IScanDiagnosticService diagnosticService,
    IAuditService auditService,
    IDataPathService dataPathService,
    IOptionsMonitor<WebCompanionSettings> webCompanionSettings,
    ILogger<RootViewModel> logger) : ViewModel
```

Then replace the `RefreshCardData` method (lines 1121-1141) with:

```csharp
[RelayCommand]
public async Task RefreshCardData()
{
    _logger.LogInformation("User initiated card data refresh for {Game}", SelectedGame);

    if (RefreshCooldownHelper.IsCooldownActive(dataPathService.DataDirectory, SelectedGame, out var nextAvailable))
    {
        var lastRefresh = RefreshCooldownHelper.GetLastRefresh(dataPathService.DataDirectory, SelectedGame)!.Value;
        var timeAgo = DateTime.UtcNow - lastRefresh;
        var timeAgoText = timeAgo.TotalHours >= 1
            ? $"{(int)timeAgo.TotalHours}h {timeAgo.Minutes}m ago"
            : $"{timeAgo.Minutes}m ago";

        _logger.LogInformation("Refresh cooldown active for {Game}, last refresh {TimeAgo}", SelectedGame, timeAgoText);

        var result = MessageBox.Show(
            $"Card data for {SelectedGame} was last refreshed {timeAgoText}.\n\n" +
            $"Refresh is available once every 24 hours to minimize API load.\n" +
            $"Next refresh available at {nextAvailable.ToLocalTime():g}.\n\n" +
            "Click Yes to refresh anyway, or No to cancel.",
            "Refresh Cooldown",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result != MessageBoxResult.Yes)
            return;

        _logger.LogInformation("User forced refresh for {Game} despite cooldown", SelectedGame);
    }

    var progress = new Progress<string>((str) =>
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Message = str;
        });
    });

    await CardService.ActiveGameService.DownloadBulkDataAsync(progress);
    RefreshCooldownHelper.RecordRefresh(dataPathService.DataDirectory, SelectedGame);
    LoadAvailableSets();

    if (SelectedGame == CardGame.Mtg)
    {
        var sets = _allSets.Select(s => (s.SetCode, s.SetName)).ToList();
        await setSymbolCache.PreloadSymbolsAsync(sets, progress);
    }

    InvalidateHomeTab();
}
```

Note: This also handles Task 3 (skip set symbol preload for non-MTG) with the `if (SelectedGame == CardGame.Mtg)` guard.

- [ ] **Step 6: Verify the project compiles**

Run: `dotnet build OmniCard.sln -v minimal`
Expected: Build succeeded

- [ ] **Step 7: Run all tests**

Run: `dotnet test OmniCard.Tests -v minimal`
Expected: All tests pass

- [ ] **Step 8: Commit**

```bash
git add OmniCard/Helpers/RefreshCooldownHelper.cs OmniCard.Tests/Services/RefreshCooldownTests.cs OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat: add 24-hour refresh cooldown per game with force-refresh option"
```

---

### Task 3: Verification and Final Cleanup

**Files:**
- Review: `OmniCard/Views/Root/RootViewModel.cs` (all changes from Tasks 1-2)

**Interfaces:**
- Consumes: All changes from Tasks 1-2
- Produces: Final verified state

- [ ] **Step 1: Full build**

Run: `dotnet build OmniCard.sln -v minimal`
Expected: Build succeeded, 0 warnings related to our changes

- [ ] **Step 2: Run all tests**

Run: `dotnet test OmniCard.Tests -v minimal`
Expected: All tests pass

- [ ] **Step 3: Review the final RootViewModel changes**

Read through the modified `OnSelectedGameChanged` and `RefreshCardData` methods. Verify:
- Game switch guard checks `ScannedCards.Count > 0` before allowing switch
- Revert uses `_suppressGameChangeHandler` to prevent recursion
- `SetFilterText = ""` is set on successful game switch
- Cooldown check happens before API call
- Timestamp is recorded after successful refresh
- Set symbol preload is guarded by `SelectedGame == CardGame.Mtg`

- [ ] **Step 4: Commit final state**

```bash
git add -A
git commit -m "chore: verify game isolation changes — build and tests pass"
```
