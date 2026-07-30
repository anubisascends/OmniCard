# Design: Unified, multi-file import

**Date:** 2026-07-27
**Branch:** feat/lists-feature
**Status:** Approved (design)
**Builds on:** 2026-07-27-decklist-file-import-design.md (the single-file decklist import shipped earlier this branch)

## Summary

Rework the import UX based on testing feedback:

1. **One `Import…` command** (Ctrl+I) replaces the two separate menu items. It
   sniffs each chosen file and routes to the right dialog automatically.
2. **Multi-file decklist import** via a master-detail batch dialog: select many
   decklist files at once and assign each its own target (List or Location,
   existing or created inline), instead of importing one file at a time.
3. **Fix the refresh bug**: after an import creates a new Location (or List), the
   app's location/list views now update immediately.

## Motivation (testing feedback)

- Importing multiple decklist files one-at-a-time is tedious.
- Creating a new Location during decklist import doesn't refresh the app's
  location lists — the new location doesn't appear until something else reloads.
- Two separate import commands ("Import…" for CSV, "Import Decklist File…") is
  confusing; the app should detect the file type itself.

## Decisions (from brainstorming)

- Multi-file batch is **decklist-only**; CSV collections remain a single-file
  flow (a multi-select that mixes CSV + decklist processes the CSVs first, then
  the decklist batch).
- Batch UI is **master-detail**.
- **Force choose per file**: no silent default target; Import is disabled until
  every file has a target. Creating a new List/Location is inline and pre-fills
  the file's name for speed.
- **One `Import…`** command; the separate decklist menu item is removed.
- The single-file decklist dialog is **retired** — the batch dialog handles
  1…N files (a single file is a one-row batch).
- Each file imports to **its own** target; no cross-file "apply to all".

## Goals / Non-goals

**Goals**
- Single auto-detecting import command over a multi-file selection.
- Batch decklist import with per-file target assignment + per-file card review.
- Correct refresh of container/list views after import.

**Non-goals**
- Multi-file CSV batching (CSV stays single-file preview; multiple CSVs are just
  shown in sequence).
- Cross-file bulk target assignment ("apply to all").
- Changing the resolution ladder, foil handling, or condition defaults (all
  inherited unchanged from the single-file feature).

## Architecture

### 1. File classifier — `ImportFileClassifier`

New pure helper (no DB) classifying a file by peeking its first non-empty line.

```
enum ImportKind { Csv, Decklist, Unknown }
static ImportKind Classify(string firstNonEmptyLine)
static ImportKind ClassifyFile(string path)   // reads first non-empty line, calls Classify
```

Rules (first match wins):
- **Csv**: the line splits on `,` into a header set containing a known marker
  column — `GameCardId` (AppNative), `Printing` (TcgPlayer), `Edition`
  (Moxfield), or the Manabox trio (`Foil` + `Scryfall ID` + `Purchase price
  currency`). This mirrors `CsvExportImportService.DetectFormat`.
- **Decklist**: the line matches the decklist line regex
  `^(\d+)x?\s+(.+?)(?:\s+\(([A-Za-z0-9]+)\)\s+(\S+)(?:\s+.*)?)?$` (starts with a
  quantity). Comment (`//`) and section-header lines are skipped when finding the
  first content line.
- **Unknown**: neither.

To avoid duplicating the CSV marker-column set, expose it from
`CsvExportImportService` (e.g. a `static bool LooksLikeKnownCsvHeader(IEnumerable<string> headers)`
or reuse `DetectFormat` returning non-null) and have the classifier call it.

### 2. Unified command — `RootViewModel.Import()`

Replaces `ImportCollection()` and `ImportDecklistFile()`.

```
[RelayCommand] public void Import()
```

Flow:
1. `OpenFileDialog { Multiselect = true, Filter = "Import files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*", Title = "Import" }`.
2. Classify each selected path via `ImportFileClassifier.ClassifyFile`.
3. Partition into `csv`, `decklist`, `unknown`.
4. For each `csv` file (in order): `csvService.PreviewImport(path)` →
   `dialogService.ShowImportPreview(preview)`; accumulate imported counts.
5. If any `decklist` files: read each to text, call
   `dialogService.ShowBatchDecklistImport(files)` where `files` is a list of
   `(name, text)`; accumulate the batch summary. (No current-location argument —
   per "force choose", the batch never auto-selects a target.)
6. Compose a `Message` summarizing CSV + decklist results and, when `unknown`
   files were skipped, name them.
7. **Refresh** (see §4) once, after all dialogs close.
8. Wrap file I/O + dialogs in try/catch mirroring the current `ImportCollection`
   (log + MessageBox on error).

Per "force choose", no current-location default is passed to the batch — see §3.

### 3. Batch decklist dialog

