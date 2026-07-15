# OPTCG Fuzzy OCR Resolution — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make One Piece cards match even when Windows OCR misreads the stylized digits, by resolving the garbled OCR text against the known card-number vocabulary (closest match), and by using a taller crop region so OCR engages.

**Architecture:** A pure `OptcgCollectorNumberResolver` collapses OCR-confusable characters into digit-classes and snaps the OCR text to the closest real card number (fully unit-tested against the real garbled strings the diagnostic harness captured). `OptcgService.FindClosestMatch` Phase 0 uses it against a cached list of all card numbers; exact reads assign at confidence 100, fuzzy corrections at confidence 10 (which the existing `<15 → VeryLowConfidence` rule flags for review). `OcrMatchingService` returns the raw OCR text and uses a taller region.

**Tech Stack:** C# / .NET 10, EF Core + SQLite, Windows.Media.Ocr, xUnit.

## Global Constraints

- Builds on branch `feat/optcg-ocr-first` (Part A hardening, Part B OCR-first pipeline, and the diagnostic harness are already present).
- Confusable-collapse map (applied after uppercasing; drop non-alphanumerics): `{O,Q,D,0}→0`, `{I,L,T,J,|,1}→1`, `{S,5}→5`, `{B,8}→8`, `{Z,2}→2`, `{G,6}→6`, other A–Z unchanged, other digits unchanged. Applied to BOTH the OCR text and each candidate.
- Fuzzy match must be **unique best** and within a distance **threshold of 1** (card numbers collapse to ≤8 chars); ties or over-threshold → no match (fall back to pHash — never snap to a guess).
- Exact read (clean digits, strict-regex parse that exists in the vocabulary) → confidence **100**. Fuzzy correction → confidence **10** (triggers the existing `VeryLowConfidence` flag).
- Vocabulary = distinct `CardNumber` values (base card numbers); the resolver returns a card number, and `LookupOptcgCard` resolves the base variant (base `CardSetId == CardNumber`).
- Taller crop region: `(0.66, 0.915, 0.28, 0.075)` (was `(0.68, 0.925, 0.24, 0.055)` — too short; harness proved OCR reads nothing at 0.055 height).
- `DetectOptcgCollectorNumberAsync` keeps its signature `(string? , double)` but now returns the **raw OCR text** (best-effort), not a strict-parsed number; `(null, 0)` only when OCR reads nothing / engine unavailable.
- Do not change MTG, hashing, or the pHash threshold. The diagnostic harness stays.
- Build: `dotnet build`. Test: `dotnet test OmniCard.Tests`.

---

### Task 1: `OptcgCollectorNumberResolver` (pure, fully tested)

**Files:**
- Create: `OmniCard.CardMatching/OptcgCollectorNumberResolver.cs`
- Test: `OmniCard.Tests/Services/OptcgCollectorNumberResolverTests.cs`

**Interfaces:**
- Produces:
  - `internal readonly record struct OptcgCollectorNumberResolver.Result(string CardNumber, bool Exact)`
  - `internal static Result? OptcgCollectorNumberResolver.Resolve(string? ocrText, IReadOnlyCollection<string> cardNumbers)`
  - `internal static string OptcgCollectorNumberResolver.Collapse(string s)`

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/OptcgCollectorNumberResolverTests.cs`:

```csharp
using OmniCard.CardMatching;

namespace OmniCard.Tests.Services;

public class OptcgCollectorNumberResolverTests
{
    // Distractors incl. numbers one digit away, to prove collapse keeps them distinct.
    private static readonly string[] Vocab =
        ["OP15-011", "OP15-017", "OP15-001", "OP15-024", "EB04-003", "OP01-001"];

    [Theory]
    [InlineData("OPIS.OIIU")]                                   // harness: tight-taller
    [InlineData("opts-0110")]                                   // harness: wide, raw
    [InlineData("East Blue/Krieg PiratesJ opts-0110")]          // harness: wide preprocessed
    [InlineData("Pearl B/Krieg Pirateo OPIS.OII")]              // harness: full bottom strip
    public void Resolve_GarbledOp15011_ResolvesCorrected(string ocr)
    {
        var r = OptcgCollectorNumberResolver.Resolve(ocr, Vocab);
        Assert.NotNull(r);
        Assert.Equal("OP15-011", r!.Value.CardNumber);
        Assert.False(r.Value.Exact);   // required a correction
    }

