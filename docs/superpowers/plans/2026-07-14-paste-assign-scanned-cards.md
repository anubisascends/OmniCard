# Ctrl+V Paste-to-Assign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user press Ctrl+V while the scanned-card queue has focus to assign a card (from clipboard text) to the selected scanned card(s) — a copied code assigns directly, a copied name focuses the search field to pick a printing.

**Architecture:** Extract the pure decision logic (code-vs-name classification and the assign-vs-search branch) into a testable static helper `PasteClassifier`. `RootViewModel.PasteAssign(string?)` uses it to route between two flows built entirely from existing methods: `ManualSearch()` (runs the DB search, already rewrites `SET-NUM` → `cn:`), `AssignMatch()` (assigns the selected result to all selected cards), and `FocusManualSearch` (focuses the search box). The view's existing `PreviewKeyDown` handler gains a Ctrl+V branch that reads the clipboard and calls `PasteAssign`.

**Tech Stack:** C# / .NET 10, WPF, CommunityToolkit.Mvvm (source-generated `[GeneratedRegex]`, observable properties), xUnit.

## Global Constraints

- Discriminator regex (verbatim, matches the one `ManualSearch` already uses): `^([A-Za-z0-9]+)-(\d+[A-Za-z]*)$`. A whole-string match → code; otherwise → name.
- Reuse existing members unchanged: `ManualSearch()` (`RootViewModel:1404`), `AssignMatch()` / `AssignMatchCommand` (`RootViewModel:1438`), `FocusManualSearchBox()` (`RootViewModel:1866`) / `FocusManualSearch` action, `ManualSearchQuery`, `ManualSearchResults`, `SelectedManualSearchResult`, `SelectedScannedCards`, `Message`.
- A pasted name applies the chosen printing to ALL selected cards (existing `AssignMatch` behavior — do not change it).
- Code path assigns directly only when the lookup returns exactly one result; zero or many → fall back to focusing the search box (do not error, do not assign the wrong card).
- Clipboard I/O stays in the view (`ScannerTabView.xaml.cs`); `PasteAssign` takes a plain `string?` so its logic is testable.
- `RootViewModel` is NOT constructed in unit tests anywhere in this codebase (see `ClearMatchCommandTests` — it extracts logic instead). Follow that pattern: unit-test the extracted `PasteClassifier`; verify the `PasteAssign`/view wiring by building and running the app.
- Tests live in `OmniCard.Tests`; the app exposes internals via `InternalsVisibleTo("OmniCard.Tests")`, so an `internal` helper is testable.
- Build: `dotnet build`. Test: `dotnet test OmniCard.Tests`.

---

### Task 1: `PasteClassifier` pure decision helper

**Files:**
- Create: `OmniCard/Views/Root/PasteClassifier.cs`
- Test: `OmniCard.Tests/Services/PasteClassifierTests.cs`

**Interfaces:**
- Produces:
  - `internal enum PasteClassifier.PasteKind { Empty, Code, Name }`
  - `internal static PasteKind PasteClassifier.Classify(string? clipboardText)`
  - `internal static bool PasteClassifier.ShouldAssignDirectly(PasteKind kind, int resultCount)`

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/PasteClassifierTests.cs`:

```csharp
using OmniCard.Views.Root;

namespace OmniCard.Tests.Services;

public class PasteClassifierTests
{
    [Theory]
    [InlineData("OP15-041")]
    [InlineData("TMT-002")]
    [InlineData("ST01-001")]
    [InlineData("  OP15-041  ")] // trimmed before matching
    [InlineData("EB01-020")]
    public void Classify_CollectorNumberPattern_ReturnsCode(string text)
    {
        Assert.Equal(PasteClassifier.PasteKind.Code, PasteClassifier.Classify(text));
    }

