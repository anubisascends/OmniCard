using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.CardMatching;

public sealed class OptcgService : ICardGameService, IDisposable
{
    private const string ApiBaseUrl = "https://api.poneglyph.one";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<OptcgDbContext> _dbContextFactory;
    private readonly IPerceptualHashService _hashService;
    private readonly ILogger<OptcgService> _logger;
    private readonly string _dataDirectory;
    private OptcgDbContext _readContext;

    public OptcgService(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<OptcgDbContext> dbContextFactory,
        IPerceptualHashService hashService,
        IDataPathService dataPathService,
        ILogger<OptcgService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _hashService = hashService;
        _dataDirectory = dataPathService.DataDirectory;
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
        _readContext.ApplySchemaUpgrades();

        if (_readContext.GetSchemaVersion() < OptcgDbContext.PoneglyphSchemaVersion)
        {
            _logger.LogWarning("OPTCG database predates api.poneglyph.one; wiping for migration");
            WipeForMigration();
        }

        _logger.LogInformation("OPTCG database ready at {DbPath}", dbPath);
    }

    private void WipeForMigration()
    {
        try
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            ctx.Database.ExecuteSqlRaw("DELETE FROM Cards");
            ctx.Database.ExecuteSqlRaw("DELETE FROM HashCorrections");
        }
        catch (SqliteException ex) when (ex.Message.Contains("readonly"))
        {
            // Read-only consumer (e.g. the Web app) hitting a not-yet-migrated DB.
            // Skip the wipe and serve the stale data as-is rather than crashing.
            _logger.LogWarning(ex, "OPTCG database is read-only; skipping migration wipe");
        }

        var artDir = Path.Combine(_dataDirectory, "optcg-art");
        if (Directory.Exists(artDir))
        {
            try
            {
                Directory.Delete(artDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete OPTCG art directory during migration wipe");
            }
        }

        // Refresh the read context and drop in-memory caches so nothing stale survives.
        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        _correctionsCache = null;
        oldContext.Dispose();

        _logger.LogInformation("OPTCG migration wipe complete");
    }

    public CardGame Game => CardGame.OnePiece;

    public MatchDiagnostics? LastMatchDiagnostics { get; private set; }

    private List<(string CardSetId, ulong Hash)>? _hashCache;
    private List<(string CardSetId, ulong EdgeHash, string SetId)>? _edgeHashCache;
    private Dictionary<string, string>? _hashSetLookup;
    private List<(ulong ScanHash, string CorrectCardId)>? _correctionsCache;
    private const int CorrectionTrustBonus = 5;

    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting OPTCG card data download from poneglyph API");
        var sw = Stopwatch.StartNew();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        progress?.Report("Fetching OPTCG set list...");
        var allCards = await FetchAllVariantsAsync(client, jsonOptions,
            (done, total, _) => progress?.Report($"Fetched {done}/{total} sets..."), ct);

        // Dedupe defensively on the variant uid (primary key).
        var deduped = allCards
            .GroupBy(c => c.CardSetId)
            .Select(g => g.Last())
            .ToList();
        _logger.LogInformation("Fetched {Total} variant rows ({Unique} unique)", allCards.Count, deduped.Count);
        progress?.Report($"Fetched {deduped.Count} cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingIds = (await importContext.Cards
            .Select(c => c.CardSetId)
            .ToListAsync(ct))
            .ToHashSet();

        var inserted = 0;
        var updated = 0;

        foreach (var batch in deduped.Chunk(500))
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

            // Update all metadata (not just price) for existing rows, preserving
            // computed ImageHash / LocalImagePath.
            if (existingCardIds.Count > 0)
            {
                var tracked = await importContext.Cards
                    .Where(c => existingCardIds.Contains(c.CardSetId))
                    .ToDictionaryAsync(c => c.CardSetId, ct);

                foreach (var card in batch)
                {
                    if (tracked.TryGetValue(card.CardSetId, out var existing))
                    {
                        existing.CardNumber = card.CardNumber;
                        existing.VariantIndex = card.VariantIndex;
                        existing.VariantLabel = card.VariantLabel;
                        existing.Artist = card.Artist;
                        existing.CardName = card.CardName;
                        existing.SetId = card.SetId;
                        existing.SetName = card.SetName;
                        existing.Rarity = card.Rarity;
                        existing.CardColor = card.CardColor;
                        existing.CardType = card.CardType;
                        existing.CardCost = card.CardCost;
                        existing.CardPower = card.CardPower;
                        existing.Life = card.Life;
                        existing.CardText = card.CardText;
                        existing.SubTypes = card.SubTypes;
                        existing.Attribute = card.Attribute;
                        existing.CounterAmount = card.CounterAmount;
                        existing.InventoryPrice = card.InventoryPrice;
                        existing.MarketPrice = card.MarketPrice;
                        existing.CardImageUri = card.CardImageUri;
                        existing.DateScraped = card.DateScraped;
                    }
                }

                await importContext.SaveChangesAsync(ct);
                importContext.ChangeTracker.Clear();
                updated += existingCardIds.Count;
            }

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

