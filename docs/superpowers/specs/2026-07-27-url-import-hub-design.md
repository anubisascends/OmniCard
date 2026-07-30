# Design: URL import via a hub-first import session

**Date:** 2026-07-27
**Branch:** feat/lists-feature
**Status:** Approved (design)
**Builds on:** 2026-07-27-multi-file-import-design.md (unified multi-file Import + batch dialog)

## Summary

Add decklist-URL import by turning the existing batch decklist dialog into an
**import hub**: `Import…` (Ctrl+I) opens it directly, and inside it the user can
**Add files…** and/or **Add URL(s)…** into one session. URL decks are fetched via
the existing `FetchDecklistAsync` (Moxfield/Archidekt) and become batch rows with
the same per-row target picker as file decks. CSV collection files added via
**Add files…** are routed to the existing CSV preview dialog (imported there).

## Motivation

Testing feedback: "I still can't import from a URL." The fetch plumbing
(`IDecklistService.FetchDecklistAsync` → Moxfield/Archidekt) already exists but is
only wired into the old per-list URL box, which was retired. This wires URL
import into the current unified import, in one session with file imports
(approach "B", hub-first — user-selected).

## Decisions (from brainstorming)

- **Approach B** — URLs live inside the batch dialog (one session mixes files + URLs).
- **Hub-first** — `Import…` opens the batch hub directly; `[Add files…]` /
  `[Add URL(s)…]` are inside. (Costs the file-only case one extra click.)
- **Sites**: Moxfield + Archidekt only — both already supported; no new parsers.
- **Multi-URL**: paste one-or-many URLs, one per line.
- **CSV immediate-commit**: adding a CSV pops its existing preview dialog and
  imports on confirm (CSV keeps its own container/duplicate-handling model); it
  shows as a read-only "imported" line in the hub. Decklist rows still commit on
  the hub's Import button.
- No hard game gate on URL import — resolve against the active game; non-MTG
  active game simply yields unresolved rows.

## Goals / Non-goals

**Goals**
- Import decklists from Moxfield/Archidekt URLs, one or many, in the batch hub.
- Keep file import (decklist + CSV) working from the same hub.
- Correct post-import refresh (containers/lists) — unchanged cascade.

**Non-goals**
- New site parsers (MTGGoldfish/Deckstats/etc.).
- CSV-from-URL, or decklist-paste-text (URLs only for the new path).
- Changing the resolution ladder / foil / condition defaults (inherited).

## Architecture

### 1. `DecklistImportService.ResolveEntries`

URL fetch returns `List<DecklistEntry>` (not raw text), so add an entries-based
resolve alongside the text-based one; `ResolveFile` delegates through it.

```
// IDecklistImportService (OmniCard.Services)
IReadOnlyList<DecklistImportRow> ResolveEntries(IEnumerable<DecklistEntry> entries);
// existing:
IReadOnlyList<DecklistImportRow> ResolveFile(string fileText);   // = ResolveEntries(decklistService.ParseDecklistPrintings(fileText))
```

`ResolveEntries` is the current per-entry resolve loop (try/catch per entry,
`DecklistPrintingResolver.Resolve` against `ActiveGameService`, build
`DecklistImportRow`). `CommitToList`/`CommitToLocation` unchanged.

### 2. `DecklistFileImport` — explicit display/default name

URL rows are named by the deck's site name, not a filename. Change the per-file
item to take an explicit display name + default new-target name instead of
deriving both from a filename via `Path`:

```
public DecklistFileImport(string displayName, string defaultNewName,
    IReadOnlyList<DecklistImportRow> rows,
    IReadOnlyList<CardList> availableLists,
    IReadOnlyList<StorageContainer> availableLocations)
```

- File source: `displayName = fileName`, `defaultNewName = Path.GetFileNameWithoutExtension(fileName)`.
- URL source: `displayName = deckName` (optionally with a "(URL)" hint), `defaultNewName = deckName`.

`SourceName` stays as the display string; `DefaultNewName`/`NewName` seed from
`defaultNewName`. Everything else (target state, `HasTarget`, counts) unchanged.

