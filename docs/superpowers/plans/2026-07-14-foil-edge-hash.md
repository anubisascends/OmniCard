# Color-Robust Edge Hash for Foil Matching — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Match foil One Piece cards (whose scans have a holographic color shift) by hashing structure/edges instead of color, routed by the existing "Is Foil" scan flag.

**Architecture:** A new gradient-based `ComputeEdgeHash` (reusing the DCT/median pipeline via an extracted `ComputeHashFromPixels` helper) is computed for every reference card into a new `EdgeHash` column. When a scan is marked foil, `CardService` computes the scan's edge hash and passes it through `FindBestMatch → FindClosestMatch`, where `OptcgService` matches against an edge-hash cache instead of the luminance pHash. Non-foil scans and the MTG path are unchanged.

**Tech Stack:** C# / .NET 10, System.Drawing, EF Core + SQLite, xUnit.

## Global Constraints

- **Base:** branch off `master` AFTER `fix/webp-hash-decode` is merged (edge hashing needs `LoadBitmap`, the WebP-capable loader from that fix; reference images include WebP). Do NOT assume the OCR-first reorder / `ResolveOnePieceMatchAsync` — that's on a separate unmerged branch. Master's `OptcgService.FindClosestMatch` already has the OCR Phase 0 exact-lookup (from the API swap), which the edge path inserts after; master's scan pipeline in `CardService.AddFromStream` is the sync `FindBestMatch` + async OCR-override + 180° rotation (no `ResolveOnePieceMatchAsync`).
- `HashSize = 8`, `ImageSize = 32` (existing consts). Edge hash uses the same DCT/median hashing as the luminance pHash, differing only in that it hashes a **gradient-magnitude** image (per-pixel `|dx| + |dy|`) rather than histogram-equalized luminance. No histogram equalization on the gradient.
- Edge hashes are computed for **every** reference card (foil-ness is a scan property). `ComputeImageHashesAsync` computes both `ImageHash` and `EdgeHash` from one image; `forceAll:false` reprocesses a card when **either** is null.
- Matching: `FindClosestMatch` gains `ulong? scanEdgeHash = null` (last parameter). When non-null, OPTCG matches on the edge-hash cache (min Hamming, honoring set filter + `maxDistance = 14`); when null, behavior is exactly as today. OCR Phase 0 still runs first regardless. Scryfall accepts and ignores the param.
- `CardService` passes `scanEdgeHash` only when `scannedCard.IsFoil` and `SelectedGame == OnePiece`; otherwise null. `IsFoil` is already set on the scan (from `DefaultIsFoil`).
- Do not change the luminance pHash, its threshold for non-foil, hashing of MTG, or OCR logic.
- Reuse `LoadBitmap` (WebP-capable) for edge hashing. Build: `dotnet build`. Test: `dotnet test OmniCard.Tests`.

---

### Task 1: `ComputeEdgeHash` (gradient-based, color-robust)

**Files:**
- Modify: `OmniCard.Shared/Interfaces/IPerceptualHashService.cs`
- Modify: `OmniCard.Imaging/PerceptualHashService.cs` (extract `ComputeHashFromPixels`; add `GradientMagnitude` + `ComputeEdgeHash`)
- Test: `OmniCard.Tests/Services/EdgeHashTests.cs` (create)

**Interfaces:**
- Produces: `ulong IPerceptualHashService.ComputeEdgeHash(Stream imageStream, Action<HashStageResult>? onStage = null)`.

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/EdgeHashTests.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;

namespace OmniCard.Tests.Services;

