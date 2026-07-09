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
    void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null);
    void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update);
    List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds);
    void UpdateCollectionCard(CollectionCard card);
    void DeleteCollectionCard(int id);
    Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null);
    List<string> GetDistinctFieldValues(string field, CardGame game);
    List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode);
    void RemoveTempFile(ScannedCard card);
    void ClearTempFiles();
    void StartNewDiagnosticSession();
    (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs();
    (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null);
    void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section);
}
