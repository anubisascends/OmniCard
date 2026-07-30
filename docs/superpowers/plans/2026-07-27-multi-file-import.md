# Unified Multi-File Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two import commands with one auto-detecting `Import…` that multi-selects files, previews CSVs in sequence, batch-imports decklists (each file to its own List/Location target via a master-detail dialog), and refreshes the location/list views afterward.

**Architecture:** A pure `ImportFileClassifier` sniffs each file (CSV header vs decklist line). An app-level `DecklistImportService` owns resolve+commit (extracted from the retired single-file VM). A `BatchDecklistImportViewModel` + master-detail `BatchDecklistImportView` handle 1…N decklist files with per-file targets. The unified `RootViewModel.Import()` routes files and runs the app's container/list refresh cascade. The old single-file decklist dialog is retired.

**Tech Stack:** C# / .NET, WPF + MaterialDesignInXAML, CommunityToolkit.Mvvm source generators, EF Core, xUnit with hand-written fakes.

## Global Constraints

- All imported cards are **non-foil**; trailing `*E*`/`*F*` ignored (inherited).
- Location imports use condition `"Near Mint"`, `purchasePrice: null`.
- Resolution ladder unchanged (`DecklistPrintingResolver`): set+cn exact→unresolved-if-miss / set-only cheapest / cn-only cheapest / name-only cheapest; active game only; unresolved lines reported and skipped, never guessed.
- **Force choose per file:** batch items start with **no** target; Import disabled until every file has a valid target. Create-new pre-fills the filename; no auto-default to a location.
- Each file imports to **its own** target; no cross-file "apply to all".
- One `Import…` menu item, keeps **Ctrl+I**; the separate decklist item is removed.
- WPF dark-theme rule: explicit `Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"` on every text control; the Import button binds `IsEnabled="{Binding ViewModel.CanImport}"`.
- MVVM conventions: `[ObservableProperty] public partial` props, `[RelayCommand]`, `[NotifyPropertyChangedFor]`; hand-written xUnit fakes (no mocking library).
- Keep every task's build green; the single-file dialog stays functional until it is deliberately retired in Task 6.

---

## File Structure

**Create:**
- `OmniCard.Collection/ImportFileClassifier.cs` — pure file/line classifier.
- `OmniCard/Services/IDecklistImportService.cs` + `OmniCard/Services/DecklistImportService.cs` — resolve+commit service (app project; returns `DecklistImportRow`).
- `OmniCard.Shared/Models/BatchDecklistImportSummary.cs` — `BatchDecklistImportSummary` + `BatchFileResult`.
- `OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs` — per-file item.
- `OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs`.
- `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml` (+ `.xaml.cs`).
- Tests: `ImportFileClassifierTests`, `DecklistImportServiceTests`, `BatchDecklistImportViewModelTests`.

**Modify:**
- `OmniCard.Collection/CsvExportImportService.cs:421` — `DetectFormat` `private`→`internal`.
- `OmniCard.Collection/DecklistService.cs` — add `public static IsIgnorableLine` + `LooksLikeDecklistLine`.
- `OmniCard.Shared/Interfaces/IDialogService.cs` + `OmniCard/Services/DialogService.cs` — swap decklist dialog method.
- `OmniCard/App.xaml.cs` — DI: add `IDecklistImportService` + batch dialog; remove old decklist dialog.
- `OmniCard/Views/Lists/ListsViewModel.cs` — add `Refresh()`.
- `OmniCard/Views/Root/RootViewModel.cs` — unified `Import()` + refresh cascade (replace two commands).
- `OmniCard/Views/Root/RootView.xaml` — one Import menu item; Ctrl+I → `ImportCommand`.
- `OmniCard.Tests/Fakes/ImportFakes.cs` — add `FakeDecklistImportService`.

**Delete (Task 6):**
- `OmniCard/Views/DecklistImport/DecklistImportView.xaml(.cs)`, `DecklistImportViewModel.cs`, `OmniCard.Shared/Models/DecklistImportSummary.cs`, `OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs`.
- **Keep** `OmniCard/Views/DecklistImport/DecklistImportRow.cs` (namespace `OmniCard.Views.DecklistImport`) — still consumed by the service + batch VM.

---

### Task 1: `ImportFileClassifier` + detection helpers

**Files:**
- Modify: `OmniCard.Collection/CsvExportImportService.cs:421`
- Modify: `OmniCard.Collection/DecklistService.cs`
- Create: `OmniCard.Collection/ImportFileClassifier.cs`
- Test: `OmniCard.Tests/Services/ImportFileClassifierTests.cs`

**Interfaces:**
- Produces: `enum ImportKind { Csv, Decklist, Unknown }`; `static ImportKind ImportFileClassifier.Classify(string firstContentLine)`; `static ImportKind ImportFileClassifier.ClassifyFile(string path)`; `internal static CsvFormat? CsvExportImportService.DetectFormat(HashSet<string>)`; `public static bool DecklistService.IsIgnorableLine(string)`, `public static bool DecklistService.LooksLikeDecklistLine(string)`.

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/ImportFileClassifierTests.cs`:

```csharp
using OmniCard.Collection;
using Xunit;

namespace OmniCard.Tests.Services;

public class ImportFileClassifierTests
{
    [Theory]
    [InlineData("Game,GameCardId,Name,SetCode")]                         // AppNative marker
    [InlineData("Quantity,Name,Set Name,Number,Printing,Price")]         // TcgPlayer marker
    [InlineData("Count,Name,Edition,Collector Number")]                  // Moxfield marker
    [InlineData("Name,Set code,Foil,Scryfall ID,Purchase price currency")] // Manabox trio
    public void Classify_KnownCsvHeader_ReturnsCsv(string line)
        => Assert.Equal(ImportKind.Csv, ImportFileClassifier.Classify(line));