public class EdgeHashTests
{
    // Draws an identical layout (frame + inner block) so structure is constant; only the
    // fill colors differ between calls — mimics a foil color shift over the same artwork.
    private static byte[] DrawCard(Color frame, Color inner)
    {
        using var bmp = new Bitmap(200, 280);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
            using var framePen = new Pen(frame, 12);
            g.DrawRectangle(framePen, 10, 10, 180, 260);
            using var innerBrush = new SolidBrush(inner);
            g.FillRectangle(innerBrush, 40, 60, 120, 90);
            g.FillEllipse(innerBrush, 60, 180, 80, 60);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void EdgeHash_IsMoreColorRobustThanLuminanceHash()
    {
        var svc = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

        // Same structure, very different colors/luminance (yellow -> green, like a foil shift).
        var a = DrawCard(Color.Gold, Color.Khaki);
        var b = DrawCard(Color.Green, Color.SeaGreen);

        var edgeA = svc.ComputeEdgeHash(new MemoryStream(a));
        var edgeB = svc.ComputeEdgeHash(new MemoryStream(b));
        var lumA = svc.ComputeHash(new MemoryStream(a));
        var lumB = svc.ComputeHash(new MemoryStream(b));

        int edgeDist = PerceptualHashService.HammingDistance(edgeA, edgeB);
        int lumDist = PerceptualHashService.HammingDistance(lumA, lumB);

        // The color shift moves the luminance hash more than the edge hash.
        Assert.True(edgeDist < lumDist,
            $"edge dist {edgeDist} should be < luminance dist {lumDist} for a color-only change");
    }

    [Fact]
    public void EdgeHash_IsDeterministic()
    {
        var svc = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);
        var img = DrawCard(Color.Gold, Color.Khaki);

        var h1 = svc.ComputeEdgeHash(new MemoryStream(img));
        var h2 = svc.ComputeEdgeHash(new MemoryStream(img));

        Assert.Equal(h1, h2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~EdgeHashTests`
Expected: FAIL — build error, `ComputeEdgeHash` does not exist.

- [ ] **Step 3: Add `ComputeEdgeHash` to the interface**

In `OmniCard.Shared/Interfaces/IPerceptualHashService.cs`, add after the `ComputeHash` line:

```csharp
    ulong ComputeEdgeHash(Stream imageStream, Action<HashStageResult>? onStage = null);
```

- [ ] **Step 4: Extract `ComputeHashFromPixels` (preserve ComputeHash's stages)**

In `OmniCard.Imaging/PerceptualHashService.cs`, replace the body of `ComputeHash` from the DCT line through the hash-bit loop (currently lines ~49-79) so it delegates the DCT+median+bits to a shared helper while keeping the two diagnostic stage emissions:

```csharp
        // Extract pixel luminance as doubles
        var pixels = ExtractPixels(grayscale);

        // DCT + median-threshold hash (shared with the edge hash).
        var hash = ComputeHashFromPixels(pixels, out var dct);

        if (onStage is not null)
        {
            onStage(new HashStageResult("DCT Coefficients", RenderDctHeatmap(dct)));
            onStage(new HashStageResult("Hash", RenderHashGrid(hash)));
        }

        sw.Stop();
        _logger.LogDebug("Computed pHash {Hash:X16} from {Width}x{Height} image in {ElapsedMs}ms", hash, original.Width, original.Height, sw.ElapsedMilliseconds);
        return hash;
```

Then add the shared helper (place it just below `ComputeHash`):

```csharp
    // DCT-II of the 32x32 input, then a median-threshold hash over the low-frequency
    // 8x8 block (DC excluded from the median). Shared by ComputeHash and ComputeEdgeHash.
    private static ulong ComputeHashFromPixels(double[,] pixels, out double[,] dct)
    {
        dct = ComputeDct2D(pixels);

        var values = new double[HashSize * HashSize - 1];
        int idx = 0;
        for (int y = 0; y < HashSize; y++)
        {
            for (int x = 0; x < HashSize; x++)
            {
                if (y == 0 && x == 0) continue;
                values[idx++] = dct[y, x];
            }
        }

        var median = Median(values);
        ulong hash = 0;
        for (int y = 0; y < HashSize; y++)
        {
            for (int x = 0; x < HashSize; x++)
            {
                if (dct[y, x] > median)
                    hash |= 1UL << (y * HashSize + x);
            }
        }
        return hash;
    }
```

- [ ] **Step 5: Add `GradientMagnitude` and `ComputeEdgeHash`**

Add these methods (place `ComputeEdgeHash` right after `ComputeHash`, and `GradientMagnitude` near the other private helpers):

```csharp
    public ulong ComputeEdgeHash(Stream imageStream, Action<HashStageResult>? onStage = null)
    {
        var sw = Stopwatch.StartNew();
        using var original = LoadBitmap(imageStream);
        onStage?.Invoke(new HashStageResult("Original", BitmapToPng(original)));

        // Grayscale + resize to 32x32, then gradient magnitude — captures structure
        // (shape boundaries) and discards color/brightness, so a foil color shift barely
        // moves the hash.
        using var grayscale = ToGrayscaleResized(original, ImageSize, ImageSize);
        var pixels = ExtractPixels(grayscale);
        var gradient = GradientMagnitude(pixels);

        var hash = ComputeHashFromPixels(gradient, out var dct);
        if (onStage is not null)
        {
            onStage(new HashStageResult("DCT Coefficients", RenderDctHeatmap(dct)));
            onStage(new HashStageResult("Hash", RenderHashGrid(hash)));
        }

        sw.Stop();
        _logger.LogDebug("Computed edge hash {Hash:X16} from {Width}x{Height} image in {ElapsedMs}ms", hash, original.Width, original.Height, sw.ElapsedMilliseconds);
        return hash;
    }

    // Per-pixel gradient magnitude (|dx| + |dy|) on a [height,width] luminance array in [0,1].
    // Edge/right/bottom borders reuse the nearest interior difference (zero at the far edge).
    private static double[,] GradientMagnitude(double[,] pixels)
    {
        int h = pixels.GetLength(0);
        int w = pixels.GetLength(1);
        var result = new double[h, w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x + 1 < w ? Math.Abs(pixels[y, x + 1] - pixels[y, x]) : 0;
                double dy = y + 1 < h ? Math.Abs(pixels[y + 1, x] - pixels[y, x]) : 0;
                result[y, x] = dx + dy;
            }
        }
        return result;
    }
```

- [ ] **Step 6: Run the new tests + the existing stage test**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~EdgeHashTests|FullyQualifiedName~PerceptualHashStageTests"`
Expected: PASS — `EdgeHashTests` (2) pass AND `PerceptualHashStageTests` still emits its 5 stages (proves the ComputeHash refactor preserved diagnostics). If `EdgeHash_IsMoreColorRobustThanLuminanceHash` fails because the synthetic luminance distance is coincidentally tiny, widen the color/luminance gap in `DrawCard` (e.g. `Color.Yellow`/`Color.DarkGreen`) — do not weaken the `edgeDist < lumDist` assertion.

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Interfaces/IPerceptualHashService.cs OmniCard.Imaging/PerceptualHashService.cs OmniCard.Tests/Services/EdgeHashTests.cs
git commit -m "feat(imaging): add color-robust gradient edge hash"
```

---

### Task 2: `OptcgCard.EdgeHash` column + schema upgrade

**Files:**
- Modify: `OmniCard.Shared/Models/OptcgCard.cs`
- Modify: `OmniCard.Data/OptcgDbContext.cs`
- Test: `OmniCard.Tests/Data/OptcgSchemaTests.cs` (add a case)

**Interfaces:**
- Produces: `OptcgCard.EdgeHash` (`ulong?`), indexed, added to legacy DBs by `ApplySchemaUpgrades`.

- [ ] **Step 1: Write the failing test**

Add to `OmniCard.Tests/Data/OptcgSchemaTests.cs`:

```csharp
    [Fact]
    public void ApplySchemaUpgrades_AddsEdgeHashColumn_ToLegacyTable()
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE Cards (
                CardSetId TEXT PRIMARY KEY, CardName TEXT, SetId TEXT, SetName TEXT,
                Rarity TEXT, CardColor TEXT, CardType TEXT, CardCost TEXT, CardPower TEXT,
                Life TEXT, CardText TEXT, SubTypes TEXT, Attribute TEXT, CounterAmount INTEGER,
                InventoryPrice TEXT, MarketPrice TEXT, CardImageId TEXT, CardImageUri TEXT,
                DateScraped TEXT, ImageHash INTEGER);";
            cmd.ExecuteNonQuery();
        }