**Per-file item — `DecklistFileImport`** (one left-pane row): carries the parsed
+ resolved state and the target selection for a single file.
- `string SourceName`
- `ObservableCollection<DecklistImportRow> Rows` (reused type)
- `int ResolvedCount`, `int UnresolvedCount`, `string SummaryLabel`
- Target state (same shape as the retired single-file VM): `bool TargetIsList`,
  `bool TargetIsLocation`, `bool TargetIsLocationEditable`, `CardList?
  SelectedList`, `StorageContainer? SelectedLocation`, `bool CreateNew`, `bool
  UseExistingTarget`, `string NewName`, `ContainerType NewLocationType`.
- `string DefaultNewName` — a cleaned form of the filename (drop extension;
  used to pre-fill `NewName` when the user picks create-new).
- `bool HasTarget` — true when a valid target is selected: `CreateNew` with a
  non-blank `NewName`, or an existing `SelectedList`/`SelectedLocation` per
  `TargetIsList`.

Note: unlike the single-file feature, items start with **no** target selected
(`HasTarget == false`). No auto-default to current location.

**`BatchDecklistImportViewModel`** — owns the collection and orchestration.
- `ObservableCollection<DecklistFileImport> Files`
- `DecklistFileImport? SelectedFile` (drives the right-pane detail grid)
- Shared, loaded once: `ObservableCollection<CardList> AvailableLists`
  (active game), `ObservableCollection<StorageContainer> AvailableLocations`
- `string HeaderLabel` (e.g. "3 files · 210 cards · 4 unresolved")
- `bool CanImport` = `Files.Count > 0 && Files.All(f => f.HasTarget)`
- `Load(IReadOnlyList<(string Name, string Text)> files)`: for each file, parse
  via `IDecklistService.ParseDecklistPrintings`, resolve each entry via the
  shared `DecklistImportService` (§5), build a `DecklistFileImport`; populate
  `AvailableLists`/`AvailableLocations` once; select the first file.
- `[RelayCommand] Import()`: for each file, commit its resolved rows to its
  target via `DecklistImportService`; aggregate a `BatchDecklistImportSummary`;
  `CloseDialog?.Invoke(true)`.
- `[RelayCommand] Cancel()`.
- `Result` (`BatchDecklistImportSummary`), `CloseDialog` (`Action<bool>?`).

**`BatchDecklistImportSummary`**: `record BatchDecklistImportSummary(int FileCount,
int TotalAdded, int TotalUnresolved, IReadOnlyList<string> PerFileTargets)` (or a
small per-file result list) — enough for the `Message` and for the refresh to
know whether any List and/or Location targets were used.

**`BatchDecklistImportView`** — master-detail Window (follows `CsvImportView`
conventions; dark-theme rule: explicit `Foreground` on all text controls; Import
button `IsEnabled="{Binding ViewModel.CanImport}"`). Left: `DataGrid`/`ItemsControl`
of `Files` with an inline target editor per row (radio List/Location + ComboBox +
create-new name/type); right: `DataGrid` of `SelectedFile.Rows`.

### 4. Refresh after import — `RootViewModel`

After all import dialogs close (§2 step 7), run the app's established cascade
(mirrors `ManageStorageLocations` / `DeleteLocationWithOptions`):

```
LoadContainers();                                   // both container dropdowns (cascades to Collection.LoadContainers)
if (Collection.ShowCardList) _ = Collection.SearchCollection();
else Collection.LoadOverview();                     // surface a new location tile
if (anyListTargetUsed) <refresh Lists view>;        // so a new/updated list shows
```

- `LoadContainers()` covers `RootViewModel.AvailableContainers` +
  `CollectionViewModel.AvailableContainers`.
- `Collection.LoadOverview()` is required because `SearchCollection()` is a no-op
  for the overview tiles.
- Lists-view refresh: reload the Lists sidebar for the active game (the plan
  will identify the exact hook — the `ListsViewModel` reload path). Gate it on
  whether any batch file targeted a List (from the summary). This also closes the
  deferred "Lists sidebar doesn't refresh after a List import" item from the
  prior feature.

This refresh runs for the CSV path too (CSV app-native import can create
containers), fixing the same latent gap in the old `ImportCollection`.

### 5. Shared decklist import logic — `DecklistImportService`

Extract the resolve + commit logic currently inside `DecklistImportViewModel`
into a reusable service so the batch VM (and its tests) don't duplicate it.

```
interface IDecklistImportService
{
    // Parse + resolve a file's text against the active game into preview rows.
    IReadOnlyList<DecklistImportRow> ResolveFile(string fileText);

    // Commit a file's resolved rows to a target; returns cards added (sum of qty).
    int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows);
    int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows);
}
```

- `ResolveFile` uses `cardService.ActiveGameService` + `DecklistPrintingResolver`
  (unchanged ladder).
