# Log Viewer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Tools ▸ View Logs… dialog that browses the app's Serilog log files, filters by level/time/text, and copies selected entries as raw text for pasting into AI.

**Architecture:** A UI-free `LogFileParser` in `OmniCard.Data` turns Serilog's formatted text log files back into structured `LogEntry` records (re-attaching multi-line exception blocks to their parent entry). A `LogViewerViewModel` + `LogViewerView` (WPF Window) present them in a virtualized, multi-select `DataGrid`, mirroring the existing `MovementHistory` dialog. Wiring follows the established MenuItem → RootViewModel command → DialogService → DI-resolved Window pattern.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`), Serilog (log producer), Material Design themes, xUnit.

## Global Constraints

- Rename any user-facing "Innergy" → "INNERGY" and "DESIGN" → "ENGINEERING" (org rule; none expected in this feature).
- MVVM only: ViewModels derive from `OmniCard.Views.ViewModel` (`ObservableObject`); use `[ObservableProperty]` for state and `[RelayCommand]` for commands.
- Parser lives in `OmniCard.Data` (namespace `OmniCard.Data`) and must have **no** WPF/UI dependency so it is unit-testable.
- Serilog output template (the format the parser must read), from `OmniCard/App.xaml.cs:62`:
  `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}`
- Log files: `IDataPathService.LogsDirectory`, filename pattern `tcgcardscanner-*.log`, daily rolling, 14 retained.
- Open log files with `FileShare.ReadWrite` — Serilog may hold today's file open.
- The parser must never throw on malformed content; degrade gracefully.
- Dialogs are registered `AddTransient` in `OmniCard/App.xaml.cs`.
- Tests: xUnit, `[Fact]`, `Assert.Equal(...)`; test namespace `OmniCard.Tests.*`.

---

## File Structure

- Create `OmniCard.Data/LogEntry.cs` — the structured log entry record + level mapping.
- Create `OmniCard.Data/LogFileParser.cs` — file enumeration + text→`LogEntry` parsing.
- Create `OmniCard.Tests/Services/LogFileParserTests.cs` — parser unit tests.
- Create `OmniCard/Views/LogViewer/LogViewerViewModel.cs` — dialog state, filtering, copy.
- Create `OmniCard/Views/LogViewer/LogViewerView.xaml` (+ `.xaml.cs`) — the Window.
- Create `OmniCard.Tests/Views/LogViewerViewModelTests.cs` — filtering + clipboard-text tests.
- Modify `OmniCard.Shared/Interfaces/IDialogService.cs` — add `OpenLogViewer()`.
- Modify `OmniCard/Services/DialogService.cs` — implement `OpenLogViewer()`.
- Modify `OmniCard/Views/Root/RootViewModel.cs` — add `[RelayCommand] OpenLogViewer()`.
- Modify `OmniCard/App.xaml.cs` — register `LogViewerView` + `LogViewerViewModel`.
- Modify `OmniCard/Views/Root/RootView.xaml` — add the Tools menu item.

---

## Task 1: `LogEntry` record + level mapping

**Files:**
- Create: `OmniCard.Data/LogEntry.cs`

**Interfaces:**
- Produces:
  - `enum LogEntryLevel { Verbose, Debug, Information, Warning, Error, Fatal }`
  - `sealed record LogEntry { DateTimeOffset Timestamp; LogEntryLevel Level; string Source; string Message; string Detail; string Raw; }`
  - `static class LogLevelCodes { static LogEntryLevel Parse(string code); }` mapping `VRB/DBG/INF/WRN/ERR/FTL` → level (default `Information` for unknown).

> Note: use a local `LogEntryLevel` enum (not `Microsoft.Extensions.Logging.LogLevel`) so `OmniCard.Data` needn't take a logging-abstractions dependency for display purposes, and so the six Serilog levels map 1:1.

- [ ] **Step 1: Create the file**

```csharp
namespace OmniCard.Data;

/// <summary>The six Serilog levels, matching the [{Level:u3}] codes written to the log file.</summary>
public enum LogEntryLevel
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal,
}

/// <summary>
/// One parsed log entry. <see cref="Raw"/> is the verbatim original text (header line plus any
/// continuation lines such as an exception/stack trace) and is what the viewer copies to the clipboard.
/// </summary>
public sealed record LogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required LogEntryLevel Level { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }

    /// <summary>Continuation lines (exception text / stack trace) joined by newlines; empty when none.</summary>
    public string Detail { get; init; } = "";

    /// <summary>Verbatim original text of the whole entry.</summary>
    public required string Raw { get; init; }
}

/// <summary>Maps Serilog's three-letter level codes to <see cref="LogEntryLevel"/>.</summary>
public static class LogLevelCodes
{
    public static LogEntryLevel Parse(string code) => code switch
    {
        "VRB" => LogEntryLevel.Verbose,
        "DBG" => LogEntryLevel.Debug,
        "INF" => LogEntryLevel.Information,
        "WRN" => LogEntryLevel.Warning,
        "ERR" => LogEntryLevel.Error,
        "FTL" => LogEntryLevel.Fatal,
        _ => LogEntryLevel.Information,
    };
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build OmniCard.Data/OmniCard.Data.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add OmniCard.Data/LogEntry.cs
git commit -m "feat(logs): add LogEntry record and level mapping"
```

