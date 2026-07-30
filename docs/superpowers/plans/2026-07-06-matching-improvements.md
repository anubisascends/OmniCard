# Scan Matching Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve card matching confidence and diagnostic visibility based on diagnostic analysis of a real 250-card MOM scan session — fix OCR logging gaps, raise match tolerance for scanner noise, blend art hash into confidence, and tighten tie zones.

**Architecture:** Six targeted fixes to the existing matching pipeline in `ScryfallService.FindClosestMatch()`, `CardSevice.AddFromStream()`, `OcrMatchingService`, and `ScanDiagnosticService`. No new files — all changes modify existing code. Constants and thresholds are tuned based on empirical diagnostic data.

**Tech Stack:** WPF (.NET 10), EF Core 10 + SQLite, CommunityToolkit.Mvvm

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`
- `CardSevice` filename typo ("Sevice") is intentional — do not rename
- `FindClosestMatch` runs on scanner background thread — thread safety matters
- All diagnostic calls wrapped in try/catch — diagnostics must never break scanning
- Existing test patterns: in-memory SQLite with `SqliteConnection("Data Source=:memory:")`, `TestScryfallDbFactory` for ScryfallDbContext tests
- The 1 pre-existing test failure (`ScryfallCorrectionTests.FindClosestMatch_ConfidentHash_IgnoresFuzzyCorrection`) is unrelated — ignore it

---

### Task 1: Raise maxDistance, ConfidentHashThreshold, and Auto-Flag Threshold

**Files:**
- Modify: `OmniCard/Services/ScryfallService.cs:111,212,430`
- Modify: `OmniCard/Services/CardSevice.cs:302`
- Modify: `OmniCard.Tests/Services/TieZoneScoringTests.cs` (update any tests using maxDistance=10)
- Modify: `OmniCard.Tests/Services/ScryfallServiceTests.cs` (update any tests using maxDistance=10)

**Interfaces:**
- Produces: `maxDistance` default = 14 (was 10), `ConfidentHashThreshold` = 8 (was 6), auto-flag at confidence `< 15` (was `< 20`)

- [ ] **Step 1: Change maxDistance default parameter**

In `OmniCard/Services/ScryfallService.cs` line 111, change:

```csharp
    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 10)
```

To:

```csharp
    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14)
```

Also update the `ICardGameService` interface default if it specifies one. And update `OptcgService.FindClosestMatch` similarly.

- [ ] **Step 2: Change ConfidentHashThreshold**

In `OmniCard/Services/ScryfallService.cs` line 430, change:

```csharp
        const int ConfidentHashThreshold = 6;
```

To:

```csharp
        const int ConfidentHashThreshold = 8;
```

- [ ] **Step 3: Change auto-flag threshold**

In `OmniCard/Services/CardSevice.cs` line 302, change:

```csharp
            : match.Confidence is not null and < 20
```

To:

```csharp
            : match.Confidence is not null and < 15
```

Also update the OCR auto-unflag threshold at line 366 from `>= 20` to `>= 15`:

```csharp
                            if (ocrMatch.Confidence is null or >= 15)
```

- [ ] **Step 4: Update existing tests for new defaults**

Search the test project for `maxDistance` parameter usage and update tests that explicitly pass `maxDistance: 10` or that rely on the default being 10. Tests that explicitly pass their own maxDistance don't need changes — only those relying on the default.

Run: `dotnet test OmniCard.Tests`
Fix any failures caused by the threshold changes. Common patterns:
- Tests expecting confidence values computed with maxDistance=10 need updating to maxDistance=14
- Tests checking auto-flag at confidence < 20 need updating to < 15
- Tests checking ConfidentHashThreshold at 6 need updating to 8

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build && dotnet test OmniCard.Tests`
Expected: All tests pass except the 1 pre-existing failure.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/ScryfallService.cs OmniCard/Services/CardSevice.cs OmniCard/Services/OptcgService.cs OmniCard.Tests/
git commit -m "feat: raise maxDistance to 14 and ConfidentHashThreshold to 8 for scanner noise tolerance"
```

---

### Task 2: Tighten Tie Zone

**Files:**
- Modify: `OmniCard/Services/ScryfallService.cs:212`

**Interfaces:**
- Consumes: `maxDistance = 14` from Task 1
- Produces: `TieZone = 2` (was 4), max 10 candidates in tie zone

- [ ] **Step 1: Change TieZone constant**

In `OmniCard/Services/ScryfallService.cs` line 212, change:

```csharp
        const int TieZone = 4;
```

To:

```csharp
        const int TieZone = 2;