### 3. Batch hub — `BatchDecklistImportViewModel`

The batch VM becomes the hub. New dependencies (it now orchestrates file + CSV +
URL entry): `IDialogService` (CSV preview), `ICsvExportImportService` (CSV
preview build), `IDecklistService` (`FetchDecklistAsync`) — plus the existing
`IDecklistImportService`, `ICardService`, `IListService`, `IStorageContainerService`.

New surface:
- `Load()` (no args now): load `AvailableLists`(active game) + `AvailableLocations`;
  start with empty `Files`. (The old `Load(files)` signature is replaced.)
- `[RelayCommand] AddFiles()`: open `OpenFileDialog { Multiselect = true, Filter = "Import files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*" }`; on OK call `AddPaths(dialog.FileNames)`.
- `void AddPaths(IReadOnlyList<string> paths)` — **testable seam**: per path (each in its own try/catch), classify via `ImportFileClassifier.ClassifyFile`:
  - Decklist → `importService.ResolveFile(File.ReadAllText(path))` → add a `DecklistFileImport` row (file-named).
  - Csv → `dialogService.ShowImportPreview(csvService.PreviewImport(path))`; if it returns a count, add to `CsvImportedCount`, append a read-only "imported" line, and mark `containersChanged`.
  - Unknown / read failure → append to a status/skip message.
- `string UrlText` (bound to the inline paste box); `bool IsBusy`; `string StatusMessage`.
- `[RelayCommand] async Task AddUrls()`: split `UrlText` on newlines (trim, drop blanks); for each URL `await decklistService.FetchDecklistAsync(url)`:
  - success → `importService.ResolveEntries(entries)` → add a URL-named row (`deckName`); clear that URL.
  - null (unreachable/unsupported) → collect into a failure list surfaced in `StatusMessage`.
  Set `IsBusy` around the batch; clear `UrlText` of the succeeded URLs.
- `CanImport` unchanged: `Files.Count > 0 && Files.All(f => f.HasTarget)`.
- `Import()` unchanged for decklist rows; the returned summary gains CSV info (below). CSVs are already committed (immediate).

`AddFiles`' `OpenFileDialog` is UI and not unit-tested; `AddPaths` and `AddUrls`
are the tested seams.

### 4. Summary + refresh

Extend the summary so the caller's refresh cascade accounts for CSV imports:

```
record BatchDecklistImportSummary(
    int FileCount,              // decklist rows committed
    int TotalAdded,             // decklist cards added
    int TotalUnresolved,
    bool AnyListTarget,
    bool AnyLocationTarget,
    int CsvImportedCount,       // NEW: cards imported via CSV within the session
    IReadOnlyList<BatchFileResult> Files);
```

`RootViewModel.Import()` collapses to: open the hub, then refresh from the
summary.

```
var summary = dialogService.ShowBatchDecklistImport();   // no args; hub opens empty
if (summary is null) return;                             // cancelled with nothing
var containersChanged = summary.AnyLocationTarget || summary.CsvImportedCount > 0;
if (containersChanged) LoadContainers();
if (Collection.ShowCardList) _ = Collection.SearchCollection(); else Collection.LoadOverview();
if (summary.AnyListTarget) Lists.Refresh();
Message = /* compose from summary: decklist cards, CSV cards, unresolved */;
```

The file-classification + CSV-preview loop currently in `RootViewModel.Import()`
**moves into the hub's `AddPaths`**. `RootViewModel.Import()` no longer opens a
file dialog itself.

Note: `ShowBatchDecklistImport` changes from taking a files list to taking no
args; `DialogService` calls `wnd.ViewModel.Load()` and returns `Result` even when
`DialogResult` is null-but-something-was-imported? No — CSVs commit immediately, so
if the user imports CSVs then Cancels the hub, those CSVs are already in. To keep
the refresh correct in that case, the hub sets `Result` (with `CsvImportedCount`)
before returning even on Cancel **if any CSV was imported**; `DialogService`
returns `Result` when it is non-null regardless of the dialog's true/false. (If
nothing happened, `Result` stays null and the dialog returns null.)

### 5. View — `BatchDecklistImportView.xaml`