        // Migration complete: stamp the version so future launches skip the wipe.
        // Only stamp when we actually imported data, so a total per-set fetch
        // failure leaves the DB unmigrated and eligible for re-download.
        if (deduped.Count > 0)
            importContext.MarkMigrationComplete();

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("OPTCG download complete: {Inserted} new, {Updated} updated in {ElapsedSec:F1}s", inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        if (inserted > 0)
        {
            _logger.LogInformation("Auto-computing hashes for {Count} newly added cards", inserted);
            await ComputeImageHashesAsync(forceAll: false, progress, ct);
        }
    }

    // Fetches the full set list and, for each set, its card variants (mapped). Invokes
    // onSetCompleted(done, total, setCode) after each set finishes. Shared by the full
    // download and the price-only refresh.
    private async Task<List<OptcgCard>> FetchAllVariantsAsync(
        HttpClient client, JsonSerializerOptions jsonOptions,
        Action<int, int, string>? onSetCompleted, CancellationToken ct)
    {
        var setList = await client.GetFromJsonAsync<OptcgSetListResponse>(
            $"{ApiBaseUrl}/v1/sets", jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to fetch set list from poneglyph API.");

        _logger.LogInformation("Discovered {Count} OPTCG sets", setList.Data.Count);

        var allCards = new List<OptcgCard>();
        var cardsLock = new object();
        var fetchedSets = 0;

        await Parallel.ForEachAsync(setList.Data, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        }, async (set, token) =>
        {
            try
            {
                var detail = await client.GetFromJsonAsync<OptcgSetDetailResponse>(
                    $"{ApiBaseUrl}/v1/sets/{set.Code}", jsonOptions, token);
                if (detail is null)
                {
                    _logger.LogWarning("Set {SetCode} returned no detail; skipping", set.Code);
                    return;
                }

                var rows = detail.Data.Cards
                    .SelectMany(card => card.Variants.Select(v => MapVariant(card, v)))
                    .ToList();

                lock (cardsLock)
                    allCards.AddRange(rows);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch OPTCG set {SetCode}; skipping", set.Code);
            }
            finally
            {
                var done = Interlocked.Increment(ref fetchedSets);
                onSetCompleted?.Invoke(done, setList.Data.Count, set.Code);
            }
        });

        return allCards;
    }

