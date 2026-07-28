using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Lists;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListsViewModelTests
{
    [Fact]
    public void SetGame_LoadsListsForThatGame()
    {
        var svc = new FakeListService();
        svc.Seed(new CardList { Id = 1, Name = "A", Game = CardGame.Mtg });
        svc.Seed(new CardList { Id = 2, Name = "B", Game = CardGame.Pokemon });
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), NullLogger<ListsViewModel>.Instance);

        vm.SetGame(CardGame.Mtg);

        Assert.Single(vm.Lists);
        Assert.Equal("A", vm.Lists[0].Name);
    }

    [Fact]
    public void CreateList_AddsAndSelects()
    {
        var svc = new FakeListService();
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), NullLogger<ListsViewModel>.Instance);
        vm.SetGame(CardGame.Mtg);
        vm.NewListName = "My List";

        vm.CreateListCommand.Execute(null);

        Assert.Single(vm.Lists);
        Assert.Equal("My List", vm.SelectedList!.Name);
        Assert.Equal("", vm.NewListName);
    }

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

    [Fact]
    public void RunSummaryReport_BuildsResult_AndInvokesExport()
    {
        var svc = new FakeListService();
        var list = new CardList { Id = 1, Name = "L", Game = CardGame.Mtg };
        svc.Seed(list);
        svc.Items[1] = new List<CardListItem>
        {
            new() { Id = 1, CardListId = 1, Quantity = 1, CardName = "Sol Ring" },
        };
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), NullLogger<ListsViewModel>.Instance);
        vm.SetGame(CardGame.Mtg);
        vm.SelectedList = vm.Lists[0];

        DecklistCheckResult? exported = null;
        vm.ExportPdf = r => exported = r;
        vm.RunSummaryReportCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Same(vm.Result, exported);
    }

    private sealed class FakeListService : IListService
    {
        private readonly List<CardList> _lists = [];
        public Dictionary<int, List<CardListItem>> Items { get; } = new();
        private int _nextId = 100;
        public void Seed(CardList l) => _lists.Add(l);

        public IReadOnlyList<CardList> GetLists(CardGame game) => _lists.Where(l => l.Game == game).ToList();
        public CardList CreateList(string name, CardGame game)
        {
            var l = new CardList { Id = _nextId++, Name = name, Game = game };
            _lists.Add(l); return l;
        }
        public void RenameList(int listId, string name) { }
        public void DeleteList(int listId) => _lists.RemoveAll(l => l.Id == listId);
        public IReadOnlyList<CardListItem> GetItems(int listId) => Items.TryGetValue(listId, out var v) ? v : [];
        public CardListItem AddPrinting(int listId, CardMatch p, bool foil, int qty, ListItemSource s)
            => new() { CardListId = listId, CardName = p.Name, Quantity = qty };
        public void RemoveItem(int itemId) { }
        public void SetQuantity(int itemId, int quantity) { }
        public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries)
            => new(entries.Count(), []);
        public void RefreshPrices(int listId) { }
        public List<DecklistEntry> ToDecklistEntries(int listId)
            => GetItems(listId).Select(i => new DecklistEntry(i.Quantity, i.CardName, i.SetCode, i.CollectorNumber)).ToList();
    }

    private sealed class FakeDecklistService : IDecklistService
    {
        public Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url)
            => Task.FromResult<(string, List<DecklistEntry>)?>(null);
        public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text)
            => ("Pasted", []);
        public List<DecklistEntry> ParseDecklistPrintings(string text)
            => [];
        public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game)
            => new() { DeckName = deckName, DeckSource = deckSource, OwnedEntries = [], MissingEntries = [] };
    }
}
