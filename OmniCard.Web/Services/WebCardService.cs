using System.Collections.ObjectModel;
using System.IO;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>
/// Lightweight ICardService implementation for the web app.
/// Only GetGameService(), AvailableGames, and ActiveGameService are functional.
/// All scan/collection methods throw NotSupportedException since the web app
/// does not perform scanning or direct collection mutation.
/// </summary>
public sealed class WebCardService : ICardService
{
    private readonly Dictionary<CardGame, ICardGameService> _gameServices;

    public WebCardService(IEnumerable<ICardGameService> gameServices)
    {
        _gameServices = gameServices.ToDictionary(s => s.Game);
    }

    public IReadOnlyList<CardGame> AvailableGames => _gameServices.Keys.ToList().AsReadOnly();

    public ICardGameService ActiveGameService => GetGameService(CardGame.Mtg);

    public ICardGameService GetGameService(CardGame game)
    {
        if (_gameServices.TryGetValue(game, out var service))
            return service;
        throw new ArgumentException($"No game service registered for {game}");
    }

    // --- Properties that are not needed by the web app ---

    public ObservableCollection<ScannedCard> ScannedCards => throw new NotSupportedException();

    public CardGame SelectedGame
    {
        get => CardGame.Mtg;
        set => throw new NotSupportedException();
    }

    public HashSet<string>? SelectedSetFilter
    {
        get => null;
        set { }
    }

    public bool DefaultIsFoil
    {
        get => false;
        set { }
    }

    public decimal? DefaultPurchasePrice
    {
        get => null;
        set { }
    }

    public Action<HashStageResult>? OnHashStage
    {
        get => null;
        set { }
    }

    public ulong LastComputedHash => 0;

    public IOcrMatchingService OcrService => throw new NotSupportedException();

    // --- Methods not needed by the web app ---

    public void AddFromStream(Stream stream) => throw new NotSupportedException();
    public void ReprocessScans() => throw new NotSupportedException();
    public void CommitScans(IEnumerable<ScannedCard> scannedCards) => throw new NotSupportedException();
    public void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null) => throw new NotSupportedException();
    public void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results) => throw new NotSupportedException();
    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results) => throw new NotSupportedException();
    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results) => throw new NotSupportedException();
    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, ObservableCollection<CollectionCard> results) => throw new NotSupportedException();
    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, int skip, int take, ObservableCollection<CollectionCard> results) => throw new NotSupportedException();
    public int GetSearchCount(string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset, bool stacked) => throw new NotSupportedException();
    public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter) => throw new NotSupportedException();
    public void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null) => throw new NotSupportedException();
    public void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update) => throw new NotSupportedException();
    public List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds) => throw new NotSupportedException();
    public void UpdateCollectionCard(CollectionCard card) => throw new NotSupportedException();
    public void DeleteCollectionCard(int id) => throw new NotSupportedException();
    public Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null) => throw new NotSupportedException();
    public List<string> GetDistinctFieldValues(string field, CardGame game) => throw new NotSupportedException();
    public List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode) => throw new NotSupportedException();
    public void RemoveTempFile(ScannedCard card) => throw new NotSupportedException();
    public void ClearTempFiles() => throw new NotSupportedException();
    public void StartNewDiagnosticSession() => throw new NotSupportedException();
    public (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs() => throw new NotSupportedException();
    public (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null) => throw new NotSupportedException();
    public void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section) => throw new NotSupportedException();
    public ulong ComputeHashFromStream(Stream stream) => throw new NotSupportedException();
    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null) => throw new NotSupportedException();
}
