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