        using var ctx = NewContext();
        ctx.ApplySchemaUpgrades();

        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'EdgeHash';";
        Assert.Equal(1L, (long)check.ExecuteScalar()!);
    }

    [Fact]
    public void FreshDatabase_HasEdgeHashColumn()
    {
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
        ctx.Cards.Add(new OmniCard.Models.OptcgCard { CardSetId = "OP01-001", CardNumber = "OP01-001", EdgeHash = 12345UL });
        ctx.SaveChanges();
        Assert.Equal(12345UL, ctx.Cards.Single(c => c.CardSetId == "OP01-001").EdgeHash);
    }
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgSchemaTests`
Expected: FAIL — `OptcgCard.EdgeHash` does not exist.

- [ ] **Step 3: Add the property**

In `OmniCard.Shared/Models/OptcgCard.cs`, add next to `ImageHash`:

```csharp
    public ulong? EdgeHash { get; set; }
```

- [ ] **Step 4: Index + schema upgrade**

In `OmniCard.Data/OptcgDbContext.cs`: add an index in `OnModelCreating` after the `ImageHash` index:

```csharp
        card.HasIndex(c => c.EdgeHash);
```

And in `ApplySchemaUpgrades`, add another `AddColumnIfMissing` call alongside the others:

```csharp
        AddColumnIfMissing(conn, "EdgeHash INTEGER");
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgSchemaTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Models/OptcgCard.cs OmniCard.Data/OptcgDbContext.cs OmniCard.Tests/Data/OptcgSchemaTests.cs
git commit -m "feat(optcg): add EdgeHash column"
```

---

### Task 3: Compute + store both hashes in `ComputeImageHashesAsync`

**Files:**
- Modify: `OmniCard.CardMatching/OptcgService.cs` — the hash-computation query, the per-image compute, and `SaveHashBatchAsync`

**Interfaces:**
- Consumes: `IPerceptualHashService.ComputeEdgeHash` (Task 1); `OptcgCard.EdgeHash` (Task 2).
- Produces: after a hash pass, each hashed card has both `ImageHash` and `EdgeHash`.

- [ ] **Step 1: Broaden the "needs hashing" query**

In `ComputeImageHashesAsync`, change the incremental filter so a card is reprocessed when either hash is missing. Replace:

```csharp
        if (!forceAll)
            query = query.Where(c => c.ImageHash == null);