    [Fact]
    public void Resolve_CleanNumber_ResolvesExact()
    {
        var r = OptcgCollectorNumberResolver.Resolve("OP15-011", Vocab);
        Assert.NotNull(r);
        Assert.Equal("OP15-011", r!.Value.CardNumber);
        Assert.True(r.Value.Exact);
    }

    [Fact]
    public void Resolve_CleanNumberWithSurroundingText_ResolvesExact()
    {
        var r = OptcgCollectorNumberResolver.Resolve("Straw Hat Crew OP15-024", Vocab);
        Assert.NotNull(r);
        Assert.Equal("OP15-024", r!.Value.CardNumber);
        Assert.True(r.Value.Exact);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Straw Hat Crew")]        // no number-shaped content
    public void Resolve_NoNumber_ReturnsNull(string? ocr)
    {
        Assert.Null(OptcgCollectorNumberResolver.Resolve(ocr, Vocab));
    }

    [Fact]
    public void Resolve_DifferentCard_ResolvesToThatCard()
    {
        // "EB04-003" misread with letter-for-digit confusion (0->O, 3 clean)
        var r = OptcgCollectorNumberResolver.Resolve("EBO4-OO3", Vocab);
        Assert.NotNull(r);
        Assert.Equal("EB04-003", r!.Value.CardNumber);
    }

    [Fact]
    public void Resolve_AmbiguousGarble_ReturnsNull()
    {
        // Collapses within distance 1 of BOTH OP15-011 and OP15-017 (last digit lost/garbled)
        // -> not unique -> must NOT guess.
        var r = OptcgCollectorNumberResolver.Resolve("OPIS-O1", ["OP15-011", "OP15-017"]);
        Assert.Null(r);
    }

    [Fact]
    public void Collapse_MapsConfusablesAndDropsSeparators()
    {
        // O->0, P->P, I->1, S->5, dash dropped, O->0, I->1, I->1
        Assert.Equal("0P150111".Substring(0, 7), OptcgCollectorNumberResolver.Collapse("OP15-011"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgCollectorNumberResolverTests`
Expected: FAIL — build error, `OptcgCollectorNumberResolver` does not exist.

- [ ] **Step 3: Create the resolver**

Create `OmniCard.CardMatching/OptcgCollectorNumberResolver.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace OmniCard.CardMatching;

// Resolves possibly-garbled OCR text of a collector number to a real card number by
// snapping to the closest entry in the known vocabulary. Windows OCR misreads the
// stylized card font's digits as letters (1->I/t, 5->S, 0->O), so we collapse
// OCR-confusable characters into digit-classes on both sides and match.
internal static partial class OptcgCollectorNumberResolver
{
    internal readonly record struct Result(string CardNumber, bool Exact);

    [GeneratedRegex(@"([A-Za-z]{2,4}\d{2})\s*[-—]\s*(\d{2,3})", RegexOptions.IgnoreCase)]
    private static partial Regex StrictRegex();

    internal static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToUpperInvariant())
        {
            char c = ch switch
            {
                'O' or 'Q' or 'D' or '0' => '0',
                'I' or 'L' or 'T' or 'J' or '|' or '1' => '1',
                'S' or '5' => '5',
                'B' or '8' => '8',
                'Z' or '2' => '2',
                'G' or '6' => '6',
                >= 'A' and <= 'Z' => ch,
                >= '3' and <= '9' => ch,
                _ => '\0',
            };
            if (c != '\0') sb.Append(c);
        }
        return sb.ToString();
    }

    internal static Result? Resolve(string? ocrText, IReadOnlyCollection<string> cardNumbers)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return null;

        // Fast path: a clean read that maps to a real card number.
        var m = StrictRegex().Match(ocrText);
        if (m.Success)
        {
            var clean = $"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}";
            if (cardNumbers.Contains(clean))
                return new Result(clean, Exact: true);
        }

        // Fuzzy path.
        var haystack = Collapse(ocrText);
        if (haystack.Length < 4) return null;

        string? best = null;
        int bestDist = int.MaxValue;
        bool tie = false;

        foreach (var card in cardNumbers)
        {
            var needle = Collapse(card);
            if (needle.Length < 4) continue;
            int dist = MinWindowDistance(needle, haystack);
            if (dist < bestDist) { bestDist = dist; best = card; tie = false; }
            else if (dist == bestDist) { tie = true; }
        }

        if (best is null) return null;
        int threshold = Collapse(best).Length <= 8 ? 1 : 2;
        if (bestDist > threshold || tie) return null;
        return new Result(best, Exact: false);
    }

    // Min Levenshtein between `needle` and any window of `haystack` within ±1 of needle length.
    private static int MinWindowDistance(string needle, string haystack)
    {
        if (haystack.Length <= needle.Length + 1)
            return Levenshtein(needle, haystack);

        int min = int.MaxValue;
        for (int len = Math.Max(1, needle.Length - 1); len <= needle.Length + 1; len++)
        {
            for (int start = 0; start + len <= haystack.Length; start++)
            {
                min = Math.Min(min, Levenshtein(needle, haystack.Substring(start, len)));
                if (min == 0) return 0;
            }
        }
        return min;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgCollectorNumberResolverTests`
Expected: PASS (all cases). If `Resolve_AmbiguousGarble_ReturnsNull` or a corrected case behaves unexpectedly, the collapse map / threshold is the lever — adjust and re-run (do not loosen the uniqueness check).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.CardMatching/OptcgCollectorNumberResolver.cs OmniCard.Tests/Services/OptcgCollectorNumberResolverTests.cs
git commit -m "feat(optcg): fuzzy collector-number resolver (confusable-collapse)"
```

---

### Task 2: `OptcgService` Phase 0 uses the fuzzy resolver

**Files:**
- Modify: `OmniCard.CardMatching/OptcgService.cs` — add an all-card-numbers cache; rewrite Phase 0 (~459-479); invalidate the new cache where `_hashCache` is invalidated (~94, ~268, ~431)
- Test: `OmniCard.Tests/Services/OptcgServiceTests.cs` (add cases)

**Interfaces:**
- Consumes: `OptcgCollectorNumberResolver.Resolve` (Task 1); existing `LookupOptcgCard(string cardSetId, double? confidence)`.
- Produces: Phase 0 resolves `ocrResult.CollectorNumber` (raw text) via the fuzzy resolver; exact → confidence 100, corrected → confidence 10.

- [ ] **Step 1: Write the failing tests**

Add to `OmniCard.Tests/Services/OptcgServiceTests.cs` (the fixture already seeds OPTCG cards and builds `OptcgService`; add an unhashed OP15-011 in the constructor seed alongside the existing cards, then these tests):

First, in the test fixture's seed block (where `ctx.Cards.Add(...)` calls are), add:
```csharp
        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP15-011",
            CardNumber = "OP15-011",
            CardName = "Pearl",
            SetId = "OP15",
            SetName = "s",
            Rarity = "C",
            // no ImageHash — unhashed, so ONLY OCR can match it
        });
```
Then add the tests:
```csharp
    [Fact]
    public void FindClosestMatch_GarbledOcrText_ResolvesFuzzyWithLowConfidence()
    {
        var svc = CreateService();
        var ocr = new OcrMatchResult { CollectorNumber = "OPIS.OIIU", CollectorNumberConfidence = 0.95 };

        var match = svc.FindClosestMatch(0x0UL, ocrResult: ocr);

        Assert.NotNull(match);
        Assert.Equal("OP15-011", match!.CollectorNumber);
        Assert.Equal(10.0, match.Confidence);   // corrected -> low confidence -> VeryLowConfidence downstream
    }

    [Fact]
    public void FindClosestMatch_CleanOcrText_ResolvesExactHighConfidence()
    {
        var svc = CreateService();
        var ocr = new OcrMatchResult { CollectorNumber = "OP15-011", CollectorNumberConfidence = 0.95 };

        var match = svc.FindClosestMatch(0x0UL, ocrResult: ocr);

        Assert.NotNull(match);
        Assert.Equal("OP15-011", match!.CollectorNumber);
        Assert.Equal(100.0, match.Confidence);
    }

    [Fact]
    public void FindClosestMatch_UnresolvableOcrText_DoesNotFuzzyMatch()
    {
        var svc = CreateService();
        // Junk that isn't near any card number; with maxDistance small and hash 0 far from
        // seeded hashes, expect no OCR match (falls through to pHash which also misses here).
        var ocr = new OcrMatchResult { CollectorNumber = "Straw Hat Crew", CollectorNumberConfidence = 0.95 };

        var match = svc.FindClosestMatch(0xFFFFFFFFFFFFFFFFUL, ocrResult: ocr, maxDistance: 2);

        // The garbage text must not snap to OP15-011 or any card via OCR.
        Assert.True(match is null || match.CollectorNumber != "OP15-011");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgServiceTests`
Expected: FAIL — `FindClosestMatch_GarbledOcrText...` fails: current Phase 0 does exact `LookupOptcgCard("OPIS.OIIU")` → null → no fuzzy match.

- [ ] **Step 3: Add the all-card-numbers cache**

In `OmniCard.CardMatching/OptcgService.cs`, add a field near `_hashCache` (~line 106):
```csharp
    private List<string>? _allCardNumbers;
```
Add a helper (near `LookupOptcgCard`):
```csharp
    private void EnsureCardNumberCache()
    {
        _allCardNumbers ??= _readContext.Cards
            .AsNoTracking()
            .Select(c => c.CardNumber)
            .Where(n => n != "")
            .Distinct()
            .ToList();
    }
```
Add `_allCardNumbers = null;` immediately after each existing `_hashCache = null;` line (there are three: after the migration wipe ~94, and after the two read-context swaps ~268 and ~431), so the vocabulary refreshes when data changes.

- [ ] **Step 4: Rewrite Phase 0 to use the resolver**

Replace the Phase 0 block (currently ~459-479) with:
```csharp
        // Phase 0: OCR collector number (most reliable for OPTCG — covers unhashed cards).
        if (ocrResult?.CollectorNumber is not null && ocrResult.CollectorNumberConfidence >= 0.5)
        {
            EnsureCardNumberCache();
            var resolved = OptcgCollectorNumberResolver.Resolve(ocrResult.CollectorNumber, _allCardNumbers!);
            if (resolved is { } r)
            {
                // OCR reads only the shared printed number → base (index-0) variant by design.
                // Exact read → full confidence; fuzzy correction → low confidence so it is
                // flagged for review downstream rather than committed silently.
                var ocrMatch = LookupOptcgCard(r.CardNumber, confidence: r.Exact ? 100.0 : 10.0);
                if (ocrMatch is not null && (setFilter is null || setFilter.Contains(ocrMatch.SetCode)))
                {
                    _logger.LogInformation("OPTCG OCR {Kind} match: {CardName} ({CardId}) from \"{Raw}\"",
                        r.Exact ? "exact" : "fuzzy", ocrMatch.Name, ocrMatch.CollectorNumber, ocrResult.CollectorNumber);
                    LastMatchDiagnostics.DecisionPhase = r.Exact ? "OcrCollectorNumber" : "OcrFuzzy";
                    return ocrMatch;
                }
            }
            else
            {
                _logger.LogDebug("OPTCG OCR text \"{Raw}\" did not resolve to a card", ocrResult.CollectorNumber);
            }
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~OptcgServiceTests`
Expected: PASS (new + existing OPTCG tests — existing clean-number OCR cases resolve via the fast path at confidence 100).

- [ ] **Step 6: Commit**

```bash
git add OmniCard.CardMatching/OptcgService.cs OmniCard.Tests/Services/OptcgServiceTests.cs
git commit -m "feat(optcg): resolve OCR text via fuzzy vocabulary match in Phase 0"
```

---

### Task 3: Taller region + raw-text OCR read

**Files:**
- Modify: `OmniCard.Imaging/OcrMatchingService.cs` — `OptcgCollectorNumberRegion` (~35) and `DetectOptcgCollectorNumberAsync` (~260-296)

**Interfaces:**
- Produces: `DetectOptcgCollectorNumberAsync` returns raw OCR text (best-effort) + confidence; taller region.

- [ ] **Step 1: Raise the crop region**

In `OmniCard.Imaging/OcrMatchingService.cs`, change:
```csharp
    internal static readonly (double X, double Y, double W, double H) OptcgCollectorNumberRegion =
        (0.68, 0.925, 0.24, 0.055);
```
to:
```csharp
    // Taller/wider than the number's tight bounds: Windows OCR reads nothing from a crop
    // as short as 0.055 (its text detector needs more to engage). Extra surrounding text
    // is harmless — the number is resolved fuzzily against the card-number vocabulary.
    internal static readonly (double X, double Y, double W, double H) OptcgCollectorNumberRegion =
        (0.66, 0.915, 0.28, 0.075);
```

- [ ] **Step 2: Return the raw OCR text**

Replace the body of `DetectOptcgCollectorNumberAsync` (the part after the `rect.Width < 10` guard, currently ~272-289) with:
```csharp
            // Return the raw OCR text (preprocessed pass, then raw retry). The stylized card
            // font makes Windows OCR misread digits as letters, so the caller resolves the
            // text fuzzily against the known card-number vocabulary rather than parsing it here.
            var (preText, _) = await OcrCroppedRegionAsync(bitmap, rect, preprocess: true);
            if (!string.IsNullOrWhiteSpace(preText))
            {
                _logger.LogDebug("OPTCG OCR region text (preprocessed): \"{Text}\"", preText);
                return (preText, 0.95);
            }

            var (rawText, _) = await OcrCroppedRegionAsync(bitmap, rect, preprocess: false);
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                _logger.LogDebug("OPTCG OCR region text (raw): \"{Text}\"", rawText);
                return (rawText, 0.95);
            }

            _logger.LogDebug("OPTCG OCR read no text from the collector-number region");
            return (null, 0);
```
(The `ExtractCollectorNumber` helper and `ApplyOcrPreprocessing` stay — `ExtractCollectorNumber` is now used only inside the resolver's fast path in Task 1's file, so it is no longer referenced here; leave it in `OcrMatchingService` as-is, it is still covered by `OcrPreprocessingTests` and harmless. Do not delete it.)

- [ ] **Step 3: Build the solution**

Run: `dotnet build --nologo -clp:ErrorsOnly`
Expected: 0 errors. (Callers `CardService.cs:180,447` and `RootViewModel.cs:979` still compile — the tuple signature is unchanged; they pass the text straight into `OcrMatchResult.CollectorNumber`.)

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test OmniCard.Tests --nologo`
Expected: PASS. `OcrPreprocessingTests` still pass (helpers unchanged). `ExtractCollectorNumber` tests still pass. No test asserts the old region value or the old strict-parse return of `DetectOptcgCollectorNumberAsync` (verify by search: `git grep -n "DetectOptcgCollectorNumberAsync" -- '*Tests*'` — the stubs return canned tuples, they don't call the real method).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Imaging/OcrMatchingService.cs
git commit -m "feat(ocr): taller OPTCG region; return raw OCR text for fuzzy resolution"
```

---

### Task 4: Live verification (manual — user)

**Files:** none.

- [ ] **Step 1:** Build and launch the app, select One Piece, scan **OP15-011** (and EB04-003).

- [ ] **Step 2:** Confirm in the log (`<dataDir>/logs/tcgcardscanner-<date>.log`):
  - `OPTCG OCR region text (preprocessed): "OPIS.OIIU"` (or similar) — OCR now engages.
  - `OPTCG OCR fuzzy match: Pearl (OP15-011) from "..."` — resolved.
  - `One Piece resolved to "Pearl" (OP15 #OP15-011)`.
  - The card appears matched (flagged VeryLowConfidence for review, since it was a fuzzy correction), not "missing from database".

- [ ] **Step 3:** If it still misses, capture `OPTCG OCR read no text...` (region still not engaging → widen/heighten further) or `did not resolve` (OCR text captured but too garbled → collapse-map/threshold tuning). Re-run the diagnostic harness (`OcrDiagnosticHarness`) on the new scan for full ground truth.

---

## Self-Review

**1. Spec coverage:**
- §1 taller region → Task 3 Step 1.
- §2 OCR returns raw text → Task 3 Step 2.
- §3 fuzzy resolver in OptcgService (fast path + confusable-collapse + windowed Levenshtein + unique-best-within-threshold) → Task 1 (pure resolver) + Task 2 (Phase 0 wiring, all-card-numbers cache).
- §4 safety: exact→100, corrected→10 (→ VeryLowConfidence via existing rule), ambiguous→null→pHash → Task 2 Step 4 + Task 1 (uniqueness/threshold).
- §5 testability: real harness strings + distractors + negatives + collision → Task 1 tests; unhashed-card integration → Task 2 tests; region engagement → Task 4 (live).
- Out of scope (hash coverage, MTG, threshold, keep harness) respected.

**2. Placeholder scan:** No TBD/TODO; complete code in every code step; concrete tuning guidance (not "handle errors").

**3. Type consistency:** `Resolve(string?, IReadOnlyCollection<string>) → Result?`, `Result(string CardNumber, bool Exact)`, `Collapse(string)→string` defined in Task 1 and used identically in Task 2. `LookupOptcgCard(string, double?)` matches its existing signature. `_allCardNumbers` (`List<string>?`) defined and invalidated consistently. `DetectOptcgCollectorNumberAsync` keeps `(string?, double)` so callers are untouched. `OptcgCard.CardNumber` (from the prior swap) is the vocabulary source.
