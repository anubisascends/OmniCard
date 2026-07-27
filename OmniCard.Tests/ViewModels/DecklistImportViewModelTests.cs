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

    [Fact]
    public void Import_ToExistingList_CallsAddPrinting_WithFileSource_NonFoil()
    {
        var (vm, gs, lists, containers, cards, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        lists.Lists.Add(new CardList { Id = 42, Name = "My Deck", Game = CardGame.Mtg });
        decks.Printings =
        [
            new DecklistEntry(4, "Island", "SCD", "337"),
            new DecklistEntry(1, "Nonesuch", "SCD", "999"),
        ];
        gs.OnSearchCards = (q, _) => q.Contains("337") ? [M("a", "SCD", "337")] : [];
        vm.Load("deck.txt", "ignored", null);

        vm.TargetIsList = true;
        vm.SelectedList = vm.AvailableLists.Single(l => l.Id == 42);
        vm.ImportCommand.Execute(null);

        var call = Assert.Single(lists.Printings);
        Assert.Equal(42, call.ListId);
        Assert.Equal("a", call.Printing.GameSpecificId);
        Assert.Equal(4, call.Quantity);
        Assert.False(call.IsFoil);
        Assert.Equal(ListItemSource.File, call.Source);
        Assert.Equal(4, vm.Result!.Added);          // resolved quantity
        Assert.Equal(1, vm.Result.Unresolved);       // one line unresolved
        Assert.Equal("My Deck", vm.Result.TargetName);
    }

    [Fact]
    public void Import_ToExistingLocation_CallsAddCardToCollection_NearMint_NonFoil_NoPrice()
    {
        var (vm, gs, _, containers, cards, decks) = Build();
        var box = new StorageContainer { Id = 7, Name = "Deck Box" };
        containers.Containers.Add(box);
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(3, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a", "SCD", "337")];
        vm.Load("deck.txt", "ignored", defaultContainerId: 7);

        vm.ImportCommand.Execute(null);

        var call = Assert.Single(cards.Added);
        Assert.Equal("a", call.Match.GameSpecificId);
        Assert.Equal("Near Mint", call.Condition);
        Assert.False(call.IsFoil);
        Assert.Null(call.PurchasePrice);
        Assert.Equal(3, call.Quantity);
        Assert.Equal(7, call.Container!.Id);
        Assert.Equal(3, vm.Result!.Added);
        Assert.Equal("Deck Box", vm.Result.TargetName);
    }

    [Fact]
    public void Import_CreateNewList_CreatesThenPopulates()
    {
        var (vm, gs, lists, containers, _, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(1, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a", "SCD", "337")];
        vm.Load("deck.txt", "ignored", null);

        vm.TargetIsList = true;
        vm.CreateNew = true;
        vm.NewName = "Fresh List";
        vm.ImportCommand.Execute(null);

        var created = Assert.Single(lists.Lists);
        Assert.Equal("Fresh List", created.Name);
        var call = Assert.Single(lists.Printings);
        Assert.Equal(created.Id, call.ListId);
        Assert.Equal("Fresh List", vm.Result!.TargetName);
    }

    [Fact]
    public void Import_CreateNewLocation_CreatesWithTypeThenPopulates()
    {
        var (vm, gs, _, containers, cards, decks) = Build();
        containers.Bulk = new StorageContainer { Id = 1, Name = "Bulk", IsSystem = true };
        decks.Printings = [new DecklistEntry(2, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a", "SCD", "337")];
        vm.Load("deck.txt", "ignored", null);

        vm.TargetIsList = false;
        vm.CreateNew = true;
        vm.NewName = "New Binder";
        vm.NewLocationType = ContainerType.Binder;
        vm.ImportCommand.Execute(null);

        Assert.Contains(("New Binder", ContainerType.Binder), containers.Created);
        var call = Assert.Single(cards.Added);
        Assert.Equal("New Binder", call.Container!.Name);
        Assert.Equal("New Binder", vm.Result!.TargetName);
    }
}