```
with:
```csharp
        if (!forceAll)
            query = query.Where(c => c.ImageHash == null || c.EdgeHash == null);
```

- [ ] **Step 2: Compute both hashes per image**

In the `Parallel.ForEachAsync` body, where the luminance hash is computed (currently `var hash = _hashService.ComputeHash(buffer);` with `results.Add((card.CardSetId, hash));`), compute both and carry both. Change the results collection type from `List<(string CardSetId, ulong Hash)>` to `List<(string CardSetId, ulong Hash, ulong EdgeHash)>` (update the two declarations of `results` and the `toSave` local), and change the compute block to:

```csharp
                    using var buffer = new MemoryStream(imageBytes);
                    var hash = _hashService.ComputeHash(buffer);
                    buffer.Position = 0;
                    var edgeHash = _hashService.ComputeEdgeHash(buffer);

                    lock (saveLock)
                    {
                        results.Add((card.CardSetId, hash, edgeHash));
                    }
```

- [ ] **Step 3: Persist both in `SaveHashBatchAsync`**

Change `SaveHashBatchAsync`'s signature and body to store both:

```csharp
    private async Task SaveHashBatchAsync(List<(string CardSetId, ulong Hash, ulong EdgeHash)> batch, CancellationToken ct)
    {
        await using var context = _dbContextFactory.CreateDbContext();
        foreach (var (cardSetId, hash, edgeHash) in batch)
        {
            var artRelativePath = GetLocalArtRelativePath(cardSetId);
            await context.Cards
                .Where(c => c.CardSetId == cardSetId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ImageHash, hash)
                    .SetProperty(c => c.EdgeHash, edgeHash)
                    .SetProperty(c => c.LocalImagePath, artRelativePath), ct);
        }
    }
