using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IScryfallService
{
    Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default);
    Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default);
    CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 10);
    List<CardMatch> SearchCards(string query, int maxResults = 20);
    IQueryable<Card> Cards { get; }
    Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null);
    List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers);
}

public sealed class ScryfallService : IScryfallService, ICardGameService, IDisposable
{
    private static readonly JsonSerializerOptions ScryfallJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<ScryfallDbContext> _dbContextFactory;
    private readonly IPerceptualHashService _hashService;
    private readonly SetSymbolCache _symbolCache;
    private readonly ILogger<ScryfallService> _logger;
    private readonly HashSet<string> _languages;
    private readonly string _dataDirectory;
    private ScryfallDbContext _readContext;

    public ScryfallService(IHttpClientFactory httpClientFactory, IDbContextFactory<ScryfallDbContext> dbContextFactory, IPerceptualHashService hashService, SetSymbolCache symbolCache, IOptions<ScryfallSettings> scryfallSettings, ILogger<ScryfallService> logger, IDataPathService dataPathService)
    {
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _hashService = hashService;
        _symbolCache = symbolCache;
        _logger = logger;
        _dataDirectory = dataPathService.DataDirectory;
        _languages = scryfallSettings.Value.Languages.Select(l => l.ToLowerInvariant()).ToHashSet();

        _logger.LogInformation("Initializing Scryfall service");
        _readContext = _dbContextFactory.CreateDbContext();
        var dbPath = _readContext.Database.GetConnectionString();
        if (dbPath is not null)
        {
            var dataSource = dbPath.Replace("Data Source=", "");
            var dir = Path.GetDirectoryName(dataSource);
            if (dir is not null && dir.Length > 0)
                Directory.CreateDirectory(dir);
        }
        _readContext.Database.EnsureCreated();
        if (dbPath is not null)
        {
            var dataSourcePath = dbPath.Replace("Data Source=", "");
            ScryfallDbContext.RunScryfallMigrations(dataSourcePath);
        }
        _logger.LogInformation("Scryfall database ready at {DbPath}", dbPath);
    }

    public CardGame Game => CardGame.Mtg;

    public IQueryable<Card> Cards => _readContext.Cards.AsNoTracking();

    private List<(Guid Id, ulong Hash)>? _hashCache;
    private List<(Guid Id, ulong ArtHash)>? _artHashCache;
    private Dictionary<Guid, string>? _hashSetLookup;
    private Dictionary<Guid, int>? _hashCollectorNumberLookup;

    // Sets that should be deprioritized in matching — reprints/promos that share art with canonical sets
    private static readonly HashSet<string> DeprioritizedSets = new(StringComparer.OrdinalIgnoreCase)
    {
        "plst",  // The List
        "plist", // The List (alternate code)
        "ainr",  // Innistrad Remastered Art Series
        "sis",   // Shadows of the Past Art Series
        "afc",   // Forgotten Realms Art Series
        "amh1",  // Modern Horizons Art Series
        "amh2",  // Modern Horizons 2 Art Series
        "amh3",  // Modern Horizons 3 Art Series
        "aone",  // One Piece Art Series
        "prm",   // Magic Online Promos
        "sld",   // Secret Lair Drop
    };
    private List<(ulong ScanHash, string CorrectCardId, ulong? ArtScanHash, string? CardName, string? SetCode)>? _correctionsCache;
    private Dictionary<string, ulong>? _symbolHashCache;
    private const int CorrectionTrustBonus = 5;

