using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Tests.Fakes;
using OmniCard.Views.DeckBoxSync;
using Xunit;

namespace OmniCard.Tests.ViewModels;

public class DeckBoxSyncViewModelTests
{
    private sealed class FakeSyncService : IDeckBoxSyncService
    {
        public DeckBoxSyncPlan Plan { get; set; } = new()
        {
            DeckBoxId = 1, DeckBoxName = "Deck A", Cuts = [], Adds = [], KeepCount = 0,
        };
        public DeckBoxSyncCommitRequest? Applied { get; private set; }

        public DeckBoxSyncPlan BuildPlan(int deckBoxId, List<DecklistEntry> targetEntries, CardGame game) => Plan;
        public void ApplySync(DeckBoxSyncCommitRequest request) => Applied = request;
    }

    private static StorageContainer DeckBox(int id = 1) => new() { Id = id, Name = "Deck A", ContainerType = ContainerType.DeckBox };

    private static DeckBoxSyncViewModel CreateVm(FakeSyncService sync, FakeDecklistParseService decklist, out RecordingContainerService containers)
    {
        containers = new RecordingContainerService();
        containers.Containers.Add(DeckBox());
        containers.Containers.Add(new StorageContainer { Id = 2, Name = "Box C", ContainerType = ContainerType.Box });
        return new DeckBoxSyncViewModel(decklist, sync, containers, NullLogger<DeckBoxSyncViewModel>.Instance);
    }

    [Fact]
    public void Load_ExcludesDeckBoxFromLocations_AndHasNoPlanYet()
    {
        var vm = CreateVm(new FakeSyncService(), new FakeDecklistParseService(), out _);
        vm.Load(DeckBox());

        Assert.False(vm.HasPlan);
        Assert.False(vm.CanCommit);
        Assert.DoesNotContain(vm.AvailableLocations, c => c.Id == 1); // the deck box itself
        Assert.Contains(vm.AvailableLocations, c => c.Id == 2);
    }

    [Fact]
    public void ParsePasted_BuildsPlan_EnablesCommit()
    {
        var sync = new FakeSyncService
        {
            Plan = new DeckBoxSyncPlan { DeckBoxId = 1, DeckBoxName = "Deck A", KeepCount = 1, Cuts = [], Adds = [] },
        };
        var decklist = new FakeDecklistParseService { Printings = [new DecklistEntry(1, "Sol Ring", null, null)] };
        var vm = CreateVm(sync, decklist, out _);
        vm.Load(DeckBox());

        vm.PasteText = "1 Sol Ring";
        vm.ParsePastedCommand.Execute(null);

        Assert.True(vm.HasPlan);
        Assert.True(vm.CanCommit);
    }

    [Fact]
    public void CanCommit_FalseWhenCutRowSetToMoveWithoutLocation()
    {
        var sync = new FakeSyncService
        {
            Plan = new DeckBoxSyncPlan
            {
                DeckBoxId = 1, DeckBoxName = "Deck A", KeepCount = 0, Adds = [],
                Cuts = [new DeckBoxCutRow(7, "Llanowar Elves", "cmm", false, 1, null, null)],
            },
        };
        var decklist = new FakeDecklistParseService { Printings = [new DecklistEntry(1, "Sol Ring", null, null)] };
        var vm = CreateVm(sync, decklist, out _);
        vm.Load(DeckBox());
        vm.PasteText = "x";
        vm.ParsePastedCommand.Execute(null);

        Assert.True(vm.CanCommit); // defaults to sideboard = resolved

        vm.Cuts[0].MoveToEditable = true; // switch to "Move to" with no location chosen
        Assert.False(vm.CanCommit);

        vm.Cuts[0].SelectedLocation = vm.AvailableLocations.First();
        Assert.True(vm.CanCommit);
    }

    [Fact]
    public void Commit_BuildsRequest_FromCutAndAddDecisions()
    {
        var sync = new FakeSyncService
        {
            Plan = new DeckBoxSyncPlan
            {
                DeckBoxId = 1, DeckBoxName = "Deck A", KeepCount = 0,
                Cuts = [new DeckBoxCutRow(7, "Llanowar Elves", "cmm", false, 1, null, null)],
                Adds =
                [
                    new DeckBoxAddRow("Brainstorm", "mmq", "60", 2,
                        [new DeckBoxAddSource(5, 9, "Bulk", 4, "mmq", false, true)], null, null),
                ],
            },
        };
        var decklist = new FakeDecklistParseService { Printings = [new DecklistEntry(1, "Sol Ring", null, null)] };
        var vm = CreateVm(sync, decklist, out _);
        vm.Load(DeckBox());
        vm.PasteText = "x";
        vm.ParsePastedCommand.Execute(null);

        var closed = false;
        vm.CloseDialog = r => closed = r;
        vm.CommitCommand.Execute(null);

        Assert.True(vm.DidCommit);
        Assert.True(closed);
        Assert.NotNull(sync.Applied);
        // Sideboard cut (default) → sideboard decision on lot 7.
        var cut = Assert.Single(sync.Applied!.Cuts);
        Assert.Equal(7, cut.LotId);
        Assert.True(cut.Sideboard);
        // Add pulls min(needed 2, available 4) = 2 from source lot 5.
        var add = Assert.Single(sync.Applied.Adds);
        Assert.Equal(5, add.SourceLotId);
        Assert.Equal(2, add.Quantity);
    }

    [Fact]
    public async Task Fetch_NullResult_ShowsMessage_NoPlan()
    {
        var decklist = new FakeDecklistParseService { OnFetch = _ => null };
        var vm = CreateVm(new FakeSyncService(), decklist, out _);
        vm.Load(DeckBox());

        vm.UrlText = "https://moxfield.com/decks/bad";
        await vm.FetchCommand.ExecuteAsync(null);

        Assert.False(vm.HasPlan);
        Assert.Contains("Couldn't fetch", vm.StatusMessage);
    }
}