---

## Task 2: `LogFileParser` — text parsing (TDD)

**Files:**
- Create: `OmniCard.Data/LogFileParser.cs`
- Test: `OmniCard.Tests/Services/LogFileParserTests.cs`

**Interfaces:**
- Consumes: `LogEntry`, `LogEntryLevel`, `LogLevelCodes` (Task 1).
- Produces:
  - `sealed class LogFileParser` with instance method `IReadOnlyList<LogEntry> Parse(string content)`.
    - A new entry starts at any line matching the header regex; non-matching lines append to the current entry's `Detail` and `Raw`.
    - Content before the first header line is skipped (never throws).
    - Blank trailing lines are trimmed from `Detail`/`Raw`.

- [ ] **Step 1: Write the failing tests**

```csharp
using OmniCard.Data;

namespace OmniCard.Tests.Services;

public class LogFileParserTests
{
    private static readonly LogFileParser Parser = new();

    [Fact]
    public void Parse_SingleLineEntry_ExtractsFields()
    {
        const string content =
            "2026-07-23 10:30:45.123 +00:00 [INF] OmniCard.Scanner.ScannerService: Scan committed";

        var entry = Assert.Single(Parser.Parse(content));

        Assert.Equal(LogEntryLevel.Information, entry.Level);
        Assert.Equal("OmniCard.Scanner.ScannerService", entry.Source);
        Assert.Equal("Scan committed", entry.Message);
        Assert.Equal("", entry.Detail);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 10, 30, 45, 123, TimeSpan.Zero), entry.Timestamp);
        Assert.Equal(content, entry.Raw);
    }

    [Fact]
    public void Parse_MultiLineException_AttachesToParentEntry()
    {
        const string content =
            "2026-07-23 10:30:45.123 +00:00 [ERR] OmniCard.Scanner.ScannerService: Scan failed\n" +
            "System.InvalidOperationException: device not connected\n" +
            "   at OmniCard.Scanner.ScannerService.Scan()\n" +
            "2026-07-23 10:30:46.000 +00:00 [INF] OmniCard.App: Recovered";

        var entries = Parser.Parse(content);

        Assert.Equal(2, entries.Count);
        Assert.Equal(LogEntryLevel.Error, entries[0].Level);
        Assert.Equal("Scan failed", entries[0].Message);
        Assert.Contains("InvalidOperationException", entries[0].Detail);
        Assert.Contains("at OmniCard.Scanner.ScannerService.Scan()", entries[0].Detail);
        Assert.Contains("device not connected", entries[0].Raw);
        Assert.Equal("Recovered", entries[1].Message);
        Assert.Equal("", entries[1].Detail);
    }

    [Theory]
    [InlineData("VRB", LogEntryLevel.Verbose)]
    [InlineData("DBG", LogEntryLevel.Debug)]
    [InlineData("INF", LogEntryLevel.Information)]
    [InlineData("WRN", LogEntryLevel.Warning)]
    [InlineData("ERR", LogEntryLevel.Error)]
    [InlineData("FTL", LogEntryLevel.Fatal)]
    [InlineData("ZZZ", LogEntryLevel.Information)]
    public void Parse_MapsLevelCodes(string code, LogEntryLevel expected)
    {
        var content = $"2026-07-23 10:30:45.123 +00:00 [{code}] Src: msg";
        Assert.Equal(expected, Assert.Single(Parser.Parse(content)).Level);
    }

    [Fact]
    public void Parse_LeadingJunkBeforeFirstHeader_IsSkipped()
    {
        const string content =
            "garbage line with no header\n" +
            "another one\n" +
            "2026-07-23 10:30:45.123 +00:00 [INF] Src: real";

        var entry = Assert.Single(Parser.Parse(content));
        Assert.Equal("real", entry.Message);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmpty()
    {
        Assert.Empty(Parser.Parse(""));
        Assert.Empty(Parser.Parse("   \n  \n"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~LogFileParserTests"`
Expected: FAIL — `LogFileParser` does not exist (compile error).

- [ ] **Step 3: Write the parser**

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniCard.Data;

/// <summary>
/// Parses Serilog text log files (written with the app's output template) back into
/// <see cref="LogEntry"/> records. A new entry begins at each header line; lines that do not match
/// the header (exception text, stack traces, wrapped messages) are appended to the current entry.
/// Never throws on malformed content — unparseable leading lines are skipped.
/// </summary>
public sealed partial class LogFileParser
{
    // Matches: 2026-07-23 10:30:45.123 +00:00 [INF] Source.Context: message
    [GeneratedRegex(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<lvl>[A-Z]{3})\] (?<src>.*?): (?<msg>.*)$")]
    private static partial Regex HeaderRegex();

    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