```

- [ ] **Step 2: Add tie zone cap after candidate collection**

After the `pHashCandidates` loop (around line 238, after the deprioritized set penalty block), add a cap:

```csharp
        // Cap tie zone to top 10 candidates by distance
        if (pHashCandidates.Count > 10)
        {
            pHashCandidates = pHashCandidates
                .OrderBy(c => c.Distance)
                .Take(10)
                .ToList();
        }
```

This goes after the preferred set bonus and deprioritized set penalty are applied, but before art hash disambiguation.

- [ ] **Step 3: Build and run tests**

Run: `dotnet build && dotnet test OmniCard.Tests`
Expected: All tests pass. Update any tie zone tests that expect TieZone=4 behavior.

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Services/ScryfallService.cs OmniCard.Tests/
git commit -m "feat: tighten tie zone from 4 to 2 with 10-candidate cap"
```

---

### Task 3: Blend Art Hash into Confidence

**Files:**
- Modify: `OmniCard/Services/ScryfallService.cs:433,538-541`
- Create or modify: test file for blended confidence

**Interfaces:**
- Consumes: `maxDistance = 14` from Task 1, art hash caches from existing code
- Produces: Blended confidence calculation at Phase 3 return and final return

- [ ] **Step 1: Write failing tests for blended confidence**

Add to an appropriate test file (e.g., `OmniCard.Tests/Services/ArtHashMatchingTests.cs` or create a new section in existing tests):

```csharp
    [Fact]
    public void BlendedConfidence_WithArtHash_HigherThanPHashAlone()
    {
        // Setup: card with pHash distance 8, art hash distance 4
        // With maxDistance=14:
        //   pHashConfidence = (1 - 8/14) * 100 = 42.9%
        //   artConfidence = (1 - 4/20) * 100 = 80%
        //   blended = 0.5 * 42.9 + 0.5 * 80 = 61.4%
        // Without blending: 42.9%
        // Test that the returned confidence is higher than pHash alone

        // Create a card in the test DB with known image hash and art hash
        // Compute a scan hash that is distance 8 from the card hash
        // Compute art hashes that are distance 4 from the card's art hash
        // Call FindClosestMatch and verify confidence > 50
    }
```

The exact test setup depends on the test infrastructure. The key assertion: when art hash distance is low, confidence should be higher than pHash-only confidence.

- [ ] **Step 2: Implement blended confidence at the final return**

In `OmniCard/Services/ScryfallService.cs`, replace lines 538-541:

```csharp
        // Confidence is always based on pHash distance of the winning card
        var winnerHash = hashCache.FirstOrDefault(h => h.Id == bestPHashId).Hash;
        var winnerDistance = winnerHash != 0 ? PerceptualHashService.HammingDistance(imageHash, winnerHash) : maxDistance;
        var matchConfidence = Math.Max(0, (1.0 - (double)winnerDistance / maxDistance)) * 100;
```

With:

```csharp
        // Blended confidence: pHash + art hash when available
        var winnerHash = hashCache.FirstOrDefault(h => h.Id == bestPHashId).Hash;
        var winnerDistance = winnerHash != 0 ? PerceptualHashService.HammingDistance(imageHash, winnerHash) : maxDistance;
        var pHashConfidence = Math.Max(0, (1.0 - (double)winnerDistance / maxDistance)) * 100;

        double matchConfidence = pHashConfidence;
        if (artHashes is not null && artHashCache.Count > 0)
        {
            var winnerArtHash = artHashCache.FirstOrDefault(a => a.Id == bestPHashId);
            if (winnerArtHash != default)
            {
                var bestArtDist = artHashes.Where(h => h != 0)
                    .Select(h => PerceptualHashService.HammingDistance(h, winnerArtHash.ArtHash))
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();
                if (bestArtDist < int.MaxValue)
                {
                    var artConfidence = Math.Max(0, (1.0 - (double)bestArtDist / 20.0)) * 100;
                    matchConfidence = 0.5 * pHashConfidence + 0.5 * artConfidence;
                }
            }
        }
```

- [ ] **Step 3: Implement blended confidence at Phase 3 return**

In `OmniCard/Services/ScryfallService.cs`, replace lines 433-435:

```csharp
            var confidence = Math.Max(0, (1.0 - (double)bestPHashDistance / maxDistance)) * 100;
            _logger.LogDebug("Confident hash match at distance {Distance} (confidence {Confidence:F0}%)", bestPHashDistance, confidence);
            var confidentCard = LookupCard(bestPHashId, confidence);
```

With:

```csharp
            var pHashConf = Math.Max(0, (1.0 - (double)bestPHashDistance / maxDistance)) * 100;
            double confidence = pHashConf;
            if (artHashes is not null && artHashCache.Count > 0)
            {
                var winnerArt = artHashCache.FirstOrDefault(a => a.Id == bestPHashId);
                if (winnerArt != default)
                {
                    var bestArtDist = artHashes.Where(h => h != 0)
                        .Select(h => PerceptualHashService.HammingDistance(h, winnerArt.ArtHash))
                        .DefaultIfEmpty(int.MaxValue)
                        .Min();
                    if (bestArtDist < int.MaxValue)
                    {
                        var artConf = Math.Max(0, (1.0 - (double)bestArtDist / 20.0)) * 100;
                        confidence = 0.5 * pHashConf + 0.5 * artConf;
                    }
                }
            }
            _logger.LogDebug("Confident hash match at distance {Distance} (confidence {Confidence:F0}%)", bestPHashDistance, confidence);
            var confidentCard = LookupCard(bestPHashId, confidence);
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet build && dotnet test OmniCard.Tests`
Expected: All tests pass. Some confidence-assertion tests may need updating since confidence values will be higher when art hashes are present.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Services/ScryfallService.cs OmniCard.Tests/
git commit -m "feat: blend art hash into confidence calculation (50/50 pHash + artHash)"
```

---

### Task 4: Always Log OCR Results to Diagnostics

**Files:**
- Modify: `OmniCard/Services/CardSevice.cs:334-387`

**Interfaces:**
- Consumes: `IScanDiagnosticService.LogScanCompleted()` from existing code
- Produces: OCR results always appear in diagnostic events

- [ ] **Step 1: Restructure the OCR callback to always log**

In `OmniCard/Services/CardSevice.cs`, inside the `BeginInvoke` callback (lines 334-387), restructure so the diagnostic log happens after OCR regardless of whether it changed the match.

Replace the OCR block (lines 338-386) with:

```csharp
            // Run OCR after card is in the queue
            OcrMatchResult? ocrResult = null;
            try
            {
                ocrResult = await _ocrService.AnalyzeCardAsync(rawBytes);
                if (ocrResult?.RecognizedName is not null)
                {
                    _logger.LogInformation("OCR recognized: \"{Name}\" (confidence: {Conf:F2})", ocrResult.RecognizedName, ocrResult.NameConfidence);

                    // Merge set preferences from initial symbol detection and async OCR
                    var mergedPreferredSets = detectedSets;
                    if (ocrResult.SymbolConfidence >= 0.5 && ocrResult.CandidateSetCodes is { Count: > 0 })
                    {
                        mergedPreferredSets ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var code in ocrResult.CandidateSetCodes)
                            mergedPreferredSets.Add(code);
                    }

                    // Re-match with combined scoring and set preferences
                    var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, mergedPreferredSets);
                    if (ocrMatch is not null && (scannedCard.Match is null || ocrMatch.GameSpecificId != scannedCard.Match?.GameSpecificId))
                    {
                        scannedCard.Match = ocrMatch;
                        scannedCard.Game = ocrGame;
                        _logger.LogInformation("OCR improved match to \"{CardName}\" ({SetCode} #{Number})", ocrMatch.Name, ocrMatch.SetCode, ocrMatch.CollectorNumber);

                        // Clear auto-flag if OCR improved the match above the threshold
                        if (scannedCard.FlagReason is FlagReason.NoMatch or FlagReason.VeryLowConfidence)
                        {
                            if (ocrMatch.Confidence is null or >= 15)
                            {
                                scannedCard.FlagReason = FlagReason.None;
                                _logger.LogInformation("Auto-flag cleared after OCR improvement");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR analysis failed");
            }

            // Always log OCR results to diagnostics (even if OCR didn't change the match)
            try
            {
                var currentGame = scannedCard.Game;
                var ocrDiag = _gameServices.TryGetValue(currentGame, out var gs2) ? gs2.LastMatchDiagnostics : null;
                _diagnosticService.LogScanCompleted(_currentSessionId, capturedHash, scannedCard.Match, ocrDiag, scannedCard.ArtHashes, ocrResult, scannedCard.FlagReason);
            }
            catch { }
```

This replaces the conditional-only second log with an unconditional one that always includes OCR data.

- [ ] **Step 2: Build and run tests**

Run: `dotnet build && dotnet test OmniCard.Tests`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add OmniCard/Services/CardSevice.cs
git commit -m "fix: always log OCR results to diagnostics regardless of match change"
```

---

### Task 5: Add Warning Logs for Empty Symbol Hashes

**Files:**
- Modify: `OmniCard/Services/OcrMatchingService.cs:107,49-55`
- Modify: `OmniCard/Services/CardSevice.cs:276-278`

**Interfaces:**
- Produces: Warning logs when symbol hashes are empty — helps diagnose missing OCR data

- [ ] **Step 1: Add warning in DetectSetSymbol**

In `OmniCard/Services/OcrMatchingService.cs` line 107-108, change:

```csharp
        if (SymbolHashes.Count == 0)
            return ([], 0);
```

To:

```csharp
        if (SymbolHashes.Count == 0)
        {
            _logger.LogWarning("DetectSetSymbol called with empty SymbolHashes dictionary — no set detection possible");
            return ([], 0);
        }
```

- [ ] **Step 2: Add warning in AnalyzeCardAsync**

In `OmniCard/Services/OcrMatchingService.cs`, near the start of `AnalyzeCardAsync` (around line 55), add:

```csharp
        if (SymbolHashes.Count == 0)
            _logger.LogWarning("AnalyzeCardAsync: SymbolHashes is empty — symbol detection will be skipped");
```

- [ ] **Step 3: Log symbol hash loading result in CardSevice**

In `OmniCard/Services/CardSevice.cs` lines 276-278, change:

```csharp
            try { _ocrService.SymbolHashes = scryfall.GetSymbolHashes(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load symbol hashes into OCR service"); }
```

To:

```csharp
            try
            {
                _ocrService.SymbolHashes = scryfall.GetSymbolHashes();
                _logger.LogInformation("Loaded {Count} symbol hashes into OCR service", _ocrService.SymbolHashes.Count);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load symbol hashes into OCR service"); }
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Clean build.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Services/OcrMatchingService.cs OmniCard/Services/CardSevice.cs
git commit -m "fix: add warning logs when symbol hashes are empty for OCR diagnosis"
```

---

### Task 6: Fix Diagnostic Capture Completeness

**Files:**
- Modify: `OmniCard/Services/ScanDiagnosticService.cs:57-101,154-163`

**Interfaces:**
- Consumes: `ScanDiagnosticEvent` model, `CollectionDbContext.ScanDiagnosticEvents`
- Produces: User action events always have a matching ScanCompleted (backfilled if missing)

- [ ] **Step 1: Add backfill logic to user action log methods**

In `OmniCard/Services/ScanDiagnosticService.cs`, create a helper method that ensures a `ScanCompleted` event exists before logging a user action:

```csharp
    private string EnsureScanEventExists(ulong scanHash, ScannedCard card)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var existing = ctx.ScanDiagnosticEvents
            .AsNoTracking()
            .Where(e => e.ScanHash == scanHash && e.EventType == "ScanCompleted")
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();

        if (existing is not null)
            return existing.SessionId;

        // Backfill a minimal ScanCompleted event from the ScannedCard
        var payload = new Dictionary<string, object?>
        {
            ["matchedCardId"] = card.Match?.GameSpecificId,
            ["matchedName"] = card.Match?.Name,
            ["matchedSet"] = card.Match?.SetCode,
            ["matchedNumber"] = card.Match?.CollectorNumber,
            ["confidence"] = card.Match?.Confidence,
            ["decisionPhase"] = "Backfilled",
            ["pHashDistance"] = 0,
            ["autoFlagReason"] = card.FlagReason.ToString(),
            ["tieZoneCandidates"] = Array.Empty<object>(),
        };

        var sessionId = "backfilled";
        LogEvent(sessionId, scanHash, "ScanCompleted", payload);
        return sessionId;
    }
```

- [ ] **Step 2: Update LogUserFlagged to use EnsureScanEventExists**

Replace `LookupSessionId(scanHash)` with `EnsureScanEventExists(scanHash, card)` in `LogUserFlagged` (line 59).

- [ ] **Step 3: Update LogUserConfirmed, LogUserCorrected, LogUserUnflagged similarly**

In each method, replace the `LookupSessionId(scanHash)` call with `EnsureScanEventExists(scanHash, card)`.

For `LogUserUnflagged`, it takes `FlagReason previousReason` instead of using the card directly. Pass the card through — the `EnsureScanEventExists` method needs it. Update the interface and method signature:

Actually, `LogUserUnflagged` already receives the `ScannedCard` via the `card` parameter. Just change `LookupSessionId(scanHash)` to `EnsureScanEventExists(scanHash, card)`.

- [ ] **Step 4: Build and run diagnostic tests**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ScanDiagnosticServiceTests"`
Expected: All 6 tests pass (they already create ScanCompleted events before user actions, so the backfill won't trigger in existing tests).

- [ ] **Step 5: Run all tests**

Run: `dotnet test OmniCard.Tests`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/ScanDiagnosticService.cs
git commit -m "fix: backfill ScanCompleted events for orphaned user actions"
```
