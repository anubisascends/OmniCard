# Design: Import decklist file into a List or Location

**Date:** 2026-07-27
**Branch:** feat/lists-feature
**Status:** Approved (design)

## Summary

Add a new import path that reads a plain-text decklist file (Moxfield/Archidekt
style, one printing per line) and imports the cards into a user-chosen target:
either an existing/new **List** (`CardList`) or an existing/new **Location**
(`StorageContainer`). When the user is currently browsing a Location, that
Location is the default target. Cards are resolved against the **active game**'s
catalog using a graduated ladder based on which fields each line provides.

### Line format

```
[qty] [name] ([set code]) [collector no]  <optional trailing text, ignored>
```

Examples from the reference file (`first-flight-starter-commander-precon-decklist`):

```
1 Isperia, Supreme Judge (SCD) 4 *E*      -> qty 1, "Isperia, Supreme Judge", SCD, 4  (*E* ignored)
4 Island (SCD) 338                        -> qty 4, "Island", SCD, 338
1 Sol Ring (SCD) 276                       -> qty 1, "Sol Ring", SCD, 276
```

Set code and collector number are each **optional**. Text after the collector
number (e.g. `*E*`, `*F*` foil/etched markers) is ignored — every imported card
is treated as **non-foil**.

## Goals / Non-goals

**Goals**
- Import a decklist `.txt` file into a List or a Location.
- Let the user pick an existing target or create a new one inline.
- Default to the current Location when the user is browsing one.
- Resolve each line to an exact printing when possible; report the rest.

**Non-goals**
- Foil/etched handling (explicitly ignored per requirements).
- URL fetching (already covered by the existing `FetchDecklistAsync`).
- Multi-game inference — resolution runs against the active game only.
- Editing individual resolved printings inside the import dialog. The user
  adjusts cards afterward in the target List/Location ("change it later").

## Flow

```
Top-level "Import > Decklist file..." command (RootViewModel)
  -> OpenFileDialog (*.txt)
  -> IDecklistService.ParseDecklistPrintings(text)   -> List<DecklistEntry>
  -> resolve each entry against ActiveGameService     -> resolved / unresolved
  -> IDialogService.ShowDecklistImport(...)           -> preview + target picker
       (default target = current Location if browsing one, else Bulk)
  -> user picks target + clicks Import
  -> commit resolved entries to List or Location
  -> summary message ("Imported N cards to <target>. K unresolved.")
```

This mirrors the existing `RootViewModel.ImportCollection` -> `DialogService.ShowImportPreview`
-> `CsvImportView` pattern.

## Components

### 1. Parsing — `IDecklistService.ParseDecklistPrintings(string text)`

New method (the existing `ParseDecklistText` is unsuitable: it dedupes by name
only, collapsing distinct printings such as `Island 337/338/339/340`, and its
regex silently drops lines with trailing tokens like `*E*`).

- Regex tolerates trailing junk **without** weakening set/collector capture by
  nesting the trailer inside the optional group:

  ```
  ^(\d+)x?\s+(.+?)(?:\s+\(([A-Za-z0-9]+)\)\s+(\S+)(?:\s+.*)?)?$
  ```

  - Group 1: quantity
  - Group 2: name (lazy)
  - Group 3: set code (optional)
  - Group 4: collector number (optional)
  - Trailing `(?:\s+.*)?` inside the optional group swallows `*E*` etc.

- Skips blank lines, `//` comments, and known section headers (reuse the
  existing `SectionHeaders` set).
- Dedupe key = **(name, setCode, collectorNumber)** (case-insensitive name,
  upper-cased set). True duplicates sum quantities; distinct printings are kept
  separate.
- Returns `List<DecklistEntry>` (existing record:
  `DecklistEntry(int Quantity, string CardName, string? SetCode, string? CollectorNumber)`).

The same trailing-text regex fix is folded into the shared `DecklistLineRegex`
used by `ParseDecklistText` — strictly safer for the existing paste import
(it currently drops such lines).

### 2. Resolution ladder — resolve one `DecklistEntry` against `cardService.ActiveGameService`

Per line, choose the rung by which fields are present:

1. **set + collector** -> exact match on set code + collector number
   (`SearchCards("set:<code> cn:<num>")`, take the exact `SetCode`+`CollectorNumber`
   hit). Found -> use it. **Not found -> unresolved** (do not guess when the line
   was explicit).
2. **set, no collector** -> match by name within that set
   (`SearchCards('name:"<name>" set:<code>')`); if several, pick the cheapest.
3. **collector, no set** -> match by name with that collector number across sets
   (`SearchCards('name:"<name>" cn:<num>')`); if several, pick the cheapest.
4. **neither** -> cheapest printing of the name (existing `ResolveCheapest`
   behavior over `GetPrintings(name)`).

Any rung that finds nothing -> the line is reported **unresolved** and skipped
(never guessed). "Cheapest" reuses `ListService`'s existing logic: cheapest
priced non-foil printing via `GetCurrentPrices(...)`, else the first printing
flagged unpriced.

The resolved printing (the set + collector actually chosen) is shown in the
preview so the user can sanity-check it before committing.

