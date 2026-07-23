# Log Viewer Dialog — Design

**Date:** 2026-07-23
**Status:** Approved for implementation

## Purpose

Give the user an in-app way to browse the application's diagnostic logs, filter
them by time and level, and copy one or more entries to the clipboard as raw
text for pasting into an AI for examination. Removes the need to hunt through
raw `.log` files on disk.

## Context

Logs are written by Serilog (configured in `OmniCard/App.xaml.cs`) as
daily-rolling text files to `IDataPathService.LogsDirectory`
(`<data>/logs/tcgcardscanner-YYYYMMDD.log`), 14 files retained. The output
template is:

```
{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}
```

Example single entry (an exception spans multiple physical lines):

```
2026-07-23 10:30:45.123 +00:00 [ERR] OmniCard.Scanner.ScannerService: Scan failed
System.InvalidOperationException: The device is not connected
   at OmniCard.Scanner.ScannerService.Scan()
   at ...
```

The dialog pattern in this codebase is well established: a `MenuItem` binds to a
`RootViewModel` command, which calls `DialogService`, which resolves a `Window`
+ `ViewModel` pair from DI and calls `ShowDialog()`. `MovementHistory`
(`OmniCard/Views/MovementHistory/`) is the closest template — a read-only,
filterable `DataGrid` dialog — and this feature deliberately mirrors it.

## Decisions (from brainstorming)

- **Log scope:** Load today's log file by default, with a file picker
  (dropdown) to switch to any retained daily file. One file at a time — keeps
  load fast and memory light.
- **Copy format:** Raw log text — the exact original lines from the file,
  including full exception blocks. Most faithful; pastes cleanly into any AI.
- **Multi-select:** `DataGrid` `SelectionMode="Extended"` so the user can select
  one or many entries to copy.
- **Free-text search:** Included (search over message + source). Cheap and pairs
  naturally with the requested filters.

## Architecture

### 1. `LogFileParser` (in `OmniCard.Data`)

A UI-free, unit-testable parser. Responsibilities:

- **Enumerate files:** List files matching `tcgcardscanner-*.log` in a given
  logs directory, returning newest-first (with a display label, e.g. the date).
- **Parse a file into entries:** Read the file and split it into `LogEntry`
  records.
  - A **new entry** begins at any line matching the Serilog header regex:
    `^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<lvl>[A-Z]{3})\] (?<src>[^:]*): (?<msg>.*)$`
  - Any subsequent line that does **not** match the header (exception text,
    stack traces, wrapped messages) is appended to the **current** entry's
    detail and to its raw text. This keeps exceptions attached to their parent
    entry.
  - Lines appearing before the first header (rare/corrupt) are collected into a
    synthetic leading entry or ignored — chosen: attach to nothing / skip, but
    never throw.
  - Level codes map: `VRB→Verbose`, `DBG→Debug`, `INF→Information`,
    `WRN→Warning`, `ERR→Error`, `FTL→Fatal`. Unknown code → treat raw string,
    default level `Information`.

### 2. `LogEntry` record

```csharp
public sealed record LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }        // Microsoft.Extensions.Logging.LogLevel
    public string Source { get; init; }          // SourceContext
    public string Message { get; init; }         // single-line message (first line)
    public string Detail { get; init; }          // appended continuation lines (exception/stack), may be empty
    public string Raw { get; init; }             // verbatim original text of the whole entry — used by Copy
}
```

### 3. `LogViewerViewModel` (in `OmniCard/Views/LogViewer/`)

Mirrors `MovementHistoryViewModel` conventions (CommunityToolkit.Mvvm,
`[ObservableProperty]`, base `ViewModel` class, `ILogger` injected).

- Depends on `IDataPathService` (for `LogsDirectory`) and `LogFileParser`.
- On `Load()`: enumerate available files, select today's (or newest), parse it
  off the UI thread via `Task.Run`, store the full parsed list, then apply
  filters to populate the observable collection.
- State:
  - `AvailableFiles` (list for the picker) + `SelectedFile` (re-parses on change).
  - All parsed entries held in a private backing list.
  - `Entries` (`ObservableCollection<LogEntry>`) — the filtered view shown.
  - Level toggles: a bool per level (Verbose/Debug/Information/Warning/Error/Fatal),
    all on by default.
  - `FromTime` / `ToTime` (nullable time-of-day filters for the selected day).
  - `SearchText` (free text over message + source, case-insensitive).
  - `IsBusy`, `StatusMessage` (same idioms as Movement History).