```

- [ ] **Step 4: Build + full suite**

Run: `dotnet build --nologo -clp:ErrorsOnly` (0 errors), then `dotnet test OmniCard.Tests --nologo` (all green).
Note: the download/hash loop hits the network and isn't unit-tested (pre-existing); this task is verified by build + the downstream matching test (Task 4) + a live hash pass. Confirm the tuple/type changes compile across all `results`/`toSave`/`SaveHashBatchAsync` uses.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/OptcgService.cs
git commit -m "feat(optcg): compute and store edge hash alongside pHash"
```

---

### Task 4: Edge-hash cache + `scanEdgeHash` match path

**Files:**
- Modify: `OmniCard.Shared/Interfaces/ICardGameService.cs` (signature)
- Modify: `OmniCard.CardMatching/ScryfallService.cs` (signature, ignore param)
- Modify: `OmniCard.CardMatching/OptcgService.cs` (edge cache + match path)
- Test: `OmniCard.Tests/Services/OptcgServiceTests.cs` (add cases)

**Interfaces:**
- Consumes: `OptcgCard.EdgeHash`.
- Produces: `FindClosestMatch(..., int maxDistance = 14, ulong? scanEdgeHash = null)`; when `scanEdgeHash` is set, OPTCG matches on the edge-hash cache.

- [ ] **Step 1: Write the failing tests**

Add to `OmniCard.Tests/Services/OptcgServiceTests.cs`. In the fixture seed, give the existing cards distinct `EdgeHash` values, e.g. add to the `OP01-001` seed `EdgeHash = 0x0F0F0F0F0F0F0F0FUL` and to `OP01-002` `EdgeHash = 0xF0F0F0F0F0F0F0F0UL`. Then:

```csharp
    [Fact]
    public void FindClosestMatch_FoilScan_MatchesViaEdgeHash_NotLuminance()
    {
        var svc = CreateService();
        // Luminance hash is deliberately far from every card (would miss); edge hash is
        // exact for OP01-001.
        var match = svc.FindClosestMatch(
            imageHash: 0x1234_5678_9ABC_DEF0UL,
            scanEdgeHash: 0x0F0F0F0F0F0F0F0FUL);

        Assert.NotNull(match);
        Assert.Equal("OP01-001", match!.GameSpecificId);
    }

    [Fact]
    public void FindClosestMatch_NonFoil_UsesLuminanceHash()
    {
        var svc = CreateService();
        // scanEdgeHash null -> luminance path; 0x0 is exact for OP01-001's ImageHash.
        var match = svc.FindClosestMatch(0x0000000000000000UL);

        Assert.NotNull(match);
        Assert.Equal("OP01-001", match!.GameSpecificId);
    }
```

