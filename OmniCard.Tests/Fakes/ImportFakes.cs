using System.Collections.ObjectModel;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Tests.Fakes;

/// <summary>ICardGameService whose lookup methods are set per-test via delegates.</summary>
public sealed class ConfigurableGameService : ICardGameService
{
    public Func<string, int, List<CardMatch>> OnSearchCards = (_, _) => [];
    public Func<string, List<CardMatch>> OnGetPrintings = _ => [];
    public Func<IEnumerable<string>, bool, Dictionary<string, decimal>> OnGetCurrentPrices = (_, _) => new();

    public CardGame Game => CardGame.Mtg;
    public List<CardMatch> SearchCards(string query, int maxResults = 20) => OnSearchCards(query, maxResults);
    public List<CardMatch> GetPrintings(string cardName) => OnGetPrintings(cardName);
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => OnGetCurrentPrices(gameCardIds, isFoil);

    // Unused members
    public MatchDiagnostics? LastMatchDiagnostics => null;
    public Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => null;
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) { }
    public IReadOnlyList<SetInfo> GetAvailableSets() => [];
    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => Task.FromResult(new List<SetCompletionSummary>());
    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => [];
    public object? FindCardById(string gameCardId) => null;
}

/// <summary>ICardService that exposes a single active game service and records AddCardToCollection calls.</summary>
public sealed class RecordingCardService(ICardGameService active) : ICardService
{
    public sealed record AddCall(CardMatch Match, CardGame Game, string Condition, bool IsFoil, decimal? PurchasePrice, int Quantity, StorageContainer? Container);
    public List<AddCall> Added { get; } = [];

    public ICardGameService ActiveGameService => active;
    public ICardGameService GetGameService(CardGame game) => active;
    public void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section)
        => Added.Add(new AddCall(match, game, condition, isFoil, purchasePrice, quantity, container));

    // Unused members
    public ObservableCollection<ScannedCard> ScannedCards { get; } = [];
    public CardGame SelectedGame { get; set; }
    public HashSet<string>? SelectedSetFilter { get; set; }
    public bool DefaultIsFoil { get; set; }
    public decimal? DefaultPurchasePrice { get; set; }
    public IReadOnlyList<CardGame> AvailableGames => [];
    public Action<HashStageResult>? OnHashStage { get; set; }
    public ulong LastComputedHash => 0;
    public IOcrMatchingService OcrService => null!;
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
    public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter = null) => throw new NotImplementedException();
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
    public int ImportCollectionCards(IEnumerable<CollectionCard> cards, bool skipDuplicates) => throw new NotImplementedException();
    public ulong ComputeHashFromStream(Stream stream) => throw new NotImplementedException();
    public ulong ComputeEdgeHashFromStream(Stream stream) => throw new NotImplementedException();
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => throw new NotImplementedException();
    public bool IsFirstCopy(CardGame game, string gameCardId, bool isFoil) => throw new NotImplementedException();
    public void AnnotateScan(ScannedCard scan) => throw new NotImplementedException();
}

/// <summary>IListService that records AddPrinting/CreateList calls.</summary>
public sealed class RecordingListService : IListService
{
    public sealed record AddPrintingCall(int ListId, CardMatch Printing, bool IsFoil, int Quantity, ListItemSource Source);
    public List<AddPrintingCall> Printings { get; } = [];
    public List<CardList> Lists { get; } = [];
    private int _nextId = 500;

    public IReadOnlyList<CardList> GetLists(CardGame game) => Lists.Where(l => l.Game == game).ToList();
    public CardList CreateList(string name, CardGame game)
    {
        var l = new CardList { Id = _nextId++, Name = name, Game = game };
        Lists.Add(l);
        return l;
    }
    public CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, int quantity, ListItemSource source)
    {
        Printings.Add(new AddPrintingCall(listId, printing, isFoil, quantity, source));
        return new CardListItem { CardListId = listId, GameCardId = printing.GameSpecificId, Quantity = quantity, Source = source };
    }

    // Unused members
    public void RenameList(int listId, string name) => throw new NotImplementedException();
    public void DeleteList(int listId) => throw new NotImplementedException();
    public IReadOnlyList<CardListItem> GetItems(int listId) => throw new NotImplementedException();
    public void RemoveItem(int itemId) => throw new NotImplementedException();
    public void SetQuantity(int itemId, int quantity) => throw new NotImplementedException();
    public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries) => throw new NotImplementedException();
    public void RefreshPrices(int listId) => throw new NotImplementedException();
    public List<DecklistEntry> ToDecklistEntries(int listId) => throw new NotImplementedException();
    public CommitToLocationResult CommitToLocation(int listId, StorageContainer container, string condition) => throw new NotImplementedException();
}

/// <summary>IStorageContainerService that records Create and serves a seeded list.</summary>
public sealed class RecordingContainerService : IStorageContainerService
{
    public List<StorageContainer> Containers { get; } = [];
    public StorageContainer Bulk { get; set; } = new() { Id = 1, Name = "Bulk", IsSystem = true };
    public List<(string Name, ContainerType Type)> Created { get; } = [];
    private int _nextId = 900;

    public List<StorageContainer> GetAll() => Containers;
    public StorageContainer GetBulk() => Bulk;
    public StorageContainer Create(string name, ContainerType type)
    {
        Created.Add((name, type));
        var c = new StorageContainer { Id = _nextId++, Name = name, ContainerType = type };
        Containers.Add(c);
        return c;
    }

    // Unused members
    public void Rename(int id, string newName) => throw new NotImplementedException();
    public void Delete(int id, bool moveCardsToBulk = true) => throw new NotImplementedException();
    public int GetCardCount(int containerId) => throw new NotImplementedException();
    public void SetCoverCard(int containerId, int? cardId) => throw new NotImplementedException();
    public List<CollectionCard> GetCardsInContainer(int containerId) => throw new NotImplementedException();
    public void SetExcludeFromDeckCheck(int containerId, bool exclude) => throw new NotImplementedException();
}

/// <summary>IDecklistService that returns canned printing entries.</summary>
public sealed class FakeDecklistParseService : IDecklistService
{
    public List<DecklistEntry> Printings { get; set; } = [];
    public List<DecklistEntry> ParseDecklistPrintings(string text) => Printings;

    public Func<string, (string DeckName, List<DecklistEntry> Entries)?> OnFetch = _ => null;
    public Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url) => Task.FromResult(OnFetch(url));

    public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text) => throw new NotImplementedException();
    public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game) => throw new NotImplementedException();
}

/// <summary>IDecklistImportService returning canned rows and recording commits.</summary>
public sealed class FakeDecklistImportService : IDecklistImportService
{
    public Func<string, List<DecklistImportRow>> OnResolve = _ => [];
    public Func<IEnumerable<DecklistEntry>, List<DecklistImportRow>> OnResolveEntries = _ => [];
    public List<(int ListId, int Count)> ListCommits { get; } = [];
    public List<(StorageContainer Container, int Count)> LocationCommits { get; } = [];

    public IReadOnlyList<DecklistImportRow> ResolveFile(string fileText) => OnResolve(fileText);
    public IReadOnlyList<DecklistImportRow> ResolveEntries(IEnumerable<DecklistEntry> entries) => OnResolveEntries(entries);

    public int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows)
    {
        var rows = resolvedRows.ToList();
        ListCommits.Add((listId, rows.Count));
        return rows.Sum(r => r.Quantity);
    }

    public int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows)
    {
        var rows = resolvedRows.ToList();
        LocationCommits.Add((container, rows.Count));
        return rows.Sum(r => r.Quantity);
    }
}