    [Theory]
    [InlineData("1 Isperia, Supreme Judge (SCD) 4 *E*")]
    [InlineData("1x Sol Ring (SCD) 276")]
    [InlineData("4 Island")]
    public void Classify_DecklistLine_ReturnsDecklist(string line)
        => Assert.Equal(ImportKind.Decklist, ImportFileClassifier.Classify(line));

    [Theory]
    [InlineData("just some random prose")]
    [InlineData("Name,RandomColumn,Other")]   // comma-list but no known marker → not CSV; not a qty line
    public void Classify_Unrecognized_ReturnsUnknown(string line)
        => Assert.Equal(ImportKind.Unknown, ImportFileClassifier.Classify(line));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ImportFileClassifierTests" -v minimal`
Expected: FAIL to compile — `ImportFileClassifier` not defined.

- [ ] **Step 3: Expose the detection helpers**

In `OmniCard.Collection/CsvExportImportService.cs:421`, change the modifier only:

```csharp
    internal static CsvFormat? DetectFormat(HashSet<string> headers)
```

In `OmniCard.Collection/DecklistService.cs`, add two public static helpers that reuse the existing `SectionHeaders` set and `DecklistLineRegex()` (both already in this class). Place them near `ParseDecklistPrintings`:

```csharp
    /// <summary>True for lines the decklist parser skips: blank, // comment, or a section header.</summary>
    public static bool IsIgnorableLine(string line)
    {
        var t = line.Trim();
        return t.Length == 0 || t.StartsWith("//") || SectionHeaders.Contains(t);
    }

    /// <summary>True if the line looks like a decklist entry ("1 Card", "1x Card (SET) 4 ...").</summary>
    public static bool LooksLikeDecklistLine(string line)
        => !IsIgnorableLine(line) && DecklistLineRegex().IsMatch(line.Trim());
```

- [ ] **Step 4: Create the classifier**

Create `OmniCard.Collection/ImportFileClassifier.cs`:

```csharp
namespace OmniCard.Collection;

public enum ImportKind { Csv, Decklist, Unknown }

/// <summary>Sniffs a file (or its first content line) to route imports to the right dialog.</summary>
public static class ImportFileClassifier
{
    public static ImportKind Classify(string firstContentLine)
    {
        if (string.IsNullOrWhiteSpace(firstContentLine))
            return ImportKind.Unknown;

        var headers = new HashSet<string>(
            firstContentLine.Split(',').Select(h => h.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (CsvExportImportService.DetectFormat(headers) is not null)
            return ImportKind.Csv;

        if (DecklistService.LooksLikeDecklistLine(firstContentLine))
            return ImportKind.Decklist;

        return ImportKind.Unknown;
    }

    /// <summary>Reads the first non-ignorable line of the file and classifies it. Unknown on empty/unreadable content.</summary>
    public static ImportKind ClassifyFile(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            if (DecklistService.IsIgnorableLine(raw))
                continue;
            return Classify(raw.Trim());
        }
        return ImportKind.Unknown;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ImportFileClassifierTests" -v minimal`
Expected: PASS. Then confirm no regression in existing decklist/CSV tests:
Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~Decklist|FullyQualifiedName~Csv" -v minimal` — Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Collection/ImportFileClassifier.cs OmniCard.Collection/CsvExportImportService.cs OmniCard.Collection/DecklistService.cs OmniCard.Tests/Services/ImportFileClassifierTests.cs
git commit -m "feat(import): file-type classifier (CSV vs decklist) + detection helpers"
```

---

### Task 2: `DecklistImportService` (resolve + commit)

**Files:**
- Create: `OmniCard/Services/IDecklistImportService.cs`, `OmniCard/Services/DecklistImportService.cs`
- Modify: `OmniCard/App.xaml.cs`
- Test: `OmniCard.Tests/Services/DecklistImportServiceTests.cs`

**Interfaces:**
- Consumes: `IDecklistService.ParseDecklistPrintings`, `ICardService.ActiveGameService`/`AddCardToCollection`, `IListService.AddPrinting`, `DecklistPrintingResolver.Resolve`, `DecklistImportRow` (`OmniCard.Views.DecklistImport`).
- Produces: `IReadOnlyList<DecklistImportRow> ResolveFile(string fileText)`; `int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows)`; `int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows)`.

- [ ] **Step 1: Write the failing tests**

Create `OmniCard.Tests/Services/DecklistImportServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Models;
using OmniCard.Services;
using OmniCard.Tests.Fakes;
using OmniCard.Views.DecklistImport;
using Xunit;

namespace OmniCard.Tests.Services;

public class DecklistImportServiceTests
{
    private static CardMatch M(string id) => new() { GameSpecificId = id, Name = "Island", SetCode = "SCD", CollectorNumber = "337" };

    private static (DecklistImportService svc, ConfigurableGameService gs, RecordingCardService cards,
        RecordingListService lists, FakeDecklistParseService decks) Build()
    {
        var gs = new ConfigurableGameService();
        var cards = new RecordingCardService(gs);
        var lists = new RecordingListService();
        var decks = new FakeDecklistParseService();
        var svc = new DecklistImportService(decks, cards, lists, NullLogger<DecklistImportService>.Instance);
        return (svc, gs, cards, lists, decks);
    }

