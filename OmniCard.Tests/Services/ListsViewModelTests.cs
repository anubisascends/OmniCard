using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Tests.Fakes;
using OmniCard.Views.Lists;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListsViewModelTests
{
    [Fact]
    public void MoveSelectedListToLocation_CommitsAndReloadsLists()
    {
        var svc = new FakeListService();
        var list = new CardList { Id = 1, Name = "L", Game = CardGame.Mtg };
        svc.Seed(list);
        var container = new StorageContainer { Id = 5, Name = "Binder A", ContainerType = ContainerType.Binder };
        svc.CommitResult = new CommitToLocationResult(3, 0, true);
        var dialogService = new FakeDialogService
        {
            MoveListResult = new MoveListToLocationResult { ExistingContainer = container, Condition = "NM" },
        };
        var containerService = new RecordingContainerService();
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), dialogService, containerService, NullLogger<ListsViewModel>.Instance);
        vm.SetGame(CardGame.Mtg);
        vm.SelectedList = vm.Lists[0];

        vm.MoveSelectedListToLocationCommand.Execute(null);

        var call = Assert.Single(svc.CommitToLocationCalls);
        Assert.Equal((list.Id, container, "NM"), call);
        Assert.Contains("Moved 3 cards", vm.StatusMessage);
        Assert.Empty(vm.Lists); // list was deleted server-side; Refresh/LoadLists reflects that
    }

    [Fact]
    public void MoveSelectedListToLocation_DialogCancelled_DoesNothing()
    {
        var svc = new FakeListService();
        svc.Seed(new CardList { Id = 1, Name = "L", Game = CardGame.Mtg });
        var dialogService = new FakeDialogService { MoveListResult = null };
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), dialogService, new RecordingContainerService(), NullLogger<ListsViewModel>.Instance);
        vm.SetGame(CardGame.Mtg);
        vm.SelectedList = vm.Lists[0];

        vm.MoveSelectedListToLocationCommand.Execute(null);

        Assert.Empty(svc.CommitToLocationCalls);
    }

    [Fact]
    public void SetGame_LoadsListsForThatGame()
    {
        var svc = new FakeListService();
        svc.Seed(new CardList { Id = 1, Name = "A", Game = CardGame.Mtg });
        svc.Seed(new CardList { Id = 2, Name = "B", Game = CardGame.Pokemon });
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), null!, null!, NullLogger<ListsViewModel>.Instance);

        vm.SetGame(CardGame.Mtg);

        Assert.Single(vm.Lists);
        Assert.Equal("A", vm.Lists[0].Name);
    }

    [Fact]
    public void CreateList_AddsAndSelects()
    {
        var svc = new FakeListService();
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), null!, null!, NullLogger<ListsViewModel>.Instance);
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
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), null!, null!, NullLogger<ListsViewModel>.Instance);
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
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), null!, null!, NullLogger<ListsViewModel>.Instance);
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
        public CardListItem AddPrinting(int listId, CardMatch p, bool foil, string? foilType, int qty, ListItemSource s)
            => new() { CardListId = listId, CardName = p.Name, Quantity = qty, IsFoil = foil, FoilType = foilType };
        public void RemoveItem(int itemId) { }
        public void SetQuantity(int itemId, int quantity) { }
        public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries)
            => new(entries.Count(), []);
        public void RefreshPrices(int listId) { }
        public List<DecklistEntry> ToDecklistEntries(int listId)
            => GetItems(listId).Select(i => new DecklistEntry(i.Quantity, i.CardName, i.SetCode, i.CollectorNumber)).ToList();

        public List<(int ListId, StorageContainer Container, string Condition)> CommitToLocationCalls { get; } = [];
        public CommitToLocationResult CommitResult { get; set; } = new(0, 0, false);
        public CommitToLocationResult CommitToLocation(int listId, StorageContainer container, string condition)
        {
            CommitToLocationCalls.Add((listId, container, condition));
            if (CommitResult.ListDeleted) _lists.RemoveAll(l => l.Id == listId);
            return CommitResult;
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public MoveListToLocationResult? MoveListResult { get; set; }
        public MoveListToLocationResult? PickMoveListToLocation() => MoveListResult;

        public (bool Connected, bool SetAsDefault) ConnectToScanner() => throw new NotImplementedException();
        public bool? ConnectToEbay() => throw new NotImplementedException();
        public void ShowCard(ScannedCard card) => throw new NotImplementedException();
        public bool IsCardPreviewOpen => throw new NotImplementedException();
        public void UpdateCardPreview(ScannedCard? card) => throw new NotImplementedException();
        public bool? EditCollectionCard(CollectionCard card) => throw new NotImplementedException();
        public void ManageStorageContainers() => throw new NotImplementedException();
        public int? ShowImportPreview(CsvImportPreview preview) => throw new NotImplementedException();
        public bool OpenSortFilterBuilder(CardGame game) => throw new NotImplementedException();
        public IReadOnlyList<string>? OpenSetFilterBuilder(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter) => throw new NotImplementedException();
        public void ShowSettings() => throw new NotImplementedException();
        public int? PickCoverArt(int containerId, string containerName) => throw new NotImplementedException();
        public MoveToLocationResult? PickMoveToLocation() => throw new NotImplementedException();
        public void ShowBinderView(int containerId) => throw new NotImplementedException();
        public (int InsertIndex, bool DoubleSided)? InsertBinderPage(int containerId, int? nearPage) => throw new NotImplementedException();
        public int? MoveBinderPage(int containerId, int movingSheetIndex) => throw new NotImplementedException();
        public IReadOnlyList<ScanListTargetResult>? PickListTargetsForScans(IReadOnlyList<(CardGame Game, int Count)> groups, string defaultName) => throw new NotImplementedException();
        public void ShowAuditReport(AuditReport report) => throw new NotImplementedException();
        public bool? OpenEbayListingDialog(CollectionCard card) => throw new NotImplementedException();
        public bool? OpenEbayListingDialog(Product product, int lotId, decimal? suggestedPrice) => throw new NotImplementedException();
        public bool? OpenManualAdd(StorageContainer? defaultContainer = null) => throw new NotImplementedException();
        public bool? OpenManualAddToSlot(int containerId, int page, int slot) => throw new NotImplementedException();
        public void ShowDecklistCheck() => throw new NotImplementedException();
        public Product? EditProduct(Product? existing) => throw new NotImplementedException();
        public (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? AddLotDialog(int productId) => throw new NotImplementedException();
        public (int Quantity, decimal? UnitCost, int? LocationId, string? Source, DateTime AcquisitionDate)? EditLotDialog(InventoryLot lot) => throw new NotImplementedException();
        public bool OpenUnitsDialog(Product product) => throw new NotImplementedException();
        public bool OpenUnitsDialog(Product product, int? preselectLotId) => throw new NotImplementedException();
        public void OpenMovementHistory() => throw new NotImplementedException();
        public void OpenLogViewer() => throw new NotImplementedException();
        public ListForSaleResult? PickListForSale(decimal suggestedPrice) => throw new NotImplementedException();
        public TradeSummary? PickTrade() => throw new NotImplementedException();
        public int ShowTcgOrderImportPreview(TcgOrderImportPreview preview) => throw new NotImplementedException();
        public bool Confirm(string message, string title) => throw new NotImplementedException();
        public BatchDecklistImportSummary? ShowBatchDecklistImport() => throw new NotImplementedException();
        public string? RequireReason(string title, string message) => throw new NotImplementedException();
        public void ManageTags() => throw new NotImplementedException();
        public (CardGame Game, int? ContainerId)? ShowTopValueCards() => throw new NotImplementedException();
        public void ShowAbout() => throw new NotImplementedException();
        public void ShowDocumentation() => throw new NotImplementedException();
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
