using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class DecklistMatchingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _dbFactory;

    public DecklistMatchingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbFactory = new TestCollectionDbFactory(options);
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // Seed Bulk container
        ctx.StorageContainers.Add(new StorageContainer
        {
            Name = "Bulk", ContainerType = ContainerType.Bulk,
            IsSystem = true, SortOrder = 0,
        });
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private DecklistService CreateService(ICardService? cardService = null)
    {
        return new DecklistService(_dbFactory, null!, cardService ?? new StubCardService());
    }

    private void SeedCard(string name, string setCode, string number, int containerId = 1,
        int? page = null, int? slot = null, string? section = null, bool isFoil = false)
    {
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.Mtg, GameCardId = Guid.NewGuid().ToString(),
            Name = name, SetCode = setCode, Number = number,
            SetName = setCode, Rarity = "common",
            ContainerId = containerId, Page = page, Slot = slot, Section = section,
            IsFoil = isFoil,
        });
        ctx.SaveChanges();
    }

    private int SeedContainer(string name, ContainerType type)
    {
        using var ctx = _dbFactory.CreateDbContext();
        var c = new StorageContainer { Name = name, ContainerType = type, SortOrder = 1 };
        ctx.StorageContainers.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    [Fact]
    public void CheckAgainstCollection_CardOwned_ShowsInOwnedWithLocation()
    {
        var binderId = SeedContainer("Binder A", ContainerType.Binder);
        SeedCard("Lightning Bolt", "M11", "149", binderId, page: 3, slot: 2);

        var service = CreateService();
        var entries = new List<DecklistEntry> { new(1, "Lightning Bolt", "M11", "149") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        Assert.Equal(1, result.TotalOwned);
        Assert.Equal(0, result.TotalMissing);
        var owned = Assert.Single(result.OwnedEntries);
        Assert.Equal("Lightning Bolt", owned.CardName);
        var loc = Assert.Single(owned.Locations);
        Assert.Equal("Binder A", loc.ContainerName);
        Assert.Equal(3, loc.Page);
        Assert.Equal(2, loc.Slot);
        Assert.True(loc.IsExactSetMatch);
    }

    [Fact]
    public void CheckAgainstCollection_CardMissing_ShowsInMissing()
    {
        var service = CreateService();
        var entries = new List<DecklistEntry> { new(1, "Ragavan, Nimble Pilferer", "MH2", "138") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        Assert.Equal(0, result.TotalOwned);
        Assert.Equal(1, result.TotalMissing);
        var missing = Assert.Single(result.MissingEntries);
        Assert.Equal("Ragavan, Nimble Pilferer", missing.CardName);
    }

    [Fact]
    public void CheckAgainstCollection_PartialOwnership_SplitsOwnedAndMissing()
    {
        SeedCard("Lightning Bolt", "M11", "149");

        var service = CreateService();
        var entries = new List<DecklistEntry> { new(3, "Lightning Bolt", "M11", "149") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        // Owned 1 copy, missing 2
        Assert.Equal(1, result.TotalOwned);
        Assert.Equal(2, result.TotalMissing);
    }

    [Fact]
    public void CheckAgainstCollection_DifferentSet_FallbackMatch_NotExactSetMatch()
    {
        SeedCard("Lightning Bolt", "2ED", "162");

        var service = CreateService();
        var entries = new List<DecklistEntry> { new(1, "Lightning Bolt", "M11", "149") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        Assert.Equal(1, result.TotalOwned);
        var loc = Assert.Single(result.OwnedEntries.Single().Locations);
        Assert.False(loc.IsExactSetMatch);
        Assert.Equal("2ED", loc.SetCode);
    }

    [Fact]
    public void CheckAgainstCollection_ExactSetPreferred()
    {
        SeedCard("Lightning Bolt", "2ED", "162");
        SeedCard("Lightning Bolt", "M11", "149");

        var service = CreateService();
        var entries = new List<DecklistEntry> { new(1, "Lightning Bolt", "M11", "149") };
        var result = service.CheckAgainstCollection("Test", "Test", entries);

        Assert.Equal(1, result.TotalOwned);
        var owned = Assert.Single(result.OwnedEntries);
        // Should show both locations but exact set match first
        Assert.Equal(2, owned.Locations.Count);
        Assert.True(owned.Locations[0].IsExactSetMatch);
    }

    private class TestCollectionDbFactory(DbContextOptions<CollectionDbContext> options)
        : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }

    private class StubCardService : ICardService
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
        public ICardGameService GetGameService(CardGame game) => new StubGameService();
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
        public List<string> GetDistinctFieldValues(string field, CardGame game) => [];
        public List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode) => [];
        public void RemoveTempFile(ScannedCard card) { }
        public void ClearTempFiles() { }
        public void StartNewDiagnosticSession() { }
        public (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs() => (0, 0, 0);
        public (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null) => (0, 0);
        public void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section) { }
        public ulong ComputeHashFromStream(System.IO.Stream stream) => 0;
        public IOcrMatchingService OcrService => null!;
        public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null) => (null, CardGame.Mtg);
    }

    private class StubGameService : ICardGameService
    {
        public CardGame Game => CardGame.Mtg;
        public MatchDiagnostics? LastMatchDiagnostics => null;
        public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14) => null;
        public List<CardMatch> SearchCards(string query, int maxResults = 20) => [];
        public List<CardMatch> GetPrintings(string cardName) => [];
        public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
        public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => new();
        public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
        public IReadOnlyList<SetInfo> GetAvailableSets() => [];
        public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
        public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
        public object? FindCardById(string gameCardId) => null;
    }
}

public class TypeCategoryTests
{
    [Theory]
    [InlineData("Creature — Human Pirate", "Creature")]
    [InlineData("Artifact Creature — Construct", "Creature")]
    [InlineData("Legendary Planeswalker — Jace", "Planeswalker")]
    [InlineData("Instant", "Instant")]
    [InlineData("Sorcery", "Sorcery")]
    [InlineData("Artifact", "Artifact")]
    [InlineData("Legendary Enchantment", "Enchantment")]
    [InlineData("Basic Land — Mountain", "Land")]
    [InlineData("Enchantment Creature — God", "Creature")]
    [InlineData("Tribal Instant — Goblin", "Instant")]
    [InlineData(null, "Other")]
    [InlineData("", "Other")]
    public void GetTypeCategory_ReturnsCorrectCategory(string? typeLine, string expected)
    {
        Assert.Equal(expected, DecklistService.GetTypeCategory(typeLine));
    }
}
