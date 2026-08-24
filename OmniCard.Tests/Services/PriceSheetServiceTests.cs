using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class PriceSheetServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;
    private readonly StubCardService _cardService = new();

    public PriceSheetServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private PriceSheetService CreateService() => new(_factory, _cardService);

    private int CreateContainer(string name = "Box")
    {
        using var ctx = _factory.CreateDbContext();
        var container = new StorageContainer { Name = name, ContainerType = ContainerType.Box };
        ctx.StorageContainers.Add(container);
        ctx.SaveChanges();
        return container.Id;
    }

    private void AddSingleLot(int containerId, string gameCardId, string name, CardGame game,
        string setCode, string collectorNumber, bool foil = false, int quantity = 1)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product
        {
            Game = game,
            Category = ProductCategory.Single,
            GameCardId = gameCardId,
            Name = name,
            SetCode = setCode,
            SetName = setCode,
            CollectorNumber = collectorNumber,
            Foil = foil,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, LocationId = containerId, Quantity = quantity });
        ctx.SaveChanges();
    }

    private void AddSealedLot(int containerId, string name, CardGame game, string? setName,
        decimal? lastMarketPrice, int quantity = 1)
    {
        using var ctx = _factory.CreateDbContext();
        var product = new Product
        {
            Game = game,
            Category = ProductCategory.Box,
            Name = name,
            SetName = setName,
            LastMarketPrice = lastMarketPrice,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, LocationId = containerId, Quantity = quantity });
        ctx.SaveChanges();
    }

    [Fact]
    public void BuildReport_ExpandsLotQuantityIntoRepeatedLines()
    {
        var containerId = CreateContainer();
        AddSingleLot(containerId, "a", "Lightning Bolt", CardGame.Mtg, "LEA", "1", quantity: 3);
        _cardService.SetPrice(CardGame.Mtg, "a", isFoil: false, price: 2.50m);

        var svc = CreateService();
        var report = svc.BuildReport(containerId, "Box");

        Assert.Equal(3, report.Lines.Count);
        Assert.All(report.Lines, l => Assert.Equal(2.50m, l.Price));
    }

    [Fact]
    public void BuildReport_FoilCard_UsesFoilPriceAndSuffixesName()
    {
        var containerId = CreateContainer();
        AddSingleLot(containerId, "a", "Lightning Bolt", CardGame.Mtg, "LEA", "1", foil: true);
        _cardService.SetPrice(CardGame.Mtg, "a", isFoil: false, price: 2.50m);
        _cardService.SetPrice(CardGame.Mtg, "a", isFoil: true, price: 40m);

        var svc = CreateService();
        var report = svc.BuildReport(containerId, "Box");

        var line = Assert.Single(report.Lines);
        Assert.Equal("Lightning Bolt (Foil)", line.Name);
        Assert.Equal(40m, line.Price);
    }

    [Fact]
    public void BuildReport_SealedProduct_UsesLastMarketPriceAndBlankCollectorNumber()
    {
        var containerId = CreateContainer();
        AddSealedLot(containerId, "Booster Box", CardGame.Mtg, "LEA", lastMarketPrice: 199.99m);

        var svc = CreateService();
        var report = svc.BuildReport(containerId, "Box");

        var line = Assert.Single(report.Lines);
        Assert.Equal(199.99m, line.Price);
        Assert.Null(line.CollectorNumber);
        Assert.Equal("LEA", line.SetCode);
        Assert.Equal("LEA", line.CardCode); // sealed: set code only, no collector number
    }

    [Fact]
    public void BuildReport_MissingPrice_ShowsZero()
    {
        var containerId = CreateContainer();
        AddSingleLot(containerId, "unpriced", "Unpriced Card", CardGame.Mtg, "LEA", "1");
        // No price registered on the stub game service for "unpriced" -> GetCurrentPrice returns null.
        AddSealedLot(containerId, "No Price Box", CardGame.Mtg, "LEA", lastMarketPrice: null);

        var svc = CreateService();
        var report = svc.BuildReport(containerId, "Box");

        Assert.All(report.Lines, l => Assert.Equal(0m, l.Price));
    }

    [Fact]
    public void BuildReport_SortsByCardNameAscendingAcrossAllGames()
    {
        var containerId = CreateContainer();
        AddSingleLot(containerId, "yugioh-a", "Alpha", CardGame.YuGiOh, "SDK", "1");
        AddSingleLot(containerId, "mtg-b", "Bolt", CardGame.Mtg, "ZZZ", "1");
        AddSingleLot(containerId, "mtg-a", "Ambush", CardGame.Mtg, "AAA", "1");
        AddSingleLot(containerId, "mtg-c", "Counterspell", CardGame.Mtg, "AAA", "2");

        var svc = CreateService();
        var report = svc.BuildReport(containerId, "Box");

        // Flat list, sorted by card name ascending regardless of game or set.
        Assert.Equal(["Alpha", "Ambush", "Bolt", "Counterspell"], report.Lines.Select(l => l.Name));
    }

    [Fact]
    public void BuildReport_SingleCardCode_CombinesSetCodeAndCollectorNumber()
    {
        var containerId = CreateContainer();
        AddSingleLot(containerId, "a", "Lightning Bolt", CardGame.Mtg, "lea", "42");

        var svc = CreateService();
        var report = svc.BuildReport(containerId, "Box");

        var line = Assert.Single(report.Lines);
        Assert.Equal("LEA-42", line.CardCode);
        Assert.Equal("Magic: The Gathering", line.GameDisplayName);
    }

    [Fact]
    public void GetGamesPresent_OnlyReturnsSinglesGames()
    {
        var containerId = CreateContainer();
        AddSingleLot(containerId, "a", "Card A", CardGame.Mtg, "LEA", "1");
        AddSealedLot(containerId, "Booster Box", CardGame.Pokemon, "Base", 20m);

        var svc = CreateService();
        var games = svc.GetGamesPresent(containerId);

        Assert.Single(games);
        Assert.Contains(CardGame.Mtg, games);
    }

    [Fact]
    public void HasSealedProduct_TrueOnlyWhenSealedLotPresent()
    {
        var containerId = CreateContainer();
        var svc = CreateService();
        Assert.False(svc.HasSealedProduct(containerId));

        AddSealedLot(containerId, "Booster Box", CardGame.Mtg, "LEA", 100m);
        Assert.True(svc.HasSealedProduct(containerId));
    }

    [Fact]
    public void HasAnyProduct_FalseForEmptyContainer()
    {
        var containerId = CreateContainer();
        var svc = CreateService();
        Assert.False(svc.HasAnyProduct(containerId));

        AddSingleLot(containerId, "a", "Card A", CardGame.Mtg, "LEA", "1");
        Assert.True(svc.HasAnyProduct(containerId));
    }

    private class TestDbContextFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    /// <summary>Minimal ICardService stub: only GetGameService is functional, backed by a
    /// per-game stub whose prices are set explicitly per (gameCardId, isFoil) key.</summary>
    private sealed class StubCardService : ICardService
    {
        private readonly Dictionary<CardGame, StubGameService> _games = new();

        public void SetPrice(CardGame game, string gameCardId, bool isFoil, decimal price)
        {
            if (!_games.TryGetValue(game, out var svc))
                _games[game] = svc = new StubGameService(game);
            svc.Prices[(gameCardId, isFoil)] = price;
        }

        public ICardGameService GetGameService(CardGame game)
        {
            if (!_games.TryGetValue(game, out var svc))
                _games[game] = svc = new StubGameService(game);
            return svc;
        }

        // Unused members
        public ObservableCollection<ScannedCard> ScannedCards => throw new NotImplementedException();
        public CardGame SelectedGame { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public HashSet<string>? SelectedSetFilter { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool DefaultIsFoil { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string? DefaultFoilType { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public decimal? DefaultPurchasePrice { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IReadOnlyList<CardGame> AvailableGames => throw new NotImplementedException();
        public ICardGameService ActiveGameService => throw new NotImplementedException();
        public Action<HashStageResult>? OnHashStage { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ulong LastComputedHash => throw new NotImplementedException();
        public IOcrMatchingService OcrService => throw new NotImplementedException();
        public void AddFromStream(Stream stream) => throw new NotImplementedException();
        public void ReprocessScans() => throw new NotImplementedException();
        public void CommitScans(IEnumerable<ScannedCard> scannedCards) => throw new NotImplementedException();
        public void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, int skip, int take, ObservableCollection<CollectionCard> results) => throw new NotImplementedException();
        public int GetSearchCount(string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset, bool stacked) => throw new NotImplementedException();
        public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter) => throw new NotImplementedException();
        public List<CollectionCard> GetUnplacedBinderCards(int containerId, FilterPreset? filterPreset) => throw new NotImplementedException();
        public void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null) => throw new NotImplementedException();
        public void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update) => throw new NotImplementedException();
        public List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds) => throw new NotImplementedException();
        public void UpdateCollectionCard(CollectionCard card) => throw new NotImplementedException();
        public void DeleteCollectionCard(int id) => throw new NotImplementedException();
        public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null) => throw new NotImplementedException();
        public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame? game, IProgress<string>? progress = null) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, decimal> GetCurrentPrices(CardGame game, IEnumerable<string> gameCardIds, bool foil) => throw new NotImplementedException();
        public List<string> GetDistinctFieldValues(string field, CardGame game) => throw new NotImplementedException();
        public List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode) => throw new NotImplementedException();
        public void RemoveTempFile(ScannedCard card) => throw new NotImplementedException();
        public void ClearTempFiles() => throw new NotImplementedException();
        public void StartNewDiagnosticSession() => throw new NotImplementedException();
        public (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs() => throw new NotImplementedException();
        public (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null) => throw new NotImplementedException();
        public void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, string? foilType, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section) => throw new NotImplementedException();
        public void AddMissingCardToSlot(CardMatch match, CardGame game, string condition, bool isFoil, string? foilType, decimal? purchasePrice, int containerId, int page, int slot) => throw new NotImplementedException();
        public int ImportCollectionCards(IEnumerable<CollectionCard> cards, bool skipDuplicates) => throw new NotImplementedException();
        public ulong ComputeHashFromStream(Stream stream) => throw new NotImplementedException();
        public ulong ComputeEdgeHashFromStream(Stream stream) => throw new NotImplementedException();
        public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => throw new NotImplementedException();
        public bool IsFirstCopy(CardGame game, string gameCardId, bool isFoil) => throw new NotImplementedException();
        public void AnnotateScan(ScannedCard scan) => throw new NotImplementedException();
    }

    private sealed class StubGameService(CardGame game) : ICardGameService
    {
        public Dictionary<(string GameCardId, bool IsFoil), decimal> Prices { get; } = new();

        public CardGame Game => game;
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) =>
            Prices.TryGetValue((gameCardId, isFoil), out var price) ? price : null;

        // Unused members
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => new();
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public object? FindCardById(string gameCardId) => null;
    }
}
