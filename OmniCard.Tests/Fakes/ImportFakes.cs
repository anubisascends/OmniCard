using System.Collections.ObjectModel;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Fakes;

/// <summary>
/// A configurable <see cref="ICardGameService"/> test double. Wire up only the
/// On* delegates a given test needs via object initializer syntax; every other
/// member returns an empty/default value so callers never NRE.
/// </summary>
public class ConfigurableGameService : ICardGameService
{
    public CardGame Game { get; set; } = CardGame.Mtg;
    public MatchDiagnostics? LastMatchDiagnostics => null;

    public Func<string, int, List<CardMatch>>? OnSearchCards { get; set; }
    public Func<string, List<CardMatch>>? OnGetPrintings { get; set; }
    public Func<IEnumerable<string>, bool, Dictionary<string, decimal>>? OnGetCurrentPrices { get; set; }

    public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
    public List<CardMatch> SearchCards(string query, int maxResults = 20) => OnSearchCards?.Invoke(query, maxResults) ?? [];
    public List<CardMatch> GetPrintings(string cardName) => OnGetPrintings?.Invoke(cardName) ?? [];
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => OnGetCurrentPrices?.Invoke(gameCardIds, isFoil) ?? new();
    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
    public IReadOnlyList<SetInfo> GetAvailableSets() => [];
    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
    public object? FindCardById(string gameCardId) => null;
}

/// <summary>
/// An <see cref="ICardService"/> test double that hands out a configurable
/// <see cref="ICardGameService"/> (defaults to a fresh <see cref="ConfigurableGameService"/>)
/// and records the calls made through it that decklist-file import cares about.
/// </summary>
public class RecordingCardService : ICardService
{
    public ObservableCollection<ScannedCard> ScannedCards { get; } = [];
    public CardGame SelectedGame { get; set; }
    public HashSet<string>? SelectedSetFilter { get; set; }
    public bool DefaultIsFoil { get; set; }
    public decimal? DefaultPurchasePrice { get; set; }
    public IReadOnlyList<CardGame> AvailableGames => [];
    public ICardGameService ActiveGameService => GameService;
    public Action<HashStageResult>? OnHashStage { get; set; }
    public ulong LastComputedHash => 0;

    /// <summary>The service handed back by <see cref="ActiveGameService"/> and <see cref="GetGameService"/>.</summary>
    public ICardGameService GameService { get; set; } = new ConfigurableGameService();

    public List<CardGame> GetGameServiceCalls { get; } = [];
    public ICardGameService GetGameService(CardGame game)
    {
        GetGameServiceCalls.Add(game);
        return GameService;
    }

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
    public ulong ComputeHashFromStream(Stream stream) => 0;
    public ulong ComputeEdgeHashFromStream(Stream stream) => 0;
    public IOcrMatchingService OcrService => null!;
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => (null, CardGame.Mtg);
}

/// <summary>
/// An <see cref="IListService"/> test double that records every call so tests can
/// assert on what a view model committed, while letting a test configure the
/// handful of return values that actually vary (AddCardsByName, ToDecklistEntries).
/// </summary>
public class RecordingListService : IListService
{
    public List<CardList> Lists { get; } = [];
    public List<CardListItem> Items { get; } = [];
    private int _nextListId = 1;
    private int _nextItemId = 1;

    public Func<int, IEnumerable<DecklistEntry>, AddCardsResult>? OnAddCardsByName { get; set; }
    public Func<int, List<DecklistEntry>>? OnToDecklistEntries { get; set; }

    public List<(int ListId, CardMatch Printing, bool IsFoil, int Quantity, ListItemSource Source)> AddPrintingCalls { get; } = [];
    public List<int> RefreshPricesCalls { get; } = [];
    public List<(int ListId, IReadOnlyList<DecklistEntry> Entries)> AddCardsByNameCalls { get; } = [];

    public IReadOnlyList<CardList> GetLists(CardGame game) => Lists.Where(l => l.Game == game).ToList();

    public CardList CreateList(string name, CardGame game)
    {
        var list = new CardList { Id = _nextListId++, Name = name, Game = game };
        Lists.Add(list);
        return list;
    }

