using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Models;
using OmniCard.Tests.Fakes;
using OmniCard.Views.DecklistImport;
using Xunit;

namespace OmniCard.Tests.ViewModels;

public class DecklistImportViewModelTests
{
    private static CardMatch M(string id, string set, string cn) =>
        new() { GameSpecificId = id, Name = "Island", SetCode = set, CollectorNumber = cn };

    private static (DecklistImportViewModel vm, ConfigurableGameService gs, RecordingListService lists,
        RecordingContainerService containers, RecordingCardService cards, FakeDecklistParseService decks) Build()
    {
        var gs = new ConfigurableGameService();
        var cards = new RecordingCardService(gs);
        var lists = new RecordingListService();
        var containers = new RecordingContainerService();
        var decks = new FakeDecklistParseService();
        var vm = new DecklistImportViewModel(decks, cards, lists, containers, NullLogger<DecklistImportViewModel>.Instance);
        return (vm, gs, lists, containers, cards, decks);
    }

    [Fact]
    public void Load_ResolvesRows_AndCountsResolvedVsUnresolved()
    {
        var (vm, gs, _, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings =
        [
            new DecklistEntry(4, "Island", "SCD", "337"),
            new DecklistEntry(1, "Nonesuch", "SCD", "999"),
        ];
        gs.OnSearchCards = (q, _) => q.Contains("337") ? [M("a", "SCD", "337")] : [];

        vm.Load("deck.txt", "ignored", defaultContainerId: null);

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(1, vm.ResolvedCount);
        Assert.Equal(1, vm.UnresolvedCount);
        Assert.True(vm.Rows[0].IsResolved);
        Assert.False(vm.Rows[1].IsResolved);
    }

    [Fact]
    public void Load_DefaultsToGivenLocation_WhenProvided()
    {
        var (vm, _, _, containers, _, decks) = Build();
        containers.Containers.Add(new StorageContainer { Id = 7, Name = "Deck Box" });
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };

        vm.Load("deck.txt", "ignored", defaultContainerId: 7);

        Assert.False(vm.TargetIsList);
        Assert.Equal(7, vm.SelectedLocation!.Id);
    }

    [Fact]
    public void Load_DefaultsToBulk_WhenNoLocationProvided()
    {
        var (vm, _, _, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        containers.Containers.Add(containers.Bulk);

        vm.Load("deck.txt", "ignored", defaultContainerId: null);

        Assert.False(vm.TargetIsList);
        Assert.Equal(1, vm.SelectedLocation!.Id);
    }

    [Fact]
    public void CanImport_False_WhenNoResolvedRows()
    {
        var (vm, gs, _, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(1, "Nonesuch", "SCD", "999")];
        gs.OnSearchCards = (_, _) => [];

        vm.Load("deck.txt", "ignored", null);

        Assert.False(vm.CanImport);
    }

    [Fact]
    public void CanImport_False_WhenCreateNewButNameBlank()
    {
        var (vm, gs, _, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(1, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a", "SCD", "337")];
        vm.Load("deck.txt", "ignored", null);

        vm.CreateNew = true;
        vm.NewName = "   ";

        Assert.False(vm.CanImport);
    }
}