    /// <summary>Art crop regions for MTG cards (percentage of card dimensions).</summary>
    internal static readonly (double X, double Y, double W, double H)[] ArtCropRegions =
    [
        (0.07, 0.11, 0.86, 0.44), // Modern frame (post-2003)
        (0.00, 0.00, 1.00, 0.55), // Borderless / full art
        (0.10, 0.10, 0.80, 0.42), // Retro (pre-8th edition)
    ];

    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 10)
    {
        _logger.LogDebug("Finding closest match for pHash {Hash:X16} (set filter: {SetFilter}, max distance: {MaxDistance})", imageHash, setFilter is not null ? string.Join(",", setFilter) : "none", maxDistance);

        // Load caches
        if (_hashCache is null)
        {
            _logger.LogInformation("Building in-memory hash cache from database");
            var entries = _readContext.Cards
                .Where(c => c.ImageHash != null)
                .Select(c => new { c.Id, Hash = c.ImageHash!.Value, c.SetCode, c.CollectorNumber })
                .AsNoTracking()
                .AsEnumerable()
                .ToList();
            _hashCache = entries.Select(c => (c.Id, c.Hash)).ToList();
            _hashSetLookup = entries.ToDictionary(c => c.Id, c => c.SetCode);
            _hashCollectorNumberLookup = entries.ToDictionary(c => c.Id, c => ParseCollectorNumber(c.CollectorNumber));
            _logger.LogInformation("Hash cache loaded with {Count} entries", _hashCache.Count);
        }

        if (_correctionsCache is null)
        {
            _logger.LogInformation("Building corrections cache from database");
            using var ctx = _dbContextFactory.CreateDbContext();
            _correctionsCache = ctx.HashCorrections
                .AsNoTracking()
                .Select(h => new { h.ScanHash, h.CorrectCardId, h.ArtScanHash, h.CardName, h.SetCode })
                .AsEnumerable()
                .Select(h => (h.ScanHash, h.CorrectCardId, h.ArtScanHash, h.CardName, h.SetCode))
                .ToList();
            _logger.LogInformation("Corrections cache loaded with {Count} entries", _correctionsCache.Count);
        }

        if (_artHashCache is null)
        {
            _logger.LogInformation("Building in-memory art hash cache from database");
            _artHashCache = _readContext.Cards
                .Where(c => c.ArtHash != null)
                .Select(c => new { c.Id, ArtHash = c.ArtHash!.Value })
                .AsNoTracking()
                .AsEnumerable()
                .Select(c => (c.Id, c.ArtHash))
                .ToList();
            _logger.LogInformation("Art hash cache loaded with {Count} entries", _artHashCache.Count);
        }

        // Capture local references so concurrent cache invalidation (e.g. RecordCorrection
        // on the UI thread nulling _correctionsCache) can't cause NullReferenceException
        // while this method runs on the scanner thread.
        var hashCache = _hashCache;
        var hashSetLookup = _hashSetLookup;
        var hashCollectorNumberLookup = _hashCollectorNumberLookup;
        var correctionsCache = _correctionsCache;
        var artHashCache = _artHashCache;

        // Phase 1: Exact correction lookup (specific scan hash → known card)
        var exactCorrection = correctionsCache.FirstOrDefault(c => c.ScanHash == imageHash);
        if (exactCorrection.CorrectCardId is not null && exactCorrection != default)
        {
            // Respect set filter even for exact corrections — look up the card to get its
            // actual SetCode so corrections with null SetCode metadata can't bypass the filter
            var correctedCard = LookupCard(Guid.Parse(exactCorrection.CorrectCardId), confidence: 100);
            if (correctedCard is not null)
            {
                if (setFilter is null || setFilter.Contains(correctedCard.SetCode))
                {
                    _logger.LogDebug("Exact correction found for hash {Hash:X16} → {CardId}", imageHash, exactCorrection.CorrectCardId);
                    return correctedCard;
                }
                _logger.LogDebug("Exact correction for hash {Hash:X16} → {CardId} rejected by set filter (set {Set})", imageHash, exactCorrection.CorrectCardId, correctedCard.SetCode);
            }
        }

        // Phase 2: pHash matching (optionally filtered by set from symbol detection)
        if (hashCache.Count == 0)
        {
            _logger.LogWarning("Hash cache is empty, no cards to match against");
            return null;
        }

        const int TieZone = 4;
        int bestPHashDistance = int.MaxValue;

        foreach (var (id, hash) in hashCache)
        {
            if (setFilter is not null && !setFilter.Contains(hashSetLookup![id]))
                continue;

            var distance = PerceptualHashService.HammingDistance(imageHash, hash);
            if (distance < bestPHashDistance)
                bestPHashDistance = distance;
        }

        if (bestPHashDistance == int.MaxValue)
        {
            _logger.LogDebug("No cards matched the set filter");
            return null;
        }

        var pHashCandidates = new List<(Guid Id, int Distance)>();
        foreach (var (id, hash) in hashCache)
        {
            if (setFilter is not null && !setFilter.Contains(hashSetLookup![id]))
                continue;

            var distance = PerceptualHashService.HammingDistance(imageHash, hash);
            if (distance <= bestPHashDistance + TieZone)
                pHashCandidates.Add((id, distance));
        }

        // Soft-weight preferred sets: apply a distance bonus to candidates from detected symbol sets.
        // This breaks ties among reprints with identical art without eliminating non-preferred sets.
        // Bonus of 5 ensures symbol detection can overcome small pHash differences between printings.
        const int PreferredSetBonus = 5;
        if (preferredSets is { Count: > 0 } && hashSetLookup is not null)
        {
            for (int i = 0; i < pHashCandidates.Count; i++)
            {
                var (id, dist) = pHashCandidates[i];
                if (hashSetLookup.TryGetValue(id, out var setCode) && preferredSets.Contains(setCode))
                    pHashCandidates[i] = (id, Math.Max(0, dist - PreferredSetBonus));
            }

            // Recalculate best distance after bonus
            bestPHashDistance = pHashCandidates.Min(c => c.Distance);
        }

        // Penalize deprioritized sets (plst, art series, promos) — makes canonical printings win ties
        const int DeprioritizedSetPenalty = 4;
        if (hashSetLookup is not null)
        {
            for (int i = 0; i < pHashCandidates.Count; i++)
            {
                var (id, dist) = pHashCandidates[i];
                if (hashSetLookup.TryGetValue(id, out var setCode) && DeprioritizedSets.Contains(setCode))
                    pHashCandidates[i] = (id, dist + DeprioritizedSetPenalty);
            }
        }

        // Art hash disambiguation among tie-zone candidates
        Guid bestPHashId;
        if (artHashes is not null && artHashCache.Count > 0 && pHashCandidates.Count > 1)
        {
            var artLookup = new Dictionary<Guid, ulong>();
            foreach (var (id, artHash) in artHashCache)
                artLookup.TryAdd(id, artHash);

            int bestCombined = int.MaxValue;
            bestPHashId = pHashCandidates[0].Id;

            foreach (var (candidateId, pDist) in pHashCandidates)
            {
                if (!artLookup.TryGetValue(candidateId, out var refArtHash))
                    continue;

                int bestArtDist = int.MaxValue;
                foreach (var scanArtHash in artHashes)
                {
                    if (scanArtHash == 0) continue;
                    var artDist = PerceptualHashService.HammingDistance(scanArtHash, refArtHash);
                    if (artDist < bestArtDist) bestArtDist = artDist;
                }

                var combined = pDist + bestArtDist;
                var candidateCollectorNum = hashCollectorNumberLookup?.GetValueOrDefault(candidateId, int.MaxValue) ?? int.MaxValue;
                var currentBestCollectorNum = hashCollectorNumberLookup?.GetValueOrDefault(bestPHashId, int.MaxValue) ?? int.MaxValue;

                // Prefer lower combined score; tiebreak by lower collector number
                if (combined < bestCombined || (combined == bestCombined && candidateCollectorNum < currentBestCollectorNum))
                {
                    bestCombined = combined;
                    bestPHashId = candidateId;
                    bestPHashDistance = pDist;
                }
            }

            if (bestPHashDistance > maxDistance)
            {
                // Art-only fallback
                Guid bestArtOnlyId = default;
                int bestArtOnlyDist = int.MaxValue;
                foreach (var (id, refArtHash) in artHashCache)
                {
                    if (setFilter is not null && (!hashSetLookup!.TryGetValue(id, out var sc) || !setFilter.Contains(sc)))
                        continue;
                    foreach (var scanArtHash in artHashes)
                    {
                        if (scanArtHash == 0) continue;
                        var artDist = PerceptualHashService.HammingDistance(scanArtHash, refArtHash);
                        if (artDist < bestArtOnlyDist) { bestArtOnlyDist = artDist; bestArtOnlyId = id; }
                    }
                }
                if (bestArtOnlyDist <= maxDistance)
                {
                    bestPHashId = bestArtOnlyId;
                    bestPHashDistance = bestArtOnlyDist;
                }
            }
        }
        else
        {
            // Tiebreak by collector number — lower numbers (regular printings) preferred over higher (extended art, showcase)
            bestPHashId = pHashCandidates
                .OrderBy(c => c.Distance)
                .ThenBy(c => hashCollectorNumberLookup?.GetValueOrDefault(c.Id, int.MaxValue) ?? int.MaxValue)
                .First().Id;
        }

        // Phase 2b: Fuzzy correction matching — nearby confirmed hashes boost confidence
        // If the user previously confirmed a match for a similar scan hash,
        // that correction's card gets a trust bonus that can outweigh the pHash winner.
        const int CorrectionTrustBonus = 5;
        foreach (var (corrScanHash, corrCardId, corrArtHash, _, corrSetCode) in correctionsCache)
        {
            // Respect set filter — never match a correction outside the selected set(s).
            // If correction has no SetCode metadata, resolve it from hashSetLookup;
            // if it can't be resolved, skip it when a filter is active.
            if (setFilter is not null)
            {
                var resolvedSetCode = corrSetCode
                    ?? (Guid.TryParse(corrCardId, out var corrGuid) && hashSetLookup!.TryGetValue(corrGuid, out var looked) ? looked : null);
                if (resolvedSetCode is null || !setFilter.Contains(resolvedSetCode))
                    continue;
            }

            var corrDist = PerceptualHashService.HammingDistance(imageHash, corrScanHash);
            if (corrDist == 0 || corrDist > maxDistance) continue; // exact already handled; too far = skip

            var adjustedDist = Math.Max(0, corrDist - CorrectionTrustBonus);

            // Also factor in art hash if both the correction and scan have one
            if (artHashes is not null && corrArtHash is > 0)
            {
                var bestArtDist = artHashes
                    .Where(h => h != 0)
                    .Select(h => PerceptualHashService.HammingDistance(h, corrArtHash.Value))
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();
                if (bestArtDist <= maxDistance)
                    adjustedDist = Math.Max(0, adjustedDist - (maxDistance - bestArtDist) / 2);
            }

            if (adjustedDist < bestPHashDistance)
            {
                var corrId = Guid.Parse(corrCardId);
                _logger.LogDebug("Fuzzy correction wins: hash distance {Raw} adjusted to {Adjusted} (pHash best was {PHashBest}) → {CardId}",
                    corrDist, adjustedDist, bestPHashDistance, corrCardId);
                bestPHashId = corrId;
                bestPHashDistance = adjustedDist;
            }
        }

        // Phase 3: Confident hash — if distance is low, return immediately
        const int ConfidentHashThreshold = 6;
        if (bestPHashDistance <= ConfidentHashThreshold)
        {
            var confidence = Math.Max(0, (1.0 - (double)bestPHashDistance / maxDistance)) * 100;
            _logger.LogDebug("Confident hash match at distance {Distance} (confidence {Confidence:F0}%)", bestPHashDistance, confidence);
            var confidentCard = LookupCard(bestPHashId, confidence);
            if (confidentCard is not null)
                return confidentCard;
        }

        // Phase 4: OCR-assisted scoring (only when pHash isn't confident)
        if (ocrResult?.RecognizedName is not null && ocrResult.NameConfidence > 0.3)
        {
            const double PHashWeight = 0.50;
            const double NameWeight = 0.50;
            const double MinThreshold = 0.3;

            var candidates = new List<(Guid Id, double Score)>();

            // pHash winner as a candidate
            if (bestPHashDistance <= maxDistance * 2)
            {
                var pScore = 1.0 - ((double)bestPHashDistance / (maxDistance * 2));
                var nScore = StringSimilarity(ocrResult.RecognizedName, LookupCardName(bestPHashId));
                candidates.Add((bestPHashId, PHashWeight * pScore + NameWeight * nScore));
            }

            // OCR name matches as candidates
            var ocrName = ocrResult.RecognizedName;
            using var ocrCtx = _dbContextFactory.CreateDbContext();
            var nameMatches = ocrCtx.Cards
                .AsNoTracking()
                .Where(c => EF.Functions.Like(c.Name, $"%{ocrName}%"))
                .Where(c => c.ImageHash != null)
                .Select(c => new { c.Id, c.Name, c.SetCode })
                .Take(10)
                .ToList();

            foreach (var nm in nameMatches)
            {
                if (candidates.Any(c => c.Id == nm.Id)) continue;
                if (setFilter is not null && !setFilter.Contains(nm.SetCode)) continue;

                var nmHash = hashCache.FirstOrDefault(h => h.Id == nm.Id).Hash;
                var nmDist = nmHash != 0 ? PerceptualHashService.HammingDistance(imageHash, nmHash) : 64;
                var pScore = Math.Max(0, 1.0 - ((double)nmDist / (maxDistance * 2)));
                var nScore = StringSimilarity(ocrResult.RecognizedName, nm.Name);
                candidates.Add((nm.Id, PHashWeight * pScore + NameWeight * nScore));
            }

            if (candidates.Count > 0)
            {
                var best = candidates.OrderByDescending(c => c.Score).First();
                if (best.Score >= MinThreshold)
                {
                    bestPHashId = best.Id;
                    _logger.LogDebug("OCR scoring winner: {Id} (score {Score:F2})", best.Id, best.Score);
                }
            }
        }

        // Final: return the best match if within max distance
        if (bestPHashDistance > maxDistance)
        {
            _logger.LogDebug("Best distance {Distance} exceeds max {MaxDistance}", bestPHashDistance, maxDistance);
            return null;
        }

        // Safety net: never return a card outside the selected set filter.
        // Also reject if the card can't be found in hashSetLookup — unknown set means not in filter.
        if (setFilter is not null)
        {
            if (!hashSetLookup!.TryGetValue(bestPHashId, out var finalSetCode) || !setFilter.Contains(finalSetCode))
            {
                _logger.LogDebug("Best match {CardId} is in set {Set} which is outside the set filter; rejecting", bestPHashId, finalSetCode ?? "(unknown)");
                return null;
            }
        }

        var card = _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.Id == bestPHashId);
        _logger.LogDebug("Best match: {CardName}", card?.Name);
        if (card is null) return null;

        // Confidence is always based on pHash distance of the winning card
        var winnerHash = hashCache.FirstOrDefault(h => h.Id == bestPHashId).Hash;
        var winnerDistance = winnerHash != 0 ? PerceptualHashService.HammingDistance(imageHash, winnerHash) : maxDistance;
        var matchConfidence = Math.Max(0, (1.0 - (double)winnerDistance / maxDistance)) * 100;

        return new CardMatch
        {
            Name = card.Name,
            SetCode = card.SetCode,
            SetName = card.SetName,
            CollectorNumber = card.CollectorNumber,
            Rarity = card.Rarity,
            ImageUri = card.ImageUris?.Normal ?? card.ImageUris?.Small,
            GameSpecificId = card.Id.ToString(),
            LocalImagePath = card.LocalImagePath,
            Confidence = matchConfidence,
            Source = card
        };
    }

    /// <summary>
    /// Parses the numeric prefix of a collector number (e.g., "13" from "13", "341" from "341a").
    /// Non-numeric collector numbers (e.g., "T1") return int.MaxValue so they sort last.
    /// </summary>
    private static int ParseCollectorNumber(string collectorNumber)
    {
        var span = collectorNumber.AsSpan();
        int i = 0;
        while (i < span.Length && char.IsDigit(span[i])) i++;
        return i > 0 && int.TryParse(span[..i], out var num) ? num : int.MaxValue;
    }

    internal static double StringSimilarity(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;

        a = a.ToLowerInvariant().Trim();
        b = b.ToLowerInvariant().Trim();

        if (a == b) return 1.0;

        // Levenshtein distance
        var len1 = a.Length;
        var len2 = b.Length;
        var matrix = new int[len1 + 1, len2 + 1];

        for (var i = 0; i <= len1; i++) matrix[i, 0] = i;
        for (var j = 0; j <= len2; j++) matrix[0, j] = j;

        for (var i = 1; i <= len1; i++)
        {
            for (var j = 1; j <= len2; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        var distance = matrix[len1, len2];
        var maxLen = Math.Max(len1, len2);
        return maxLen == 0 ? 1.0 : 1.0 - ((double)distance / maxLen);
    }

    public IReadOnlyList<SetInfo> GetAvailableSets()
    {
        return _readContext.Cards
            .AsNoTracking()
            .Select(c => new { c.SetCode, c.SetName })
            .Distinct()
            .OrderBy(s => s.SetName)
            .AsEnumerable()
            .Select(s => new SetInfo(s.SetCode, s.SetName))
            .ToList();
    }

    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
    {
        _logger.LogInformation("Calculating set completion for MTG");

        // Total cards per set (distinct collector numbers)
        var setTotals = _readContext.Cards
            .AsNoTracking()
            .GroupBy(c => new { c.SetCode, c.SetName })
            .Select(g => new { g.Key.SetCode, g.Key.SetName, Total = g.Select(c => c.CollectorNumber).Distinct().Count() })
            .ToDictionary(s => s.SetCode, s => (s.SetName, s.Total));

        // Owned cards per set (distinct collector numbers)
        var ownedPerSet = ownedCards
            .GroupBy(c => c.SetCode)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Number).Distinct().Count());

        var results = new List<SetCompletionSummary>();
        var processed = 0;

        foreach (var (setCode, (setName, total)) in setTotals)
        {
            ownedPerSet.TryGetValue(setCode, out var owned);
            results.Add(new SetCompletionSummary
            {
                SetCode = setCode,
                SetName = setName,
                OwnedCount = owned,
                TotalCount = total,
            });

            processed++;
            if (processed % 50 == 0)
                progress?.Report($"Calculating set completion... {processed}/{setTotals.Count} sets");
        }

        _logger.LogInformation("Set completion calculated: {Count} sets", results.Count);
        return Task.FromResult(results);
    }

    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers)
    {
        var ownedSet = ownedCollectorNumbers.ToHashSet();

        return _readContext.Cards
            .AsNoTracking()
            .Where(c => c.SetCode == setCode)
            .AsEnumerable()
            .Where(c => !ownedSet.Contains(c.CollectorNumber))
            .GroupBy(c => c.CollectorNumber).Select(g => g.First())
            .Select(c => new MissingCard
            {
                Name = c.Name,
                CollectorNumber = c.CollectorNumber,
                SetCode = c.SetCode,
                Rarity = c.Rarity,
                ImageUri = c.ImageUris?.Normal ?? c.ImageUris?.Small,
                LocalImagePath = c.LocalImagePath,
                TypeLine = c.TypeLine,
                ManaCost = c.ManaCost,
                OracleText = c.OracleText,
                Power = c.Power,
                Toughness = c.Toughness,
                Artist = c.Artist,
            })
            .OrderBy(m => m.CollectorNumber)
            .ToList();
    }

    private string? LookupCardName(Guid cardId)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().Where(c => c.Id == cardId).Select(c => c.Name).FirstOrDefault();
    }

    private CardMatch? LookupCard(Guid cardId, double? confidence = null)
    {
        var card = _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.Id == cardId);
        if (card is null) return null;

        return new CardMatch
        {
            Name = card.Name,
            SetCode = card.SetCode,
            SetName = card.SetName,
            CollectorNumber = card.CollectorNumber,
            Rarity = card.Rarity,
            ImageUri = card.ImageUris?.Normal ?? card.ImageUris?.Small,
            GameSpecificId = card.Id.ToString(),
            LocalImagePath = card.LocalImagePath,
            Confidence = confidence,
            Source = card
        };
    }

    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null)
    {
        _logger.LogInformation("Recording correction: hash {Hash:X16} (art: {ArtHash:X16}) → card {CardId}", scanHash, artScanHash, correctCardId);
        using var ctx = _dbContextFactory.CreateDbContext();

        // Look up identifying fields so the correction survives data refreshes
        var card = ctx.Cards.AsNoTracking()
            .Where(c => c.Id == Guid.Parse(correctCardId))
            .Select(c => new { c.Name, c.SetCode, c.CollectorNumber })
            .FirstOrDefault();

        // Build SQL with explicit NULLs to avoid EF Core DBNull type-mapping issues
        var artSql = artScanHash.HasValue ? $"{(long)artScanHash.Value}" : "NULL";
        var nameSql = card?.Name is not null ? $"'{card.Name.Replace("'", "''")}'" : "NULL";
        var setSql = card?.SetCode is not null ? $"'{card.SetCode.Replace("'", "''")}'" : "NULL";
        var numSql = card?.CollectorNumber is not null ? $"'{card.CollectorNumber.Replace("'", "''")}'" : "NULL";

        ctx.Database.ExecuteSqlRaw(
            $"INSERT OR REPLACE INTO HashCorrections (ScanHash, CorrectCardId, ArtScanHash, CardName, SetCode, CollectorNumber, CreatedAt) VALUES ({{0}}, {{1}}, {artSql}, {nameSql}, {setSql}, {numSql}, {{2}})",
            (long)scanHash, correctCardId, DateTime.UtcNow.ToString("o"));

        // Invalidate cache
        _correctionsCache = null;
    }

    private void RelinkOrphanedCorrections()
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        var corrections = ctx.HashCorrections.ToList();
        if (corrections.Count == 0) return;

        var relinked = 0;
        var removed = 0;

        foreach (var correction in corrections)
        {
            // Check if the current card ID still exists
            var exists = ctx.Cards.Any(c => c.Id == Guid.Parse(correction.CorrectCardId));
            if (exists) continue;

            // Try to re-link using identifying fields
            if (correction.CardName is not null && correction.SetCode is not null && correction.CollectorNumber is not null)
            {
                var newCard = ctx.Cards
                    .AsNoTracking()
                    .Where(c => c.Name == correction.CardName
                             && c.SetCode == correction.SetCode
                             && c.CollectorNumber == correction.CollectorNumber)
                    .Select(c => new { c.Id })
                    .FirstOrDefault();

                if (newCard is not null)
                {
                    correction.CorrectCardId = newCard.Id.ToString();
                    relinked++;
                    _logger.LogInformation("Re-linked correction #{Id}: {Name} [{Set} #{Num}] → {NewId}",
                        correction.Id, correction.CardName, correction.SetCode, correction.CollectorNumber, newCard.Id);
                    continue;
                }
            }

            // Can't re-link — remove the orphaned correction
            ctx.HashCorrections.Remove(correction);
            removed++;
            _logger.LogWarning("Removed orphaned correction #{Id} (card {CardId}, name: {Name})",
                correction.Id, correction.CorrectCardId, correction.CardName ?? "(unknown)");
        }

        if (relinked > 0 || removed > 0)
        {
            ctx.SaveChanges();
            _correctionsCache = null;
            _logger.LogInformation("Correction maintenance: {Relinked} re-linked, {Removed} removed", relinked, removed);
        }
    }

    /// <summary>
    /// Searches cards using Scryfall-style syntax. Supported prefixes:
    ///   name: or n:    — card name
    ///   set: s: or e:  — set code or set name
    ///   cn: or number: — collector number
    ///   t: or type:    — type line
    ///   o: or oracle:  — oracle text
    ///   r: or rarity:  — rarity (common, uncommon, rare, mythic)
    ///   c: or color:   — color identity (w, u, b, r, g)
    /// Plain text (no prefix) searches by name. Multiple terms are ANDed.
    /// Quoted values are supported: name:"lightning bolt"
    /// </summary>
    public List<CardMatch> SearchCards(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        _logger.LogDebug("Searching cards with query: {Query} (max: {MaxResults})", query, maxResults);
        var filters = ScryfallQueryParser.Parse(query);
        IQueryable<Card> cards = _readContext.Cards.AsNoTracking();

        foreach (var (field, value) in filters)
        {
            var v = value; // capture for closure
            cards = field switch
            {
                "name" => cards.Where(c => EF.Functions.Like(c.Name, $"%{v}%")),
                "set" => cards.Where(c => EF.Functions.Like(c.SetCode, $"%{v}%")
                                       || EF.Functions.Like(c.SetName, $"%{v}%")),
                "cn" => cards.Where(c => c.CollectorNumber == v),
                "type" => cards.Where(c => EF.Functions.Like(c.TypeLine, $"%{v}%")),
                "oracle" => cards.Where(c => c.OracleText != null
                                          && EF.Functions.Like(c.OracleText, $"%{v}%")),
                "rarity" => cards.Where(c => EF.Functions.Like(c.Rarity, $"%{v}%")),
                "color" => cards.Where(c => c.ColorIdentity.Contains(ScryfallQueryParser.ExpandColor(v))),
                _ => cards.Where(c => EF.Functions.Like(c.Name, $"%{v}%")),
            };
        }

        var results = cards.OrderBy(c => c.Name).Take(maxResults).ToList();
        _logger.LogDebug("Search returned {Count} results for query: {Query}", results.Count, query);
        return results.Select(c => new CardMatch
        {
            Name = c.Name,
            SetCode = c.SetCode,
            SetName = c.SetName,
            CollectorNumber = c.CollectorNumber,
            Rarity = c.Rarity,
            ImageUri = c.ImageUris?.Normal ?? c.ImageUris?.Small,
            GameSpecificId = c.Id.ToString(),
            LocalImagePath = c.LocalImagePath,
            Source = c
        }).ToList();
    }

    public List<CardMatch> GetPrintings(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName))
            return [];

        var results = _readContext.Cards
            .AsNoTracking()
            .Where(c => c.Name == cardName)
            .OrderBy(c => c.SetName)
            .ThenBy(c => c.CollectorNumber)
            .ToList();

        return results.Select(c => new CardMatch
        {
            Name = c.Name,
            SetCode = c.SetCode,
            SetName = c.SetName,
            CollectorNumber = c.CollectorNumber,
            Rarity = c.Rarity,
            ImageUri = c.ImageUris?.Normal ?? c.ImageUris?.Small,
            GameSpecificId = c.Id.ToString(),
            LocalImagePath = c.LocalImagePath,
            Source = c
        }).ToList();
    }

    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Scryfall bulk data download");
        var sw = Stopwatch.StartNew();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        // 1. Get the bulk data download URI
        progress?.Report("Fetching bulk data info...");
        _logger.LogDebug("Fetching bulk data metadata from Scryfall API");
        using var bulkDataResponse = await client.GetAsync(
            "https://api.scryfall.com/bulk-data/default_cards", ct);
        bulkDataResponse.EnsureSuccessStatusCode();
        var bulkData = await bulkDataResponse.Content.ReadFromJsonAsync<BulkDataInfo>(ScryfallJsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to fetch bulk data info from Scryfall.");
        _logger.LogInformation("Bulk data download URI obtained: {Uri}", bulkData.DownloadUri);

        // 2. Stream the card data
        progress?.Report("Downloading card data...");
        using var cardResponse = await client.GetAsync(bulkData.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead, ct);
        cardResponse.EnsureSuccessStatusCode();
        using var stream = await cardResponse.Content.ReadAsStreamAsync(ct);

        // 3. Upsert into database (preserves ImageHash / HashedIllustrationId)
        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        // Load existing IDs so we know which cards to update vs insert
        var existingIds = (await importContext.Cards
            .Select(c => c.Id)
            .ToListAsync(ct))
            .ToHashSet();

        var inserted = 0;
        var updated = 0;
        var batch = new List<Card>(1000);

        await foreach (var card in JsonSerializer.DeserializeAsyncEnumerable<Card>(stream, ScryfallJsonOptions, ct))
        {
            if (card is null) continue;

            // Filter by configured languages
            if (!_languages.Contains(card.Lang))
                continue;

            FlattenFrontFace(card);
            batch.Add(card);

            if (batch.Count >= 1000)
            {
                var (ins, upd) = await UpsertBatchAsync(importContext, batch, existingIds, ct);
                inserted += ins;
                updated += upd;
                batch.Clear();
                progress?.Report($"Processed {inserted + updated} cards ({inserted} new, {updated} updated)...");
            }
        }

        if (batch.Count > 0)
        {
            var (ins, upd) = await UpsertBatchAsync(importContext, batch, existingIds, ct);
            inserted += ins;
            updated += upd;
        }

        // Swap read context to pick up new data and invalidate hash cache
        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _artHashCache = null;
        _hashSetLookup = null;
        _hashCollectorNumberLookup = null;
        _symbolHashCache = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("Bulk data download complete: {Inserted} new, {Updated} updated in {ElapsedSec:F1}s", inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        // Re-link orphaned corrections whose card IDs no longer exist
        RelinkOrphanedCorrections();

        // Auto-hash new records (incremental only)
        if (inserted > 0)
        {
            _logger.LogInformation("Auto-computing hashes for {Count} newly added cards", inserted);
            await ComputeImageHashesAsync(forceAll: false, progress, ct);
        }
    }

    private async Task<(int Inserted, int Updated)> UpsertBatchAsync(
        ScryfallDbContext context, List<Card> batch, HashSet<Guid> existingIds, CancellationToken ct)
    {
        var newCards = new List<Card>();
        var existingCardIds = new List<Guid>();

        foreach (var card in batch)
        {
            if (existingIds.Contains(card.Id))
                existingCardIds.Add(card.Id);
            else
                newCards.Add(card);
        }

        // Update prices for existing cards
        if (existingCardIds.Count > 0)
        {
            var tracked = await context.Cards
                .Where(c => existingCardIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

            foreach (var card in batch)
            {
                if (tracked.TryGetValue(card.Id, out var existing))
                    existing.Prices = card.Prices;
            }

            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        // Insert new cards
        if (newCards.Count > 0)
        {
            foreach (var card in newCards)
                MapAllParts(card);

            context.Cards.AddRange(newCards);
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();

            foreach (var card in newCards)
                existingIds.Add(card.Id);
        }

        return (newCards.Count, existingCardIds.Count);
    }

    public async Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting image hash computation (forceAll: {ForceAll})", forceAll);
        var sw = Stopwatch.StartNew();

        await using var context = _dbContextFactory.CreateDbContext();

        // forceAll: re-download art and recompute ALL hashes
        // incremental: only cards missing a hash or with changed art
        var query = context.Cards.Where(c => c.ImageUris != null);
        if (!forceAll)
            query = query.Where(c => c.ImageHash == null || c.ArtHash == null || c.IllustrationId != c.HashedIllustrationId);

        var cards = await query
            .Select(c => new { c.Id, c.IllustrationId, c.SetCode, c.CollectorNumber, c.ImageUris!.Small, c.ImageUris!.Normal })
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} cards requiring hash computation", cards.Count);

        // Group by IllustrationId — hash one representative per unique illustration
        var groups = cards
            .GroupBy(c => c.IllustrationId ?? Guid.NewGuid()) // null IllustrationId = unique group each
            .Select(g => new
            {
                IllustrationId = g.First().IllustrationId,
                Representative = g.First(),
                AllCards = g.ToList(),
            })
            .ToList();

        _logger.LogInformation("Grouped into {GroupCount} unique illustrations from {CardCount} cards", groups.Count, cards.Count);
        progress?.Report($"Computing hashes for {groups.Count} unique illustrations ({cards.Count} cards)...");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        // Scryfall asks for <=10 requests/second; use a semaphore to throttle
        using var throttle = new SemaphoreSlim(8);
        var completed = 0;
        var failed = 0;

        // Process in parallel batches, save to DB every 100 illustrations
        var results = new List<(Guid? IllustrationId, List<(Guid Id, string SetCode, string CollectorNumber)> Cards, ulong Hash, ulong[] ArtHashes)>();
        var saveLock = new object();

        await Parallel.ForEachAsync(groups, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct
        }, async (group, token) =>
        {
            var rep = group.Representative;
            var imageUrl = rep.Normal ?? rep.Small;
            if (imageUrl is null)
            {
                Interlocked.Increment(ref failed);
                return;
            }

            try
            {
                await throttle.WaitAsync(token);
                try
                {
                    using var response = await client.GetAsync(imageUrl, token);
                    response.EnsureSuccessStatusCode();
                    var imageBytes = await response.Content.ReadAsByteArrayAsync(token);

                    // Save art to disk for the representative
                    var artFullPath = GetLocalArtFullPath(rep.SetCode, rep.CollectorNumber);
                    var artDir = Path.GetDirectoryName(artFullPath)!;
                    Directory.CreateDirectory(artDir);
                    await File.WriteAllBytesAsync(artFullPath, imageBytes, token);

                    // Compute full-card hash
                    using var buffer = new MemoryStream(imageBytes);
                    var hash = _hashService.ComputeHash(buffer);

                    // Compute art-region hash (best of all crop regions)
                    buffer.Position = 0;
                    var artHashes = _hashService.ComputeArtHash(buffer, ArtCropRegions);

                    var cardInfos = group.AllCards
                        .Select(c => (c.Id, c.SetCode, c.CollectorNumber))
                        .ToList();

                    lock (saveLock)
                    {
                        results.Add((group.IllustrationId, cardInfos, hash, artHashes));
                    }
                }
                finally
                {
                    throttle.Release();
                    // Brief delay to respect Scryfall rate limits
                    await Task.Delay(50, token);
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to process illustration for card {CardId}", rep.Id);
                Interlocked.Increment(ref failed);
            }

            var done = Interlocked.Increment(ref completed);
            if (done % 100 == 0)
                progress?.Report($"Processed {done}/{groups.Count} illustrations ({failed} failed)...");

            // Flush to DB periodically
            List<(Guid? IllustrationId, List<(Guid Id, string SetCode, string CollectorNumber)> Cards, ulong Hash, ulong[] ArtHashes)>? toSave = null;
            lock (saveLock)
            {
                if (results.Count >= 100)
                {
                    toSave = [.. results];
                    results.Clear();
                }
            }

            if (toSave is not null)
                await SaveArtHashBatchAsync(toSave, ct);
        });

        // Save remaining
        if (results.Count > 0)
            await SaveArtHashBatchAsync(results, ct);

        // Refresh read context and invalidate hash cache
        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _artHashCache = null;
        _hashSetLookup = null;
        _hashCollectorNumberLookup = null;
        _symbolHashCache = null;
        oldContext.Dispose();

        // Phase 2: Set symbol hash computation
        progress?.Report("Computing set symbol hashes...");
        await ComputeSymbolHashesAsync(ct);

        sw.Stop();
        _logger.LogInformation("Hash computation complete: {Processed} illustrations, {Failed} failed in {ElapsedSec:F1}s", completed - failed, failed, sw.Elapsed.TotalSeconds);
        progress?.Report($"Done \u2014 processed {completed - failed} illustrations ({failed} failed).");
    }

    private async Task SaveArtHashBatchAsync(
        List<(Guid? IllustrationId, List<(Guid Id, string SetCode, string CollectorNumber)> Cards, ulong Hash, ulong[] ArtHashes)> batch,
        CancellationToken ct)
    {
        await using var context = _dbContextFactory.CreateDbContext();
        foreach (var (illustrationId, cards, hash, artHashes) in batch)
        {
            // Pick the best art hash — use the first (modern frame) as default
            // since Scryfall reference images use modern framing
            ulong? bestArtHash = artHashes.Length > 0 ? artHashes[0] : null;

            var repCard = cards[0];
            var artRelativePath = GetLocalArtRelativePath(repCard.SetCode, repCard.CollectorNumber);

            foreach (var (id, setCode, collectorNumber) in cards)
            {
                // All cards in the group point to the same art file (the representative's)
                await context.Cards
                    .Where(c => c.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.ImageHash, hash)
                        .SetProperty(c => c.ArtHash, bestArtHash)
                        .SetProperty(c => c.HashedIllustrationId, illustrationId)
                        .SetProperty(c => c.LocalImagePath, artRelativePath), ct);
            }
        }
    }

    private async Task ComputeSymbolHashesAsync(CancellationToken ct)
    {
        var setCodes = _readContext.Cards
            .AsNoTracking()
            .Select(c => c.SetCode)
            .Distinct()
            .ToList();

        _logger.LogInformation("Computing symbol hashes for {Count} sets", setCodes.Count);

        await using var context = _dbContextFactory.CreateDbContext();
        var computed = 0;

        foreach (var setCode in setCodes)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                using var bmp = await _symbolCache.RasterizeSymbolAsync(setCode);
                if (bmp is null) continue;

                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var hash = _hashService.ComputeHash(ms);

                await context.Database.ExecuteSqlRawAsync(
                    "INSERT OR REPLACE INTO SetSymbolHashes (SetCode, ImageHash) VALUES ({0}, {1})",
                    setCode, (long)hash);
                computed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute symbol hash for {SetCode}", setCode);
            }
        }

        _symbolHashCache = null; // Invalidate cache
        _logger.LogInformation("Computed {Count} set symbol hashes", computed);
    }

    public Dictionary<string, ulong> GetSymbolHashes() => LoadSymbolHashCache();

    private Dictionary<string, ulong> LoadSymbolHashCache()
    {
        if (_symbolHashCache is null)
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            _symbolHashCache = ctx.SetSymbolHashes
                .AsNoTracking()
                .ToDictionary(s => s.SetCode, s => s.ImageHash);
            _logger.LogInformation("Symbol hash cache loaded with {Count} entries", _symbolHashCache.Count);
        }
        return _symbolHashCache;
    }

    internal static void FlattenFrontFace(Card card)
    {
        if (card.CardFaces is not { Count: > 0 })
            return;

        var front = card.CardFaces[0];
        card.ManaCost ??= front.ManaCost;
        card.OracleText ??= front.OracleText;
        card.Colors ??= front.Colors;
        card.ColorIndicator ??= front.ColorIndicator;
        card.Power ??= front.Power;
        card.Toughness ??= front.Toughness;
        card.Loyalty ??= front.Loyalty;
        card.Defense ??= front.Defense;
        card.FlavorText ??= front.FlavorText;
        card.Artist ??= front.Artist;
        card.IllustrationId ??= front.IllustrationId;
        card.ImageUris ??= front.ImageUris;
        card.Watermark ??= front.Watermark;
        card.PrintedName ??= front.PrintedName;
        card.PrintedTypeLine ??= front.PrintedTypeLine;
        card.PrintedText ??= front.PrintedText;

        if (card.ArtistIds is null && front.ArtistId.HasValue)
            card.ArtistIds = [front.ArtistId.Value];

        card.CardFaces = null;
    }

    internal static void MapAllParts(Card card)
    {
        if (card.AllParts is not { Count: > 0 })
            return;

        foreach (var entry in card.AllParts)
        {
            card.RelatedCards.Add(new RelatedCard
            {
                CardId = card.Id,
                ScryfallId = entry.Id,
                Component = entry.Component,
                Name = entry.Name,
                TypeLine = entry.TypeLine,
                Uri = entry.Uri
            });
        }

        card.AllParts = null;
    }

    public decimal? GetCurrentPrice(string gameCardId, bool isFoil)
    {
        if (!Guid.TryParse(gameCardId, out var id))
            return null;

        using var ctx = _dbContextFactory.CreateDbContext();
        var prices = ctx.Cards.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => c.Prices)
            .FirstOrDefault();

        if (prices is null)
            return null;

        var priceStr = isFoil ? prices.UsdFoil : prices.Usd;
        return decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : null;
    }

    public object? FindCardById(string gameCardId)
    {
        if (!Guid.TryParse(gameCardId, out var guid))
            return null;
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().FirstOrDefault(c => c.Id == guid);
    }

    public void Dispose() => _readContext.Dispose();

    private static string GetLocalArtRelativePath(string setCode, string collectorNumber)
    {
        return $"art/{setCode}/{collectorNumber}.jpg";
    }

    private string GetLocalArtFullPath(string setCode, string collectorNumber)
    {
        return Path.Combine(_dataDirectory, "art", setCode, $"{collectorNumber}.jpg");
    }

    private class BulkDataInfo
    {
        public string DownloadUri { get; set; } = "";
    }
}