    public void RenameList(int listId, string name)
    {
        var list = Lists.FirstOrDefault(l => l.Id == listId);
        if (list is not null) list.Name = name;
    }

    public void DeleteList(int listId)
    {
        Lists.RemoveAll(l => l.Id == listId);
        Items.RemoveAll(i => i.CardListId == listId);
    }

    public IReadOnlyList<CardListItem> GetItems(int listId) => Items.Where(i => i.CardListId == listId).ToList();

    public CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, int quantity, ListItemSource source)
    {
        AddPrintingCalls.Add((listId, printing, isFoil, quantity, source));
        var item = new CardListItem
        {
            Id = _nextItemId++,
            CardListId = listId,
            Quantity = quantity,
            GameCardId = printing.GameSpecificId,
            CardName = printing.Name,
            SetCode = printing.SetCode,
            CollectorNumber = printing.CollectorNumber,
            IsFoil = isFoil,
            Source = source,
        };
        Items.Add(item);
        return item;
    }

    public void RemoveItem(int itemId) => Items.RemoveAll(i => i.Id == itemId);

    public void SetQuantity(int itemId, int quantity)
    {
        var item = Items.FirstOrDefault(i => i.Id == itemId);
        if (item is not null) item.Quantity = quantity;
    }

    public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries)
    {
        var list = entries.ToList();
        AddCardsByNameCalls.Add((listId, list));
        return OnAddCardsByName?.Invoke(listId, list) ?? new AddCardsResult(0, []);
    }

    public void RefreshPrices(int listId) => RefreshPricesCalls.Add(listId);

    public List<DecklistEntry> ToDecklistEntries(int listId) => OnToDecklistEntries?.Invoke(listId) ?? [];
}

/// <summary>An <see cref="IStorageContainerService"/> test double with in-memory bookkeeping.</summary>
public class RecordingContainerService : IStorageContainerService
{
    public List<StorageContainer> Containers { get; } = [];
    private int _nextId = 1;

    public List<StorageContainer> GetAll() => Containers;

    public StorageContainer GetBulk() =>
        Containers.FirstOrDefault(c => c.ContainerType == ContainerType.Bulk)
        ?? Create("Bulk", ContainerType.Bulk);

    public StorageContainer Create(string name, ContainerType type)
    {
        var container = new StorageContainer { Id = _nextId++, Name = name, ContainerType = type };
        Containers.Add(container);
        return container;
    }

    public void Rename(int id, string newName)
    {
        var container = Containers.FirstOrDefault(c => c.Id == id);
        if (container is not null) container.Name = newName;
    }

    public void Delete(int id, bool moveCardsToBulk = true) => Containers.RemoveAll(c => c.Id == id);

    public int GetCardCount(int containerId) => 0;

    public void SetCoverCard(int containerId, int? cardId) { }

    public List<CollectionCard> GetCardsInContainer(int containerId) => [];

    public void SetExcludeFromDeckCheck(int containerId, bool exclude) { }
}

/// <summary>
/// An <see cref="IDecklistService"/> test double for file-based decklist import tests
/// that need to control what parsing/checking returns without exercising the real
/// text-parsing regex.
/// </summary>
public class FakeDecklistParseService : IDecklistService
{
    public Func<string, List<DecklistEntry>>? OnParseDecklistPrintings { get; set; }
    public Func<string, (string DeckName, List<DecklistEntry> Entries)>? OnParseDecklistText { get; set; }
    public Func<string, DecklistCheckResult>? OnCheckAgainstCollection { get; set; }

    public Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url) =>
        Task.FromResult<(string DeckName, List<DecklistEntry> Entries)?>(null);

    public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text) =>
        OnParseDecklistText?.Invoke(text) ?? ("", []);

    public List<DecklistEntry> ParseDecklistPrintings(string text) =>
        OnParseDecklistPrintings?.Invoke(text) ?? [];

    public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game) =>
        OnCheckAgainstCollection?.Invoke(deckName) ?? new DecklistCheckResult
        {
            DeckName = deckName,
            DeckSource = deckSource,
            OwnedEntries = [],
            MissingEntries = [],
        };
}
