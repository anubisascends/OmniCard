using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public sealed class OptcgService : ICardGameService, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<OptcgDbContext> _dbContextFactory;
    private readonly IPerceptualHashService _hashService;
    private readonly ILogger<OptcgService> _logger;
    private OptcgDbContext _readContext;

    public OptcgService(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<OptcgDbContext> dbContextFactory,
        IPerceptualHashService hashService,
        ILogger<OptcgService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _hashService = hashService;
        _logger = logger;

        _logger.LogInformation("Initializing OPTCG service");
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
        _logger.LogInformation("OPTCG database ready at {DbPath}", dbPath);
    }

    public CardGame Game => CardGame.OnePiece;

    private List<(string CardSetId, ulong Hash)>? _hashCache;
    private Dictionary<string, string>? _hashSetLookup;
    private List<(ulong ScanHash, string CorrectCardId)>? _correctionsCache;
    private const int CorrectionTrustBonus = 5;

    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting OPTCG card data download");
        var sw = Stopwatch.StartNew();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        progress?.Report("Downloading OPTCG set cards...");
        _logger.LogDebug("Fetching all set cards from OPTCG API");

        var jsonOptions = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var cards = await client.GetFromJsonAsync<List<OptcgCard>>(
            "https://www.optcgapi.com/api/allSetCards/", jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to fetch card data from OPTCG API.");

        // API can return duplicate CardSetIds (alternate art variants); keep the last occurrence
        var deduped = cards
            .GroupBy(c => c.CardSetId)
            .Select(g => g.Last())
            .ToList();
        _logger.LogInformation("Downloaded {Count} cards from OPTCG API ({Unique} unique)", cards.Count, deduped.Count);
        cards = deduped;
        progress?.Report($"Downloaded {cards.Count} unique cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingIds = (await importContext.Cards
            .Select(c => c.CardSetId)
            .ToListAsync(ct))
            .ToHashSet();

        var inserted = 0;
        var updated = 0;

        // Process in batches of 500
        foreach (var batch in cards.Chunk(500))
        {
            var newCards = new List<OptcgCard>();
            var existingCardIds = new List<string>();

            foreach (var card in batch)
            {
                if (existingIds.Contains(card.CardSetId))
                    existingCardIds.Add(card.CardSetId);
                else
                    newCards.Add(card);
            }

            // Update prices for existing cards
            if (existingCardIds.Count > 0)
            {
                var tracked = await importContext.Cards
                    .Where(c => existingCardIds.Contains(c.CardSetId))
                    .ToDictionaryAsync(c => c.CardSetId, ct);

                foreach (var card in batch)
                {
                    if (tracked.TryGetValue(card.CardSetId, out var existing))
                    {
                        existing.InventoryPrice = card.InventoryPrice;
                        existing.MarketPrice = card.MarketPrice;
                        existing.DateScraped = card.DateScraped;
                    }
                }

                await importContext.SaveChangesAsync(ct);
                importContext.ChangeTracker.Clear();
                updated += existingCardIds.Count;
            }

            // Insert new cards
            if (newCards.Count > 0)
            {
                importContext.Cards.AddRange(newCards);
                await importContext.SaveChangesAsync(ct);
                importContext.ChangeTracker.Clear();

                foreach (var card in newCards)
                    existingIds.Add(card.CardSetId);

                inserted += newCards.Count;
            }

            progress?.Report($"Processed {inserted + updated} cards ({inserted} new, {updated} updated)...");
        }

        // Swap read context and invalidate hash cache
        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("OPTCG download complete: {Inserted} new, {Updated} updated in {ElapsedSec:F1}s", inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        // Auto-hash new records (incremental only)
        if (inserted > 0)
        {
            _logger.LogInformation("Auto-computing hashes for {Count} newly added cards", inserted);
            await ComputeImageHashesAsync(forceAll: false, progress, ct);
        }
    }

    public async Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting OPTCG image hash computation (forceAll: {ForceAll})", forceAll);
        var sw = Stopwatch.StartNew();

        await using var context = _dbContextFactory.CreateDbContext();

        var query = context.Cards.Where(c => c.CardImageUri != null);
        if (!forceAll)
            query = query.Where(c => c.ImageHash == null);

        var cards = await query
            .Select(c => new { c.CardSetId, c.CardImageUri })
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} OPTCG cards requiring hash computation", cards.Count);
        progress?.Report($"Computing hashes for {cards.Count} cards...");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        using var throttle = new SemaphoreSlim(8);
        var completed = 0;
        var failed = 0;

        var results = new List<(string CardSetId, ulong Hash)>();
        var saveLock = new object();

        await Parallel.ForEachAsync(cards, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct
        }, async (card, token) =>
        {
            if (card.CardImageUri is null)
            {
                Interlocked.Increment(ref failed);
                return;
            }

            try
            {
                await throttle.WaitAsync(token);
                try
                {
                    using var response = await client.GetAsync(card.CardImageUri, token);
                    response.EnsureSuccessStatusCode();
                    using var imageStream = await response.Content.ReadAsStreamAsync(token);
                    using var buffer = new MemoryStream();
                    await imageStream.CopyToAsync(buffer, token);
                    buffer.Position = 0;

                    var hash = _hashService.ComputeHash(buffer);

                    lock (saveLock)
                    {
                        results.Add((card.CardSetId, hash));
                    }
                }
                finally
                {
                    throttle.Release();
                    await Task.Delay(50, token);
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to compute hash for OPTCG card {CardSetId}", card.CardSetId);
                Interlocked.Increment(ref failed);
            }

            var done = Interlocked.Increment(ref completed);
            if (done % 100 == 0)
                progress?.Report($"Hashed {done}/{cards.Count} cards ({failed} failed)...");

            List<(string CardSetId, ulong Hash)>? toSave = null;
            lock (saveLock)
            {
                if (results.Count >= 200)
                {
                    toSave = [.. results];
                    results.Clear();
                }
            }

            if (toSave is not null)
                await SaveHashBatchAsync(toSave, ct);
        });

        if (results.Count > 0)
            await SaveHashBatchAsync(results, ct);

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("OPTCG hash computation complete: {Hashed} hashed, {Failed} failed in {ElapsedSec:F1}s", completed - failed, failed, sw.Elapsed.TotalSeconds);
        progress?.Report($"Done — hashed {completed - failed} cards ({failed} failed).");
    }

    private async Task SaveHashBatchAsync(List<(string CardSetId, ulong Hash)> batch, CancellationToken ct)
    {
        await using var context = _dbContextFactory.CreateDbContext();
        foreach (var (cardSetId, hash) in batch)
        {
            await context.Cards
                .Where(c => c.CardSetId == cardSetId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ImageHash, hash), ct);
        }
    }

    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 10)
    {
        _logger.LogDebug("Finding closest OPTCG match for pHash {Hash:X16} (set filter: {SetFilter}, max distance: {MaxDistance})", imageHash, setFilter is not null ? string.Join(",", setFilter) : "none", maxDistance);

        if (_hashCache is null)
        {
            _logger.LogInformation("Building OPTCG in-memory hash cache from database");
            var entries = _readContext.Cards
                .Where(c => c.ImageHash != null)
                .Select(c => new { c.CardSetId, Hash = c.ImageHash!.Value, c.SetId })
                .AsNoTracking()
                .AsEnumerable()
                .ToList();
            _hashCache = entries.Select(c => (c.CardSetId, c.Hash)).ToList();
            _hashSetLookup = entries.ToDictionary(c => c.CardSetId, c => c.SetId);
            _logger.LogInformation("OPTCG hash cache loaded with {Count} entries", _hashCache.Count);
        }

        if (_correctionsCache is null)
        {
            _logger.LogInformation("Building OPTCG corrections cache from database");
            using var ctx = _dbContextFactory.CreateDbContext();
            _correctionsCache = ctx.HashCorrections
                .AsNoTracking()
                .Select(h => new { h.ScanHash, h.CorrectCardId })
                .AsEnumerable()
                .Select(h => (h.ScanHash, h.CorrectCardId))
                .ToList();
            _logger.LogInformation("OPTCG corrections cache loaded with {Count} entries", _correctionsCache.Count);
        }

        // Phase 1: Exact correction
        var exactCorrection = _correctionsCache.FirstOrDefault(c => c.ScanHash == imageHash);
        if (exactCorrection.CorrectCardId is not null)
        {
            var correctedCard = LookupOptcgCard(exactCorrection.CorrectCardId, confidence: 100);
            if (correctedCard is not null)
            {
                if (setFilter is null || setFilter.Contains(correctedCard.SetCode))
                {
                    _logger.LogDebug("Exact OPTCG correction found for hash {Hash:X16} → {CardId}", imageHash, exactCorrection.CorrectCardId);
                    return correctedCard;
                }
                _logger.LogDebug("Exact OPTCG correction for hash {Hash:X16} → {CardId} rejected by set filter (set {Set})", imageHash, exactCorrection.CorrectCardId, correctedCard.SetCode);
            }
        }

        // Phase 2: Combined fuzzy search
        if (_hashCache.Count == 0)
        {
            _logger.LogWarning("OPTCG hash cache is empty, no cards to match against");
            return null;
        }

        string bestPHashId = "";
        int bestPHashDistance = int.MaxValue;

        foreach (var (cardSetId, hash) in _hashCache)
        {
            if (setFilter is not null && !setFilter.Contains(_hashSetLookup![cardSetId]))
                continue;

            var distance = PerceptualHashService.HammingDistance(imageHash, hash);
            if (distance < bestPHashDistance)
            {
                bestPHashDistance = distance;
                bestPHashId = cardSetId;
            }
        }

        string? bestCorrectionCardId = null;
        int bestCorrectionAdjusted = int.MaxValue;

        foreach (var (scanHash, correctCardId) in _correctionsCache)
        {
            // Resolve correction's set from hashSetLookup; skip if outside set filter
            if (setFilter is not null)
            {
                var corrSet = _hashSetLookup!.GetValueOrDefault(correctCardId);
                if (corrSet is null || !setFilter.Contains(corrSet))
                    continue;
            }

            var distance = PerceptualHashService.HammingDistance(imageHash, scanHash);
            if (distance <= maxDistance)
            {
                var adjusted = Math.Max(0, distance - CorrectionTrustBonus);
                if (adjusted < bestCorrectionAdjusted)
                {
                    bestCorrectionAdjusted = adjusted;
                    bestCorrectionCardId = correctCardId;
                }
            }
        }

        if (bestCorrectionCardId is not null && bestCorrectionAdjusted <= bestPHashDistance)
        {
            _logger.LogDebug("Fuzzy OPTCG correction wins: {CardId} (adjusted distance {Distance})", bestCorrectionCardId, bestCorrectionAdjusted);
            var correctionConfidence = Math.Max(0, (1.0 - (double)bestCorrectionAdjusted / maxDistance)) * 100;
            var correctedCard = LookupOptcgCard(bestCorrectionCardId, correctionConfidence);
            if (correctedCard is not null)
                return correctedCard;
        }

        // Phase 3: Threshold check
        if (bestPHashDistance > maxDistance)
        {
            _logger.LogDebug("Best OPTCG match distance {Distance} exceeds max {MaxDistance}, no match", bestPHashDistance, maxDistance);
            return null;
        }

        // Safety net: never return a card outside the selected set filter
        if (setFilter is not null)
        {
            var bestSetCode = _hashSetLookup!.GetValueOrDefault(bestPHashId);
            if (bestSetCode is null || !setFilter.Contains(bestSetCode))
            {
                _logger.LogDebug("Best OPTCG match {CardId} is in set {Set} which is outside the set filter; rejecting", bestPHashId, bestSetCode ?? "(unknown)");
                return null;
            }
        }

        var card = _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.CardSetId == bestPHashId);
        _logger.LogDebug("Best OPTCG match: {CardName} with Hamming distance {Distance}", card?.CardName, bestPHashDistance);
        if (card is null) return null;

        var confidence = Math.Max(0, (1.0 - (double)bestPHashDistance / maxDistance)) * 100;
        return new CardMatch
        {
            Name = card.CardName,
            SetCode = card.SetId,
            SetName = card.SetName,
            CollectorNumber = card.CardSetId,
            Rarity = card.Rarity,
            ImageUri = card.CardImageUri,
            GameSpecificId = card.CardSetId,
            Confidence = confidence,
            Source = card
        };
    }

    public IReadOnlyList<SetInfo> GetAvailableSets()
    {
        return _readContext.Cards
            .AsNoTracking()
            .Select(c => new { c.SetId, c.SetName })
            .Distinct()
            .OrderBy(s => s.SetName)
            .AsEnumerable()
            .Select(s => new SetInfo(s.SetId, s.SetName))
            .ToList();
    }

    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
    {
        _logger.LogInformation("Calculating set completion for OPTCG");

        // Total cards per set (OPTCG uses CardSetId as unique key, SetId as set code)
        var setTotals = _readContext.Cards
            .AsNoTracking()
            .GroupBy(c => new { c.SetId, c.SetName })
            .Select(g => new { g.Key.SetId, g.Key.SetName, Total = g.Count() })
            .ToDictionary(s => s.SetId, s => (s.SetName, s.Total));

        // Owned cards per set (CollectionCard.SetCode stores SetId for OPTCG,
        // CollectionCard.Number stores CardSetId)
        var ownedPerSet = ownedCards
            .GroupBy(c => c.SetCode)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Number).Distinct().Count());

        var results = new List<SetCompletionSummary>();
        var processed = 0;

        foreach (var (setId, (setName, total)) in setTotals)
        {
            ownedPerSet.TryGetValue(setId, out var owned);
            results.Add(new SetCompletionSummary
            {
                SetCode = setId,
                SetName = setName,
                OwnedCount = owned,
                TotalCount = total,
            });

            processed++;
            if (processed % 50 == 0)
                progress?.Report($"Calculating set completion... {processed}/{setTotals.Count} sets");
        }

        _logger.LogInformation("OPTCG set completion calculated: {Count} sets", results.Count);
        return Task.FromResult(results);
    }

    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers)
    {
        var ownedSet = ownedCollectorNumbers.ToHashSet();

        return _readContext.Cards
            .AsNoTracking()
            .Where(c => c.SetId == setCode)
            .AsEnumerable()
            .Where(c => !ownedSet.Contains(c.CardSetId))
            .Select(c => new MissingCard
            {
                Name = c.CardName,
                CollectorNumber = c.CardSetId,
                SetCode = c.SetId,
                Rarity = c.Rarity,
                ImageUri = c.CardImageUri,
                TypeLine = c.CardType,
                OracleText = c.CardText,
                Power = c.CardPower,
                CardColor = c.CardColor,
                CardCost = c.CardCost,
            })
            .OrderBy(m => m.CollectorNumber)
            .ToList();
    }

    private CardMatch? LookupOptcgCard(string cardSetId, double? confidence = null)
    {
        var card = _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.CardSetId == cardSetId);
        if (card is null) return null;

        return new CardMatch
        {
            Name = card.CardName,
            SetCode = card.SetId,
            SetName = card.SetName,
            CollectorNumber = card.CardSetId,
            Rarity = card.Rarity,
            ImageUri = card.CardImageUri,
            GameSpecificId = card.CardSetId,
            Confidence = confidence,
            Source = card
        };
    }

    public List<CardMatch> SearchCards(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        _logger.LogDebug("Searching OPTCG cards with query: {Query} (max: {MaxResults})", query, maxResults);
        IQueryable<OptcgCard> cards = _readContext.Cards.AsNoTracking();

        // Simple search: filter by name, set, color, or type
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            var t = term;
            if (t.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[4..];
                cards = cards.Where(c => EF.Functions.Like(c.SetId, $"%{val}%")
                                       || EF.Functions.Like(c.SetName, $"%{val}%"));
            }
            else if (t.StartsWith("color:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("c:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t.Contains(':') ? t[(t.IndexOf(':') + 1)..] : t;
                cards = cards.Where(c => EF.Functions.Like(c.CardColor, $"%{val}%"));
            }
            else if (t.StartsWith("type:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t.Contains(':') ? t[(t.IndexOf(':') + 1)..] : t;
                cards = cards.Where(c => EF.Functions.Like(c.CardType, $"%{val}%"));
            }
            else if (t.StartsWith("power:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[6..];
                cards = cards.Where(c => c.CardPower == val);
            }
            else if (t.StartsWith("cost:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[5..];
                cards = cards.Where(c => c.CardCost == val);
            }
            else
            {
                cards = cards.Where(c => EF.Functions.Like(c.CardName, $"%{t}%"));
            }
        }

        var results = cards.OrderBy(c => c.CardName).Take(maxResults).ToList();
        _logger.LogDebug("OPTCG search returned {Count} results for query: {Query}", results.Count, query);
        return results.Select(c => new CardMatch
        {
            Name = c.CardName,
            SetCode = c.SetId,
            SetName = c.SetName,
            CollectorNumber = c.CardSetId,
            Rarity = c.Rarity,
            ImageUri = c.CardImageUri,
            GameSpecificId = c.CardSetId,
            Source = c
        }).ToList();
    }

    public List<CardMatch> GetPrintings(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName))
            return [];

        var results = _readContext.Cards
            .AsNoTracking()
            .Where(c => c.CardName == cardName)
            .OrderBy(c => c.SetName)
            .ThenBy(c => c.CardSetId)
            .ToList();

        return results.Select(c => new CardMatch
        {
            Name = c.CardName,
            SetCode = c.SetId,
            SetName = c.SetName,
            CollectorNumber = c.CardSetId,
            Rarity = c.Rarity,
            ImageUri = c.CardImageUri,
            GameSpecificId = c.CardSetId,
            Source = c
        }).ToList();
    }

    public decimal? GetCurrentPrice(string gameCardId, bool isFoil)
    {
        return _readContext.Cards.AsNoTracking()
            .Where(c => c.CardSetId == gameCardId)
            .Select(c => c.MarketPrice)
            .FirstOrDefault();
    }

    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)
    {
        var ids = gameCardIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var result = new Dictionary<string, decimal>(ids.Count);

        foreach (var chunk in ids.Chunk(500))
        {
            var rows = _readContext.Cards.AsNoTracking()
                .Where(c => chunk.Contains(c.CardSetId))
                .Select(c => new { c.CardSetId, c.MarketPrice })
                .ToList();

            foreach (var row in rows)
            {
                if (row.MarketPrice.HasValue)
                    result[row.CardSetId] = row.MarketPrice.Value;
            }
        }

        return result;
    }

    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null)
    {
        _logger.LogInformation("Recording OPTCG correction: hash {Hash:X16} → card {CardId}", scanHash, correctCardId);
        using var ctx = _dbContextFactory.CreateDbContext();

        ctx.Database.ExecuteSqlRaw(
            "INSERT OR REPLACE INTO HashCorrections (ScanHash, CorrectCardId, CreatedAt) VALUES ({0}, {1}, {2})",
            (long)scanHash, correctCardId, DateTime.UtcNow.ToString("o"));

        _correctionsCache = null;
    }

    public object? FindCardById(string gameCardId)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().FirstOrDefault(c => c.CardSetId == gameCardId);
    }

    public void Dispose() => _readContext.Dispose();
}