- `CommitToList` → `IListService.AddPrinting(listId, row.Match!, isFoil:false,
  row.Quantity, ListItemSource.File)` per row.
- `CommitToLocation` → `ICardService.AddCardToCollection(row.Match!, game,
  "Near Mint", isFoil:false, purchasePrice:null, row.Quantity, container, null,
  null, null)` per row.
- Create-new (list/location) happens in the batch VM's `Import` loop (calls
  `IListService.CreateList` / `IStorageContainerService.Create`), then the
  matching `CommitTo*`.

Registered in DI as a singleton alongside the other services.

### 6. Retire / rewire

- Delete `OmniCard/Views/DecklistImport/DecklistImportView.xaml(.cs)` and
  `DecklistImportViewModel.cs`; move their reusable target-state logic into
  `DecklistFileImport` and their commit logic into `DecklistImportService`.
- Remove `IDialogService.ShowDecklistImport`; add
  `BatchDecklistImportSummary? ShowBatchDecklistImport(IReadOnlyList<(string Name,
  string Text)> files)`.
- `DecklistImportRow` is retained (reused by the batch).
- `RootView.xaml`: remove the "Import Decklist File…" menu item; the remaining
  item binds to `ImportCommand` (renamed from `ImportCollectionCommand`), keeps
  Ctrl+I and the "_Import..." header.
- DI: register `BatchDecklistImportView`/`BatchDecklistImportViewModel` and
  `IDecklistImportService`; drop the old decklist dialog registrations.
- The old `DecklistImportViewModelTests` are replaced by
  `BatchDecklistImportViewModelTests` + `DecklistImportServiceTests`.

## Error handling & edge cases

- No files chosen → command returns quietly.
- All files Unknown → message "No importable files (unrecognized format)"; no
  dialogs.
- A decklist file with zero parseable lines → its row shows 0/0; it still
  requires a target and imports with 0 cards added (not a blocking error).
- A file that can't be read → reported in the error message; other files still
  process.
- Mixed selection → CSVs previewed in order first, then the decklist batch.
- Unresolved lines within a file are reported and skipped (unchanged behavior).

## Testing

**Unit (xUnit, existing fake patterns):**
- `ImportFileClassifierTests`: CSV header lines (each marker column) → Csv;
  decklist lines (`1 Card`, `1x Card (SET) 4 *E*`) → Decklist; comment/header
  first lines skipped to find content; junk → Unknown.
- `DecklistImportServiceTests`: `ResolveFile` produces resolved/unresolved rows
  via the ladder; `CommitToList` calls `AddPrinting` with `ListItemSource.File`
  non-foil; `CommitToLocation` calls `AddCardToCollection` with "Near Mint",
  non-foil, null price, the given container. (Reuse `ImportFakes.cs`.)
- `BatchDecklistImportViewModelTests`: `Load` builds one item per file with
  correct counts and `DefaultNewName`; items start with `HasTarget == false`;
  `CanImport` false until all files have targets; `Import` commits each file to
  its own target (list vs location, existing vs create-new) and aggregates the
  summary; per-file create-new creates then populates.

**Build + manual (WPF, no unit tests):** the classifier-routing inside the
`Import` command, both dialog wirings, the refresh cascade, and the master-detail
XAML. Manual smoke: multi-select several decklist files → assign mixed targets
(new location, existing list, new list) → import → confirm cards land, new
locations/lists appear immediately, summary is correct; a CSV + decklist
multi-select processes CSV then batch.

## Files touched (anticipated)

Create:
- `OmniCard.Collection/ImportFileClassifier.cs`
- `OmniCard.Collection/DecklistImportService.cs` + `OmniCard.Shared/Interfaces/IDecklistImportService.cs`
- `OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs`
- `OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs`
- `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml(.cs)`
- `OmniCard.Shared/Models/BatchDecklistImportSummary.cs`
- Tests: `ImportFileClassifierTests`, `DecklistImportServiceTests`, `BatchDecklistImportViewModelTests`

Modify:
- `OmniCard.Collection/CsvExportImportService.cs` — expose header-marker check for the classifier.
- `OmniCard.Shared/Interfaces/IDialogService.cs` + `OmniCard/Services/DialogService.cs` — swap decklist dialog method for batch.
- `OmniCard/Views/Root/RootViewModel.cs` — unified `Import()` + refresh cascade (replace two commands).
- `OmniCard/Views/Root/RootView.xaml` — single Import menu item.
- `OmniCard/App.xaml.cs` — DI: add batch dialog + `IDecklistImportService`; drop old decklist dialog.

Delete:
- `OmniCard/Views/DecklistImport/DecklistImportView.xaml(.cs)`, `DecklistImportViewModel.cs`
- `OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs` (superseded)