Resolution helper lives in the new `DecklistImportViewModel` (or a small
resolver it owns), composing `ICardGameService.SearchCards` / `GetPrintings` /
`GetCurrentPrices`. No new method is added to `ICardGameService`.

### 3. Target model

The dialog binds to a target that is one of:

- **Existing List** of the active game (`IListService.GetLists(activeGame)`).
- **Existing Location** (`IStorageContainerService.GetAll()`).
- **New List** (name entry).
- **New Location** (name + `ContainerType`).

Default selection = current Location if the user is browsing one
(`CollectionViewModel.CurrentLocationId`), else the Bulk container
(`IStorageContainerService.GetBulk()`). The launch command passes that default
container id into the dialog.

### 4. UI — `DecklistImportView` + `DecklistImportViewModel`

New WPF `Window` + VM under `OmniCard/Views/DecklistImport/`.

- Registered in DI in `App.xaml.cs` (both the view and the VM), following the
  `CsvImportView`/`CsvImportViewModel` registration.
- Exposed via `IDialogService.ShowDecklistImport(...)` returning an import
  summary (added count + unresolved lines), mirroring `ShowImportPreview`.
- Displays: source file name; a header line "N lines - M resolved - K unresolved";
  a preview `DataGrid` (qty, name, set, collector, resolved status); the target
  picker (List / Location radio, each with a ComboBox + a "Create new" option
  with a name field and, for Location, a `ContainerType` selector); Import /
  Cancel buttons.
- Themed for dark mode per the project note: explicit `Foreground` on
  `TextBlock`s (implicit styles lose to MaterialDesign).

VM constructor deps: `IDecklistService`, `ICardService`, `IListService`,
`IStorageContainerService`, `ILogger`.

### 5. Commit

Per resolved entry:

- **List target:** `IListService.AddPrinting(listId, match, isFoil: false, qty,
  ListItemSource.File)` — uses the exact resolved `CardMatch` (not name
  resolution).
- **Location target:** `cardService.AddCardToCollection(match, activeGame,
  condition: "Near Mint", isFoil: false, purchasePrice: null, quantity: qty,
  container, page: null, slot: null, section: null)`.

Create-new targets are created first (`IListService.CreateList` /
`IStorageContainerService.Create`) then populated. Returns `(addedCount,
unresolvedLines)`.

### 6. New enum value

Add `File` to `ListItemSource` (`OmniCard.Shared/Models/CardList.cs`):
`enum ListItemSource { Manual, Url, Paste, File }`.

## Error handling & edge cases

- File unreadable / no parseable lines -> message box, no dialog opens.
- Unresolved lines are listed in the preview and repeated in the final summary;
  the import proceeds with the resolved lines. Unresolved lines are skipped,
  never guessed.
- Basic-land distinct art (e.g. Island 337-340) resolves correctly because the
  dedupe/resolve keys on collector number.
- Explicit set+collector that does not exist in the catalog (typo, or printing
  absent from the local catalog) -> reported unresolved (confirmed requirement).
- Empty/undownloaded catalog for the active game -> all lines unresolved,
  surfaced clearly in the header and summary.

## Testing

`DecklistServiceTests` (`OmniCard.Tests/Services/`):
- `ParseDecklistPrintings` on the reference file: correct distinct-printing count,
  `*E*` line parsed with set/collector intact, four Islands kept distinct,
  quantities summed for true duplicates, `//`/headers skipped.
- Shared regex fix: `ParseDecklistText` no longer drops a line with trailing
  `*E*`.

`DecklistImportViewModelTests` (`OmniCard.Tests/Services/`):
- Resolution ladder: set+cn exact hit; set-only picks cheapest in set; cn-only
  picks cheapest across sets; name-only picks cheapest; each "no match" rung ->
  unresolved.
- Explicit set+cn with no catalog match -> unresolved.
- Commit routing: List target calls `IListService.AddPrinting`; Location target
  calls `ICardService.AddCardToCollection` with `container` + "Near Mint".
- Create-new List and Create-new Location paths create then populate.
- Default target = current Location when provided, else Bulk.
- Resolved/unresolved counts reported correctly.

Follow the async-VM test determinism note (signal from mocks + `WaitAsync`;
avoid `Task.Yield`-dependent fire-and-forget assertions).

## Files touched (anticipated)

- `OmniCard.Shared/Interfaces/IDecklistService.cs` — add `ParseDecklistPrintings`.
- `OmniCard.Collection/DecklistService.cs` — implement it; fix shared regex.
- `OmniCard.Shared/Models/CardList.cs` — add `ListItemSource.File`.
- `OmniCard.Shared/Interfaces/IDialogService.cs` — add `ShowDecklistImport`.
- `OmniCard/Services/DialogService.cs` — implement `ShowDecklistImport`.
- `OmniCard/Views/DecklistImport/DecklistImportView.xaml(.cs)` — new.
- `OmniCard/Views/DecklistImport/DecklistImportViewModel.cs` — new.
- `OmniCard/Views/Root/RootViewModel.cs` — add `ImportDecklistFile` command.
- `OmniCard/App.xaml.cs` — DI registration.
- Menu/UI wiring for the top-level "Import > Decklist file..." entry.
- Tests as above.
