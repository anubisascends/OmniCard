# Ctrl+V Paste-to-Assign in the Scanned Queue

**Date:** 2026-07-14
**Status:** Approved

## Summary

Add a `Ctrl+V` shortcut to the scanned-card queue that assigns a card to the
selected scanned card(s) based on clipboard text. This complements the existing
copy shortcuts (Ctrl+C copies the code, Ctrl+Shift+C copies the name).

- If the clipboard text is a **code** (collector-number pattern, e.g. `OP15-041`),
  look it up and assign it directly to all selected cards.
- If the clipboard text is a **name**, prefill and focus the manual search field
  so the user can pick a printing; picking one and clicking the existing Assign
  button assigns it to all selected cards.

The feature composes existing machinery — it adds no new search or assignment
logic.

## Existing pieces reused

| Piece | Location | Role |
|---|---|---|
| `SetCollectorNumberRegex` | `RootViewModel` | Code-vs-name discriminator |
| `ManualSearch()` | `RootViewModel:1404` | Runs the search; already rewrites `SET-NUM` → `cn:` / `set:+cn:` and honors the active set filter |
| `AssignMatch()` (`AssignMatchCommand`) | `RootViewModel:1439` | Assigns `SelectedManualSearchResult` to all `SelectedScannedCards`, records corrections, clears search |
| `FocusManualSearch` (Action) | `RootViewModel:164` → `RootView.xaml.cs:35` | Focuses the manual search box |
| `ManualSearchQuery`, `ManualSearchResults`, `SelectedManualSearchResult` | `RootViewModel` | Search state bound to `CardSearchControl` |
| `SelectedScannedCards`, `HasSelection` | `RootViewModel:573-575` | Current multi-selection |
| `ScannedCardsListView_PreviewKeyDown` | `ScannerTabView.xaml.cs:157` | Existing Ctrl+C / Ctrl+Shift+C handler |

## Design

### 1. Trigger and scope

Add a `Ctrl+V` branch to `ScannedCardsListView_PreviewKeyDown` in
`ScannerTabView.xaml.cs`, alongside the existing copy branches. It fires only
when the queue `ListView` has focus (the handler is on that control). The view:

1. Reads `Clipboard.GetText()` inside a try/catch (clipboard access can throw).
2. Calls `ViewModel.PasteAssign(text)`.
3. Sets `e.Handled = true`.

`PasteAssign` is a public method on `RootViewModel`. Clipboard I/O stays in the
view so the method takes a plain string and is unit-testable.

### 2. Discriminator (in `PasteAssign`)

- Trim the input. If empty/whitespace → no-op.
- If `SelectedScannedCards.Count == 0` → set a status message
  ("Select one or more cards first.") and return.
- Test the trimmed text against `SetCollectorNumberRegex`. Match → **code path**;
  otherwise → **name path**.

### 3. Code path

```
ManualSearchQuery = code
ManualSearch()                       // populates ManualSearchResults
if ManualSearchResults.Count == 1:
    SelectedManualSearchResult = ManualSearchResults[0]
    AssignMatch()                    // assigns to all selected, clears search
    Message = $"Assigned {name} to {N} card(s)."
else:
    // zero or multiple (e.g. code outside the active set filter)
    FocusManualSearchBox()           // fall back: leave query, let user resolve
    Message = $"No exact match for {code} — refine in search."
```

`ManualSearch()` already applies the active set filter, so a code outside the
current filter yields zero results and falls back to the search box rather than
assigning the wrong card or erroring.

### 4. Name path

```
ManualSearchQuery = text
ManualSearch()                       // populates ManualSearchResults
FocusManualSearchBox()               // focus the search field
```

The user selects a printing from `ManualSearchResults` and clicks the existing
Assign button (`AssignMatchCommand`), which assigns the chosen printing to **all**
selected cards. No new code for this step — it is the current manual-assign flow.

### 5. Architecture

- **`RootViewModel.PasteAssign(string clipboardText)`** — new public method
  holding the discriminator and code/name routing. Depends only on already-injected
  members (`CardService`, `_logger`, `_diagnosticService` via `AssignMatch`) and the
  `FocusManualSearch` action.
- **`ScannerTabView.xaml.cs`** — new `Ctrl+V` branch in the existing
  `PreviewKeyDown` handler; reads the clipboard and calls `PasteAssign`.

No changes to `CardSearchControl`, `AssignMatch`, or `ManualSearch` internals.

### 6. Error handling

- No selection → status message, no-op.
- Empty/whitespace clipboard → no-op.
- Clipboard read throws (view side) → caught, logged, status message
  ("Couldn't read clipboard.").
- Code path with no/many results → falls back to the search box (section 3).

### 7. Testing

Unit tests on `RootViewModel.PasteAssign` (the VM is constructed with injectable
dependencies; `FocusManualSearch` is a settable `Action`, so focus can be asserted
via a flag):

1. **Code resolves to one card** → every `SelectedScannedCard.Match` equals that
   card; `ManualSearchQuery` cleared (via `AssignMatch`).
2. **Name text** → `ManualSearchQuery` set to the text, `ManualSearchResults`
   populated, focus action invoked, and **no** card assigned.
3. **Code with no match (or filtered out)** → falls back to name path: query set,
   focus requested, no assignment.
4. **No selection** → no-op (no query change, no assignment).

Tests seed a fake/real `CardService` with known cards (mirrors existing
`RootViewModel`/`CardService` test setup) and pre-populate `SelectedScannedCards`.

## Out of scope (YAGNI)

- Changing the copy shortcuts or what they copy.
- Auto-assigning on the name path (the user explicitly wants to pick the printing).
- Special handling for MTG set codes: a bare set code does not match the
  collector-number regex, so it routes to the name path (search prefilled) — no
  extra logic needed.
- Pasting multiple codes/names at once (one clipboard value per paste).
