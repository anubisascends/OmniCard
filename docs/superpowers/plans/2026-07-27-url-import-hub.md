# URL Import Hub Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the batch decklist dialog into an import hub: `Import…` opens it directly, and inside it the user can Add files… and/or Add URL(s)… (Moxfield/Archidekt) into one session, each deck a row with its own target.

**Architecture:** Add `ResolveEntries` to `DecklistImportService` (URL fetch returns entries, not text). The batch VM becomes the hub: `AddFiles`/`AddPaths` (file classify + CSV routing), `AddUrls` (fetch + resolve). File-picking and CSV preview are host callbacks (`PickFiles`, `ImportCsvFile`) wired by `DialogService`, so the VM stays testable. `RootViewModel.Import()` collapses to open-hub + refresh-from-summary.

**Tech Stack:** C# / .NET, WPF + MaterialDesignInXAML, CommunityToolkit.Mvvm source generators, EF Core, xUnit with hand-written fakes.

## Global Constraints

- URL fetch uses the existing `IDecklistService.FetchDecklistAsync` (Moxfield/Archidekt only); no new site parsers.
- Imported cards non-foil; Location commit "Near Mint"/null price; resolution ladder unchanged (all inherited via `DecklistImportService`).
- **CSV immediate-commit:** a CSV added via Add files… imports through its existing preview dialog right then; it is NOT a targetable row. Its count is tracked so the caller still refreshes.
- Resolution runs against the active game (Moxfield/Archidekt decks are MTG; a non-MTG active game just yields unresolved rows — no hard gate).
- Force-choose per row still applies; Import disabled until every row has a target.
- WPF dark-theme rule: explicit `Foreground` on every text control; Import button `IsEnabled="{Binding ViewModel.CanImport}"`.
- MVVM conventions: `[ObservableProperty] public partial`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`; hand-written xUnit fakes.
- Keep every task's build green (Task 2 is the coordinated pivot that swaps the entry flow).

---

## File Structure

**Modify:**
- `OmniCard/Services/IDecklistImportService.cs` + `OmniCard/Services/DecklistImportService.cs` — add `ResolveEntries`.
- `OmniCard.Shared/Models/BatchDecklistImportSummary.cs` — add `CsvImportedCount`.
- `OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs` — explicit `(displayName, defaultNewName, …)` ctor.
- `OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs` — hub (Load no-args, AddFiles/AddPaths, UrlText/AddUrls, callbacks, CSV tracking, extended summary).
- `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml` — toolbar (Add files, URL box + Fetch, status).
- `OmniCard.Shared/Interfaces/IDialogService.cs` + `OmniCard/Services/DialogService.cs` — `ShowBatchDecklistImport()` (no args) + callback wiring.
- `OmniCard/Views/Root/RootViewModel.cs` — `Import()` opens hub + refresh-from-summary.
- `OmniCard.Tests/Fakes/ImportFakes.cs` — `ResolveEntries` on the import-service fake; fetch-capable decklist fake.
- Tests: `DecklistImportServiceTests`, `BatchDecklistImportViewModelTests` (rewritten to drive AddPaths/AddUrls).

---

### Task 1: `ResolveEntries` on the import service

**Files:**
- Modify: `OmniCard/Services/IDecklistImportService.cs`, `OmniCard/Services/DecklistImportService.cs`
- Modify: `OmniCard.Tests/Fakes/ImportFakes.cs` (add `ResolveEntries` to `FakeDecklistImportService`)
- Test: `OmniCard.Tests/Services/DecklistImportServiceTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<DecklistImportRow> IDecklistImportService.ResolveEntries(IEnumerable<DecklistEntry> entries)`; `ResolveFile` now delegates to it.

- [ ] **Step 1: Write the failing test**

Append to `OmniCard.Tests/Services/DecklistImportServiceTests.cs` (reuses the existing `Build()`/`M()` helpers in that file):

```csharp
    [Fact]
    public void ResolveEntries_ResolvesDirectly_WithoutParsing()
    {
        var (svc, gs, _, _, _) = Build();
        gs.OnSearchCards = (q, _) => q.Contains("337") ? [M("a")] : [];
        var entries = new[]
        {
            new DecklistEntry(4, "Island", "SCD", "337"),
            new DecklistEntry(1, "Nonesuch", "SCD", "999"),
        };

        var rows = svc.ResolveEntries(entries);

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsResolved);
        Assert.Equal(4, rows[0].Quantity);
        Assert.False(rows[1].IsResolved);
    }

    [Fact]
    public void ResolveFile_DelegatesToResolveEntries_ViaParser()
    {
        var (svc, gs, _, _, decks) = Build();
        decks.Printings = [new DecklistEntry(2, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a")];

        var rows = svc.ResolveFile("ignored");

        var row = Assert.Single(rows);
        Assert.True(row.IsResolved);
        Assert.Equal(2, row.Quantity);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistImportServiceTests.ResolveEntries" -v minimal`
Expected: FAIL to compile — `ResolveEntries` not defined.

- [ ] **Step 3: Add `ResolveEntries` to the interface**

`OmniCard/Services/IDecklistImportService.cs` — add (with `using OmniCard.Models;` already present):

```csharp
    /// <summary>Resolve already-parsed decklist entries (e.g. from a URL fetch) into preview rows.</summary>
    IReadOnlyList<DecklistImportRow> ResolveEntries(IEnumerable<DecklistEntry> entries);
```

- [ ] **Step 4: Implement + delegate**

In `OmniCard/Services/DecklistImportService.cs`, replace the `ResolveFile` method with `ResolveEntries` + a delegating `ResolveFile`:

```csharp
    public IReadOnlyList<DecklistImportRow> ResolveFile(string fileText)
        => ResolveEntries(decklistService.ParseDecklistPrintings(fileText));

    public IReadOnlyList<DecklistImportRow> ResolveEntries(IEnumerable<DecklistEntry> entries)
    {
        var gs = cardService.ActiveGameService;
        var rows = new List<DecklistImportRow>();
        foreach (var e in entries)
        {
            CardMatch? match;
            try
            {
                match = DecklistPrintingResolver.Resolve(gs, e);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve decklist entry {Name}", e.CardName);
                match = null;
            }
            rows.Add(new DecklistImportRow
            {
                Quantity = e.Quantity,
                Name = e.CardName,
                SetCode = e.SetCode,
                CollectorNumber = e.CollectorNumber,
                Match = match,
            });
        }
        return rows;
    }
```

- [ ] **Step 5: Add `ResolveEntries` to the fake**

In `OmniCard.Tests/Fakes/ImportFakes.cs`, in `FakeDecklistImportService`, add (next to `OnResolve`):

```csharp
    public Func<IEnumerable<DecklistEntry>, List<DecklistImportRow>> OnResolveEntries = _ => [];
    public IReadOnlyList<DecklistImportRow> ResolveEntries(IEnumerable<DecklistEntry> entries) => OnResolveEntries(entries);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistImportServiceTests" -v minimal`
Expected: PASS (existing 3 + 2 new). Full suite to confirm no regression: `dotnet test OmniCard.Tests -v minimal`.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Services/IDecklistImportService.cs OmniCard/Services/DecklistImportService.cs OmniCard.Tests/Fakes/ImportFakes.cs OmniCard.Tests/Services/DecklistImportServiceTests.cs
git commit -m "feat(import): DecklistImportService.ResolveEntries (for URL-fetched decks)"
```

---

### Task 2: Batch dialog becomes the import hub (VM + entry rewire)

This is the coordinated pivot: the VM, summary, `DecklistFileImport` ctor, `DialogService`, and `RootViewModel.Import()` change together so the build + tests stay green. TDD drives the VM.

**Files:**
- Modify: `OmniCard.Shared/Models/BatchDecklistImportSummary.cs`
- Modify: `OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs`
- Modify: `OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs`
- Modify: `OmniCard.Shared/Interfaces/IDialogService.cs`, `OmniCard/Services/DialogService.cs`
- Modify: `OmniCard/Views/Root/RootViewModel.cs`
- Modify: `OmniCard.Tests/Fakes/ImportFakes.cs` (fetch-capable decklist fake)
- Test: `OmniCard.Tests/ViewModels/BatchDecklistImportViewModelTests.cs` (rewritten)

**Interfaces:**
- Consumes: `IDecklistImportService` (`ResolveFile`/`ResolveEntries`/`CommitTo*`), `IDecklistService.FetchDecklistAsync`, `ImportFileClassifier`, `ICardService`/`IListService`/`IStorageContainerService`.
- Produces: `BatchDecklistImportViewModel` hub — `Load()`, `AddFilesCommand`, `void AddPaths(IReadOnlyList<string>)`, `UrlText`, `AddUrlsCommand` (async), `StatusMessage`, `IsBusy`, `CsvImportedCount`, `Func<IReadOnlyList<string>?>? PickFiles`, `Func<string,int?>? ImportCsvFile`, `Result`, `CloseDialog`, `ImportCommand`, `CancelCommand`; `BatchDecklistImportSummary` with `CsvImportedCount`; `DecklistFileImport(string displayName, string defaultNewName, …)`; `IDialogService.ShowBatchDecklistImport()`.

- [ ] **Step 1: Rewrite the failing tests**

Replace the body of `OmniCard.Tests/ViewModels/BatchDecklistImportViewModelTests.cs` with (drives the hub via `AddPaths`/`AddUrls`; uses temp files for the file path since `ClassifyFile` reads content):

```csharp
using System.IO;
using OmniCard.Models;
using OmniCard.Tests.Fakes;
using OmniCard.Views.BatchDecklistImport;
using OmniCard.Views.DecklistImport;
using Xunit;

namespace OmniCard.Tests.ViewModels;

public class BatchDecklistImportViewModelTests
{
    private static DecklistImportRow Row(int qty, bool resolved) =>
        new() { Quantity = qty, Name = "Island",
                Match = resolved ? new CardMatch { GameSpecificId = "a", Name = "Island" } : null };

    private static (BatchDecklistImportViewModel vm, FakeDecklistImportService imp,
        RecordingListService lists, RecordingContainerService containers, RecordingCardService cards,
        FakeDecklistParseService decks) Build()
    {
        var gs = new ConfigurableGameService();
        var cards = new RecordingCardService(gs);
        var lists = new RecordingListService();
        var containers = new RecordingContainerService();
        var imp = new FakeDecklistImportService();
        var decks = new FakeDecklistParseService();
        var vm = new BatchDecklistImportViewModel(imp, cards, lists, containers, decks);
        vm.Load();
        return (vm, imp, lists, containers, cards, decks);
    }

    private static string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task AddUrls_FetchSucceeds_AddsRowNamedByDeck()
    {
        var (vm, imp, _, _, _, decks) = Build();
        decks.OnFetch = url => ("First Flight", new List<DecklistEntry> { new(1, "Sol Ring", "SCD", "276") });
        imp.OnResolveEntries = _ => [Row(1, true)];
        vm.UrlText = "https://moxfield.com/decks/abc";

        await vm.AddUrlsCommand.ExecuteAsync(null);

        var file = Assert.Single(vm.Files);
        Assert.Equal("First Flight", file.SourceName);
        Assert.Equal("First Flight", file.DefaultNewName);   // deck name pre-fills the new-target name
        Assert.Equal("", vm.UrlText);                        // consumed
    }

    [Fact]
    public async Task AddUrls_FetchFails_SkipsAndReports_KeepsUrl()
    {
        var (vm, _, _, _, _, decks) = Build();
        decks.OnFetch = _ => null;   // unreachable/unsupported
        vm.UrlText = "https://example.com/bad";

        await vm.AddUrlsCommand.ExecuteAsync(null);

        Assert.Empty(vm.Files);
        Assert.Contains("bad", vm.UrlText);                  // failed URL retained
        Assert.Contains("fetch", vm.StatusMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddPaths_DecklistFile_AddsRow()
    {
        var (vm, imp, _, _, _, _) = Build();
        imp.OnResolve = _ => [Row(4, true), Row(1, false)];
        var path = TempFile("1 Island (SCD) 337\n");

        vm.AddPaths([path]);

        var file = Assert.Single(vm.Files);
        Assert.Equal(1, file.ResolvedCount);
        Assert.Equal(1, file.UnresolvedCount);
    }

    [Fact]
    public void AddPaths_CsvFile_ImportsViaCallback_NoRow()
    {
        var (vm, _, _, _, _, _) = Build();
        vm.ImportCsvFile = _ => 5;
        var path = TempFile("GameCardId,Name,SetCode\n");   // AppNative CSV header

        vm.AddPaths([path]);

        Assert.Empty(vm.Files);
        Assert.Equal(5, vm.CsvImportedCount);
    }

    [Fact]
    public void AddPaths_UnknownFile_SkipsWithMessage()
    {
        var (vm, _, _, _, _, _) = Build();
        var path = TempFile("just some prose\n");

        vm.AddPaths([path]);

        Assert.Empty(vm.Files);
        Assert.Contains("unrecognized", vm.StatusMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanImport_FalseUntilAllRowsHaveTargets()
    {
        var (vm, imp, _, containers, _, _) = Build();
        var box = new StorageContainer { Id = 7, Name = "Box" };
        containers.Containers.Add(box);
        imp.OnResolve = _ => [Row(1, true)];
        vm.AddPaths([TempFile("1 Island (SCD) 337\n"), TempFile("1 Plains (SCD) 333\n")]);

        vm.Files[0].SelectedLocation = box;
        Assert.False(vm.CanImport);
        vm.Files[1].SelectedLocation = box;
        Assert.True(vm.CanImport);
    }

    [Fact]
    public void Import_SummaryIncludesCsvCount_AndTargetFlags()
    {
        var (vm, imp, _, containers, _, _) = Build();
        var box = new StorageContainer { Id = 7, Name = "Box" };
        containers.Containers.Add(box);
        imp.OnResolve = _ => [Row(4, true)];
        vm.ImportCsvFile = _ => 5;
        vm.AddPaths([TempFile("GameCardId,Name\n")]);            // CSV → immediate import
        vm.AddPaths([TempFile("1 Island (SCD) 337\n")]);        // decklist → row
        vm.Files[0].SelectedLocation = box;

        vm.ImportCommand.Execute(null);

        Assert.Equal(1, vm.Result!.FileCount);
        Assert.Equal(4, vm.Result.TotalAdded);
        Assert.Equal(5, vm.Result.CsvImportedCount);
        Assert.True(vm.Result.AnyLocationTarget);
        Assert.Single(imp.LocationCommits);
    }

    [Fact]
    public void Cancel_WithCsvImported_SetsResultSoCallerRefreshes()
    {
        var (vm, _, _, _, _, _) = Build();
        vm.ImportCsvFile = _ => 3;
        vm.AddPaths([TempFile("GameCardId,Name\n")]);

        vm.CancelCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal(3, vm.Result!.CsvImportedCount);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~BatchDecklistImportViewModelTests" -v minimal`
Expected: FAIL to compile (new members / ctor / signatures not defined).

- [ ] **Step 3: Extend the summary**

`OmniCard.Shared/Models/BatchDecklistImportSummary.cs`:

```csharp
namespace OmniCard.Models;

public record BatchFileResult(string FileName, string TargetName, int Added, int Unresolved);

public record BatchDecklistImportSummary(
    int FileCount,
    int TotalAdded,
    int TotalUnresolved,
    bool AnyListTarget,
    bool AnyLocationTarget,
    int CsvImportedCount,
    IReadOnlyList<BatchFileResult> Files);
```

- [ ] **Step 4: Change the `DecklistFileImport` ctor to take explicit names**

In `OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs`, change the constructor signature + body head (rest unchanged):

```csharp
    public DecklistFileImport(
        string displayName,
        string defaultNewName,
        IReadOnlyList<DecklistImportRow> rows,
        IReadOnlyList<CardList> availableLists,
        IReadOnlyList<StorageContainer> availableLocations)
    {
        SourceName = displayName;
        DefaultNewName = defaultNewName;
        NewName = defaultNewName;
        AvailableLists = availableLists;
        AvailableLocations = availableLocations;
        foreach (var r in rows) Rows.Add(r);
        ResolvedCount = Rows.Count(r => r.IsResolved);
        UnresolvedCount = Rows.Count - ResolvedCount;
        SummaryLabel = $"{ResolvedCount} resolved · {UnresolvedCount} unresolved";
    }
```

(The `using System.IO;` for `Path` is no longer needed here — remove it if the file no longer references `Path`.)

- [ ] **Step 5: Rewrite the hub view model**

Replace `OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs` with:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Views.BatchDecklistImport;

public sealed partial class BatchDecklistImportViewModel(
    IDecklistImportService importService,
    ICardService cardService,
    IListService listService,
    IStorageContainerService containerService,
    IDecklistService decklistService) : ViewModel
{
    public ObservableCollection<DecklistFileImport> Files { get; } = [];
    public ObservableCollection<CardList> AvailableLists { get; } = [];
    public ObservableCollection<StorageContainer> AvailableLocations { get; } = [];

    [ObservableProperty] public partial DecklistFileImport? SelectedFile { get; set; }
    [ObservableProperty] public partial string HeaderLabel { get; set; } = "";
    [ObservableProperty] public partial string UrlText { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    public partial bool IsBusy { get; set; }

    public bool NotBusy => !IsBusy;

    public int CsvImportedCount { get; private set; }

    public bool CanImport => Files.Count > 0 && Files.All(f => f.HasTarget);

    public BatchDecklistImportSummary? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    /// <summary>Host callback (DialogService): open the OS file picker; return chosen paths or null.</summary>
    public Func<IReadOnlyList<string>?>? PickFiles { get; set; }
    /// <summary>Host callback (DialogService): run the CSV preview+import for a path; return imported count or null.</summary>
    public Func<string, int?>? ImportCsvFile { get; set; }

    public void Load()
    {
        var game = cardService.ActiveGameService.Game;
        AvailableLists.Clear();
        foreach (var l in listService.GetLists(game)) AvailableLists.Add(l);
        AvailableLocations.Clear();
        foreach (var c in containerService.GetAll()) AvailableLocations.Add(c);
        Files.Clear();
        UpdateHeader();
    }

    [RelayCommand]
    public void AddFiles()
    {
        var paths = PickFiles?.Invoke();
        if (paths is not null && paths.Count > 0) AddPaths(paths);
    }

    /// <summary>Classify + route each path: decklist → row, CSV → immediate import (callback), else skip.</summary>
    public void AddPaths(IReadOnlyList<string> paths)
    {
        var notes = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var kind = ImportFileClassifier.ClassifyFile(path);
                if (kind == ImportKind.Decklist)
                {
                    var rows = importService.ResolveFile(File.ReadAllText(path));
                    AddRow(Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), rows);
                }
                else if (kind == ImportKind.Csv)
                {
                    var imported = ImportCsvFile?.Invoke(path);
                    if (imported.HasValue)
                    {
                        CsvImportedCount += imported.Value;
                        notes.Add($"Imported {imported.Value} from {Path.GetFileName(path)}");
                    }
                }
                else
                {
                    notes.Add($"Skipped {Path.GetFileName(path)} (unrecognized)");
                }
            }
            catch (Exception)
            {
                notes.Add($"Failed to read {Path.GetFileName(path)}");
            }
        }
        if (notes.Count > 0) StatusMessage = string.Join(" · ", notes);
        UpdateHeader();
    }

    [RelayCommand]
    public async Task AddUrls()
    {
        var urls = UrlText.Split('\n').Select(u => u.Trim()).Where(u => u.Length > 0).ToList();
        if (urls.Count == 0) { StatusMessage = "Paste one or more deck URLs (one per line)."; return; }

        IsBusy = true;
        try
        {
            var failed = new List<string>();
            foreach (var url in urls)
            {
                var fetched = await decklistService.FetchDecklistAsync(url);
                if (fetched is null) { failed.Add(url); continue; }
                var (deckName, entries) = fetched.Value;
                var rows = importService.ResolveEntries(entries);
                AddRow(deckName, deckName, rows);
            }
            UrlText = string.Join("\n", failed);
            var added = urls.Count - failed.Count;
            StatusMessage = failed.Count == 0
                ? $"Added {added} deck(s)."
                : $"Added {added} deck(s). Couldn't fetch: {string.Join(", ", failed)}";
        }
        finally
        {
            IsBusy = false;
        }
        UpdateHeader();
    }

    private void AddRow(string displayName, string defaultNewName, IReadOnlyList<DecklistImportRow> rows)
    {
        var item = new DecklistFileImport(displayName, defaultNewName, rows, AvailableLists, AvailableLocations);
        item.PropertyChanged += OnItemChanged;
        Files.Add(item);
        SelectedFile ??= item;
    }

    private void UpdateHeader()
    {
        HeaderLabel = $"{Files.Count} deck(s) · {Files.Sum(f => f.ResolvedCount)} resolved · {Files.Sum(f => f.UnresolvedCount)} unresolved";
        OnPropertyChanged(nameof(CanImport));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DecklistFileImport.HasTarget))
            OnPropertyChanged(nameof(CanImport));
    }

    [RelayCommand]
    public void Import()
    {
        var game = cardService.ActiveGameService.Game;
        var perFile = new List<BatchFileResult>();
        var totalAdded = 0;
        var totalUnresolved = 0;
        var anyList = false;
        var anyLocation = false;

        foreach (var f in Files)
        {
            var resolved = f.Rows.Where(r => r.IsResolved).ToList();
            int added;
            string targetName;

            if (f.TargetIsList)
            {
                anyList = true;
                var listId = f.CreateNew ? listService.CreateList(f.NewName.Trim(), game).Id : f.SelectedList!.Id;
                targetName = f.CreateNew ? f.NewName.Trim() : f.SelectedList!.Name;
                added = importService.CommitToList(listId, resolved);
            }
            else
            {
                anyLocation = true;
                var container = f.CreateNew ? containerService.Create(f.NewName.Trim(), f.NewLocationType) : f.SelectedLocation!;
                targetName = container.Name;
                added = importService.CommitToLocation(container, resolved);
            }

            totalAdded += added;
            totalUnresolved += f.UnresolvedCount;
            perFile.Add(new BatchFileResult(f.SourceName, targetName, added, f.UnresolvedCount));
        }

        Result = new BatchDecklistImportSummary(Files.Count, totalAdded, totalUnresolved, anyList, anyLocation, CsvImportedCount, perFile);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel()
    {
        // Preserve CSVs imported during this session so the caller still refreshes its views.
        if (CsvImportedCount > 0)
            Result = new BatchDecklistImportSummary(0, 0, 0, false, false, CsvImportedCount, []);
        CloseDialog?.Invoke(false);
    }
}
```

- [ ] **Step 6: Make the decklist fake fetch-capable**

In `OmniCard.Tests/Fakes/ImportFakes.cs`, replace `FakeDecklistParseService.FetchDecklistAsync` (currently throws) with a canned-result delegate:

```csharp
    public Func<string, (string DeckName, List<DecklistEntry> Entries)?> OnFetch = _ => null;
    public Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url) => Task.FromResult(OnFetch(url));
```

(Leave `ParseDecklistPrintings`/`Printings` and the other throwing members as-is.)

- [ ] **Step 7: Swap the dialog service to no-args + wire callbacks**

`OmniCard.Shared/Interfaces/IDialogService.cs` — change the signature:

```csharp
    BatchDecklistImportSummary? ShowBatchDecklistImport();
```

`OmniCard/Services/DialogService.cs` — replace the method body (needs `ICsvExportImportService` via DI and `OpenFileDialog`):

```csharp
    public BatchDecklistImportSummary? ShowBatchDecklistImport()
    {
        var wnd = Services.GetRequiredService<BatchDecklistImportView>();
        SetOwner(wnd);
        var csv = Services.GetRequiredService<ICsvExportImportService>();
        wnd.ViewModel.ImportCsvFile = path => ShowImportPreview(csv.PreviewImport(path));
        wnd.ViewModel.PickFiles = () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Import files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
                Title = "Add files",
                Multiselect = true,
            };
            return d.ShowDialog() == true ? d.FileNames : null;
        };
        wnd.ViewModel.Load();
        wnd.ShowDialog();
        return wnd.ViewModel.Result;   // set on Import, or on Cancel when CSVs were imported
    }
```

- [ ] **Step 8: Collapse `RootViewModel.Import()`**

In `OmniCard/Views/Root/RootViewModel.cs`, replace the whole `Import()` method body with the hub-open + refresh-from-summary:

```csharp
    [RelayCommand]
    public void Import()
    {
        try
        {
            var summary = dialogService.ShowBatchDecklistImport();
            if (summary is null)
                return;

            var containersChanged = summary.AnyLocationTarget || summary.CsvImportedCount > 0;
            if (containersChanged)
                LoadContainers();
            if (Collection.ShowCardList)
                _ = Collection.SearchCollection();
            else
                Collection.LoadOverview();
            if (summary.AnyListTarget)
                Lists.Refresh();

            var parts = new List<string>();
            if (summary.FileCount > 0)
                parts.Add($"Imported {summary.TotalAdded} cards across {summary.FileCount} deck(s)"
                    + (summary.TotalUnresolved > 0 ? $"; {summary.TotalUnresolved} lines unresolved" : ""));
            if (summary.CsvImportedCount > 0)
                parts.Add($"Imported {summary.CsvImportedCount} cards from CSV");
            if (parts.Count > 0)
                Message = string.Join(" · ", parts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import");
            System.Windows.MessageBox.Show($"Failed to import: {ex.Message}", "Import Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
```

Then clean up: if `csvService` and `ImportFileClassifier`/`using OmniCard.Collection;` are no longer referenced anywhere else in `RootViewModel`, remove the now-unused constructor parameter / usings (let the build's unused-warning + a grep for `csvService`/`ImportFileClassifier` in the file guide you; if `csvService` is used by other commands like export, leave it).

- [ ] **Step 9: Run tests + build**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~BatchDecklistImportViewModelTests" -v minimal` — Expected: PASS (8 tests).
Run: `dotnet build OmniCard/OmniCard.csproj -v minimal` and `dotnet build -v minimal` — Expected: Build succeeded (no callers of the old `ShowBatchDecklistImport(files)` / `Load(files)` remain).
Run: `dotnet test OmniCard.Tests -v minimal` — Expected: full suite green.

- [ ] **Step 10: Commit**

```bash
git add OmniCard.Shared/Models/BatchDecklistImportSummary.cs OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs OmniCard.Shared/Interfaces/IDialogService.cs OmniCard/Services/DialogService.cs OmniCard/Views/Root/RootViewModel.cs OmniCard.Tests/Fakes/ImportFakes.cs OmniCard.Tests/ViewModels/BatchDecklistImportViewModelTests.cs
git commit -m "feat(import): batch dialog becomes the import hub (files + URLs)"
```

---

### Task 3: Hub toolbar UI (Add files + Add URLs) + manual smoke

**Files:**
- Modify: `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml`

UI wiring — verified by build + manual smoke; no unit tests.

- [ ] **Step 1: Add the toolbar**

In `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml`, add a toolbar row above the existing master-detail area. Add a new top row to the outer `Grid.RowDefinitions` (so rows become: toolbar `Auto`, header `Auto`, master-detail `*`, buttons `Auto`) and place this block in the new top row (explicit `Foreground` on all text controls per the dark-theme rule):

```xml
<!-- Toolbar: Add files + Add URLs -->
<StackPanel Grid.Row="0" Margin="0,0,0,8">
    <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
        <Button Content="Add files…" Command="{Binding ViewModel.AddFilesCommand}" Padding="12,4" Margin="0,0,8,0"/>
    </StackPanel>
    <TextBlock Text="Add decks by URL (Moxfield / Archidekt, one per line):"
               Foreground="{DynamicResource MaterialDesign.Brush.Foreground}" Margin="0,0,0,2"/>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBox Grid.Column="0" Text="{Binding ViewModel.UrlText, UpdateSourceTrigger=PropertyChanged}"
                 AcceptsReturn="True" MinLines="2" MaxLines="4" VerticalScrollBarVisibility="Auto"
                 Foreground="{DynamicResource MaterialDesign.Brush.Foreground}" Margin="0,0,8,0"/>
        <Button Grid.Column="1" Content="Fetch" VerticalAlignment="Top" Padding="12,4"
                Command="{Binding ViewModel.AddUrlsCommand}"
                IsEnabled="{Binding ViewModel.NotBusy}"/>
    </Grid>
    <TextBlock Text="{Binding ViewModel.StatusMessage}" TextWrapping="Wrap"
               Foreground="{DynamicResource MaterialDesign.Brush.Foreground}" Margin="0,4,0,0"/>
</StackPanel>
```

Adjust the existing `Grid.Row` indices of the header/master-detail/buttons blocks so they shift down by one. The Fetch button binds `IsEnabled="{Binding ViewModel.NotBusy}"` — `NotBusy` was added to the VM in Task 2 (Step 5), so no VM change is needed here.

- [ ] **Step 2: Build**

Run: `dotnet build OmniCard/OmniCard.csproj -v minimal` — Expected: Build succeeded. Fix any XAML binding compile errors.

- [ ] **Step 3: Manual smoke test (verify skill)**

Use the `verify`/`run` skill to launch the app and drive the flow:
1. File ▸ Import… (Ctrl+I) → the hub opens empty.
2. **Add URL(s):** paste a Moxfield deck URL and an Archidekt deck URL (one per line) → Fetch → two rows appear, each named by the deck; the URL box clears (failed ones stay).
3. Assign each deck a target (a new location with the deck name pre-filled; an existing/new list) → Import stays disabled until both are targeted → Import → cards land; the new location/list appear immediately (no restart).
4. **Add files:** in a fresh session, Add files… → pick a decklist `.txt` (row added) and a `.csv` collection (its CSV preview pops and imports; a status note shows) → confirm the decklist row imports and the CSV count is reflected.
5. Paste a bad/unsupported URL → it's reported in the status line and skipped; other URLs still added.

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs
git commit -m "feat(import): hub toolbar — Add files + Add URLs"
```

---

## Full-suite check

- [ ] `dotnet test OmniCard.Tests -v minimal` — all green (new `ResolveEntries` + rewritten hub tests; no regressions).
- [ ] `dotnet build -v minimal` (solution) — no broken references.