(If the fixture's existing seed uses different `ImageHash` values, keep this test's `0x0` consistent with whatever hash maps to `OP01-001` in that fixture.)

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgServiceTests`
Expected: FAIL — `FindClosestMatch` has no `scanEdgeHash` parameter.

- [ ] **Step 3: Extend the interface + Scryfall**

In `OmniCard.Shared/Interfaces/ICardGameService.cs`, change the `FindClosestMatch` signature to end with `, int maxDistance = 14, ulong? scanEdgeHash = null)`.

In `OmniCard.CardMatching/ScryfallService.cs`, update the `FindClosestMatch` signature identically (add `, ulong? scanEdgeHash = null` at the end). Scryfall ignores it (no body change needed).

- [ ] **Step 4: Add the edge-hash cache + match path in OptcgService**

Add a field next to `_hashCache`:

```csharp
    private List<(string CardSetId, ulong EdgeHash)>? _edgeHashCache;
```

Add `_edgeHashCache = null;` at every site that already sets `_hashCache = null;`.

Change the `FindClosestMatch` signature to match the interface (`..., int maxDistance = 14, ulong? scanEdgeHash = null`). Immediately **after the OCR Phase 0 block** (which must still run first), before the luminance hash-cache build, insert the edge path:

```csharp
        // Foil path: the scan carries an edge (structure) hash — match on it instead of the
        // luminance pHash, which the foil color shift corrupts.
        if (scanEdgeHash is ulong scanEdge)
        {
            if (_edgeHashCache is null)
            {
                _edgeHashCache = _readContext.Cards
                    .Where(c => c.EdgeHash != null)
                    .Select(c => new { c.CardSetId, Edge = c.EdgeHash!.Value })
                    .AsNoTracking()
                    .AsEnumerable()
                    .Select(c => (c.CardSetId, c.Edge))
                    .ToList();
                _logger.LogInformation("OPTCG edge-hash cache loaded with {Count} entries", _edgeHashCache.Count);
            }

            string bestEdgeId = "";
            int bestEdgeDist = int.MaxValue;
            foreach (var (cardSetId, edge) in _edgeHashCache)
            {
                if (setFilter is not null && !setFilter.Contains(_hashSetLookup?.GetValueOrDefault(cardSetId) ?? ""))
                    continue; // set filter enforced below via LookupOptcgCard when _hashSetLookup is unset
                var dist = PerceptualHashService.HammingDistance(scanEdge, edge);
                if (dist < bestEdgeDist) { bestEdgeDist = dist; bestEdgeId = cardSetId; }
            }

            if (bestEdgeId.Length > 0 && bestEdgeDist <= maxDistance)
            {
                var card = _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.CardSetId == bestEdgeId);
                if (card is not null && (setFilter is null || setFilter.Contains(card.SetId)))
                {
                    LastMatchDiagnostics.DecisionPhase = "EdgeHashFoil";
                    LastMatchDiagnostics.PHashDistance = bestEdgeDist;
                    var confidence = Math.Max(0, (1.0 - (double)bestEdgeDist / maxDistance)) * 100;
                    _logger.LogInformation("OPTCG foil edge-hash match: {CardName} ({CardId}) dist {Dist}", card.CardName, card.CardSetId, bestEdgeDist);
                    return LookupOptcgCard(card.CardSetId, confidence);
                }
            }

            _logger.LogDebug("OPTCG foil edge-hash: no match within {Max} (best {Dist})", maxDistance, bestEdgeDist);
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }
```

Note on the set filter: the edge cache doesn't carry `SetId`, so the loop's filter guard is best-effort; the authoritative set-filter check is the `setFilter.Contains(card.SetId)` after looking the card up. Simplify the loop guard to just compute distance for all, and rely on the post-lookup `card.SetId` check (remove the in-loop `_hashSetLookup` guard if it complicates — correctness comes from the post-lookup check). Keep the in-loop filter only if `_hashSetLookup` is already populated; otherwise skip it.

(Simpler, preferred form of the loop — use this:)

```csharp
            string bestEdgeId = "";
            int bestEdgeDist = int.MaxValue;
            foreach (var (cardSetId, edge) in _edgeHashCache)
            {
                var dist = PerceptualHashService.HammingDistance(scanEdge, edge);
                if (dist < bestEdgeDist) { bestEdgeDist = dist; bestEdgeId = cardSetId; }
            }