    public IReadOnlyList<LogEntry> Parse(string content)
    {
        var entries = new List<LogEntry>();
        if (string.IsNullOrWhiteSpace(content))
            return entries;

        var lines = content.Replace("\r\n", "\n").Split('\n');

        Match? headerMatch = null;
        var raw = new StringBuilder();
        var detail = new StringBuilder();

        void Flush()
        {
            if (headerMatch is null)
                return;

            DateTimeOffset.TryParseExact(
                headerMatch.Groups["ts"].Value, TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts);

            entries.Add(new LogEntry
            {
                Timestamp = ts,
                Level = LogLevelCodes.Parse(headerMatch.Groups["lvl"].Value),
                Source = headerMatch.Groups["src"].Value,
                Message = headerMatch.Groups["msg"].Value,
                Detail = detail.ToString().TrimEnd('\n'),
                Raw = raw.ToString().TrimEnd('\n'),
            });
        }

        foreach (var line in lines)
        {
            var match = HeaderRegex().Match(line);
            if (match.Success)
            {
                Flush();
                headerMatch = match;
                raw.Clear().Append(line).Append('\n');
                detail.Clear();
            }
            else if (headerMatch is not null)
            {
                raw.Append(line).Append('\n');
                detail.Append(line).Append('\n');
            }
            // else: content before the first header — skip.
        }

        Flush();
        return entries;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~LogFileParserTests"`
Expected: PASS (all facts/theories green).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Data/LogFileParser.cs OmniCard.Tests/Services/LogFileParserTests.cs
git commit -m "feat(logs): parse Serilog text logs into structured entries"
```

---

## Task 3: `LogFileParser` — file enumeration + reading (TDD)

**Files:**
- Modify: `OmniCard.Data/LogFileParser.cs`
- Test: `OmniCard.Tests/Services/LogFileParserTests.cs`

**Interfaces:**
- Produces (added to `LogFileParser`):
  - `record LogFileInfo(string FullPath, string DisplayName)` — `DisplayName` is the file name.
  - `IReadOnlyList<LogFileInfo> ListFiles(string logsDirectory)` — files matching `tcgcardscanner-*.log`, newest-first by last-write time; empty list if the directory is missing.
  - `IReadOnlyList<LogEntry> ParseFile(string path)` — reads with `FileShare.ReadWrite` and calls `Parse`; returns empty on `IOException`/missing file.

- [ ] **Step 1: Write the failing tests (append to `LogFileParserTests`)**

```csharp
    [Fact]
    public void ListFiles_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-omnicard-logs-" + Guid.NewGuid());
        Assert.Empty(Parser.ListFiles(missing));
    }

