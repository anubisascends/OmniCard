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

// Abstract base for all TCGCSV-backed games. Concrete games subclass this, supplying a
// category id, extended-data mapping, and sub-type→price mapping. Catalog download, image
// hashing, price refresh, matching, and queries live here — implemented once.
public abstract class TcgCsvGameService<TContext> : ICardGameService, IDisposable
    where TContext : TcgCsvDbContext
{
    protected const string TcgCsvBaseUrl = "https://tcgcsv.com";
    private const int CorrectionTrustBonus = 5;

    protected readonly IHttpClientFactory _httpClientFactory;
    protected readonly IDbContextFactory<TContext> _dbContextFactory;
    protected readonly IPerceptualHashService _hashService;
    protected readonly ILogger _logger;
    protected readonly string _dataDirectory;
    protected TContext _readContext;

    private List<(int Id, ulong Hash)>? _hashCache;
    private List<(int Id, ulong EdgeHash, string SetCode)>? _edgeHashCache;
    private Dictionary<int, string>? _hashSetLookup;
    private List<(ulong ScanHash, string CorrectCardId)>? _correctionsCache;

    // TCGCSV returns camelCase JSON.
    protected static readonly JsonSerializerOptions TcgCsvJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // === Per-game hooks ===
    protected abstract int CategoryId { get; }
    public abstract CardGame Game { get; }
    protected abstract string GameKey { get; }   // art-dir prefix, e.g. "pokemon"

    // Fold a product's price rows into (normal, foil). Games differ in sub-type naming.
    protected abstract (decimal? Normal, decimal? Foil) MapSubtypePrices(List<TcgCsvPrice> productPriceRows);

    // Promote game-specific extendedData into columns. Default reads "Number"/"Rarity"/"CardType";
    // override when a game uses different keys.
    protected virtual void MapExtendedData(TcgCsvProduct product, TcgCsvCard card)
    {
        string? Val(string name) => product.ExtendedData
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
        card.CollectorNumber = Val("Number") ?? "";
        card.Rarity = Val("Rarity") ?? "";
        card.CardType = Val("CardType") ?? Val("Card Type") ?? "";
    }

    // TCGCSV product images default to _200w; upgrade for usable perceptual hashing.
    protected virtual string? UpgradeImageUrl(string? url)
        => url is null ? null : url.Replace("_200w.", "_400w.");

    protected TcgCsvGameService(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<TContext> dbContextFactory,
        IPerceptualHashService hashService,
        IDataPathService dataPathService,
        ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _hashService = hashService;
        _dataDirectory = dataPathService.DataDirectory;
        _logger = logger;

        _readContext = _dbContextFactory.CreateDbContext();
        var dbPath = _readContext.Database.GetConnectionString();
        if (dbPath is not null)
        {
            var dataSource = dbPath.Replace("Data Source=", "");
            var dir = Path.GetDirectoryName(dataSource.Replace(";Mode=ReadOnly", ""));
            if (dir is not null && dir.Length > 0)
                Directory.CreateDirectory(dir);
        }
        _readContext.Database.EnsureCreated();
        _readContext.ApplySchemaUpgrades();

        if (_readContext.GetSchemaVersion() < TcgCsvDbContext.TcgCsvSchemaVersion)
        {
            _logger.LogWarning("{Game} database predates current schema; wiping for migration", Game);
            WipeForMigration();
        }
        _logger.LogInformation("{Game} database ready at {DbPath}", Game, dbPath);
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
            _logger.LogWarning(ex, "{Game} database is read-only; skipping migration wipe", Game);
        }

        var artDir = Path.Combine(_dataDirectory, $"{GameKey}-art");
        if (Directory.Exists(artDir))
        {
            try { Directory.Delete(artDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete {Game} art directory during migration wipe", Game);
            }
        }

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null; _edgeHashCache = null; _hashSetLookup = null; _correctionsCache = null;
        oldContext.Dispose();
    }

    public MatchDiagnostics? LastMatchDiagnostics { get; private set; }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");
        return client;
    }

    // === Download ===
    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting {Game} card data download from TCGCSV", Game);
        var sw = Stopwatch.StartNew();
        var client = CreateClient();

        progress?.Report($"Fetching {Game} set list...");
        var groups = await client.GetFromJsonAsync<TcgCsvGroupsResponse>(
            $"{TcgCsvBaseUrl}/tcgplayer/{CategoryId}/groups", TcgCsvJsonOptions, ct);
        var groupList = groups?.Results ?? [];
        _logger.LogInformation("Discovered {Count} {Game} groups", groupList.Count, Game);

        var allCards = new List<TcgCsvCard>();
        var cardsLock = new object();
        var done = 0;

        await Parallel.ForEachAsync(groupList, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (group, token) =>
            {
                try
                {
                    var products = await client.GetFromJsonAsync<TcgCsvProductsResponse>(
                        $"{TcgCsvBaseUrl}/tcgplayer/{CategoryId}/{group.GroupId}/products", TcgCsvJsonOptions, token);
                    var setCode = string.IsNullOrWhiteSpace(group.Abbreviation) ? group.GroupId.ToString() : group.Abbreviation!;
                    var rows = (products?.Results ?? []).Select(p => MapProduct(p, setCode, group.Name)).ToList();
                    lock (cardsLock) allCards.AddRange(rows);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to fetch {Game} group {GroupId}; skipping", Game, group.GroupId);
                }
                finally
                {
                    var d = Interlocked.Increment(ref done);
                    progress?.Report($"Fetched {d}/{groupList.Count} sets...");
                }
            });

        var deduped = allCards.GroupBy(c => c.ProductId).Select(g => g.Last()).ToList();
        progress?.Report($"Fetched {deduped.Count} cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingIds = (await importContext.Cards.Select(c => c.ProductId).ToListAsync(ct)).ToHashSet();
        var inserted = 0; var updated = 0;

        foreach (var batch in deduped.Chunk(500))
        {
            var newCards = new List<TcgCsvCard>();
            var existingCardIds = batch.Where(c => existingIds.Contains(c.ProductId)).Select(c => c.ProductId).ToList();
            foreach (var c in batch) if (!existingIds.Contains(c.ProductId)) newCards.Add(c);

            if (existingCardIds.Count > 0)
            {
                var tracked = await importContext.Cards.Where(c => existingCardIds.Contains(c.ProductId))
                    .ToDictionaryAsync(c => c.ProductId, ct);
                foreach (var c in batch)
                {
                    if (tracked.TryGetValue(c.ProductId, out var existing))
                    {
                        // Refresh catalog fields; preserve computed hashes/paths and prices.
                        existing.Game = c.Game;
                        existing.Name = c.Name;
                        existing.CleanName = c.CleanName;
                        existing.GroupId = c.GroupId;
                        existing.SetCode = c.SetCode;
                        existing.SetName = c.SetName;
                        existing.CollectorNumber = c.CollectorNumber;
                        existing.Rarity = c.Rarity;
                        existing.CardType = c.CardType;
                        existing.ImageUrl = c.ImageUrl;
                        existing.Url = c.Url;
                        existing.ExtendedDataJson = c.ExtendedDataJson;
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
                foreach (var c in newCards) existingIds.Add(c.ProductId);
                inserted += newCards.Count;
            }
            progress?.Report($"Processed {inserted + updated} cards ({inserted} new, {updated} updated)...");
        }

        if (deduped.Count > 0) importContext.MarkMigrationComplete();

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null; _edgeHashCache = null; _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("{Game} download complete: {Ins} new, {Upd} updated in {Sec:F1}s", Game, inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        if (inserted > 0) await ComputeImageHashesAsync(forceAll: false, progress, ct);
    }

    protected TcgCsvCard MapProduct(TcgCsvProduct p, string setCode, string setName)
    {
        var card = new TcgCsvCard
        {
            ProductId = p.ProductId,
            Game = Game,
            Name = p.Name,
            CleanName = p.CleanName,
            GroupId = p.GroupId,
            SetCode = setCode,
            SetName = setName,
            ImageUrl = p.ImageUrl,
            Url = p.Url,
            ExtendedDataJson = JsonSerializer.Serialize(p.ExtendedData, TcgCsvJsonOptions),
        };
        MapExtendedData(p, card);
        return card;
    }

    public void Dispose() => _readContext.Dispose();

    // Methods added in Tasks 5-8:
    public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        await using (var ctx = _dbContextFactory.CreateDbContext())
        {
            if (!await ctx.Cards.AnyAsync(ct))
            {
                _logger.LogInformation("Skipping {Game} price refresh: card database is empty", Game);
                return;
            }
        }

        _logger.LogInformation("Starting {Game} price refresh via TCGCSV", Game);
        var client = CreateClient();
        var priceMap = await FetchTcgCsvPriceMapAsync(client, progress, ct);

        await using var context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();
        var now = DateTime.UtcNow;
        var updated = 0;

        var targetIds = (await context.Cards.Select(c => c.ProductId).ToListAsync(ct))
            .Where(priceMap.ContainsKey).ToList();

        foreach (var batch in targetIds.Chunk(500))
        {
            var tracked = await context.Cards.Where(c => batch.Contains(c.ProductId))
                .ToDictionaryAsync(c => c.ProductId, ct);
            foreach (var pid in batch)
            {
                if (tracked.TryGetValue(pid, out var existing) && priceMap.TryGetValue(pid, out var prices))
                {
                    existing.MarketPrice = prices.Normal;
                    existing.FoilMarketPrice = prices.Foil;
                    existing.PriceUpdatedAt = now;
                    updated++;
                }
            }
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        _logger.LogInformation("{Game} price refresh complete: {Updated} cards updated", Game, updated);
        progress?.Report(new PriceUpdateProgress(Game, null, 0, 0, $"{Game} prices updated ({updated} cards)"));
    }

    private async Task<Dictionary<int, (decimal? Normal, decimal? Foil)>> FetchTcgCsvPriceMapAsync(
        HttpClient client, IProgress<PriceUpdateProgress>? progress, CancellationToken ct)
    {
        var groups = await client.GetFromJsonAsync<TcgCsvGroupsResponse>(
            $"{TcgCsvBaseUrl}/tcgplayer/{CategoryId}/groups", TcgCsvJsonOptions, ct);
        var groupList = groups?.Results ?? [];
        var rowsByProduct = new Dictionary<int, List<TcgCsvPrice>>();
        var done = 0;

        foreach (var group in groupList)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var prices = await client.GetFromJsonAsync<TcgCsvPricesResponse>(
                    $"{TcgCsvBaseUrl}/tcgplayer/{CategoryId}/{group.GroupId}/prices", TcgCsvJsonOptions, ct);
                foreach (var row in prices?.Results ?? [])
                {
                    if (!rowsByProduct.TryGetValue(row.ProductId, out var list))
                        rowsByProduct[row.ProductId] = list = [];
                    list.Add(row);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch {Game} prices for group {GroupId}; skipping", Game, group.GroupId);
            }
            done++;
            progress?.Report(new PriceUpdateProgress(Game, null, done, groupList.Count, $"{Game} prices: {done}/{groupList.Count} groups"));
        }

        var map = new Dictionary<int, (decimal? Normal, decimal? Foil)>(rowsByProduct.Count);
        foreach (var (pid, rows) in rowsByProduct) map[pid] = MapSubtypePrices(rows);
        return map;
    }
    public async Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting {Game} image hash computation (forceAll: {ForceAll})", Game, forceAll);
        var sw = Stopwatch.StartNew();

        await using var context = _dbContextFactory.CreateDbContext();
        var query = context.Cards.Where(c => c.ImageUrl != null);
        if (!forceAll) query = query.Where(c => c.ImageHash == null || c.EdgeHash == null);
        var cards = await query.Select(c => new { c.ProductId, c.ImageUrl }).ToListAsync(ct);
        progress?.Report($"Computing hashes for {cards.Count} cards...");

        var client = CreateClient();
        using var throttle = new SemaphoreSlim(8);
        var completed = 0; var failed = 0;
        var results = new List<(int Id, ulong Hash, ulong EdgeHash)>();
        var saveLock = new object();

        await Parallel.ForEachAsync(cards, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (card, token) =>
            {
                var url = UpgradeImageUrl(card.ImageUrl);
                if (url is null) { Interlocked.Increment(ref failed); return; }
                try
                {
                    await throttle.WaitAsync(token);
                    try
                    {
                        var artFullPath = GetLocalArtFullPath(card.ProductId);
                        byte[] imageBytes;
                        if (File.Exists(artFullPath))
                        {
                            imageBytes = await File.ReadAllBytesAsync(artFullPath, token);
                        }
                        else
                        {
                            using var response = await client.GetAsync(url, token);
                            response.EnsureSuccessStatusCode();
                            imageBytes = await response.Content.ReadAsByteArrayAsync(token);
                            Directory.CreateDirectory(Path.GetDirectoryName(artFullPath)!);
                            await File.WriteAllBytesAsync(artFullPath, imageBytes, token);
                        }

                        using var buffer = new MemoryStream(imageBytes);
                        var hash = _hashService.ComputeHash(buffer);
                        buffer.Position = 0;
                        var edgeHash = _hashService.ComputeEdgeHash(buffer);
                        lock (saveLock) results.Add((card.ProductId, hash, edgeHash));
                    }
                    finally { throttle.Release(); await Task.Delay(50, token); }
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to compute hash for {Game} card {Id}", Game, card.ProductId);
                    Interlocked.Increment(ref failed);
                }

                var d = Interlocked.Increment(ref completed);
                if (d % 100 == 0) progress?.Report($"Hashed {d}/{cards.Count} cards ({failed} failed)...");

                List<(int, ulong, ulong)>? toSave = null;
                lock (saveLock) { if (results.Count >= 200) { toSave = [.. results]; results.Clear(); } }
                if (toSave is not null) await SaveHashBatchAsync(toSave, ct);
            });

        if (results.Count > 0) await SaveHashBatchAsync(results, ct);

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null; _edgeHashCache = null; _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("{Game} hash computation complete: {Hashed} hashed, {Failed} failed in {Sec:F1}s", Game, completed - failed, failed, sw.Elapsed.TotalSeconds);
        progress?.Report($"Done — hashed {completed - failed} cards ({failed} failed).");
    }

    private async Task SaveHashBatchAsync(List<(int Id, ulong Hash, ulong EdgeHash)> batch, CancellationToken ct)
    {
        await using var context = _dbContextFactory.CreateDbContext();
        foreach (var (id, hash, edgeHash) in batch)
        {
            var rel = GetLocalArtRelativePath(id);
            await context.Cards.Where(c => c.ProductId == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ImageHash, hash)
                    .SetProperty(c => c.EdgeHash, edgeHash)
                    .SetProperty(c => c.LocalImagePath, rel), ct);
        }
    }

    protected string GetLocalArtRelativePath(int id) => $"{GameKey}-art/{id}.png";
    protected string GetLocalArtFullPath(int id) => Path.Combine(_dataDirectory, $"{GameKey}-art", $"{id}.png");
    protected string? ResolveLocalArtPath(string? relativePath)
    {
        if (relativePath is null) return null;
        var full = Path.Combine(_dataDirectory, relativePath);
        return File.Exists(full) ? full : null;
    }

    private TcgCsvCard? LookupById(int id) => _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.ProductId == id);

    internal static string NormalizeNumber(string s) => s.Trim().ToUpperInvariant();

    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null,
        IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
    {
        LastMatchDiagnostics = new MatchDiagnostics { SetFilterActive = setFilter is not null };

        // Phase 0: OCR collector-number lookup. Number is ground truth from extendedData.
        if (ocrResult?.CollectorNumber is not null && ocrResult.CollectorNumberConfidence >= 0.5)
        {
            var num = NormalizeNumber(ocrResult.CollectorNumber);
            var candidates = _readContext.Cards.AsNoTracking()
                .Where(c => c.CollectorNumber.ToUpper() == num).ToList();
            if (setFilter is not null) candidates = candidates.Where(c => setFilter.Contains(c.SetCode)).ToList();

            if (candidates.Count == 1)
            {
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ToMatch(candidates[0], 100);
            }
            if (candidates.Count > 1)
            {
                var hashed = candidates.Where(c => c.ImageHash != null).ToList();
                var best = hashed.Count > 0
                    ? hashed.OrderBy(c => PerceptualHashService.HammingDistance(imageHash, c.ImageHash!.Value)).First()
                    : candidates[0];
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ToMatch(best, 100);
            }
        }

        // Foil path: color-robust edge hash.
        if (scanEdgeHash is ulong scanEdge)
        {
            _edgeHashCache ??= _readContext.Cards.Where(c => c.EdgeHash != null)
                .Select(c => new { c.ProductId, Edge = c.EdgeHash!.Value, c.SetCode })
                .AsNoTracking().AsEnumerable().Select(c => (c.ProductId, c.Edge, c.SetCode)).ToList();

            int bestEdgeId = -1; int bestEdgeDist = int.MaxValue;
            foreach (var (id, edge, setCode) in _edgeHashCache)
            {
                if (setFilter is not null && !setFilter.Contains(setCode)) continue;
                var dist = PerceptualHashService.HammingDistance(scanEdge, edge);
                if (dist < bestEdgeDist) { bestEdgeDist = dist; bestEdgeId = id; }
            }
            if (bestEdgeId >= 0 && bestEdgeDist <= maxDistance)
            {
                LastMatchDiagnostics.DecisionPhase = "EdgeHashFoil";
                LastMatchDiagnostics.PHashDistance = bestEdgeDist;
                var conf = Math.Max(0, 1.0 - (double)bestEdgeDist / maxDistance) * 100;
                var card = LookupById(bestEdgeId);
                return card is null ? null : ToMatch(card, conf);
            }
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

        if (_hashCache is null)
        {
            var entries = _readContext.Cards.Where(c => c.ImageHash != null)
                .Select(c => new { c.ProductId, Hash = c.ImageHash!.Value, c.SetCode })
                .AsNoTracking().AsEnumerable().ToList();
            _hashCache = entries.Select(c => (c.ProductId, c.Hash)).ToList();
            _hashSetLookup = entries.ToDictionary(c => c.ProductId, c => c.SetCode);
        }
        if (_correctionsCache is null)
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            _correctionsCache = ctx.HashCorrections.AsNoTracking()
                .Select(h => new { h.ScanHash, h.CorrectCardId }).AsEnumerable()
                .Select(h => (h.ScanHash, h.CorrectCardId)).ToList();
        }

        // Phase 1: exact correction.
        var exact = _correctionsCache.FirstOrDefault(c => c.ScanHash == imageHash);
        if (exact.CorrectCardId is not null && int.TryParse(exact.CorrectCardId, out var exactId))
        {
            var corrected = LookupById(exactId);
            if (corrected is not null && (setFilter is null || setFilter.Contains(corrected.SetCode)))
            {
                LastMatchDiagnostics.DecisionPhase = "ExactCorrection";
                return ToMatch(corrected, 100);
            }
        }

        if (_hashCache.Count == 0) { LastMatchDiagnostics.DecisionPhase = "NoMatch"; return null; }

        // Phase 2: nearest pHash + fuzzy corrections.
        int bestId = -1; int bestDist = int.MaxValue;
        foreach (var (id, hash) in _hashCache)
        {
            if (setFilter is not null && !setFilter.Contains(_hashSetLookup![id])) continue;
            var dist = PerceptualHashService.HammingDistance(imageHash, hash);
            if (dist < bestDist) { bestDist = dist; bestId = id; }
        }

        int? bestCorrId = null; int bestCorrAdjusted = int.MaxValue;
        foreach (var (scanHash, correctCardId) in _correctionsCache)
        {
            if (!int.TryParse(correctCardId, out var cid)) continue;
            if (setFilter is not null)
            {
                var corrSet = _hashSetLookup!.GetValueOrDefault(cid);
                if (corrSet is null || !setFilter.Contains(corrSet)) continue;
            }
            var dist = PerceptualHashService.HammingDistance(imageHash, scanHash);
            if (dist <= maxDistance)
            {
                var adjusted = Math.Max(0, dist - CorrectionTrustBonus);
                if (adjusted < bestCorrAdjusted) { bestCorrAdjusted = adjusted; bestCorrId = cid; }
            }
        }

        if (bestCorrId is int corrId && bestCorrAdjusted <= bestDist)
        {
            var conf = Math.Max(0, 1.0 - (double)bestCorrAdjusted / maxDistance) * 100;
            var corrected = LookupById(corrId);
            if (corrected is not null)
            {
                LastMatchDiagnostics.DecisionPhase = "PHashConfident";
                LastMatchDiagnostics.PHashDistance = bestCorrAdjusted;
                return ToMatch(corrected, conf);
            }
        }

        if (bestDist > maxDistance) { LastMatchDiagnostics.DecisionPhase = "NoMatch"; return null; }

        var bestCard = bestId >= 0 ? LookupById(bestId) : null;
        if (bestCard is null) { LastMatchDiagnostics.DecisionPhase = "NoMatch"; return null; }
        LastMatchDiagnostics.DecisionPhase = "PHashConfident";
        LastMatchDiagnostics.PHashDistance = bestDist;
        var confidence = Math.Max(0, 1.0 - (double)bestDist / maxDistance) * 100;
        return ToMatch(bestCard, confidence);
    }

    public IReadOnlyList<SetInfo> GetAvailableSets()
        => _readContext.Cards.AsNoTracking()
            .Select(c => new { c.SetCode, c.SetName }).Distinct()
            .OrderBy(s => s.SetName).AsEnumerable()
            .Select(s => new SetInfo(s.SetCode, s.SetName)).ToList();

    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
    {
        var setTotals = _readContext.Cards.AsNoTracking()
            .Select(c => new { c.SetCode, c.SetName, c.CollectorNumber }).Distinct()
            .AsEnumerable()
            .GroupBy(c => new { c.SetCode, c.SetName })
            .Select(g => new { g.Key.SetCode, g.Key.SetName, Total = g.Count() })
            .ToDictionary(s => s.SetCode, s => (s.SetName, s.Total));

        var ownedPerSet = ownedCards.GroupBy(c => c.SetCode)
            .ToDictionary(g => g.Key, g => (Distinct: g.Select(c => c.Number).Distinct().Count(), Physical: g.Count()));

        var results = new List<SetCompletionSummary>();
        foreach (var (setCode, (setName, total)) in setTotals)
        {
            ownedPerSet.TryGetValue(setCode, out var owned);
            results.Add(new SetCompletionSummary
            {
                SetCode = setCode, SetName = setName,
                OwnedCount = owned.Distinct, OwnedPhysicalCount = owned.Physical,
                TotalCount = total, Game = Game,
            });
        }
        return Task.FromResult(results);
    }

    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers)
    {
        var ownedSet = ownedCollectorNumbers.ToHashSet();
        return _readContext.Cards.AsNoTracking()
            .Where(c => c.SetCode == setCode).AsEnumerable()
            .Where(c => !ownedSet.Contains(c.CollectorNumber))
            .GroupBy(c => c.CollectorNumber)
            .Select(g => g.First())
            .Select(c => new MissingCard
            {
                Name = c.Name, CollectorNumber = c.CollectorNumber, SetCode = c.SetCode,
                Rarity = c.Rarity, ImageUri = c.ImageUrl, TypeLine = c.CardType,
            })
            .OrderBy(m => m.CollectorNumber).ToList();
    }

    public List<CardMatch> SearchCards(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        IQueryable<TcgCsvCard> cards = _readContext.Cards.AsNoTracking();
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = term;
            if (t.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[4..];
                cards = cards.Where(c => EF.Functions.Like(c.SetCode, $"%{val}%") || EF.Functions.Like(c.SetName, $"%{val}%"));
            }
            else if (t.StartsWith("type:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[(t.IndexOf(':') + 1)..];
                cards = cards.Where(c => EF.Functions.Like(c.CardType, $"%{val}%"));
            }
            else if (t.StartsWith("cn:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[3..];
                cards = cards.Where(c => EF.Functions.Like(c.CollectorNumber, $"%{val}%"));
            }
            else
            {
                cards = cards.Where(c => EF.Functions.Like(c.Name, $"%{t}%"));
            }
        }
        return cards.OrderBy(c => c.Name).Take(maxResults).AsEnumerable().Select(c => ToMatch(c)).ToList();
    }

    public List<CardMatch> GetPrintings(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return [];
        return _readContext.Cards.AsNoTracking()
            .Where(c => c.Name == cardName)
            .OrderBy(c => c.SetName).ThenBy(c => c.CollectorNumber)
            .AsEnumerable().Select(c => ToMatch(c)).ToList();
    }

    public decimal? GetCurrentPrice(string gameCardId, bool isFoil)
    {
        if (!int.TryParse(gameCardId, out var id)) return null;
        var row = _readContext.Cards.AsNoTracking().Where(c => c.ProductId == id)
            .Select(c => new { c.MarketPrice, c.FoilMarketPrice }).FirstOrDefault();
        if (row is null) return null;
        return isFoil ? row.FoilMarketPrice ?? row.MarketPrice : row.MarketPrice ?? row.FoilMarketPrice;
    }

    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)
    {
        var ids = gameCardIds.Select(s => int.TryParse(s, out var i) ? i : (int?)null).OfType<int>().Distinct().ToList();
        if (ids.Count == 0) return [];
        var result = new Dictionary<string, decimal>(ids.Count);
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = _readContext.Cards.AsNoTracking().Where(c => chunk.Contains(c.ProductId))
                .Select(c => new { c.ProductId, c.MarketPrice, c.FoilMarketPrice }).ToList();
            foreach (var row in rows)
            {
                var price = isFoil ? row.FoilMarketPrice ?? row.MarketPrice : row.MarketPrice ?? row.FoilMarketPrice;
                if (price.HasValue) result[row.ProductId.ToString()] = price.Value;
            }
        }
        return result;
    }

    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        ctx.Database.ExecuteSqlRaw(
            "INSERT OR REPLACE INTO HashCorrections (ScanHash, CorrectCardId, CreatedAt) VALUES ({0}, {1}, {2})",
            (long)scanHash, correctCardId, DateTime.UtcNow.ToString("o"));
        _correctionsCache = null;
    }

    public object? FindCardById(string gameCardId)
    {
        if (!int.TryParse(gameCardId, out var id)) return null;
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().FirstOrDefault(c => c.ProductId == id);
    }

    internal CardMatch ToMatch(TcgCsvCard c, double? confidence = null) => new()
    {
        Name = c.Name,
        SetCode = c.SetCode,
        SetName = c.SetName,
        CollectorNumber = c.CollectorNumber,
        Rarity = c.Rarity,
        ImageUri = c.ImageUrl,
        GameSpecificId = c.ProductId.ToString(),
        LocalImagePath = ResolveLocalArtPath(c.LocalImagePath),
        Confidence = confidence,
        Source = c
    };
}
