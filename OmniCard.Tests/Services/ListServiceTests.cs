using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;

    public ListServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection).Options;
        _dbFactory = new TestOmniDbFactory(options);
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestOmniDbFactory(DbContextOptions<OmniCardDbContext> options)
        : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public void CardList_And_Items_RoundTrip()
    {
        using (var ctx = _dbFactory.CreateDbContext())
        {
            var list = new CardList { Name = "Budget Deck", Game = CardGame.Mtg };
            ctx.CardLists.Add(list);
            ctx.SaveChanges();
            ctx.CardListItems.Add(new CardListItem
            {
                CardListId = list.Id, Quantity = 2, GameCardId = "abc",
                CardName = "Sol Ring", SetCode = "C21", AddedMarketPrice = 1.23m,
                Source = ListItemSource.Paste,
            });
            ctx.SaveChanges();
        }

        using (var ctx = _dbFactory.CreateDbContext())
        {
            var list = Assert.Single(ctx.CardLists.AsNoTracking().ToList());
            Assert.Equal("Budget Deck", list.Name);
            Assert.Equal(CardGame.Mtg, list.Game);
            var item = Assert.Single(ctx.CardListItems.AsNoTracking().ToList());
            Assert.Equal(2, item.Quantity);
            Assert.Equal(1.23m, item.AddedMarketPrice);
            Assert.Equal(ListItemSource.Paste, item.Source);
        }
    }

    private ListService CreateService(FakeCardService? cards = null)
        => new(_dbFactory, cards ?? new FakeCardService());

    [Fact]
    public void CreateList_Then_GetLists_FiltersByGame()
    {
        var svc = CreateService();
        svc.CreateList("MTG list", CardGame.Mtg);
        svc.CreateList("PKM list", CardGame.Pokemon);

        var mtg = svc.GetLists(CardGame.Mtg);
        Assert.Single(mtg);
        Assert.Equal("MTG list", mtg[0].Name);
    }

    [Fact]
    public void RenameList_UpdatesName()
    {
        var svc = CreateService();
        var list = svc.CreateList("old", CardGame.Mtg);
        svc.RenameList(list.Id, "new");
        Assert.Equal("new", svc.GetLists(CardGame.Mtg).Single().Name);
    }

    [Fact]
    public void DeleteList_RemovesListAndItems()
    {
        var svc = CreateService();
        var list = svc.CreateList("L", CardGame.Mtg);
        svc.AddPrinting(list.Id, new CardMatch { Name = "Sol Ring", GameSpecificId = "x" },
            isFoil: false, quantity: 1, ListItemSource.Manual);

        svc.DeleteList(list.Id);

        Assert.Empty(svc.GetLists(CardGame.Mtg));
        using var ctx = _dbFactory.CreateDbContext();
        Assert.Empty(ctx.CardListItems.AsNoTracking().ToList());
    }

    [Fact]
    public void AddPrinting_CapturesPrice_AndMergesDuplicate()
    {
        var cards = new FakeCardService();
        cards.Game.Prices["x"] = 2.50m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);
        var match = new CardMatch { Name = "Sol Ring", GameSpecificId = "x", SetCode = "C21", CollectorNumber = "1" };

        svc.AddPrinting(list.Id, match, isFoil: false, quantity: 1, ListItemSource.Manual);
        svc.AddPrinting(list.Id, match, isFoil: false, quantity: 2, ListItemSource.Manual);

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal(3, item.Quantity);          // merged
        Assert.Equal(2.50m, item.AddedMarketPrice);
        Assert.Equal("Sol Ring", item.CardName);
    }

    [Fact]
    public void SetQuantity_Zero_RemovesItem()
    {
        var svc = CreateService();
        var list = svc.CreateList("L", CardGame.Mtg);
        var item = svc.AddPrinting(list.Id, new CardMatch { Name = "A", GameSpecificId = "x" },
            false, 1, ListItemSource.Manual);
        svc.SetQuantity(item.Id, 0);
        Assert.Empty(svc.GetItems(list.Id));
    }

    private static CardMatch Printing(string name, string id, string set, string cn = "1")
        => new() { Name = name, GameSpecificId = id, SetCode = set, CollectorNumber = cn };

    [Fact]
    public void AddCardsByName_PicksCheapestNonFoilPrinting()
    {
        var cards = new FakeCardService();
        cards.Game.Printings.Add(Printing("Sol Ring", "a", "C16"));
        cards.Game.Printings.Add(Printing("Sol Ring", "b", "C21"));
        cards.Game.Prices["a"] = 5.00m;
        cards.Game.Prices["b"] = 1.50m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);

        var result = svc.AddCardsByName(list.Id, new[] { new DecklistEntry(1, "Sol Ring", null, null) });

        Assert.Equal(1, result.AddedCount);
        Assert.Empty(result.UnresolvedNames);
        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal("b", item.GameCardId);        // cheapest
        Assert.Equal(1.50m, item.AddedMarketPrice);
        Assert.False(item.IsUnpriced);
    }

    [Fact]
    public void AddCardsByName_NoPrice_FallsBackToFirst_AndFlagsUnpriced()
    {
        var cards = new FakeCardService();
        cards.Game.Printings.Add(Printing("Rare Card", "a", "SET"));
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);

        svc.AddCardsByName(list.Id, new[] { new DecklistEntry(1, "Rare Card", null, null) });

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal("a", item.GameCardId);
        Assert.Null(item.AddedMarketPrice);
        Assert.True(item.IsUnpriced);
    }

    [Fact]
    public void AddCardsByName_UnknownCard_ReportedUnresolved()
    {
        var svc = CreateService(new FakeCardService());
        var list = svc.CreateList("L", CardGame.Mtg);

        var result = svc.AddCardsByName(list.Id, new[] { new DecklistEntry(1, "Ghost", null, null) });

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(new[] { "Ghost" }, result.UnresolvedNames);
        Assert.Empty(svc.GetItems(list.Id));
    }

    [Fact]
    public void AddCardsByName_MergesQuantityForSameResolvedPrinting()
    {
        var cards = new FakeCardService();
        cards.Game.Printings.Add(Printing("Island", "isl", "SET"));
        cards.Game.Prices["isl"] = 0.10m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);

        svc.AddCardsByName(list.Id, new[]
        {
            new DecklistEntry(3, "Island", null, null),
            new DecklistEntry(2, "Island", null, null),
        });

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal(5, item.Quantity);
    }

    [Fact]
    public void ToDecklistEntries_ProjectsQuantityAndSet()
    {
        var cards = new FakeCardService();
        cards.Game.Prices["x"] = 1m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);
        svc.AddPrinting(list.Id, Printing("Sol Ring", "x", "C21", "1"), false, 2, ListItemSource.Manual);

        var entries = svc.ToDecklistEntries(list.Id);

        var e = Assert.Single(entries);
        Assert.Equal(2, e.Quantity);
        Assert.Equal("Sol Ring", e.CardName);
        Assert.Equal("C21", e.SetCode);
    }

    [Fact]
    public void RefreshPrices_Manual_UpdatesPriceKeepsPrinting()
    {
        var cards = new FakeCardService();
        cards.Game.Prices["x"] = 1.00m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);
        svc.AddPrinting(list.Id, Printing("Sol Ring", "x", "C21"), false, 1, ListItemSource.Manual);

        cards.Game.Prices["x"] = 3.00m;
        svc.RefreshPrices(list.Id);

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal("x", item.GameCardId);           // printing unchanged
        Assert.Equal(3.00m, item.AddedMarketPrice);
    }

    [Fact]
    public void RefreshPrices_Paste_ReResolvesCheapest()
    {
        var cards = new FakeCardService();
        cards.Game.Printings.Add(Printing("Sol Ring", "a", "C16"));
        cards.Game.Printings.Add(Printing("Sol Ring", "b", "C21"));
        cards.Game.Prices["a"] = 5m; cards.Game.Prices["b"] = 2m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);
        svc.AddCardsByName(list.Id, new[] { new DecklistEntry(1, "Sol Ring", null, null) }); // picks "b" @2

        cards.Game.Prices["a"] = 1m; // now "a" is cheapest
        svc.RefreshPrices(list.Id);

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal("a", item.GameCardId);
        Assert.Equal(1m, item.AddedMarketPrice);
    }

    private class FakeCardService : ICardService
    {
        public ObservableCollection<ScannedCard> ScannedCards { get; } = [];
        public CardGame SelectedGame { get; set; }
        public HashSet<string>? SelectedSetFilter { get; set; }
        public bool DefaultIsFoil { get; set; }
        public decimal? DefaultPurchasePrice { get; set; }
        public IReadOnlyList<CardGame> AvailableGames => [];
        public ICardGameService ActiveGameService => null!;
        public Action<HashStageResult>? OnHashStage { get; set; }
        public ulong LastComputedHash => 0;
        public FakeGameService Game { get; } = new();
        public ICardGameService GetGameService(CardGame game) => Game;
        public void AddFromStream(Stream stream) { }
        public void ReprocessScans() { }
        public void CommitScans(IEnumerable<ScannedCard> scannedCards) { }
        public void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null) { }
        public void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results) { }
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results) { }
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results) { }
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, ObservableCollection<CollectionCard> results) { }
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, int skip, int take, ObservableCollection<CollectionCard> results) { }
        public int GetSearchCount(string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset, bool stacked) => 0;
        public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter = null) => [];
        public void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null) { }
        public void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update) { }
        public List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds) => [];
        public void UpdateCollectionCard(CollectionCard card) { }
        public void DeleteCollectionCard(int id) { }
        public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame? game, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public IReadOnlyDictionary<string, decimal> GetCurrentPrices(CardGame game, IEnumerable<string> gameCardIds, bool foil) => new Dictionary<string, decimal>();
        public List<string> GetDistinctFieldValues(string field, CardGame game) => [];
        public List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode) => [];
        public void RemoveTempFile(ScannedCard card) { }
        public void ClearTempFiles() { }
        public void StartNewDiagnosticSession() { }
        public (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs() => (0, 0, 0);
        public (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null) => (0, 0);
        public void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section) { }
        public int ImportCollectionCards(IEnumerable<CollectionCard> cards, bool skipDuplicates) => 0;
        public ulong ComputeHashFromStream(System.IO.Stream stream) => 0;
        public ulong ComputeEdgeHashFromStream(System.IO.Stream stream) => 0;
        public IOcrMatchingService OcrService => null!;
        public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => (null, CardGame.Mtg);
    }

    private class FakeGameService : ICardGameService
    {
        public List<CardMatch> Printings { get; } = [];
        public Dictionary<string, decimal> Prices { get; } = new();

        public CardGame Game => CardGame.Mtg;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => Printings.Where(p => p.Name == cardName).ToList();
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => Prices.TryGetValue(gameCardId, out var v) ? v : null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) =>
            gameCardIds.Where(Prices.ContainsKey).ToDictionary(id => id, id => Prices[id]);
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public object? FindCardById(string gameCardId) => null;
    }
}
