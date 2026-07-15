using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IScryfallService
{
    Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default);
    Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default);
    CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 10, ulong? scanEdgeHash = null);
    List<CardMatch> SearchCards(string query, int maxResults = 20);
    IQueryable<Card> Cards { get; }
    Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null);
    List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers);
}