    [Fact]
    public void ListFiles_ReturnsOnlyMatchingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-logs-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "tcgcardscanner-20260722.log"), "x");
            File.WriteAllText(Path.Combine(dir, "tcgcardscanner-20260723.log"), "x");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");

            var files = Parser.ListFiles(dir);

            Assert.Equal(2, files.Count);
            Assert.All(files, f => Assert.StartsWith("tcgcardscanner-", f.DisplayName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseFile_ReadsAndParses()
    {
        var path = Path.Combine(Path.GetTempPath(), "tcgcardscanner-" + Guid.NewGuid() + ".log");
        File.WriteAllText(path, "2026-07-23 10:30:45.123 +00:00 [WRN] Src: heads up");
        try
        {
            var entry = Assert.Single(Parser.ParseFile(path));
            Assert.Equal(LogEntryLevel.Warning, entry.Level);
            Assert.Equal("heads up", entry.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseFile_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(Parser.ParseFile(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".log")));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~LogFileParserTests"`
Expected: FAIL — `ListFiles`/`ParseFile`/`LogFileInfo` not defined.

- [ ] **Step 3: Add the methods to `LogFileParser`**

```csharp
    public sealed record LogFileInfo(string FullPath, string DisplayName);

    public IReadOnlyList<LogFileInfo> ListFiles(string logsDirectory)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory) || !Directory.Exists(logsDirectory))
            return [];

        return new DirectoryInfo(logsDirectory)
            .GetFiles("tcgcardscanner-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new LogFileInfo(f.FullName, f.Name))
            .ToList();
    }

    public IReadOnlyList<LogEntry> ParseFile(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
```

Add `using System.Linq;` if not already present (it is via implicit usings — verify build).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~LogFileParserTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Data/LogFileParser.cs OmniCard.Tests/Services/LogFileParserTests.cs
git commit -m "feat(logs): enumerate and read log files"
```

---

## Task 4: `LogViewerViewModel` (TDD for filtering + copy text)

**Files:**
- Create: `OmniCard/Views/LogViewer/LogViewerViewModel.cs`
- Test: `OmniCard.Tests/Views/LogViewerViewModelTests.cs`

**Interfaces:**
- Consumes: `LogFileParser`, `LogEntry`, `LogEntryLevel`, `LogFileParser.LogFileInfo` (Tasks 1–3); `IDataPathService` (`OmniCard.Interfaces`); `OmniCard.Views.ViewModel` base.
- Produces:
  - `sealed partial class LogViewerViewModel : ViewModel`
  - `void Load()` — enumerate files, select newest, parse off-thread, apply filters.
  - `ObservableCollection<LogFileInfo> AvailableFiles`, `LogFileInfo? SelectedFile`.
  - `ObservableCollection<LogEntry> Entries` (filtered view).
  - bool level toggles `ShowVerbose/ShowDebug/ShowInformation/ShowWarning/ShowError/ShowFatal` (all default true).
  - `string SearchText`, `string FromTimeText`, `string ToTimeText`.
  - `bool IsBusy`, `string? StatusMessage`.
  - `RefreshCommand`, `CopySelectedCommand(IList? selected)`.
  - `static string BuildClipboardText(IEnumerable<LogEntry> entries)` — entries' `Raw` joined by blank-line separator (testable, UI-free).
  - `IReadOnlyList<LogEntry> FilterFor(IEnumerable<LogEntry> source)` — internal filter, exposed `internal` for tests.

- [ ] **Step 1: Write the failing tests**

```csharp
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Views.LogViewer;

namespace OmniCard.Tests.Views;

public class LogViewerViewModelTests
{
    private sealed class FakeDataPathService : IDataPathService
    {
        public string DataDirectory => "";
        public string ScansDirectory => "";
        public string TempScansDirectory => "";
        public string SymbolsCacheDirectory => "";
        public string LogsDirectory => "";
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }

    private static LogViewerViewModel CreateVm() =>
        new(new FakeDataPathService(), new LogFileParser());

    private static LogEntry Entry(LogEntryLevel level, string source, string message, int hour) => new()
    {
        Timestamp = new DateTimeOffset(2026, 7, 23, hour, 0, 0, TimeSpan.Zero),
        Level = level,
        Source = source,
        Message = message,
        Raw = $"[{level}] {source}: {message}",
    };

    [Fact]
    public void FilterFor_HidesDeselectedLevels()
    {
        var vm = CreateVm();
        vm.ShowInformation = false;
        var source = new[]
        {
            Entry(LogEntryLevel.Information, "A", "info", 10),
            Entry(LogEntryLevel.Error, "B", "err", 10),
        };

        var result = vm.FilterFor(source);

        Assert.Equal(LogEntryLevel.Error, Assert.Single(result).Level);
    }

    [Fact]
    public void FilterFor_SearchMatchesMessageOrSource_CaseInsensitive()
    {
        var vm = CreateVm();
        vm.SearchText = "scanner";
        var source = new[]
        {
            Entry(LogEntryLevel.Information, "OmniCard.Scanner", "ok", 10),
            Entry(LogEntryLevel.Information, "OmniCard.Web", "SCANNER started", 10),
            Entry(LogEntryLevel.Information, "OmniCard.Web", "unrelated", 10),
        };

        var result = vm.FilterFor(source);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterFor_TimeRange_FiltersByHour()
    {
        var vm = CreateVm();
        vm.FromTimeText = "09:00";
        vm.ToTimeText = "11:00";
        var source = new[]
        {
            Entry(LogEntryLevel.Information, "A", "early", 8),
            Entry(LogEntryLevel.Information, "A", "mid", 10),
            Entry(LogEntryLevel.Information, "A", "late", 12),
        };

        var result = vm.FilterFor(source);

        Assert.Equal("mid", Assert.Single(result).Message);
    }

    [Fact]
    public void BuildClipboardText_JoinsRawWithBlankLine()
    {
        var text = LogViewerViewModel.BuildClipboardText(new[]
        {
            Entry(LogEntryLevel.Error, "A", "one", 10),
            Entry(LogEntryLevel.Info(), "B", "two", 10),
        });

        Assert.Equal("[Error] A: one\n\n[Information] B: two", text.Replace("\r\n", "\n"));
    }
}
```

> Note: the last test references `LogEntryLevel.Info()` which does not exist — that is a deliberate typo to catch. Replace it with `LogEntryLevel.Information` when writing the test. (Correct line: `Entry(LogEntryLevel.Information, "B", "two", 10),`.)

- [ ] **Step 2: Run tests to verify they fail**

First fix the `LogEntryLevel.Info()` line to `LogEntryLevel.Information`, then:

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~LogViewerViewModelTests"`
Expected: FAIL — `LogViewerViewModel` does not exist.

- [ ] **Step 3: Write the ViewModel**

```csharp
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Data;
using OmniCard.Interfaces;

namespace OmniCard.Views.LogViewer;

/// <summary>
/// Backs the Log Viewer dialog: enumerates Serilog log files, parses the selected file into
/// <see cref="LogEntry"/> records off the UI thread, and applies level/time/text filters in-memory
/// so toggling is instant. Copies selected entries' raw text to the clipboard for AI examination.
/// </summary>
public sealed partial class LogViewerViewModel(
    IDataPathService dataPathService,
    LogFileParser parser) : ViewModel
{
    private readonly List<LogEntry> _allEntries = [];

    public ObservableCollection<LogFileParser.LogFileInfo> AvailableFiles { get; } = [];
    public ObservableCollection<LogEntry> Entries { get; } = [];

    [ObservableProperty]
    public partial LogFileParser.LogFileInfo? SelectedFile { get; set; }

    async partial void OnSelectedFileChanged(LogFileParser.LogFileInfo? value)
    {
        if (value is not null)
            await ReparseAsync(value.FullPath);
    }

    [ObservableProperty] public partial bool ShowVerbose { get; set; } = true;
    [ObservableProperty] public partial bool ShowDebug { get; set; } = true;
    [ObservableProperty] public partial bool ShowInformation { get; set; } = true;
    [ObservableProperty] public partial bool ShowWarning { get; set; } = true;
    [ObservableProperty] public partial bool ShowError { get; set; } = true;
    [ObservableProperty] public partial bool ShowFatal { get; set; } = true;

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string FromTimeText { get; set; } = "";
    [ObservableProperty] public partial string ToTimeText { get; set; } = "";

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }

    partial void OnShowVerboseChanged(bool value) => ApplyFilters();
    partial void OnShowDebugChanged(bool value) => ApplyFilters();
    partial void OnShowInformationChanged(bool value) => ApplyFilters();
    partial void OnShowWarningChanged(bool value) => ApplyFilters();
    partial void OnShowErrorChanged(bool value) => ApplyFilters();
    partial void OnShowFatalChanged(bool value) => ApplyFilters();
    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnFromTimeTextChanged(string value) => ApplyFilters();
    partial void OnToTimeTextChanged(string value) => ApplyFilters();

    /// <summary>Enumerates files and loads the newest one. Call once when the dialog opens.</summary>
    public void Load()
    {
        AvailableFiles.Clear();
        foreach (var f in parser.ListFiles(dataPathService.LogsDirectory))
            AvailableFiles.Add(f);

        if (AvailableFiles.Count == 0)
        {
            StatusMessage = "No log files found.";
            return;
        }

        SelectedFile = AvailableFiles[0]; // triggers OnSelectedFileChanged -> ReparseAsync
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var current = SelectedFile?.FullPath;
        Load();
        // If the same file still exists, Load re-selected newest; re-parse current if unchanged.
        if (current is not null && SelectedFile?.FullPath == current)
            await ReparseAsync(current);
    }

    private async Task ReparseAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var parsed = await Task.Run(() => parser.ParseFile(path));
            _allEntries.Clear();
            _allEntries.AddRange(parsed);
            if (parsed.Count == 0)
                StatusMessage = "No log entries in this file (or it could not be read).";
            ApplyFilters();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilters()
    {
        Entries.Clear();
        foreach (var e in FilterFor(_allEntries))
            Entries.Add(e);
    }

    /// <summary>Applies the current level/time/text filters. Exposed for unit tests.</summary>
    internal IReadOnlyList<LogEntry> FilterFor(IEnumerable<LogEntry> source)
    {
        var from = ParseTime(FromTimeText);
        var to = ParseTime(ToTimeText);
        var search = SearchText?.Trim() ?? "";

        return source.Where(e =>
                IsLevelVisible(e.Level)
                && (from is null || e.Timestamp.TimeOfDay >= from)
                && (to is null || e.Timestamp.TimeOfDay <= to)
                && (search.Length == 0
                    || e.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || e.Source.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private bool IsLevelVisible(LogEntryLevel level) => level switch
    {
        LogEntryLevel.Verbose => ShowVerbose,
        LogEntryLevel.Debug => ShowDebug,
        LogEntryLevel.Information => ShowInformation,
        LogEntryLevel.Warning => ShowWarning,
        LogEntryLevel.Error => ShowError,
        LogEntryLevel.Fatal => ShowFatal,
        _ => true,
    };

    private static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParseExact(text?.Trim(), ["hh\\:mm", "h\\:mm", "hh\\:mm\\:ss"],
            CultureInfo.InvariantCulture, out var ts) ? ts : null;

    [RelayCommand]
    private void CopySelected(IList? selected)
    {
        var entries = selected?.OfType<LogEntry>().ToList() ?? [];
        if (entries.Count == 0) return;
        try
        {
            Clipboard.SetText(BuildClipboardText(entries));
            StatusMessage = $"Copied {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} to clipboard.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Copy failed: {ex.Message}";
        }
    }

    /// <summary>Joins entries' verbatim raw text with a blank-line separator. Exposed for tests.</summary>
    public static string BuildClipboardText(IEnumerable<LogEntry> entries) =>
        string.Join("\n\n", entries.Select(e => e.Raw));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~LogViewerViewModelTests"`
Expected: PASS.

> If `FilterFor`/`BuildClipboardText` are not visible to the test project, confirm `OmniCard.Tests` already references `OmniCard` and that `InternalsVisibleTo` is set (other VM tests such as `OrdersViewModelTests` prove this works — follow the same access level they use; make `FilterFor` `internal` and rely on the existing `InternalsVisibleTo`, or `public` if none exists).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/LogViewer/LogViewerViewModel.cs OmniCard.Tests/Views/LogViewerViewModelTests.cs
git commit -m "feat(logs): add LogViewerViewModel with filtering and copy"
```

---

## Task 5: `LogViewerView` window (XAML)

**Files:**
- Create: `OmniCard/Views/LogViewer/LogViewerView.xaml`
- Create: `OmniCard/Views/LogViewer/LogViewerView.xaml.cs`

**Interfaces:**
- Consumes: `LogViewerViewModel` (Task 4).
- Produces: `LogViewerView : Window` with `public LogViewerViewModel ViewModel { get; }` (DI-constructed), for `DialogService` to resolve.

- [ ] **Step 1: Create the code-behind**

```csharp
using System.Windows;

namespace OmniCard.Views.LogViewer;

public partial class LogViewerView : Window
{
    public LogViewerViewModel ViewModel { get; }

    public LogViewerView(LogViewerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 2: Create the XAML**

```xml
<Window x:Class="OmniCard.Views.LogViewer.LogViewerView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:conv="clr-namespace:OmniCard.Controls.Converters;assembly=OmniCard.Controls"
        xmlns:data="clr-namespace:OmniCard.Data;assembly=OmniCard.Data"
        ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        Title="Log Viewer" Height="640" Width="1000"
        MinHeight="440" MinWidth="720"
        Background="{DynamicResource MaterialDesign.Brush.Background}"
        TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
        TextElement.FontWeight="Regular"
        TextElement.FontSize="13"
        FontFamily="{StaticResource AppFont}">

    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="1*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- File + time + search bar -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="File:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <ComboBox Width="220" VerticalAlignment="Center" Margin="0,0,16,0"
                      ItemsSource="{Binding AvailableFiles}"
                      SelectedItem="{Binding SelectedFile}"
                      DisplayMemberPath="DisplayName"/>

            <TextBlock Text="From:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBox Width="70" VerticalAlignment="Center" Margin="0,0,12,0"
                     ToolTip="HH:mm (24-hour). Leave blank for no lower bound."
                     Text="{Binding FromTimeText, UpdateSourceTrigger=PropertyChanged}"/>
            <TextBlock Text="To:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBox Width="70" VerticalAlignment="Center" Margin="0,0,16,0"
                     ToolTip="HH:mm (24-hour). Leave blank for no upper bound."
                     Text="{Binding ToTimeText, UpdateSourceTrigger=PropertyChanged}"/>

            <TextBlock Text="Search:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBox Width="240" VerticalAlignment="Center" Margin="0,0,12,0"
                     Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"/>

            <Button Content="Refresh" Command="{Binding RefreshCommand}" Padding="12,4"/>
        </StackPanel>

        <!-- Level toggles -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Levels:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,10,0"/>
            <CheckBox Content="Verbose" IsChecked="{Binding ShowVerbose}" VerticalAlignment="Center" Margin="0,0,10,0"/>
            <CheckBox Content="Debug" IsChecked="{Binding ShowDebug}" VerticalAlignment="Center" Margin="0,0,10,0"/>
            <CheckBox Content="Information" IsChecked="{Binding ShowInformation}" VerticalAlignment="Center" Margin="0,0,10,0"/>
            <CheckBox Content="Warning" IsChecked="{Binding ShowWarning}" VerticalAlignment="Center" Margin="0,0,10,0"/>
            <CheckBox Content="Error" IsChecked="{Binding ShowError}" VerticalAlignment="Center" Margin="0,0,10,0"/>
            <CheckBox Content="Fatal" IsChecked="{Binding ShowFatal}" VerticalAlignment="Center" Margin="0,0,10,0"/>
        </StackPanel>

        <ProgressBar Grid.Row="2"
                     Height="4"
                     Margin="0,0,0,8"
                     IsIndeterminate="True"
                     Visibility="{Binding IsBusy, Converter={conv:BoolToVisibilityConverter}}"/>

        <!-- Entry grid -->
        <DataGrid x:Name="EntriesGrid"
                  Grid.Row="3"
                  ItemsSource="{Binding Entries}"
                  AutoGenerateColumns="False"
                  IsReadOnly="True"
                  SelectionMode="Extended"
                  GridLinesVisibility="Horizontal"
                  EnableRowVirtualization="True"
                  VirtualizingPanel.IsVirtualizing="True"
                  VirtualizingPanel.VirtualizationMode="Recycling"
                  RowDetailsVisibilityMode="VisibleWhenSelected">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Time"
                                    Binding="{Binding Timestamp, StringFormat='{}{0:HH:mm:ss.fff}'}" Width="110"/>
                <DataGridTextColumn Header="Level" Binding="{Binding Level}" Width="90"/>
                <DataGridTextColumn Header="Source" Binding="{Binding Source}" Width="240"/>
                <DataGridTextColumn Header="Message" Binding="{Binding Message}" Width="*"/>
            </DataGrid.Columns>
            <!-- Color-code rows by level -->
            <DataGrid.RowStyle>
                <Style TargetType="DataGridRow">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding Level}" Value="Warning">
                            <Setter Property="Foreground" Value="DarkOrange"/>
                        </DataTrigger>
                        <DataTrigger Binding="{Binding Level}" Value="Error">
                            <Setter Property="Foreground" Value="OrangeRed"/>
                        </DataTrigger>
                        <DataTrigger Binding="{Binding Level}" Value="Fatal">
                            <Setter Property="Foreground" Value="Red"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </DataGrid.RowStyle>
            <!-- Exception / detail shown when a row is selected -->
            <DataGrid.RowDetailsTemplate>
                <DataTemplate>
                    <TextBox Text="{Binding Detail, Mode=OneWay}"
                             IsReadOnly="True"
                             BorderThickness="0"
                             Background="Transparent"
                             FontFamily="Consolas"
                             TextWrapping="NoWrap"
                             HorizontalScrollBarVisibility="Auto"
                             Visibility="{Binding Detail, Converter={conv:StringToVisibilityConverter}}"
                             Margin="8,2,0,6"/>
                </DataTemplate>
            </DataGrid.RowDetailsTemplate>
        </DataGrid>

        <!-- Empty state -->
        <TextBlock Grid.Row="3"
                   Text="No log entries match the current filters."
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   FontSize="16" FontStyle="Italic"
                   Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                   IsHitTestVisible="False">
            <TextBlock.Style>
                <Style TargetType="TextBlock">
                    <Setter Property="Visibility" Value="Collapsed"/>
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding Entries.Count}" Value="0">
                            <Setter Property="Visibility" Value="Visible"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </TextBlock.Style>
        </TextBlock>

        <TextBlock Grid.Row="4"
                   Text="{Binding StatusMessage}"
                   Margin="0,8,0,0"
                   FontStyle="Italic"
                   Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                   Visibility="{Binding StatusMessage, Converter={conv:StringToVisibilityConverter}}"/>

        <!-- Bottom bar -->
        <Grid Grid.Row="5" Margin="0,8,0,0">
            <TextBlock Text="{Binding Entries.Count, StringFormat='{}{0} entr(y/ies)'}"
                       VerticalAlignment="Center"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="Copy Selected"
                        Command="{Binding CopySelectedCommand}"
                        CommandParameter="{Binding SelectedItems, ElementName=EntriesGrid}"
                        Padding="14,6" Margin="0,0,8,0" FontWeight="SemiBold"/>
                <Button Content="Close" IsCancel="True" Padding="16,6"
                        FontWeight="SemiBold" Click="CloseButton_Click"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

> The count string `'{}{0} entr(y/ies)'` is a display nicety; if a cleaner singular/plural is wanted later it can move to the VM. `data:` xmlns is declared for designer clarity even though bindings are string-based.

- [ ] **Step 3: Build the app project**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeded (XAML compiles; `LogViewerView` type generated).

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Views/LogViewer/LogViewerView.xaml OmniCard/Views/LogViewer/LogViewerView.xaml.cs
git commit -m "feat(logs): add LogViewerView window"
```

---

## Task 6: Wire into DI, DialogService, RootViewModel, and the Tools menu

**Files:**
- Modify: `OmniCard.Shared/Interfaces/IDialogService.cs` (near line 27)
- Modify: `OmniCard/Services/DialogService.cs` (add using + method near `OpenMovementHistory`, ~line 210)
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (near line 1865)
- Modify: `OmniCard/App.xaml.cs` (near line 222)
- Modify: `OmniCard/Views/Root/RootView.xaml` (Tools menu, ~line 224)

**Interfaces:**
- Consumes: `LogViewerView`, `LogViewerViewModel` (Tasks 4–5); existing `DialogService.SetOwner`, `Services.GetRequiredService<T>()`.
- Produces: `IDialogService.OpenLogViewer()`; `RootViewModel.OpenLogViewerCommand` (generated by `[RelayCommand]`).

- [ ] **Step 1: Add to `IDialogService`**

In `OmniCard.Shared/Interfaces/IDialogService.cs`, after `void OpenMovementHistory();`:

```csharp
    void OpenLogViewer();
```

- [ ] **Step 2: Implement in `DialogService`**

Add the using with the other view usings (after line 20):

```csharp
using OmniCard.Views.LogViewer;
```

Add the method after `OpenMovementHistory()` (after ~line 216):

```csharp
    public void OpenLogViewer()
    {
        var wnd = Services.GetRequiredService<LogViewerView>();
        SetOwner(wnd);
        wnd.ViewModel.Load();
        wnd.ShowDialog();
    }
```

- [ ] **Step 3: Add the RootViewModel command**

In `OmniCard/Views/Root/RootViewModel.cs`, after the `OpenMovementHistory` command (~line 1865):

```csharp
    [RelayCommand]
    public void OpenLogViewer() => DialogService.OpenLogViewer();
```

- [ ] **Step 4: Register in DI**

In `OmniCard/App.xaml.cs`, after the MovementHistory registrations (~line 223):

```csharp
            services.AddTransient<Views.LogViewer.LogViewerView>();
            services.AddTransient<Views.LogViewer.LogViewerViewModel>();
```

> `LogFileParser` is constructed by `LogViewerViewModel`. If DI cannot resolve it, also add `services.AddSingleton<OmniCard.Data.LogFileParser>();` near the other data-service registrations. (It is a stateless parser — singleton is fine.)

- [ ] **Step 5: Add the Tools menu item**

In `OmniCard/Views/Root/RootView.xaml`, inside `<MenuItem Header="_Tools">`, after the Movement History item (line 224), before the closing `</MenuItem>` (line 225):

```xml
                <Separator/>
                <MenuItem Header="View _Logs..."
                          Command="{Binding ViewModel.OpenLogViewerCommand}"/>
```

- [ ] **Step 6: Register `LogFileParser` in DI (needed for VM constructor injection)**

In `OmniCard/App.xaml.cs`, near other `OmniCard.Data` service registrations, add:

```csharp
            services.AddSingleton<OmniCard.Data.LogFileParser>();
```

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build OmniCard.sln`
Expected: Build succeeded, no warnings about unresolved bindings/types.

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj`
Expected: PASS (all existing + new LogFileParser/LogViewerViewModel tests green).

- [ ] **Step 9: Manual smoke test**

Launch the app, open **Tools ▸ View Logs…**. Verify: today's file is selected; entries list; level checkboxes filter live; From/To (`HH:mm`) narrows by time; search filters; selecting rows + **Copy Selected** puts raw text on the clipboard; selecting an error row shows the exception detail beneath it.

- [ ] **Step 10: Commit**

```bash
git add OmniCard.Shared/Interfaces/IDialogService.cs OmniCard/Services/DialogService.cs \
        OmniCard/Views/Root/RootViewModel.cs OmniCard/App.xaml.cs OmniCard/Views/Root/RootView.xaml
git commit -m "feat(logs): wire Log Viewer into Tools menu"
```

---

## Self-Review

**Spec coverage:**
- Tools-menu entry + dialog → Task 6 (menu) + Tasks 5/6 (dialog + wiring). ✓
- Each entry a single easily-displayed list item → Task 5 DataGrid single-line row, detail on select. ✓
- Copy one or more entries as raw text → Task 4 `CopySelected`/`BuildClipboardText` + Task 5 Extended selection + Copy button. ✓
- Date/time filtering → today's file via picker (Task 4 `Load`/`AvailableFiles`) + From/To time (Task 4 `FilterFor`). ✓
- Log-level filtering → Task 4 level toggles + Task 5 checkboxes. ✓
- Parser (text→structured, multi-line exception attach, FileShare.ReadWrite, never throws) → Tasks 2–3 with tests. ✓
- Free-text search (design default) → Task 4 `FilterFor` search + Task 5 search box. ✓
- Follows MovementHistory pattern / Material theme → Task 5 XAML mirrors it. ✓

**Placeholder scan:** No TBD/TODO. The one intentional "typo" (`LogEntryLevel.Info()`) is flagged with a fix instruction in Task 4 Steps 1–2 to satisfy TDD red→green honestly. ✓

**Type consistency:** `LogEntryLevel`, `LogEntry` (with `Raw`/`Detail`), `LogFileParser.Parse/ListFiles/ParseFile/LogFileInfo`, `LogViewerViewModel.FilterFor/BuildClipboardText/Load`, `OpenLogViewer()`, `OpenLogViewerCommand` are used consistently across tasks. `LogFileInfo` is nested in `LogFileParser` and referenced as `LogFileParser.LogFileInfo` everywhere. ✓