- Filtering is **in-memory** over the parsed list, so toggling levels / typing
  search / adjusting time is instant (no re-parse). Changing the selected file
  re-parses.
- Commands: `RefreshCommand` (re-enumerate + re-parse current file),
  `CopySelectedCommand` (join selected entries' `Raw` with newlines → clipboard).

### 4. `LogViewerView` (Window)

XAML mirroring `MovementHistoryView` (Material Design theme, sizing, status
line, close button). Layout:

- **Filter bar (row 0):** file picker dropdown · level filter (checkboxes or a
  multi-check combo) · From/To time inputs · search box (Enter triggers
  refresh of filter) · Refresh button.
- **Busy bar (row 1):** indeterminate `ProgressBar` bound to `IsBusy`.
- **Grid (row 2):** `DataGrid`, `IsReadOnly=True`, `SelectionMode="Extended"`,
  virtualized. Columns: Time · Level (color-coded via a converter/style) ·
  Source · Message. Exception/detail surfaced via row-detail template or
  tooltip so the main row stays a clean single line.
- **Empty state (row 2 overlay):** "No log entries match the current filters."
- **Status line (row 3):** italic error text bound to `StatusMessage`.
- **Bottom bar (row 4):** entry count · **Copy Selected** button · **Close**.

Code-behind is minimal (constructor wiring + `CloseButton_Click`), like
`MovementHistoryView.xaml.cs`. Selected-items binding: `DataGrid` selection is
not bindable by default; pass selected items to the command via
`CommandParameter="{Binding SelectedItems, ElementName=...}"` or a tiny
code-behind handler that forwards selection to the VM. Chosen:
`CommandParameter` on the Copy button bound to the DataGrid's `SelectedItems`.

### 5. Wiring

- `IDialogService.OpenLogViewer()` + implementation in `DialogService` (resolve
  `LogViewerView`, `SetOwner`, `ViewModel.Load()`, `ShowDialog()`).
- `RootViewModel.OpenLogViewer()` → `DialogService.OpenLogViewer()`, exposed as a
  relay command.
- `App.xaml.cs`: register `LogViewerViewModel` and `LogViewerView` in DI.
- `RootView.xaml`: new `MenuItem Header="View _Logs..."` under `_Tools`, bound to
  `ViewModel.OpenLogViewerCommand`.

## Data flow

```
Tools ▸ View Logs…  →  RootViewModel.OpenLogViewer()  →  DialogService.OpenLogViewer()
   → resolve LogViewerView + VM → VM.Load()
       → LogFileParser.ListFiles(LogsDirectory)      (populate picker)
       → LogFileParser.Parse(selectedFile)  [Task.Run] → List<LogEntry>
       → ApplyFilters() → Entries (shown in DataGrid)

User toggles level / edits time / types search → ApplyFilters() (in-memory, instant)
User changes SelectedFile → re-Parse → ApplyFilters()
User selects rows → Copy Selected → clipboard (Raw joined by newline)
```

## Error handling

- Missing logs directory / no files: empty picker, empty grid, informational
  status message ("No log files found."). No exception.
- File locked / unreadable (Serilog may hold today's file): open with
  `FileShare.ReadWrite` to co-exist with the active writer; on failure set
  `StatusMessage` and leave the grid empty.
- Malformed lines / junk before first header: parser never throws; unparseable
  leading content is skipped.
- Copy with no selection: no-op (command disabled or guarded).

## Testing

`LogFileParser` unit tests in `OmniCard.Tests` (`Services/` or `Tools/`):

- Single-line entries parse into correct Timestamp/Level/Source/Message.
- Multi-line exception block is attached to its parent entry (Detail + Raw
  include the stack trace; no orphan entry created).
- All level codes map correctly (VRB/DBG/INF/WRN/ERR/FTL); unknown code handled.
- Leading junk lines before the first header do not throw and are skipped.
- Empty file yields zero entries.
- `Raw` round-trips the verbatim original text of an entry (what Copy relies on).

Filtering logic (level/time/search) is simple enough to cover via a focused VM
test if it can be exercised without the Window; otherwise validated manually.

## Out of scope (YAGNI)

- Live tail / auto-refresh.
- Merging multiple days into one view.
- Editing, deleting, or exporting logs from this dialog (Export Diagnostics
  already exists in File menu).
- Full-text indexing / regex search (simple case-insensitive contains only).