Add a top toolbar above the master-detail area:
- `[Add files…]` button → `AddFilesCommand`.
- An inline "Add URLs" group: a multiline `TextBox` bound to `UrlText`
  (placeholder "Paste Moxfield/Archidekt deck URLs, one per line"), a **Fetch**
  button → `AddUrlsCommand` (disabled while `IsBusy`), and a `StatusMessage`
  `TextBlock`.
- A read-only area (or appended list items) for CSV "imported" lines.
Dark-theme rule (explicit `Foreground`) and the Import-button `IsEnabled` gate
carry over. The per-row target editor + right-pane card grid are unchanged.

## Error handling & edge cases

- Unreachable/unsupported URL → `FetchDecklistAsync` returns null → listed in
  `StatusMessage`, other URLs still fetched.
- One bad file → isolated per-path try/catch in `AddPaths`; others proceed.
- Non-MTG active game + a Moxfield/Archidekt URL → cards resolve as unresolved
  (reported per row); not blocked.
- Hub opened and nothing added → `CanImport` false; Cancel returns null (no
  refresh). If only CSV(s) were imported then Cancel → summary carries
  `CsvImportedCount` so the refresh still runs.
- Duplicate rows (same deck added twice) → allowed; each is its own row/target
  (consistent with the file batch).

## Testing

**Unit (xUnit, existing fakes):**
- `DecklistImportServiceTests`: `ResolveEntries` resolves entries into
  resolved/unresolved rows (mirrors the `ResolveFile` test but entry-based);
  `ResolveFile` still works (delegation).
- `BatchDecklistImportViewModelTests` (hub):
  - `AddUrls` on a URL that fetches → adds a row named by the deck, with resolved
    rows; the URL is consumed. (Fake `IDecklistService.FetchDecklistAsync`
    returns a canned `(deckName, entries)`.)
  - `AddUrls` on a URL that returns null → no row added, failure surfaced in
    `StatusMessage`; a mix adds the good ones only.
  - `AddPaths` with a decklist path → adds a row; with a CSV path → calls
    `dialogService.ShowImportPreview` and increments `CsvImportedCount` (fake
    dialog service returns a count); with an unknown path → skip message.
  - `CanImport` false until every added row has a target; `Import` summary
    includes `CsvImportedCount` and the correct `AnyList/AnyLocation` flags.
  - Requires: extend `ImportFakes.cs` so the decklist-service fake implements
    `FetchDecklistAsync` (canned result), and the fake dialog service can return
    a preview-import count.

**Build + manual (WPF):** `AddFiles` file picker, the URL text box + Fetch
wiring, CSV-preview-from-hub, the toolbar XAML, and `RootViewModel.Import()`
opening the hub. Manual smoke: `Import…` → Add URL(s) (paste a Moxfield + an
Archidekt URL) → each becomes a row named by the deck → assign targets → Import →
cards land, new locations/lists appear; Add files… in the same session (a
decklist + a CSV) → decklist row added, CSV preview pops and imports; a bad URL is
reported and skipped.

## Files touched (anticipated)

Modify:
- `OmniCard/Services/IDecklistImportService.cs` + `DecklistImportService.cs` — add `ResolveEntries`.
- `OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs` — explicit display/default-name ctor.
- `OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs` — hub (AddFiles/AddPaths, UrlText/AddUrls, CSV routing, new deps, extended summary).
- `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml` — toolbar (Add files, Add URLs box + Fetch, CSV lines).
- `OmniCard.Shared/Models/BatchDecklistImportSummary.cs` — add `CsvImportedCount`.
- `OmniCard.Shared/Interfaces/IDialogService.cs` + `OmniCard/Services/DialogService.cs` — `ShowBatchDecklistImport()` (no args).
- `OmniCard/Views/Root/RootViewModel.cs` — `Import()` opens the hub + refresh-from-summary (file/CSV classification moves into the hub).
- `OmniCard.Tests/Fakes/ImportFakes.cs` — fetch-capable decklist fake + preview-count dialog fake.
- Tests: `DecklistImportServiceTests`, `BatchDecklistImportViewModelTests`.