    [Theory]
    [InlineData("Roronoa Zoro")]
    [InlineData("Monkey.D.Luffy")]
    [InlineData("Kelly Funk")]
    [InlineData("Well-Laid Plans")]   // dash but no digits after it
    [InlineData("R2-D2")]              // dash but non-digit after it
    public void Classify_FreeText_ReturnsName(string text)
    {
        Assert.Equal(PasteClassifier.PasteKind.Name, PasteClassifier.Classify(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Classify_EmptyOrWhitespace_ReturnsEmpty(string? text)
    {
        Assert.Equal(PasteClassifier.PasteKind.Empty, PasteClassifier.Classify(text));
    }

    [Fact]
    public void ShouldAssignDirectly_CodeWithExactlyOneResult_True()
    {
        Assert.True(PasteClassifier.ShouldAssignDirectly(PasteClassifier.PasteKind.Code, 1));
    }

    [Theory]
    [InlineData(PasteClassifier.PasteKind.Code, 0)]
    [InlineData(PasteClassifier.PasteKind.Code, 2)]
    [InlineData(PasteClassifier.PasteKind.Name, 1)]
    [InlineData(PasteClassifier.PasteKind.Empty, 1)]
    public void ShouldAssignDirectly_OtherwiseFalse(PasteClassifier.PasteKind kind, int count)
    {
        Assert.False(PasteClassifier.ShouldAssignDirectly(kind, count));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~PasteClassifierTests`
Expected: FAIL — build error, `PasteClassifier` does not exist.

- [ ] **Step 3: Create the helper**

Create `OmniCard/Views/Root/PasteClassifier.cs`:

```csharp
using System.Text.RegularExpressions;

namespace OmniCard.Views.Root;

// Pure decision logic for Ctrl+V paste-to-assign in the scanned queue.
// Kept separate from RootViewModel so it can be unit-tested without the view-model.
internal static partial class PasteClassifier
{
    internal enum PasteKind { Empty, Code, Name }

    // Same pattern RootViewModel.ManualSearch uses to detect SET-NUM codes.
    [GeneratedRegex(@"^([A-Za-z0-9]+)-(\d+[A-Za-z]*)$")]
    private static partial Regex CodeRegex();

    internal static PasteKind Classify(string? clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
            return PasteKind.Empty;
        return CodeRegex().IsMatch(clipboardText.Trim()) ? PasteKind.Code : PasteKind.Name;
    }

    // True when a code paste resolved to exactly one DB result and should be assigned
    // directly. Otherwise the caller prefills + focuses the search box for manual picking.
    internal static bool ShouldAssignDirectly(PasteKind kind, int resultCount)
        => kind == PasteKind.Code && resultCount == 1;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter FullyQualifiedName~PasteClassifierTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/Root/PasteClassifier.cs OmniCard.Tests/Services/PasteClassifierTests.cs
git commit -m "feat(scanner): add PasteClassifier for paste-to-assign decisions"
```

---

### Task 2: `PasteAssign` on RootViewModel + Ctrl+V wiring in the view

**Files:**
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (add `PasteAssign` method; place it next to `AssignMatch`, after line ~1489)
- Modify: `OmniCard/Views/Root/ScannerTabView.xaml.cs:157-179` (`ScannedCardsListView_PreviewKeyDown`)

**Interfaces:**
- Consumes: `PasteClassifier.Classify`, `PasteClassifier.ShouldAssignDirectly`, `PasteClassifier.PasteKind` (Task 1); existing `ManualSearch()`, `AssignMatch()`, `FocusManualSearchBox()`, `ManualSearchQuery`, `ManualSearchResults`, `SelectedManualSearchResult`, `SelectedScannedCards`, `Message`.
- Produces: `public void RootViewModel.PasteAssign(string? clipboardText)`.

- [ ] **Step 1: Add `PasteAssign` to RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, immediately after the `AssignMatch()` method (which ends at line ~1489 with `SelectedManualSearchResult = null;` then `}`), add:

```csharp
    /// <summary>
    /// Ctrl+V in the scanned queue: assign a card to the selected scanned card(s) from
    /// clipboard text. A collector-number code is looked up and assigned directly; any
    /// other text prefills and focuses the manual search box to pick a printing.
    /// </summary>
    public void PasteAssign(string? clipboardText)
    {
        var kind = PasteClassifier.Classify(clipboardText);
        if (kind == PasteClassifier.PasteKind.Empty)
            return;

        if (SelectedScannedCards.Count == 0)
        {
            Message = "Select one or more cards first.";
            return;
        }

        var text = clipboardText!.Trim();
        ManualSearchQuery = text;
        ManualSearch();

        if (PasteClassifier.ShouldAssignDirectly(kind, ManualSearchResults.Count))
        {
            SelectedManualSearchResult = ManualSearchResults[0];
            var name = SelectedManualSearchResult.Name;
            var count = SelectedScannedCards.Count;
            AssignMatch(); // assigns to all selected, records corrections, clears search
            Message = $"Assigned {name} to {count} card(s).";
            return;
        }

        // Name paste, or a code with no/many matches (e.g. outside the set filter):
        // let the user pick a printing from the prefilled search results.
        FocusManualSearchBox();
        Message = kind == PasteClassifier.PasteKind.Code
            ? $"No exact match for {text} — pick a printing."
            : $"Search results for \"{text}\" — pick a printing.";
    }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build OmniCard --nologo -clp:ErrorsOnly`
Expected: 0 errors. (`PasteClassifier` resolves from the same namespace `OmniCard.Views.Root`.)

- [ ] **Step 3: Add the Ctrl+V branch in the view**

In `OmniCard/Views/Root/ScannerTabView.xaml.cs`, in `ScannedCardsListView_PreviewKeyDown`, add a new branch after the existing Ctrl+C branch (after the block that ends at line ~178). The final `else if` becomes:

```csharp
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+V: assign a card to the selected scanned card(s) from the clipboard.
            string text;
            try
            {
                text = Clipboard.GetText();
            }
            catch (Exception)
            {
                ViewModel.Message = "Couldn't read clipboard.";
                e.Handled = true;
                return;
            }
            ViewModel.PasteAssign(text);
            e.Handled = true;
        }
```

- [ ] **Step 4: Build the full solution**

Run: `dotnet build --nologo -clp:ErrorsOnly`
Expected: 0 errors.

- [ ] **Step 5: Run the full test suite (no regressions)**

Run: `dotnet test OmniCard.Tests --nologo`
Expected: PASS — all tests green, including `PasteClassifierTests`.

- [ ] **Step 6: Verify the feature by running the app**

`RootViewModel` is not constructed in unit tests in this codebase (per `ClearMatchCommandTests`), so verify the wiring live. Use the `verify` skill (or run the WPF app manually) and confirm, with the scanned queue focused:

1. Select a matched card, press Ctrl+C, select another card, press Ctrl+V → the second card's match becomes the copied card; status shows "Assigned … to 1 card(s)."
2. Select 2+ cards, press Ctrl+V with a collector-number code on the clipboard (e.g. `OP15-041`) → all selected cards get that card.
3. Copy a name (Ctrl+Shift+C), select a card, press Ctrl+V → the manual search box is focused and prefilled with the name, showing printings; picking one and clicking Assign assigns it.
4. Press Ctrl+V with nothing selected → status "Select one or more cards first."; no assignment.

Record the observed results. If any step misbehaves, fix and re-verify before committing.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/ScannerTabView.xaml.cs
git commit -m "feat(scanner): Ctrl+V paste-to-assign for scanned cards"
```

---

## Self-Review

**1. Spec coverage:**
- §1 Trigger & scope (Ctrl+V in `PreviewKeyDown`, ≥1 selection, reads clipboard, calls `PasteAssign`) → Task 2 Steps 1, 3.
- §2 Discriminator (`SetCollectorNumberRegex` pattern) → Task 1 `Classify` (+ tests).
- §3 Code path (search → 1 result assign; else fall back to search) → Task 1 `ShouldAssignDirectly` (+ tests) and Task 2 `PasteAssign`.
- §4 Name path (prefill + `ManualSearch` + focus; user picks + Assign) → Task 2 `PasteAssign`; the pick+Assign step is the existing flow (verified in Task 2 Step 6.3).
- §5 Architecture (`PasteAssign` method, view stays thin, reuse only) → Tasks 1–2.
- §6 Error handling (no selection, empty clipboard, clipboard read throws) → Task 2 `PasteAssign` (selection/empty) and view try/catch (Step 3).
- §7 Testing → Task 1 unit tests cover the discriminator and assign-vs-search decision (the spec's intent). NOTE/deviation: the spec listed these as tests on `PasteAssign` directly, but `RootViewModel` is not unit-constructible in this codebase (16 deps incl. concrete VMs; `ClearMatchCommandTests` sets the precedent of extracting logic). The decision logic is therefore tested via `PasteClassifier`; the orchestration/wiring is verified by running the app (Task 2 Step 6). This keeps automated coverage on the risky logic without a disproportionate VM fixture.

**2. Placeholder scan:** No TBD/TODO. Every code step has complete code. Error handling is concrete (specific guards and a scoped try/catch), not "handle errors."

**3. Type consistency:** `PasteClassifier.PasteKind`, `Classify(string?)`, `ShouldAssignDirectly(PasteKind, int)` are defined in Task 1 and used with the same names/signatures in Task 2. `PasteAssign(string?)` matches the view call site `ViewModel.PasteAssign(text)`. Reused member names (`ManualSearchQuery`, `ManualSearchResults`, `SelectedManualSearchResult`, `AssignMatch`, `FocusManualSearchBox`, `Message`, `SelectedScannedCards`) match RootViewModel verbatim.
