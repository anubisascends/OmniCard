# OCR-First OPTCG Matching + Robust OCR Read — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make One Piece scans reliably match by hardening the collector-number OCR read (grayscale + autocontrast + retry) and trying OCR before pHash.

**Architecture:** Part A hardens `OcrMatchingService` by extracting two pure, testable helpers (`ApplyOcrPreprocessing`, `ExtractCollectorNumber`) and making `DetectOptcgCollectorNumberAsync` try a preprocessed pass then retry raw. Part B extracts a dispatcher-free `CardService.ResolveOnePieceMatchAsync` (OCR-number lookup first, pHash fallback) and rewires the scan pipeline so One Piece defers matching to it. The OCR engine itself can't run in tests, so pure helpers + the resolution ordering are unit-tested and the end-to-end OCR read is verified by re-scanning.

**Tech Stack:** C# / .NET 10, WPF, System.Drawing, Windows.Media.Ocr, xUnit.

## Global Constraints

- Crop region stays `(0.68, 0.925, 0.24, 0.055)`. Collector-number regex stays `^([A-Za-z0-9]+)-(\d+[A-Za-z]*)$` shape — but Part A extracts the existing per-card extraction regex (`([A-Za-z0-9]{2,4}\d{2})\s*[-—]\s*(\d{2,3})`) into `ExtractCollectorNumber`; keep that pattern verbatim.
- OCR success contract unchanged: `DetectOptcgCollectorNumberAsync` returns `(string? CollectorNumber, double Confidence)`, confidence `0.95` on success, `(null, 0)` on failure.
- One Piece only for the pipeline reorder; MTG and audit paths unchanged.
- pHash `maxDistance` stays 14. Do not change hashing.
- Preprocessing helpers live in `OmniCard.Imaging` as `internal static` (`InternalsVisibleTo("OmniCard.Tests")` is set). `ResolveOnePieceMatchAsync` is `public` on `CardService` (tests construct `CardService` directly).
- Clipboard/dispatcher/`Application.Current` must NOT appear in `ResolveOnePieceMatchAsync` — it must be callable from a unit test with no WPF app.
- Build: `dotnet build`. Test: `dotnet test OmniCard.Tests`.

---

### Task 1: Part A — robust collector-number OCR read

**Files:**
- Modify: `OmniCard.Imaging/OcrMatchingService.cs` (`DetectOptcgCollectorNumberAsync` ~206-243, `OcrCroppedRegionAsync` ~149-206; add two helpers)
- Test: `OmniCard.Tests/Services/OcrPreprocessingTests.cs` (create)