    public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting OPTCG price-only refresh");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var allCards = await FetchAllVariantsAsync(client, jsonOptions,
            (done, total, setCode) => progress?.Report(
                new PriceUpdateProgress(CardGame.OnePiece, setCode, done, total,
                    $"One Piece prices: {done}/{total} sets")), ct);

        var deduped = allCards
            .GroupBy(c => c.CardSetId)
            .Select(g => g.Last())
            .ToList();

        await using var context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();

        var updated = 0;
        foreach (var batch in deduped.Chunk(500))
        {
            var ids = batch.Select(c => c.CardSetId).ToList();
            var tracked = await context.Cards
                .Where(c => ids.Contains(c.CardSetId))
                .ToDictionaryAsync(c => c.CardSetId, ct);

            foreach (var card in batch)
            {
                if (tracked.TryGetValue(card.CardSetId, out var existing))
                {
                    existing.MarketPrice = card.MarketPrice;
                    existing.InventoryPrice = card.InventoryPrice;
                    updated++;
                }
            }

            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        // Swap the read context so reads see the refreshed prices.
        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        oldContext.Dispose();

        _logger.LogInformation("OPTCG price refresh complete: {Updated} cards updated", updated);
        progress?.Report(new PriceUpdateProgress(CardGame.OnePiece, null, 0, 0,
            $"One Piece prices updated ({updated} cards)"));
    }

    private static OptcgCard MapVariant(OptcgApiCard card, OptcgApiVariant variant)
    {
        var uid = variant.Index == 0 ? card.CardNumber : $"{card.CardNumber}_p{variant.Index}";

        var imageUri = variant.Images.Scan.Display
            ?? variant.Images.Scan.Full
            ?? variant.Images.Stock.Full
            ?? variant.Images.Stock.Thumb;

        return new OptcgCard
        {
            CardSetId = uid,
            CardNumber = card.CardNumber,
            VariantIndex = variant.Index,
            VariantLabel = variant.Label,
            Artist = variant.Artist,
            CardName = card.Name,
            SetId = card.Set,
            SetName = card.SetName,
            Rarity = card.Rarity ?? "",
            CardColor = string.Join("/", card.Color),
            CardType = card.CardType,
            CardCost = card.Cost?.ToString(),
            CardPower = card.Power?.ToString(),
            Life = card.Life?.ToString(),
            CardText = card.Effect,
            SubTypes = card.Types.Count > 0 ? string.Join("/", card.Types) : null,
            Attribute = card.Attribute is { Count: > 0 } ? string.Join("/", card.Attribute) : null,
            CounterAmount = card.Counter,
            MarketPrice = ParsePrice(variant.Market.MarketPrice),
            InventoryPrice = ParsePrice(variant.Market.LowPrice),
            CardImageUri = imageUri,
            DateScraped = DateTime.UtcNow.ToString("o"),
        };
    }

    private static decimal? ParsePrice(string? raw) =>
        decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public async Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting OPTCG image hash computation (forceAll: {ForceAll})", forceAll);
        var sw = Stopwatch.StartNew();

        await using var context = _dbContextFactory.CreateDbContext();

        var query = context.Cards.Where(c => c.CardImageUri != null);
        if (!forceAll)
            query = query.Where(c => c.ImageHash == null || c.EdgeHash == null);

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

        var results = new List<(string CardSetId, ulong Hash, ulong EdgeHash)>();
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
                    var artFullPath = GetLocalArtFullPath(card.CardSetId);
                    byte[] imageBytes;

                    if (File.Exists(artFullPath))
                    {
                        imageBytes = await File.ReadAllBytesAsync(artFullPath, token);
                    }
                    else
                    {
                        using var response = await client.GetAsync(card.CardImageUri, token);
                        response.EnsureSuccessStatusCode();
                        imageBytes = await response.Content.ReadAsByteArrayAsync(token);

                        var artDir = Path.GetDirectoryName(artFullPath)!;
                        Directory.CreateDirectory(artDir);
                        await File.WriteAllBytesAsync(artFullPath, imageBytes, token);
                    }

                    using var buffer = new MemoryStream(imageBytes);
                    var hash = _hashService.ComputeHash(buffer);
                    buffer.Position = 0;
                    var edgeHash = _hashService.ComputeEdgeHash(buffer);

                    lock (saveLock)
                    {
                        results.Add((card.CardSetId, hash, edgeHash));
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

            List<(string CardSetId, ulong Hash, ulong EdgeHash)>? toSave = null;
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
        _edgeHashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("OPTCG hash computation complete: {Hashed} hashed, {Failed} failed in {ElapsedSec:F1}s", completed - failed, failed, sw.Elapsed.TotalSeconds);
        progress?.Report($"Done — hashed {completed - failed} cards ({failed} failed).");
    }

    private async Task SaveHashBatchAsync(List<(string CardSetId, ulong Hash, ulong EdgeHash)> batch, CancellationToken ct)
    {
        await using var context = _dbContextFactory.CreateDbContext();
        foreach (var (cardSetId, hash, edgeHash) in batch)
        {
            var artRelativePath = GetLocalArtRelativePath(cardSetId);
            await context.Cards
                .Where(c => c.CardSetId == cardSetId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ImageHash, hash)
                    .SetProperty(c => c.EdgeHash, edgeHash)
                    .SetProperty(c => c.LocalImagePath, artRelativePath), ct);
        }
    }

    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
    {
        _logger.LogDebug("Finding closest OPTCG match for pHash {Hash:X16} (set filter: {SetFilter}, max distance: {MaxDistance})", imageHash, setFilter is not null ? string.Join(",", setFilter) : "none", maxDistance);
        LastMatchDiagnostics = new MatchDiagnostics { SetFilterActive = setFilter is not null };

        // Phase 0: Direct lookup via OCR collector number (most reliable for OPTCG)
        if (ocrResult?.CollectorNumber is not null && ocrResult.CollectorNumberConfidence >= 0.5)
        {
            // OCR reads only the shared printed number, so this resolves to the base
            // (index-0) variant by design — alt-art disambiguation falls to pHash below.
            var ocrMatch = LookupOptcgCard(ocrResult.CollectorNumber, confidence: 100);
            if (ocrMatch is not null)
            {
                if (setFilter is null || setFilter.Contains(ocrMatch.SetCode))
                {
                    _logger.LogInformation("OPTCG OCR direct match: {CardName} ({CardSetId})", ocrMatch.Name, ocrMatch.CollectorNumber);
                    LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                    return ocrMatch;
                }
                _logger.LogDebug("OPTCG OCR match {CardSetId} rejected by set filter", ocrResult.CollectorNumber);
            }
            else
            {
                _logger.LogDebug("OPTCG OCR collector number {Number} not found in database", ocrResult.CollectorNumber);
            }
        }

        // Foil path: the scan carries an edge (structure) hash — match on it instead of the
        // luminance pHash, which the foil color shift corrupts.
        if (scanEdgeHash is ulong scanEdge)
        {
            if (_edgeHashCache is null)
            {
                _edgeHashCache = _readContext.Cards
                    .Where(c => c.EdgeHash != null)
                    .Select(c => new { c.CardSetId, Edge = c.EdgeHash!.Value, c.SetId })
                    .AsNoTracking()
                    .AsEnumerable()
                    .Select(c => (c.CardSetId, c.Edge, c.SetId))
                    .ToList();
                _logger.LogInformation("OPTCG edge-hash cache loaded with {Count} entries", _edgeHashCache.Count);
            }

            string bestEdgeId = "";
            int bestEdgeDist = int.MaxValue;
            foreach (var (cardSetId, edge, setId) in _edgeHashCache)
            {
                if (setFilter is not null && !setFilter.Contains(setId))
                    continue;

                var dist = PerceptualHashService.HammingDistance(scanEdge, edge);
                if (dist < bestEdgeDist) { bestEdgeDist = dist; bestEdgeId = cardSetId; }
            }

            if (bestEdgeId.Length > 0 && bestEdgeDist <= maxDistance)
            {
                LastMatchDiagnostics.DecisionPhase = "EdgeHashFoil";
                LastMatchDiagnostics.PHashDistance = bestEdgeDist;
                var edgeConfidence = Math.Max(0, 1.0 - (double)bestEdgeDist / maxDistance) * 100;
                _logger.LogInformation("OPTCG foil edge-hash match: {CardId} dist {Dist}", bestEdgeId, bestEdgeDist);
                return LookupOptcgCard(bestEdgeId, edgeConfidence);
            }

            _logger.LogDebug("OPTCG foil edge-hash: no match within {Max} (best {Dist})", maxDistance, bestEdgeDist);
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

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
                    LastMatchDiagnostics.DecisionPhase = "ExactCorrection";
                    return correctedCard;
                }
                _logger.LogDebug("Exact OPTCG correction for hash {Hash:X16} → {CardId} rejected by set filter (set {Set})", imageHash, exactCorrection.CorrectCardId, correctedCard.SetCode);
            }
        }

        // Phase 2: Combined fuzzy search
        if (_hashCache.Count == 0)
        {
            _logger.LogWarning("OPTCG hash cache is empty, no cards to match against");
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
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
            {
                LastMatchDiagnostics.DecisionPhase = "PHashConfident";
                LastMatchDiagnostics.PHashDistance = bestCorrectionAdjusted;
                return correctedCard;
            }
        }

        // Phase 3: Threshold check
        if (bestPHashDistance > maxDistance)
        {
            _logger.LogDebug("Best OPTCG match distance {Distance} exceeds max {MaxDistance}, no match", bestPHashDistance, maxDistance);
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

        // Safety net: never return a card outside the selected set filter
        if (setFilter is not null)
        {
            var bestSetCode = _hashSetLookup!.GetValueOrDefault(bestPHashId);
            if (bestSetCode is null || !setFilter.Contains(bestSetCode))
            {
                _logger.LogDebug("Best OPTCG match {CardId} is in set {Set} which is outside the set filter; rejecting", bestPHashId, bestSetCode ?? "(unknown)");
                LastMatchDiagnostics.DecisionPhase = "NoMatch";
                return null;
            }
        }

        var card = _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.CardSetId == bestPHashId);
        _logger.LogDebug("Best OPTCG match: {CardName} with Hamming distance {Distance}", card?.CardName, bestPHashDistance);
        if (card is null)
        {
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

        LastMatchDiagnostics.DecisionPhase = "PHashConfident";
        LastMatchDiagnostics.PHashDistance = bestPHashDistance;
        var confidence = Math.Max(0, (1.0 - (double)bestPHashDistance / maxDistance)) * 100;
        return new CardMatch
        {
            Name = card.CardName,
            SetCode = card.SetId,
            SetName = card.SetName,
            CollectorNumber = card.CardNumber,
            Rarity = card.Rarity,
            ImageUri = card.CardImageUri,
            GameSpecificId = card.CardSetId,
            LocalImagePath = ResolveLocalArtPath(card.LocalImagePath),
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

        // Total cards per set (counted by distinct printed CardNumber, so alt-art
        // variant rows do not inflate totals; SetId is the set code)
        var setTotals = _readContext.Cards
            .AsNoTracking()
            .Select(c => new { c.SetId, c.SetName, c.CardNumber })
            .Distinct()
            .AsEnumerable()
            .GroupBy(c => new { c.SetId, c.SetName })
            .Select(g => new { g.Key.SetId, g.Key.SetName, Total = g.Count() })
            .ToDictionary(s => s.SetId, s => (s.SetName, s.Total));

        // Owned cards per set (CollectionCard.SetCode stores SetId for OPTCG,
        // CollectionCard.Number stores the printed CardNumber): distinct numbers for
        // completion, plus physical copy count (incl. duplicates) for display.
        var ownedPerSet = ownedCards
            .GroupBy(c => c.SetCode)
            .ToDictionary(g => g.Key, g => (Distinct: g.Select(c => c.Number).Distinct().Count(), Physical: g.Count()));

        var results = new List<SetCompletionSummary>();
        var processed = 0;

        foreach (var (setId, (setName, total)) in setTotals)
        {
            ownedPerSet.TryGetValue(setId, out var owned);
            results.Add(new SetCompletionSummary
            {
                SetCode = setId,
                SetName = setName,
                OwnedCount = owned.Distinct,
                OwnedPhysicalCount = owned.Physical,
                TotalCount = total,
                Game = CardGame.OnePiece,
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
            .Where(c => !ownedSet.Contains(c.CardNumber))
            .GroupBy(c => c.CardNumber)
            .Select(g => g.OrderBy(c => c.VariantIndex).First())
            .Select(c => new MissingCard
            {
                Name = c.CardName,
                CollectorNumber = c.CardNumber,
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
            CollectorNumber = card.CardNumber,
            Rarity = card.Rarity,
            ImageUri = card.CardImageUri,
            GameSpecificId = card.CardSetId,
            LocalImagePath = ResolveLocalArtPath(card.LocalImagePath),
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
            if (t.StartsWith("cn:", StringComparison.OrdinalIgnoreCase))
            {
                var upper = t[3..].ToUpperInvariant();
                cards = cards.Where(c => c.CardSetId.ToUpper() == upper);
            }
            else if (t.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
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
            CollectorNumber = c.CardNumber,
            Rarity = c.Rarity,
            ImageUri = c.CardImageUri,
            GameSpecificId = c.CardSetId,
            LocalImagePath = ResolveLocalArtPath(c.LocalImagePath),
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
            CollectorNumber = c.CardNumber,
            Rarity = c.Rarity,
            ImageUri = c.CardImageUri,
            GameSpecificId = c.CardSetId,
            LocalImagePath = ResolveLocalArtPath(c.LocalImagePath),
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

    private static string GetLocalArtRelativePath(string cardSetId)
    {
        return $"optcg-art/{cardSetId}.jpg";
    }

    private string GetLocalArtFullPath(string cardSetId)
    {
        return Path.Combine(_dataDirectory, "optcg-art", $"{cardSetId}.jpg");
    }

    private string? ResolveLocalArtPath(string? relativePath)
    {
        if (relativePath is null) return null;
        var full = Path.Combine(_dataDirectory, relativePath);
        return File.Exists(full) ? full : null;
    }

    public void Dispose() => _readContext.Dispose();
}
