using System.Collections.ObjectModel;
using System.IO;
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICardService
{
    ObservableCollection<ScannedCard> ScannedCards { get; }
    CardGame SelectedGame { get; set; }
    HashSet<string>? SelectedSetFilter { get; set; }
    bool DefaultIsFoil { get; set; }
    string? DefaultFoilType { get; set; }
    decimal? DefaultPurchasePrice { get; set; }
    IReadOnlyList<CardGame> AvailableGames { get; }
    ICardGameService ActiveGameService { get; }
    Action<HashStageResult>? OnHashStage { get; set; }
    ulong LastComputedHash { get; }
    ICardGameService GetGameService(CardGame game);
    void AddFromStream(Stream stream);
    void ReprocessScans();
    void CommitScans(IEnumerable<ScannedCard> scannedCards);
    void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null);
    void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results);
    void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results);
    void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results);
    void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, ObservableCollection<CollectionCard> results);
    void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, int skip, int take, ObservableCollection<CollectionCard> results);
    int GetSearchCount(string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset, bool stacked);
    HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter);

    /// <summary>Cards in the given (Binder) container not yet placed on a page/slot, narrowed by
    /// the same filter-preset query language used for the main collection search.</summary>
    List<CollectionCard> GetUnplacedBinderCards(int containerId, FilterPreset? filterPreset);
    void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null);

    /// <summary>Moves exactly <paramref name="quantity"/> copies of a single-card lot into a container.
    /// If the lot holds more than <paramref name="quantity"/>, it is split — the source lot's quantity is
    /// decremented and a new lot (same product/condition/foil/cost) is created in the destination.
    /// Returns the destination lot id.</summary>
    int MoveQuantityToContainer(int lotId, int quantity, int containerId, string? section = null);
    void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update);
    List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds);
    void UpdateCollectionCard(CollectionCard card);
    void DeleteCollectionCard(int id);

    /// <summary>Flags a single-card lot as physically missing (<see cref="InventoryLot.IsMissing"/>)
    /// without removing the record or its binder slot — used by the binder audit when a pocket the app
    /// expects to be filled is empty. Sets <see cref="FlagReason.Manual"/>.</summary>
    void SetCardMissing(int lotId);
    Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null);
    Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame? game, IProgress<string>? progress = null);
    IReadOnlyDictionary<string, decimal> GetCurrentPrices(CardGame game, IEnumerable<string> gameCardIds, bool foil);
    List<string> GetDistinctFieldValues(string field, CardGame game);
    List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode);
    void RemoveTempFile(ScannedCard card);
    void ClearTempFiles();
    void StartNewDiagnosticSession();
    (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs();
    (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null);
    void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, string? foilType, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section);

    /// <summary>Adds a single card directly into a specific binder page/slot. If that slot is already
    /// occupied, the existing card is displaced back to the Unplaced pool (swap) — same as dragging.</summary>
    void AddMissingCardToSlot(CardMatch match, CardGame game, string condition, bool isFoil, string? foilType, decimal? purchasePrice, int containerId, int page, int slot);
    bool IsFirstCopy(CardGame game, string gameCardId, bool isFoil);
    void AnnotateScan(ScannedCard scan);
    int ImportCollectionCards(IEnumerable<CollectionCard> cards, bool skipDuplicates);
    ulong ComputeHashFromStream(Stream stream);
    ulong ComputeEdgeHashFromStream(Stream stream);
    IOcrMatchingService OcrService { get; }
    (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null);
}