**Interfaces:**
- Produces:
  - `internal static Bitmap OcrMatchingService.ApplyOcrPreprocessing(Bitmap src)` — grayscale + min/max contrast stretch.
  - `internal static string? OcrMatchingService.ExtractCollectorNumber(string? text)` — returns formatted number (e.g. `OP15-011`) or null.
  - `DetectOptcgCollectorNumberAsync` now does a preprocessed OCR pass, then a raw retry.

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/OcrPreprocessingTests.cs`:

```csharp
using System.Drawing;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class OcrPreprocessingTests
{
    [Fact]
    public void ApplyOcrPreprocessing_StretchesContrastToFullRange()
    {
        using var src = new Bitmap(3, 1);
        src.SetPixel(0, 0, Color.FromArgb(100, 100, 100)); // min luminance
        src.SetPixel(1, 0, Color.FromArgb(120, 120, 120));
        src.SetPixel(2, 0, Color.FromArgb(140, 140, 140)); // max luminance

        using var outp = OcrMatchingService.ApplyOcrPreprocessing(src);

        Assert.Equal(0, outp.GetPixel(0, 0).R);     // min -> 0
        Assert.Equal(255, outp.GetPixel(2, 0).R);   // max -> 255
    }

    [Fact]
    public void ApplyOcrPreprocessing_ProducesGrayscale()
    {
        using var src = new Bitmap(2, 1);
        src.SetPixel(0, 0, Color.FromArgb(200, 30, 30));  // red
        src.SetPixel(1, 0, Color.FromArgb(30, 30, 200));  // blue

        using var outp = OcrMatchingService.ApplyOcrPreprocessing(src);

        var p0 = outp.GetPixel(0, 0);
        var p1 = outp.GetPixel(1, 0);
        Assert.Equal(p0.R, p0.G); Assert.Equal(p0.G, p0.B); // R==G==B
        Assert.Equal(p1.R, p1.G); Assert.Equal(p1.G, p1.B);
    }

    [Fact]
    public void ApplyOcrPreprocessing_FlatImage_DoesNotThrow()
    {
        using var src = new Bitmap(2, 1);
        src.SetPixel(0, 0, Color.FromArgb(50, 50, 50));
        src.SetPixel(1, 0, Color.FromArgb(50, 50, 50)); // min==max

        using var outp = OcrMatchingService.ApplyOcrPreprocessing(src); // must not divide by zero
        Assert.Equal(2, outp.Width);
    }

    [Theory]
    [InlineData("OP15-011", "OP15-011")]
    [InlineData("Straw Hat Crew OP15-024", "OP15-024")]
    [InlineData("EB04-003", "EB04-003")]
    [InlineData("op15-011", "OP15-011")]      // uppercased
    [InlineData("OP15 — 011", "OP15-011")]    // em-dash + spaces normalized
    public void ExtractCollectorNumber_ReturnsFormattedNumber(string raw, string expected)
    {
        Assert.Equal(expected, OcrMatchingService.ExtractCollectorNumber(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Straw Hat Crew")]
    [InlineData("Dressrosa")]
    public void ExtractCollectorNumber_NoPattern_ReturnsNull(string? raw)
    {
        Assert.Null(OcrMatchingService.ExtractCollectorNumber(raw));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OcrPreprocessingTests`
Expected: FAIL — build error, `ApplyOcrPreprocessing` / `ExtractCollectorNumber` do not exist.

- [ ] **Step 3: Add the two helpers**

In `OmniCard.Imaging/OcrMatchingService.cs`, add these `internal static` methods to the class:

```csharp
    // Grayscale + linear min/max contrast stretch. Windows OCR reads high-contrast
    // black-on-white text far more reliably than low-contrast stylized card text.
    internal static Bitmap ApplyOcrPreprocessing(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        var lum = new int[w * h];
        int min = 255, max = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = src.GetPixel(x, y);
                int l = (int)(0.299 * p.R + 0.587 * p.G + 0.114 * p.B);
                lum[y * w + x] = l;
                if (l < min) min = l;
                if (l > max) max = l;
            }
        }

        int range = Math.Max(1, max - min); // guard flat images
        var outBmp = new Bitmap(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int s = Math.Clamp((lum[y * w + x] - min) * 255 / range, 0, 255);
                outBmp.SetPixel(x, y, Color.FromArgb(s, s, s));
            }
        }
        return outBmp;
    }

    // Extract a collector number like "OP15-011" from raw OCR text, or null.
    internal static string? ExtractCollectorNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"([A-Za-z0-9]{2,4}\d{2})\s*[-—]\s*(\d{2,3})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}";
    }
```

Note: the extraction regex is the existing pattern from `DetectOptcgCollectorNumberAsync`, moved verbatim (group 1 allows the `PRB01`-style 3-letter prefixes via `{2,4}`).

- [ ] **Step 4: Add a `preprocess` option to `OcrCroppedRegionAsync`**

In `OcrCroppedRegionAsync`, change the signature and apply preprocessing after the upscale. Replace the method header and the block just before the `try` with:

```csharp
    private async Task<(string Text, double Confidence)> OcrCroppedRegionAsync(Bitmap source, Rectangle cropRect, bool preprocess = false)
    {
        // Crop
        using var cropped = source.Clone(cropRect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        // Upscale if too small (OCR works better with larger text)
        Bitmap toOcr = cropped;
        bool needsDispose = false;
        if (cropped.Width < 200)
        {
            var scale = 200.0 / cropped.Width;
            var newWidth = (int)(cropped.Width * scale);
            var newHeight = (int)(cropped.Height * scale);
            toOcr = new Bitmap(newWidth, newHeight);
            needsDispose = true;
            using var g = Graphics.FromImage(toOcr);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(cropped, 0, 0, newWidth, newHeight);
        }

        if (preprocess)
        {
            var pre = ApplyOcrPreprocessing(toOcr);
            if (needsDispose) toOcr.Dispose();
            toOcr = pre;
            needsDispose = true;
        }
```

(The rest of the method body — the `try { ... } finally { if (needsDispose) toOcr.Dispose(); }` — is unchanged.)

- [ ] **Step 5: Rewrite `DetectOptcgCollectorNumberAsync` to preprocess-then-retry**

Replace the body of `DetectOptcgCollectorNumberAsync` (from the `var (text, confidence) = await OcrCroppedRegionAsync(...)` line through the `return (null, 0);` before the `catch`) with:

```csharp
            // First pass: preprocessed (grayscale + contrast) — most reliable.
            var (preText, _) = await OcrCroppedRegionAsync(bitmap, rect, preprocess: true);
            var cn = ExtractCollectorNumber(preText);

            if (cn is null)
            {
                // Retry on the raw (color) crop — occasionally reads when preprocessing doesn't.
                var (rawText, _) = await OcrCroppedRegionAsync(bitmap, rect, preprocess: false);
                _logger.LogDebug("OPTCG OCR miss — preprocessed: \"{Pre}\", raw: \"{Raw}\"", preText, rawText);
                cn = ExtractCollectorNumber(rawText);
                if (cn is null)
                    return (null, 0);
            }

            _logger.LogInformation("OPTCG collector number detected: {Number}", cn);
            return (cn, 0.95);
```

(The method's opening — engine-null guard, `ToPixelRect`, `rect.Width < 10` guard — and the outer `try/catch` stay as they are. The old inline regex block is replaced by the two `ExtractCollectorNumber` calls above.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OcrPreprocessingTests`
Expected: PASS (all cases).

- [ ] **Step 7: Build the solution**

Run: `dotnet build --nologo -clp:ErrorsOnly`
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Imaging/OcrMatchingService.cs OmniCard.Tests/Services/OcrPreprocessingTests.cs
git commit -m "feat(ocr): grayscale+contrast preprocess and retry for OPTCG number read"
```

---

### Task 2: Part B — OCR-first One Piece match pipeline

**Files:**
- Modify: `OmniCard.Collection/CardService.cs` — add `ResolveOnePieceMatchAsync`; rewire `AddFromStream` sync section (~287-321) and the async One Piece branch (~342-366)
- Test: `OmniCard.Tests/Services/OcrFirstMatchingTests.cs` (create)

**Interfaces:**
- Consumes: existing `FindBestMatch(ulong, ulong[]?, OcrMatchResult?, IReadOnlySet<string>?, IReadOnlySet<string>?)`, `_ocrService.DetectOptcgCollectorNumberAsync(byte[])`, `SelectedSetFilter`.
- Produces: `public async Task<CardMatch?> CardService.ResolveOnePieceMatchAsync(ulong hash, ulong[]? artHashes, byte[] rawBytes, IReadOnlySet<string>? setFilter)`.

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/OcrFirstMatchingTests.cs`:

```csharp
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class OcrFirstMatchingTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public OcrFirstMatchingTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>().UseSqlite(_conn).Options;
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private static CardMatch Op(string id) => new()
    {
        Name = "Card " + id, SetCode = "OP15", SetName = "s", CollectorNumber = id,
        Rarity = "R", GameSpecificId = id, Source = new OptcgCard { CardSetId = id, CardName = "Card " + id },
    };

    private CardService Create(ICardGameService game, IOcrMatchingService ocr) => new(
        new StubHashService(), [game], new Factory(_options), ocr,
        new ScanImageCache(new DataPathService(Path.GetTempPath()), NullLogger<ScanImageCache>.Instance),
        NullLogger<CardService>.Instance, new DataPathService(Path.GetTempPath()),
        new NullDiag(), new NullAudit()) { SelectedGame = CardGame.OnePiece };

    [Fact]
    public async Task OcrResolves_ReturnsOcrMatch_WithoutPHashFallback()
    {
        var game = new RecordingGameService(ocrMatch: Op("OP15-011"), phashMatch: Op("WRONG"));
        var ocr = new FixedOcr("OP15-011", 0.95);
        var svc = Create(game, ocr);

        var result = await svc.ResolveOnePieceMatchAsync(0x1, null, [1, 2, 3], null);

        Assert.NotNull(result);
        Assert.Equal("OP15-011", result!.CollectorNumber);
        Assert.True(game.CalledWithOcr);
        Assert.False(game.CalledWithoutOcr); // pHash fallback not consulted
    }

    [Fact]
    public async Task OcrEmpty_FallsBackToPHash()
    {
        var game = new RecordingGameService(ocrMatch: null, phashMatch: Op("OP15-011"));
        var ocr = new FixedOcr(null, 0);
        var svc = Create(game, ocr);

        var result = await svc.ResolveOnePieceMatchAsync(0x1, null, [1], null);

        Assert.Equal("OP15-011", result!.CollectorNumber);
        Assert.True(game.CalledWithoutOcr);
    }

    [Fact]
    public async Task OcrNumberUnresolved_FallsBackToPHash()
    {
        // OCR yields a number, but the game service can't resolve it (ocrMatch null);
        // pHash then resolves.
        var game = new RecordingGameService(ocrMatch: null, phashMatch: Op("OP15-011"));
        var ocr = new FixedOcr("OP99-999", 0.95);
        var svc = Create(game, ocr);

        var result = await svc.ResolveOnePieceMatchAsync(0x1, null, [1], null);

        Assert.Equal("OP15-011", result!.CollectorNumber);
        Assert.True(game.CalledWithOcr);
        Assert.True(game.CalledWithoutOcr);
    }

    [Fact]
    public async Task BothFail_ReturnsNull()
    {
        var game = new RecordingGameService(ocrMatch: null, phashMatch: null);
        var svc = Create(game, new FixedOcr(null, 0));

        Assert.Null(await svc.ResolveOnePieceMatchAsync(0x1, null, [1], null));
    }

    // --- stubs ---
    private class RecordingGameService(CardMatch? ocrMatch, CardMatch? phashMatch) : ICardGameService
    {
        public bool CalledWithOcr { get; private set; }
        public bool CalledWithoutOcr { get; private set; }
        public CardGame Game => CardGame.OnePiece;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public CardMatch? FindClosestMatch(ulong h, ulong[]? a = null, OcrMatchResult? ocr = null, IReadOnlySet<string>? sf = null, IReadOnlySet<string>? ps = null, int md = 14)
        {
            if (ocr?.CollectorNumber is not null) { CalledWithOcr = true; return ocrMatch; }
            CalledWithoutOcr = true; return phashMatch;
        }
        public decimal? GetCurrentPrice(string id, bool f) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> ids, bool f) => [];
        public Task DownloadBulkDataAsync(IProgress<string>? p = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool fa = false, IProgress<string>? p = null, CancellationToken ct = default) => Task.CompletedTask;
        public List<CardMatch> SearchCards(string q, int m = 20) => [];
        public List<CardMatch> GetPrintings(string n) => [];
        public void RecordCorrection(ulong h, string id, ulong? a = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> o, IProgress<string>? p = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string s, IEnumerable<string> o) => [];
        public object? FindCardById(string id) => null;
    }
    private class FixedOcr(string? cn, double conf) : IOcrMatchingService
    {
        public Dictionary<string, ulong> SymbolHashes { get; set; } = [];
        public Task<OcrMatchResult> AnalyzeCardAsync(byte[] d) => Task.FromResult(new OcrMatchResult());
        public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] d) => ([], 0);
        public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] d) => Task.FromResult((cn, conf));
    }
    private class StubHashService : IPerceptualHashService
    {
        public ulong ComputeHash(Stream s, Action<HashStageResult>? o = null) => 0;
        public ulong[] ComputeArtHash(Stream s, (double X, double Y, double W, double H)[] r, Action<HashStageResult>? o = null) => new ulong[r.Length];
    }
    private class Factory(DbContextOptions<CollectionDbContext> o) : IDbContextFactory<CollectionDbContext>
    { public CollectionDbContext CreateDbContext() => new(o); }
    private class NullDiag : IScanDiagnosticService
    {
        public void LogScanCompleted(string s, ulong h, CardMatch? m, MatchDiagnostics? d, ulong[]? a, OcrMatchResult? o, FlagReason f) { }
        public void LogUserFlagged(ulong h, ScannedCard c) { } public void LogUserConfirmed(ulong h, ScannedCard c) { }
        public void LogUserCorrected(ulong h, ScannedCard c, CardMatch m) { } public void LogUserUnflagged(ulong h, ScannedCard c, FlagReason p) { }
        public void ExportDiagnostics(string f) { } public void ClearDiagnostics() { } public int GetEventCount() => 0;
    }
    private class NullAudit : IAuditService
    {
        public bool IsAuditActive => false; public int? AuditLocationId => null; public string? AuditLocationName => null;
        public void StartAudit(int c) { } public void EndAudit() { }
        public CardMatch? FindScopedMatch(ulong h, ulong[]? a) => null;
        public AuditReport GenerateReport(IEnumerable<ScannedCard> s) => throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OcrFirstMatchingTests`
Expected: FAIL — build error, `ResolveOnePieceMatchAsync` does not exist.

- [ ] **Step 3: Add `ResolveOnePieceMatchAsync` to CardService**

In `OmniCard.Collection/CardService.cs`, add this public method (place it right after `FindBestMatch`, ~line 173):

```csharp
    // One Piece OCR-first resolution: try the collector-number OCR lookup first;
    // fall back to pHash only when OCR yields no resolving number. Dispatcher-free
    // so it is unit-testable.
    public async Task<CardMatch?> ResolveOnePieceMatchAsync(ulong hash, ulong[]? artHashes, byte[] rawBytes, IReadOnlySet<string>? setFilter)
    {
        var (collectorNumber, conf) = await _ocrService.DetectOptcgCollectorNumberAsync(rawBytes);
        if (collectorNumber is not null && conf >= 0.5)
        {
            var ocrResult = new OcrMatchResult { CollectorNumber = collectorNumber, CollectorNumberConfidence = conf };
            var (ocrMatch, _) = FindBestMatch(hash, artHashes, ocrResult, setFilter, null);
            if (ocrMatch is not null)
            {
                _logger.LogInformation("OPTCG OCR-first match: {CardName} ({Number})", ocrMatch.Name, ocrMatch.CollectorNumber);
                return ocrMatch;
            }
            _logger.LogDebug("OCR number {Number} did not resolve; falling back to pHash", collectorNumber);
        }

        var (pMatch, _) = FindBestMatch(hash, artHashes, null, setFilter, null);
        return pMatch;
    }
```

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OcrFirstMatchingTests`
Expected: PASS (all four cases).

- [ ] **Step 5: Rewire `AddFromStream` — defer One Piece matching**

In `OmniCard.Collection/CardService.cs`, the synchronous match block (currently ~288-303):

```csharp
        // pHash match — branch on audit mode
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
        if (match is not null)
            _logger.LogInformation("Matched scanned card to \"{CardName}\" ({SetCode} #{Number}) in {Game}", match.Name, match.SetCode, match.CollectorNumber, game);
        else
            _logger.LogWarning("No matching card found for pHash {Hash:X16} in any game", hash);
```

Replace with (One Piece defers to the async OCR-first step; MTG/audit unchanged):

```csharp
        // Match — branch on audit mode. One Piece defers to the async OCR-first
        // resolution below (OCR is more reliable than pHash for OPTCG).
        CardMatch? match = null;
        if (_auditService.IsAuditActive)
        {
            match = _auditService.FindScopedMatch(hash, artHashes);
        }
        else if (SelectedGame != CardGame.OnePiece)
        {
            var (bestMatch, matchedGame) = FindBestMatch(hash, artHashes, null, SelectedSetFilter, detectedSets);
            match = bestMatch;
            game = matchedGame;
        }

        if (match is not null)
            _logger.LogInformation("Matched scanned card to \"{CardName}\" ({SetCode} #{Number}) in {Game}", match.Name, match.SetCode, match.CollectorNumber, game);
        else if (_auditService.IsAuditActive || SelectedGame != CardGame.OnePiece)
            _logger.LogWarning("No matching card found for pHash {Hash:X16} in any game", hash);
```

- [ ] **Step 6: Rewire the async One Piece branch to OCR-first resolution**

In the `Application.Current.Dispatcher.BeginInvoke` block, replace the One Piece OCR branch (currently ~348-366, the `if (game == CardGame.OnePiece) { ... }` block that calls `DetectOptcgCollectorNumberAsync` and conditionally overrides) with a call to the new resolver:

```csharp
                    if (game == CardGame.OnePiece)
                    {
                        // OCR-first: resolve via collector number, else pHash fallback.
                        var resolved = await ResolveOnePieceMatchAsync(capturedHash, scannedCard.ArtHashes, rawBytes, capturedSetFilter);
                        if (resolved is not null)
                        {
                            scannedCard.Match = resolved;
                            scannedCard.Game = CardGame.OnePiece;
                            scannedCard.FlagReason = FlagReason.None;
                            _logger.LogInformation("One Piece resolved to \"{CardName}\" ({SetCode} #{Number})", resolved.Name, resolved.SetCode, resolved.CollectorNumber);
                        }
                    }
                    else
                    {
```

(The MTG `else` branch body — `AnalyzeCardAsync` etc. — is unchanged; only the One Piece branch above changes. The subsequent `if (scannedCard.Match is null)` 180° rotation block, the flag-upgrade block, and the diagnostics block all stay as-is.)

- [ ] **Step 7: Build the solution**

Run: `dotnet build --nologo -clp:ErrorsOnly`
Expected: 0 errors.

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test OmniCard.Tests --nologo`
Expected: PASS — all tests green, including `OcrFirstMatchingTests` and `OcrPreprocessingTests`, and the existing `FallbackMatchingTests` (which exercise `FindBestMatch`/`ReprocessScans`, unchanged).

- [ ] **Step 9: Commit**

```bash
git add OmniCard.Collection/CardService.cs OmniCard.Tests/Services/OcrFirstMatchingTests.cs
git commit -m "feat(scanner): OCR-first One Piece match pipeline"
```

---

### Task 3: Live verification (manual — controller/user)

**Files:** none.

Windows OCR cannot run in the unit-test host, so the end-to-end read fix is verified by re-scanning.

- [ ] **Step 1: Build and launch the app**, select One Piece, and scan the cards that previously missed: OP15-011 and EB04-003 (Smoker & Tashigi).

- [ ] **Step 2: Confirm in the log** (`<dataDir>/logs/tcgcardscanner-<date>.log`):
  - `OPTCG collector number detected: OP15-011` / `EB04-003`
  - `One Piece resolved to "..."`
  - No `Card flagged as missing from database` for these cards.

- [ ] **Step 3:** If a card still misses, capture the new DBG line `OPTCG OCR miss — preprocessed: "..." raw: "..."` — it shows exactly what OCR read, to guide the next adjustment.

---

## Self-Review

**1. Spec coverage:**
- Part A: grayscale+autocontrast (`ApplyOcrPreprocessing`), retry (preprocessed→raw in `DetectOptcgCollectorNumberAsync`), per-attempt DBG logging, region unchanged → Task 1.
- Part B: OCR-first order, pHash fallback only when OCR yields no resolving number, One Piece only, MTG/audit unchanged → Task 2 (`ResolveOnePieceMatchAsync` + rewire).
- Threading: pHash still computed sync; only the One Piece match decision moves async; `ResolveOnePieceMatchAsync` is dispatcher-free → Task 2 Steps 3, 5, 6.
- Error handling: OCR unresolved → pHash fallback (Task 2 Step 3); OCR engine exceptions → existing outer try/catch (unchanged); both fail → existing rotation + `MissingFromDatabase` flag (untouched).
- Testing: pure helpers (Task 1) + ordering (Task 2) unit-tested; OCR-engine read verified live (Task 3).
- Out-of-scope items (hash coverage, threshold, MTG) correctly excluded.

**2. Placeholder scan:** No TBD/TODO; every code step has complete code; error handling is concrete.

**3. Type consistency:** `ApplyOcrPreprocessing(Bitmap)→Bitmap`, `ExtractCollectorNumber(string?)→string?`, `ResolveOnePieceMatchAsync(ulong, ulong[]?, byte[], IReadOnlySet<string>?)→Task<CardMatch?>` are defined in their tasks and used consistently. `OcrCroppedRegionAsync`'s new `preprocess` param defaults false so the unchanged MTG name-OCR calls compile untouched. `FindBestMatch` signature matches its existing definition. `FindClosestMatch` stub signature in tests matches `ICardGameService`.