```

- [ ] **Step 5: Run tests**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgServiceTests`
Expected: PASS (new + existing). If the fixture lacks `EdgeHash` seeds, the foil test won't find a match — ensure Step 1's seed edit is applied.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/ICardGameService.cs OmniCard.CardMatching/ScryfallService.cs OmniCard.CardMatching/OptcgService.cs OmniCard.Tests/Services/OptcgServiceTests.cs
git commit -m "feat(optcg): match foil scans via edge-hash cache"
```

---

### Task 5: Plumb foil scan edge hash through `CardService`

**Files:**
- Modify: `OmniCard.Shared/Models/ScannedCard.cs` (add `ScanEdgeHash`)
- Modify: `OmniCard.Collection/CardService.cs` (`FindBestMatch` param; compute + pass for foil scans)
- Test: `OmniCard.Tests/Services/FallbackMatchingTests.cs` (add a pass-through case)

**Interfaces:**
- Consumes: `FindClosestMatch(..., scanEdgeHash)` (Task 4); `IPerceptualHashService.ComputeEdgeHash` (Task 1).
- Produces: `FindBestMatch(..., ulong? scanEdgeHash = null)`; foil One Piece scans pass their edge hash into matching.

- [ ] **Step 1: Write the failing test**

In `OmniCard.Tests/Services/FallbackMatchingTests.cs`, extend the `StubGameService` to record the `scanEdgeHash` it was called with, then add a test. First update the stub's `FindClosestMatch`:

```csharp
        public ulong? LastScanEdgeHash { get; private set; }
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
        {
            LastScanEdgeHash = scanEdgeHash;
            return match;
        }
```

Then add:

```csharp
    [Fact]
    public void FindBestMatch_PassesScanEdgeHashThrough()
    {
        var op = new StubGameService(CardGame.OnePiece, match: null);
        var svc = CreateCardService([op]);
        svc.SelectedGame = CardGame.OnePiece;

        svc.FindBestMatch(0xAAAA, scanEdgeHash: 0xBEEF);

        Assert.Equal(0xBEEFUL, op.LastScanEdgeHash);
    }
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~FallbackMatchingTests`
Expected: FAIL — `FindBestMatch` has no `scanEdgeHash` parameter.

- [ ] **Step 3: Add `ScanEdgeHash` to ScannedCard**

In `OmniCard.Shared/Models/ScannedCard.cs`, add near `ArtHashes`:

```csharp
    public ulong? ScanEdgeHash { get; set; }
```

- [ ] **Step 4: Thread `scanEdgeHash` through `FindBestMatch`**

In `OmniCard.Collection/CardService.cs`, change `FindBestMatch`'s signature and its `FindClosestMatch` call:

```csharp
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null)
    {
        if (setFilter is { Count: 0 })
            setFilter = null;

        if (_gameServices.TryGetValue(SelectedGame, out var primaryService))
        {
            var primaryMatch = primaryService.FindClosestMatch(hash, artHashes, ocrResult, setFilter, preferredSets, scanEdgeHash: scanEdgeHash);
            if (primaryMatch is not null)
                return (primaryMatch, SelectedGame);
        }

        return (null, SelectedGame);
    }
```

- [ ] **Step 5: Compute + pass the foil edge hash in the scan pipeline**

In `AddFromStream`, where the scan pHash is computed (`var hash = _hashService.ComputeHash(buffer, OnHashStage);` ~line 232), add the edge hash for foil One Piece scans. After the existing hash/artHashes computation and before the sync match block, add:

```csharp
        // Foil cards scan with a holographic color shift that corrupts the luminance pHash;
        // compute a color-robust edge hash so matching can use it (One Piece only).
        ulong? scanEdgeHash = null;
        if (DefaultIsFoil && SelectedGame == CardGame.OnePiece)
        {
            buffer.Position = 0;
            scanEdgeHash = _hashService.ComputeEdgeHash(buffer);
        }
