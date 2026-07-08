using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICardGameService
{
    CardGame Game { get; }
    MatchDiagnostics? LastMatchDiagnostics { get; }
    Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default);
    Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default);
    CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14);
    List<CardMatch> SearchCards(string query, int maxResults = 20);
    List<CardMatch> GetPrintings(string cardName);
    decimal? GetCurrentPrice(string gameCardId, bool isFoil);
    Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil);
    void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null);
    IReadOnlyList<SetInfo> GetAvailableSets();
    Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null);
    List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers);
    object? FindCardById(string gameCardId);
}