    [Fact]
    public void ResolveFile_ReturnsRows_WithResolvedAndUnresolved()
    {
        var (svc, gs, _, _, decks) = Build();
        decks.Printings =
        [
            new DecklistEntry(4, "Island", "SCD", "337"),
            new DecklistEntry(1, "Nonesuch", "SCD", "999"),
        ];
        gs.OnSearchCards = (q, _) => q.Contains("337") ? [M("a")] : [];

        var rows = svc.ResolveFile("ignored");

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsResolved);
        Assert.Equal(4, rows[0].Quantity);
        Assert.False(rows[1].IsResolved);
    }

    [Fact]
    public void CommitToList_CallsAddPrinting_FileSource_NonFoil_ReturnsQuantitySum()
    {
        var (svc, _, _, lists, _) = Build();
        var rows = new List<DecklistImportRow>
        {
            new() { Quantity = 4, Name = "Island", Match = M("a") },
            new() { Quantity = 2, Name = "Plains", Match = M("b") },
        };

        var added = svc.CommitToList(42, rows);

        Assert.Equal(6, added);
        Assert.Equal(2, lists.Printings.Count);
        Assert.All(lists.Printings, p => Assert.Equal(42, p.ListId));
        Assert.All(lists.Printings, p => Assert.False(p.IsFoil));
        Assert.All(lists.Printings, p => Assert.Equal(ListItemSource.File, p.Source));
    }

    [Fact]
    public void CommitToLocation_CallsAddCardToCollection_NearMint_NonFoil_NoPrice()
    {
        var (svc, _, cards, _, _) = Build();
        var box = new StorageContainer { Id = 7, Name = "Deck Box" };
        var rows = new List<DecklistImportRow> { new() { Quantity = 3, Name = "Island", Match = M("a") } };

        var added = svc.CommitToLocation(box, rows);

        Assert.Equal(3, added);
        var call = Assert.Single(cards.Added);
        Assert.Equal("Near Mint", call.Condition);
        Assert.False(call.IsFoil);
        Assert.Null(call.PurchasePrice);
        Assert.Equal(3, call.Quantity);
        Assert.Equal(7, call.Container!.Id);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistImportServiceTests" -v minimal`
Expected: FAIL to compile — `DecklistImportService` not defined.

- [ ] **Step 3: Create the interface**

Create `OmniCard/Services/IDecklistImportService.cs`:

```csharp
using OmniCard.Models;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Services;

public interface IDecklistImportService
{
    /// <summary>Parse + resolve a decklist file's text against the active game into preview rows.</summary>
    IReadOnlyList<DecklistImportRow> ResolveFile(string fileText);

    /// <summary>Add resolved rows to a list; returns total cards added (sum of quantities).</summary>
    int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows);

    /// <summary>Add resolved rows to a location; returns total cards added (sum of quantities).</summary>
    int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows);
}
```

- [ ] **Step 4: Create the implementation**

Create `OmniCard/Services/DecklistImportService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Services;

public sealed class DecklistImportService(
    IDecklistService decklistService,
    ICardService cardService,
    IListService listService,
    ILogger<DecklistImportService> logger) : IDecklistImportService
{
    public IReadOnlyList<DecklistImportRow> ResolveFile(string fileText)
    {
        var gs = cardService.ActiveGameService;
        var rows = new List<DecklistImportRow>();
        foreach (var e in decklistService.ParseDecklistPrintings(fileText))
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

    public int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows)
    {
        var added = 0;
        foreach (var row in resolvedRows)
        {
            listService.AddPrinting(listId, row.Match!, isFoil: false, row.Quantity, ListItemSource.File);
            added += row.Quantity;
        }
        return added;
    }

    public int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows)
    {
        var added = 0;
        var game = cardService.ActiveGameService.Game;
        foreach (var row in resolvedRows)
        {
            cardService.AddCardToCollection(row.Match!, game, condition: "Near Mint", isFoil: false,
                purchasePrice: null, quantity: row.Quantity, container, page: null, slot: null, section: null);
            added += row.Quantity;
        }
        return added;
    }
}
```

- [ ] **Step 5: Register in DI**

In `OmniCard/App.xaml.cs`, near the other service registrations (e.g. after `IListService`), add (and a `using OmniCard.Services;` if not present):

```csharp
            services.AddSingleton<IDecklistImportService, DecklistImportService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistImportServiceTests" -v minimal`
Expected: PASS (3 tests). Build the app to confirm DI compiles: `dotnet build OmniCard/OmniCard.csproj -v minimal`.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Services/IDecklistImportService.cs OmniCard/Services/DecklistImportService.cs OmniCard/App.xaml.cs OmniCard.Tests/Services/DecklistImportServiceTests.cs
git commit -m "feat(import): extract DecklistImportService (resolve + commit)"
```

---

### Task 3: Batch summary, per-file item, batch view model

**Files:**
- Create: `OmniCard.Shared/Models/BatchDecklistImportSummary.cs`
- Create: `OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs`
- Create: `OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs`
- Modify: `OmniCard.Tests/Fakes/ImportFakes.cs` (add `FakeDecklistImportService`)
- Test: `OmniCard.Tests/ViewModels/BatchDecklistImportViewModelTests.cs`

**Interfaces:**
- Consumes: `IDecklistImportService`, `ICardService`, `IListService`, `IStorageContainerService`, `DecklistImportRow`.
- Produces: `record BatchFileResult(string FileName, string TargetName, int Added, int Unresolved)`; `record BatchDecklistImportSummary(int FileCount, int TotalAdded, int TotalUnresolved, bool AnyListTarget, bool AnyLocationTarget, IReadOnlyList<BatchFileResult> Files)`; `class DecklistFileImport`; `class BatchDecklistImportViewModel` with `Files`, `SelectedFile`, `AvailableLists`, `AvailableLocations`, `HeaderLabel`, `CanImport`, `Result`, `CloseDialog`, `Load(IReadOnlyList<(string Name, string Text)>)`, `ImportCommand`, `CancelCommand`.

- [ ] **Step 1: Add `FakeDecklistImportService` to `ImportFakes.cs`**

Append to `OmniCard.Tests/Fakes/ImportFakes.cs` (needs `using OmniCard.Services;` and `using OmniCard.Views.DecklistImport;` at the file top — add if missing):

```csharp
/// <summary>IDecklistImportService returning canned rows and recording commits.</summary>
public sealed class FakeDecklistImportService : IDecklistImportService
{
    public Func<string, List<DecklistImportRow>> OnResolve = _ => [];
    public List<(int ListId, int Count)> ListCommits { get; } = [];
    public List<(StorageContainer Container, int Count)> LocationCommits { get; } = [];

    public IReadOnlyList<DecklistImportRow> ResolveFile(string fileText) => OnResolve(fileText);

    public int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows)
    {
        var rows = resolvedRows.ToList();
        ListCommits.Add((listId, rows.Count));
        return rows.Sum(r => r.Quantity);
    }

    public int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows)
    {
        var rows = resolvedRows.ToList();
        LocationCommits.Add((container, rows.Count));
        return rows.Sum(r => r.Quantity);
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `OmniCard.Tests/ViewModels/BatchDecklistImportViewModelTests.cs`:

```csharp
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
        RecordingListService lists, RecordingContainerService containers, RecordingCardService cards) Build()
    {
        var gs = new ConfigurableGameService();
        var cards = new RecordingCardService(gs);
        var lists = new RecordingListService();
        var containers = new RecordingContainerService();
        var imp = new FakeDecklistImportService();
        var vm = new BatchDecklistImportViewModel(imp, cards, lists, containers);
        return (vm, imp, lists, containers, cards);
    }

    [Fact]
    public void Load_BuildsOneItemPerFile_WithCountsAndDefaultName_AndNoTarget()
    {
        var (vm, imp, _, containers, _) = Build();
        containers.Containers.Add(new StorageContainer { Id = 7, Name = "Box" });
        imp.OnResolve = t => t == "A"
            ? [Row(4, true), Row(1, false)]
            : [Row(2, true)];

        vm.Load([("deckA.txt", "A"), ("deckB.txt", "B")]);

        Assert.Equal(2, vm.Files.Count);
        Assert.Equal("deckA", vm.Files[0].DefaultNewName);
        Assert.Equal(1, vm.Files[0].ResolvedCount);   // 1 resolved row
        Assert.Equal(1, vm.Files[0].UnresolvedCount);
        Assert.False(vm.Files[0].HasTarget);           // force-choose: nothing selected
        Assert.Same(vm.Files[0], vm.SelectedFile);     // first file selected for detail pane
        Assert.False(vm.CanImport);
    }

    [Fact]
    public void CanImport_TrueOnlyWhenAllFilesHaveTargets()
    {
        var (vm, imp, _, containers, _) = Build();
        var box = new StorageContainer { Id = 7, Name = "Box" };
        containers.Containers.Add(box);
        imp.OnResolve = _ => [Row(1, true)];
        vm.Load([("a.txt", "a"), ("b.txt", "b")]);

        vm.Files[0].SelectedLocation = box;            // file 0 → existing location
        Assert.False(vm.CanImport);                    // file 1 still unset
        vm.Files[1].TargetIsList = true;
        vm.Files[1].CreateNew = true;
        vm.Files[1].NewName = "New List";              // file 1 → new list
        Assert.True(vm.CanImport);
    }

    [Fact]
    public void Import_RoutesEachFileToItsOwnTarget_AndAggregatesSummary()
    {
        var (vm, imp, lists, containers, _) = Build();
        var box = new StorageContainer { Id = 7, Name = "Box" };
        containers.Containers.Add(box);
        imp.OnResolve = t => t == "a" ? [Row(4, true), Row(1, false)] : [Row(2, true)];
        vm.Load([("a.txt", "a"), ("b.txt", "b")]);

        vm.Files[0].SelectedLocation = box;                    // location target
        vm.Files[1].TargetIsList = true;
        vm.Files[1].CreateNew = true;
        vm.Files[1].NewName = "Fresh";                          // new-list target

        vm.ImportCommand.Execute(null);

        Assert.Single(imp.LocationCommits);
        Assert.Equal(7, imp.LocationCommits[0].Container.Id);
        Assert.Single(imp.ListCommits);
        var created = Assert.Single(lists.Lists);
        Assert.Equal("Fresh", created.Name);
        Assert.Equal(created.Id, imp.ListCommits[0].ListId);

        Assert.Equal(2, vm.Result!.FileCount);
        Assert.Equal(6, vm.Result.TotalAdded);                 // 4 + 2 resolved quantities
        Assert.Equal(1, vm.Result.TotalUnresolved);
        Assert.True(vm.Result.AnyListTarget);
        Assert.True(vm.Result.AnyLocationTarget);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~BatchDecklistImportViewModelTests" -v minimal`
Expected: FAIL to compile — types not defined.

- [ ] **Step 4: Create the summary types**

Create `OmniCard.Shared/Models/BatchDecklistImportSummary.cs`:

```csharp
namespace OmniCard.Models;

public record BatchFileResult(string FileName, string TargetName, int Added, int Unresolved);

public record BatchDecklistImportSummary(
    int FileCount,
    int TotalAdded,
    int TotalUnresolved,
    bool AnyListTarget,
    bool AnyLocationTarget,
    IReadOnlyList<BatchFileResult> Files);
```

- [ ] **Step 5: Create the per-file item**

Create `OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs`:

```csharp
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Views.BatchDecklistImport;

/// <summary>One decklist file in the batch: its resolved rows plus its chosen target.</summary>
public sealed partial class DecklistFileImport : ObservableObject
{
    public DecklistFileImport(
        string sourceName,
        IReadOnlyList<DecklistImportRow> rows,
        IReadOnlyList<CardList> availableLists,
        IReadOnlyList<StorageContainer> availableLocations)
    {
        SourceName = sourceName;
        DefaultNewName = Path.GetFileNameWithoutExtension(sourceName);
        NewName = DefaultNewName;
        AvailableLists = availableLists;
        AvailableLocations = availableLocations;
        foreach (var r in rows) Rows.Add(r);
        ResolvedCount = Rows.Count(r => r.IsResolved);
        UnresolvedCount = Rows.Count - ResolvedCount;
        SummaryLabel = $"{ResolvedCount} resolved · {UnresolvedCount} unresolved";
    }

    public string SourceName { get; }
    public string DefaultNewName { get; }
    public ObservableCollection<DecklistImportRow> Rows { get; } = [];
    public int ResolvedCount { get; }
    public int UnresolvedCount { get; }
    public string SummaryLabel { get; }
    public IReadOnlyList<CardList> AvailableLists { get; }
    public IReadOnlyList<StorageContainer> AvailableLocations { get; }
    public IReadOnlyList<ContainerType> LocationTypes { get; } = Enum.GetValues<ContainerType>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetIsLocation))]
    [NotifyPropertyChangedFor(nameof(TargetIsLocationEditable))]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial bool TargetIsList { get; set; }

    public bool TargetIsLocation => !TargetIsList;
    public bool TargetIsLocationEditable { get => !TargetIsList; set => TargetIsList = !value; }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasTarget))] public partial CardList? SelectedList { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasTarget))] public partial StorageContainer? SelectedLocation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseExistingTarget))]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial bool CreateNew { get; set; }

    public bool UseExistingTarget => !CreateNew;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasTarget))] public partial string NewName { get; set; } = "";
    [ObservableProperty] public partial ContainerType NewLocationType { get; set; } = ContainerType.Box;

    public bool HasTarget
    {
        get
        {
            if (CreateNew) return !string.IsNullOrWhiteSpace(NewName);
            return TargetIsList ? SelectedList is not null : SelectedLocation is not null;
        }
    }
}
```

- [ ] **Step 6: Create the batch view model**

Create `OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.BatchDecklistImport;

public sealed partial class BatchDecklistImportViewModel(
    IDecklistImportService importService,
    ICardService cardService,
    IListService listService,
    IStorageContainerService containerService) : ViewModel
{
    public ObservableCollection<DecklistFileImport> Files { get; } = [];
    public ObservableCollection<CardList> AvailableLists { get; } = [];
    public ObservableCollection<StorageContainer> AvailableLocations { get; } = [];

    [ObservableProperty] public partial DecklistFileImport? SelectedFile { get; set; }
    [ObservableProperty] public partial string HeaderLabel { get; set; } = "";

    public bool CanImport => Files.Count > 0 && Files.All(f => f.HasTarget);

    public BatchDecklistImportSummary? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    public void Load(IReadOnlyList<(string Name, string Text)> files)
    {
        var game = cardService.ActiveGameService.Game;

        AvailableLists.Clear();
        foreach (var l in listService.GetLists(game)) AvailableLists.Add(l);
        AvailableLocations.Clear();
        foreach (var c in containerService.GetAll()) AvailableLocations.Add(c);

        Files.Clear();
        foreach (var (name, text) in files)
        {
            var rows = importService.ResolveFile(text);
            var item = new DecklistFileImport(name, rows, AvailableLists, AvailableLocations);
            item.PropertyChanged += OnItemChanged;
            Files.Add(item);
        }

        SelectedFile = Files.FirstOrDefault();
        HeaderLabel = $"{Files.Count} files · {Files.Sum(f => f.ResolvedCount)} resolved · {Files.Sum(f => f.UnresolvedCount)} unresolved";
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

        Result = new BatchDecklistImportSummary(Files.Count, totalAdded, totalUnresolved, anyList, anyLocation, perFile);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~BatchDecklistImportViewModelTests" -v minimal`
Expected: PASS (3 tests). Then run the full suite to confirm no regression: `dotnet test OmniCard.Tests -v minimal`.

- [ ] **Step 8: Commit**

```bash
git add OmniCard.Shared/Models/BatchDecklistImportSummary.cs OmniCard/Views/BatchDecklistImport/DecklistFileImport.cs OmniCard/Views/BatchDecklistImport/BatchDecklistImportViewModel.cs OmniCard.Tests/Fakes/ImportFakes.cs OmniCard.Tests/ViewModels/BatchDecklistImportViewModelTests.cs
git commit -m "feat(import): batch decklist import view model (per-file targets)"
```

---

### Task 4: Batch dialog window + `IDialogService` + DI

**Files:**
- Create: `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml` (+ `.xaml.cs`)
- Modify: `OmniCard.Shared/Interfaces/IDialogService.cs`, `OmniCard/Services/DialogService.cs`, `OmniCard/App.xaml.cs`

**Interfaces:**
- Consumes: `BatchDecklistImportViewModel.Load/Result/CloseDialog`.
- Produces: `BatchDecklistImportSummary? IDialogService.ShowBatchDecklistImport(IReadOnlyList<(string Name, string Text)> files)`.

UI wiring — verified by build; no unit tests.

- [ ] **Step 1: Create the code-behind**

Create `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml.cs` (mirror `CsvImportView.xaml.cs` — resolve the `IView<T>` pattern that project uses; the minimal correct form):

```csharp
using System.Windows;

namespace OmniCard.Views.BatchDecklistImport;

public partial class BatchDecklistImportView : Window
{
    public BatchDecklistImportViewModel ViewModel { get; }

    public BatchDecklistImportView(BatchDecklistImportViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = this;
        ViewModel.CloseDialog = result => { DialogResult = result; Close(); };
    }
}
```

> Before writing, open `OmniCard/Views/CsvImport/CsvImportView.xaml.cs` and match its exact shape (it implements `IView<CsvImportViewModel>` with an explicit `IViewModel IView.ViewModel => ViewModel;`). Reproduce that shape here for `BatchDecklistImportViewModel` so DI/owner wiring is consistent.

- [ ] **Step 2: Create the XAML (master-detail)**

Create `OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml`. Follow `CsvImportView.xaml` window conventions; dark-theme rule (explicit `Foreground` on all text controls); Import button `IsEnabled="{Binding ViewModel.CanImport}"`. Left = files list with inline target editor; right = selected file's card grid.

```xml
<Window x:Class="OmniCard.Views.BatchDecklistImport.BatchDecklistImportView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="clr-namespace:OmniCard.Views.BatchDecklistImport"
        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
        mc:Ignorable="d"
        Title="Import Decklists" Height="620" Width="1000"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False"
        d:DataContext="{d:DesignInstance {x:Type local:BatchDecklistImportView}}"
        Background="{DynamicResource MaterialDesign.Brush.Background}"
        TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
        TextElement.FontWeight="Regular"
        TextElement.FontSize="13"
        FontFamily="{StaticResource AppFont}">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
    </Window.Resources>
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="{Binding ViewModel.HeaderLabel}" FontWeight="SemiBold" FontSize="14"
                   Margin="0,0,0,8" Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="440"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- Left: files with per-file target editor -->
            <ListBox Grid.Column="0" ItemsSource="{Binding ViewModel.Files}"
                     SelectedItem="{Binding ViewModel.SelectedFile}"
                     HorizontalContentAlignment="Stretch" Margin="0,0,8,0">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="2,4">
                            <TextBlock Text="{Binding SourceName}" FontWeight="SemiBold"
                                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                            <TextBlock Text="{Binding SummaryLabel}" FontSize="11"
                                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                            <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                <!-- No GroupName: WPF scopes radios to their item container (per row) automatically;
                                     an explicit GroupName would wrongly merge rows that share a filename. -->
                                <RadioButton Content="Location" IsChecked="{Binding TargetIsLocationEditable}"
                                             Margin="0,0,10,0"
                                             Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                                <RadioButton Content="List" IsChecked="{Binding TargetIsList}"
                                             Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                            </StackPanel>
                            <ComboBox Margin="0,4,0,0" DisplayMemberPath="Name"
                                      Visibility="{Binding TargetIsLocation, Converter={StaticResource BoolToVis}}"
                                      ItemsSource="{Binding AvailableLocations}" SelectedItem="{Binding SelectedLocation}"
                                      IsEnabled="{Binding UseExistingTarget}"
                                      Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                            <ComboBox Margin="0,4,0,0" DisplayMemberPath="Name"
                                      Visibility="{Binding TargetIsList, Converter={StaticResource BoolToVis}}"
                                      ItemsSource="{Binding AvailableLists}" SelectedItem="{Binding SelectedList}"
                                      IsEnabled="{Binding UseExistingTarget}"
                                      Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                            <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                <CheckBox Content="New" IsChecked="{Binding CreateNew}" VerticalAlignment="Center"
                                          Margin="0,0,6,0" Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                                <TextBox Width="150" Margin="0,0,6,0" Text="{Binding NewName, UpdateSourceTrigger=PropertyChanged}"
                                         IsEnabled="{Binding CreateNew}"
                                         Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                                <ComboBox Width="100" ItemsSource="{Binding LocationTypes}" SelectedItem="{Binding NewLocationType}"
                                          Visibility="{Binding TargetIsLocation, Converter={StaticResource BoolToVis}}"
                                          IsEnabled="{Binding CreateNew}"
                                          Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                            </StackPanel>
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Right: selected file's cards -->
            <DataGrid Grid.Column="1" ItemsSource="{Binding ViewModel.SelectedFile.Rows}"
                      AutoGenerateColumns="False" IsReadOnly="True">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Qty" Binding="{Binding Quantity}" Width="40"/>
                    <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="*"/>
                    <DataGridTextColumn Header="Set" Binding="{Binding ResolvedSet}" Width="70"/>
                    <DataGridTextColumn Header="#" Binding="{Binding ResolvedNumber}" Width="60"/>
                    <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="90"/>
                </DataGrid.Columns>
            </DataGrid>
        </Grid>

        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
            <Button Content="Import" Command="{Binding ViewModel.ImportCommand}"
                    IsEnabled="{Binding ViewModel.CanImport}" Padding="16,4" Margin="0,0,8,0"/>
            <Button Content="Cancel" Command="{Binding ViewModel.CancelCommand}" Padding="16,4" IsCancel="True"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: Add the `IDialogService` method + implementation**

`OmniCard.Shared/Interfaces/IDialogService.cs` — add (keep the old `ShowDecklistImport` for now; it is removed in Task 6):

```csharp
    BatchDecklistImportSummary? ShowBatchDecklistImport(IReadOnlyList<(string Name, string Text)> files);
```

`OmniCard/Services/DialogService.cs` — add (with `using OmniCard.Views.BatchDecklistImport;`):

```csharp
    public BatchDecklistImportSummary? ShowBatchDecklistImport(IReadOnlyList<(string Name, string Text)> files)
    {
        var wnd = Services.GetRequiredService<BatchDecklistImportView>();
        SetOwner(wnd);
        wnd.ViewModel.Load(files);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }
```

- [ ] **Step 4: Register in DI**

`OmniCard/App.xaml.cs` (near the CsvImport registrations), add `using OmniCard.Views.BatchDecklistImport;` and:

```csharp
            services.AddTransient<BatchDecklistImportView>();
            services.AddTransient<BatchDecklistImportViewModel>();
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build OmniCard/OmniCard.csproj -v minimal`
Expected: Build succeeded. Fix any XAML binding compile errors.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml OmniCard/Views/BatchDecklistImport/BatchDecklistImportView.xaml.cs OmniCard.Shared/Interfaces/IDialogService.cs OmniCard/Services/DialogService.cs OmniCard/App.xaml.cs
git commit -m "feat(import): batch decklist master-detail dialog + dialog service"
```

---

### Task 5: `ListsViewModel.Refresh()` + unified `Import()` command + menu

**Files:**
- Modify: `OmniCard/Views/Lists/ListsViewModel.cs`
- Modify: `OmniCard/Views/Root/RootViewModel.cs`
- Modify: `OmniCard/Views/Root/RootView.xaml`
- Test: `OmniCard.Tests/Services/ListsViewModelTests.cs` (add a `Refresh` test)

**Interfaces:**
- Consumes: `IDialogService.ShowImportPreview`/`ShowBatchDecklistImport`, `ImportFileClassifier.ClassifyFile`, `RootViewModel.LoadContainers`, `Collection.ShowCardList`/`LoadOverview`/`SearchCollection`, `Lists.Refresh`.
- Produces: `void ListsViewModel.Refresh()`; `RootViewModel.ImportCommand`.

- [ ] **Step 1: Write the failing `ListsViewModel.Refresh` test**

Add to `OmniCard.Tests/Services/ListsViewModelTests.cs`:

```csharp
    [Fact]
    public void Refresh_ReloadsLists_PreservingSelection()
    {
        var svc = new FakeListService();
        svc.Seed(new CardList { Id = 1, Name = "A", Game = CardGame.Mtg });
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), NullLogger<ListsViewModel>.Instance);
        vm.SetGame(CardGame.Mtg);
        vm.SelectedList = vm.Lists[0];

        // Another list is created out-of-band (e.g. by a batch import), then Refresh.
        svc.Seed(new CardList { Id = 2, Name = "B", Game = CardGame.Mtg });
        vm.Refresh();

        Assert.Equal(2, vm.Lists.Count);
        Assert.Equal(1, vm.SelectedList!.Id);   // selection preserved by id
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ListsViewModelTests.Refresh_ReloadsLists_PreservingSelection" -v minimal`
Expected: FAIL to compile — `Refresh` not defined.

- [ ] **Step 3: Add `ListsViewModel.Refresh()`**

In `OmniCard/Views/Lists/ListsViewModel.cs`, add a public method that reloads the lists (via the existing private `LoadLists()`) and re-selects the previously selected list by id:

```csharp
    /// <summary>Reload the lists for the current game, preserving the current selection by id.
    /// Call after an external change added/updated lists (e.g. a decklist import).</summary>
    public void Refresh()
    {
        var selectedId = SelectedList?.Id;
        LoadLists();   // clears SelectedList
        if (selectedId is int id)
            SelectedList = Lists.FirstOrDefault(l => l.Id == id);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ListsViewModelTests" -v minimal`
Expected: PASS.

- [ ] **Step 5: Replace the two import commands with a unified `Import()`**

In `OmniCard/Views/Root/RootViewModel.cs`, **delete** the `ImportCollection()` and `ImportDecklistFile()` `[RelayCommand]` methods and add (uses existing fields `csvService`, `dialogService`, `logger`, and properties `Collection`, `Lists`, `LoadContainers()`; add `using OmniCard.Collection;` if not already present):

```csharp
    [RelayCommand]
    public void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Import files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
            Title = "Import",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var csvFiles = new List<string>();
            var decklistFiles = new List<string>();
            var unknownFiles = new List<string>();
            foreach (var path in dialog.FileNames)
            {
                switch (ImportFileClassifier.ClassifyFile(path))
                {
                    case ImportKind.Csv: csvFiles.Add(path); break;
                    case ImportKind.Decklist: decklistFiles.Add(path); break;
                    default: unknownFiles.Add(path); break;
                }
            }

            var messages = new List<string>();
            var containersChanged = false;
            var listsChanged = false;

            // CSV collections first (each its own preview dialog).
            foreach (var path in csvFiles)
            {
                var preview = csvService.PreviewImport(path);
                var imported = dialogService.ShowImportPreview(preview);
                if (imported.HasValue)
                {
                    messages.Add($"Imported {imported.Value} cards from {System.IO.Path.GetFileName(path)}");
                    containersChanged = true;   // app-native CSV import can create containers
                }
            }

            // Decklists in one batch dialog.
            if (decklistFiles.Count > 0)
            {
                var files = decklistFiles
                    .Select(p => (Name: System.IO.Path.GetFileName(p), Text: System.IO.File.ReadAllText(p)))
                    .ToList();
                var summary = dialogService.ShowBatchDecklistImport(files);
                if (summary is not null)
                {
                    messages.Add($"Imported {summary.TotalAdded} cards across {summary.FileCount} file(s)"
                        + (summary.TotalUnresolved > 0 ? $"; {summary.TotalUnresolved} lines unresolved" : ""));
                    containersChanged |= summary.AnyLocationTarget;
                    listsChanged |= summary.AnyListTarget;
                }
            }

            if (unknownFiles.Count > 0)
                messages.Add($"Skipped {unknownFiles.Count} unrecognized file(s): "
                    + string.Join(", ", unknownFiles.Select(System.IO.Path.GetFileName)));

            // Refresh: container dropdowns + overview tiles (new locations), lists sidebar (new lists), card grid.
            if (containersChanged)
                LoadContainers();
            if (Collection.ShowCardList)
                _ = Collection.SearchCollection();
            else
                Collection.LoadOverview();
            if (listsChanged)
                Lists.Refresh();

            if (messages.Count > 0)
                Message = string.Join(" · ", messages);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import");
            System.Windows.MessageBox.Show($"Failed to import: {ex.Message}", "Import Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
```

- [ ] **Step 6: Update the menu + keybinding**

In `OmniCard/Views/Root/RootView.xaml`:
- Change the keybinding at line ~110 from `ImportCollectionCommand` to `ImportCommand`:
  ```xml
  <KeyBinding Gesture="Ctrl+I" Command="{Binding ViewModel.ImportCommand}"/>
  ```
- Replace the two import `MenuItem`s (the `_Import...` bound to `ImportCollectionCommand` and the `Import _Decklist File...` bound to `ImportDecklistFileCommand`) with a single item:
  ```xml
  <MenuItem Header="_Import..." Command="{Binding ViewModel.ImportCommand}" InputGestureText="Ctrl+I"/>
  ```

- [ ] **Step 7: Build + full suite**

Run: `dotnet build OmniCard/OmniCard.csproj -v minimal` — Expected: Build succeeded (no references to the deleted `ImportCollectionCommand`/`ImportDecklistFileCommand`).
Run: `dotnet test OmniCard.Tests -v minimal` — Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add OmniCard/Views/Lists/ListsViewModel.cs OmniCard/Views/Root/RootViewModel.cs OmniCard/Views/Root/RootView.xaml OmniCard.Tests/Services/ListsViewModelTests.cs
git commit -m "feat(import): unified auto-detecting Import command + post-import refresh"
```

---

### Task 6: Retire the single-file decklist dialog

**Files:**
- Delete: `OmniCard/Views/DecklistImport/DecklistImportView.xaml`, `OmniCard/Views/DecklistImport/DecklistImportView.xaml.cs`, `OmniCard/Views/DecklistImport/DecklistImportViewModel.cs`, `OmniCard.Shared/Models/DecklistImportSummary.cs`, `OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs`
- Modify: `OmniCard.Shared/Interfaces/IDialogService.cs`, `OmniCard/Services/DialogService.cs`, `OmniCard/App.xaml.cs`
- **Keep:** `OmniCard/Views/DecklistImport/DecklistImportRow.cs`

**Interfaces:** removes `IDialogService.ShowDecklistImport` and the old dialog's DI registrations. No new API.

- [ ] **Step 1: Delete the retired files**

```bash
git rm OmniCard/Views/DecklistImport/DecklistImportView.xaml \
       OmniCard/Views/DecklistImport/DecklistImportView.xaml.cs \
       OmniCard/Views/DecklistImport/DecklistImportViewModel.cs \
       OmniCard.Shared/Models/DecklistImportSummary.cs \
       OmniCard.Tests/ViewModels/DecklistImportViewModelTests.cs
```

(`DecklistImportRow.cs` stays — the service and batch VM still use it.)

- [ ] **Step 2: Remove the old dialog method + registrations**

- `OmniCard.Shared/Interfaces/IDialogService.cs`: delete the `DecklistImportSummary? ShowDecklistImport(...)` line.
- `OmniCard/Services/DialogService.cs`: delete the `ShowDecklistImport(...)` method and the `using OmniCard.Views.DecklistImport;` if it is now unused (leave it if `DecklistImportRow`'s namespace is still referenced there — it is not; the method used `DecklistImportView`).
- `OmniCard/App.xaml.cs`: delete the `services.AddTransient<DecklistImportView>();` and `services.AddTransient<DecklistImportViewModel>();` lines; remove the now-unused `using OmniCard.Views.DecklistImport;` only if nothing else in the file references that namespace.

- [ ] **Step 3: Build + full suite**

Run: `dotnet build OmniCard/OmniCard.csproj -v minimal` — Expected: Build succeeded (no dangling references to `DecklistImportView`/`ViewModel`/`ShowDecklistImport`/`DecklistImportSummary`).
Run: `dotnet test OmniCard.Tests -v minimal` — Expected: all green (the retired `DecklistImportViewModelTests` are gone; `DecklistImportServiceTests` + `BatchDecklistImportViewModelTests` cover the behavior).

- [ ] **Step 4: Manual smoke test (verify skill)**

Use the `verify`/`run` skill to launch the app and drive the new flow:
1. File ▸ Import… (Ctrl+I) → multi-select two or more decklist files (use the sample `first-flight-starter-commander-precon-decklist` twice, or several precons).
2. In the batch dialog: assign file 1 → a **new Location** (name pre-filled), file 2 → an **existing List** (or new List). Confirm Import stays disabled until every file has a target.
3. Confirm the selected file's cards show on the right; import; confirm the summary message.
4. Confirm the **new location appears immediately** in the collection's location list/overview and container dropdowns, and a new/updated **List** shows in the Lists tab — without restarting.
5. Select a single `.csv` collection file via the same Import… → confirm it opens the CSV preview dialog.
6. (Optional) Multi-select a CSV + a decklist → confirm CSV preview shows first, then the decklist batch.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(import): retire single-file decklist dialog (superseded by batch)"
```

---

## Full-suite check

- [ ] `dotnet test OmniCard.Tests -v minimal` — all green (incl. the new classifier, service, and batch VM tests; no regressions).
- [ ] `dotnet build -v minimal` (solution) — no broken references.
