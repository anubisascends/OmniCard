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

    [Fact]
    public void Import_CreateNewLocation_CreatesContainer_AndCommitsToIt()
    {
        var (vm, imp, _, containers, _) = Build();
        imp.OnResolve = _ => [Row(3, true)];
        vm.Load([("a.txt", "a")]);

        vm.Files[0].TargetIsList = false;
        vm.Files[0].CreateNew = true;
        vm.Files[0].NewName = "New Binder";
        vm.Files[0].NewLocationType = ContainerType.Binder;

        vm.ImportCommand.Execute(null);

        var created = Assert.Single(containers.Created);
        Assert.Equal("New Binder", created.Name);
        Assert.Equal(ContainerType.Binder, created.Type);

        Assert.Single(imp.LocationCommits);
        Assert.Equal(created.Name, imp.LocationCommits[0].Container.Name);
        Assert.Equal(ContainerType.Binder, imp.LocationCommits[0].Container.ContainerType);

        Assert.True(vm.Result!.AnyLocationTarget);
        Assert.False(vm.Result.AnyListTarget);
    }
}