```

(Compute this before `buffer` is disposed / before `rawBytes` is taken; if `buffer` is already converted to `rawBytes` at that point, compute from `new MemoryStream(rawBytes)` instead.) Set it on the scanned card (`ScanEdgeHash = scanEdgeHash,` in the `ScannedCard` initializer) and pass it into the synchronous match:

```csharp
            var (bestMatch, matchedGame) = FindBestMatch(hash, artHashes, null, SelectedSetFilter, detectedSets, scanEdgeHash);
```

Also pass the foil scan's edge hash into the other `FindBestMatch` call sites in the pipeline so foils benefit everywhere: the async OCR re-match block (the `if (game == CardGame.OnePiece)` async branch that calls `FindBestMatch` after OCR) and the 180°-rotation retry. For the rotation retry, recompute `ComputeEdgeHash` on the **rotated** bytes when foil (structure rotates too); elsewhere reuse `scannedCard.ScanEdgeHash`. (This is master's pipeline — plain `FindBestMatch` calls, not `ResolveOnePieceMatchAsync`.)

- [ ] **Step 6: Build + full suite**

Run: `dotnet build --nologo -clp:ErrorsOnly` (0 errors) then `dotnet test OmniCard.Tests --nologo` (all green, incl. the new pass-through test and existing `FallbackMatchingTests`).

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Models/ScannedCard.cs OmniCard.Collection/CardService.cs OmniCard.Tests/Services/FallbackMatchingTests.cs
git commit -m "feat(scanner): use edge hash to match foil One Piece scans"
```

---

### Task 6: Live verification (manual — user)

**Files:** none.

- [ ] **Step 1:** Run a hash pass ("Card Data → Recompute All Hashes", or "Compute Missing Hashes") so references get `EdgeHash` populated.
- [ ] **Step 2:** Enable foil scanning (set the foil flag), scan OP16-106 (the yellow/green foil), and confirm via the log it matches — look for `OPTCG foil edge-hash match: ... ` and a resulting match where it previously missed.
- [ ] **Step 3:** Scan a few non-foil cards with the flag off and confirm they still match as before (luminance path unaffected).

---

## Self-Review

**1. Spec coverage:**
- §1 edge hash (gradient, reuse pipeline) → Task 1.
- §2 EdgeHash column + schema + hash for all refs → Task 2 (column) + Task 3 (populate).
- §3 matching (scanEdgeHash param, OCR-first still first, edge cache, set filter, maxDistance 14) → Task 4.
- §4 scan-side plumbing (compute for foil One Piece, IsFoil already set, store on ScannedCard, rotation/re-match reuse) → Task 5.
- §5 unchanged non-foil/MTG/OCR → preserved (Scryfall ignores param, luminance path when scanEdgeHash null).
- Testing (color-invariance, determinism, foil match path, schema, live) → Tasks 1, 2, 4, 6.

**2. Placeholder scan:** No TBD/TODO; complete code in code steps. The Task 4 set-filter note gives a concrete simpler form; Task 5 gives concrete compute/placement guidance rather than "handle it."

**3. Type consistency:** `ComputeEdgeHash(Stream, Action<HashStageResult>?) → ulong` and `ComputeHashFromPixels(double[,], out double[,]) → ulong` and `GradientMagnitude(double[,]) → double[,]` are consistent across Tasks 1/3/5. `FindClosestMatch(..., int maxDistance = 14, ulong? scanEdgeHash = null)` is identical in the interface, Scryfall, and OptcgService (Task 4) and called with `scanEdgeHash:` named arg (Task 4/5). `FindBestMatch(..., ulong? scanEdgeHash = null)` (Task 5) matches its call sites. `EdgeHash` (`ulong?`) on `OptcgCard` (Task 2) used in Tasks 3/4. `ScanEdgeHash` (`ulong?`) on `ScannedCard` (Task 5).
