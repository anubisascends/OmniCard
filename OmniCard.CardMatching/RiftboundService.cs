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

public sealed class RiftboundService : ICardGameService, IDisposable
{
    private const string ApiBaseUrl = "https://api.riftcodex.com";
    private const int CorrectionTrustBonus = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<RiftboundDbContext> _dbContextFactory;
    private readonly IPerceptualHashService _hashService;
    private readonly ILogger<RiftboundService> _logger;
    private readonly string _dataDirectory;
    private RiftboundDbContext _readContext;

    private List<(string Id, ulong Hash)>? _hashCache;
    private List<(string Id, ulong EdgeHash, string SetId)>? _edgeHashCache;
    private Dictionary<string, string>? _hashSetLookup;
    private List<(ulong ScanHash, string CorrectCardId)>? _correctionsCache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public RiftboundService(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<RiftboundDbContext> dbContextFactory,
        IPerceptualHashService hashService,
        IDataPathService dataPathService,
        ILogger<RiftboundService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _hashService = hashService;
        _dataDirectory = dataPathService.DataDirectory;
        _logger = logger;

        _logger.LogInformation("Initializing Riftbound service");
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

        if (_readContext.GetSchemaVersion() < RiftboundDbContext.RiftboundSchemaVersion)
        {
            _logger.LogWarning("Riftbound database predates current schema; wiping for migration");
            WipeForMigration();
        }

        _logger.LogInformation("Riftbound database ready at {DbPath}", dbPath);
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
            _logger.LogWarning(ex, "Riftbound database is read-only; skipping migration wipe");
        }

        var artDir = Path.Combine(_dataDirectory, "riftbound-art");
        if (Directory.Exists(artDir))
        {
            try { Directory.Delete(artDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete Riftbound art directory during migration wipe");
            }
        }

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        _correctionsCache = null;
        oldContext.Dispose();
    }

    public CardGame Game => CardGame.Riftbound;
    public MatchDiagnostics? LastMatchDiagnostics { get; private set; }

    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Riftbound card data download from Riftcodex API");
        var sw = Stopwatch.StartNew();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        progress?.Report("Fetching Riftbound set list...");
        var allCards = await FetchAllCardsAsync(client,
            (done, total, code) => progress?.Report($"Fetched {done}/{total} sets ({code})..."), ct);

        var deduped = allCards
            .GroupBy(c => c.Id)
            .Select(g => g.Last())
            .ToList();
        _logger.LogInformation("Fetched {Total} card rows ({Unique} unique)", allCards.Count, deduped.Count);
        progress?.Report($"Fetched {deduped.Count} cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingIds = (await importContext.Cards.Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        var inserted = 0;
        var updated = 0;

        foreach (var batch in deduped.Chunk(500))
        {
            var newCards = new List<RiftboundCard>();
            var existingCardIds = new List<string>();

            foreach (var card in batch)
            {
                if (existingIds.Contains(card.Id)) existingCardIds.Add(card.Id);
                else newCards.Add(card);
            }

            if (existingCardIds.Count > 0)
            {
                var tracked = await importContext.Cards
                    .Where(c => existingCardIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, ct);

                foreach (var card in batch)
                {
                    if (tracked.TryGetValue(card.Id, out var existing))
                    {
                        // Refresh metadata; preserve computed ImageHash/EdgeHash/LocalImagePath.
                        existing.RiftboundId = card.RiftboundId;
                        existing.TcgplayerId = card.TcgplayerId;
                        existing.CollectorNumber = card.CollectorNumber;
                        existing.Name = card.Name;
                        existing.CleanName = card.CleanName;
                        existing.SetId = card.SetId;
                        existing.SetName = card.SetName;
                        existing.Rarity = card.Rarity;
                        existing.CardType = card.CardType;
                        existing.Supertype = card.Supertype;
                        existing.Domain = card.Domain;
                        existing.Energy = card.Energy;
                        existing.Might = card.Might;
                        existing.Power = card.Power;
                        existing.CardText = card.CardText;
                        existing.Flavour = card.Flavour;
                        existing.Artist = card.Artist;
                        existing.Orientation = card.Orientation;
                        existing.AlternateArt = card.AlternateArt;
                        existing.Overnumbered = card.Overnumbered;
                        existing.Signature = card.Signature;
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
                foreach (var card in newCards) existingIds.Add(card.Id);
                inserted += newCards.Count;
            }

            progress?.Report($"Processed {inserted + updated} cards ({inserted} new, {updated} updated)...");
        }

        if (deduped.Count > 0)
            importContext.MarkMigrationComplete();

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("Riftbound download complete: {Inserted} new, {Updated} updated in {Sec:F1}s", inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        if (inserted > 0)
            await ComputeImageHashesAsync(forceAll: false, progress, ct);
    }

    // Fetch the set list, then page every set's cards. onSetCompleted(done,total,setId) after each set.
    private async Task<List<RiftboundCard>> FetchAllCardsAsync(
        HttpClient client, Action<int, int, string>? onSetCompleted, CancellationToken ct)
    {
        var setList = await client.GetFromJsonAsync<RiftboundSetListResponse>(
            $"{ApiBaseUrl}/sets?size=100", JsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to fetch set list from Riftcodex API.");

        _logger.LogInformation("Discovered {Count} Riftbound sets", setList.Items.Count);

        var allCards = new List<RiftboundCard>();
        var cardsLock = new object();
        var fetchedSets = 0;

        await Parallel.ForEachAsync(setList.Items, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        }, async (set, token) =>
        {
            try
            {
                var page = 1;
                var pages = 1;
                do
                {
                    var resp = await client.GetFromJsonAsync<RiftboundCardListResponse>(
                        $"{ApiBaseUrl}/cards?set_id={set.SetId}&size=100&page={page}", JsonOptions, token);
                    if (resp is null) break;
                    pages = resp.Pages;

                    var rows = resp.Items.Select(MapCard).ToList();
                    lock (cardsLock) allCards.AddRange(rows);
                    page++;
                } while (page <= pages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch Riftbound set {SetId}; skipping", set.SetId);
            }
            finally
            {
                var done = Interlocked.Increment(ref fetchedSets);
                onSetCompleted?.Invoke(done, setList.Items.Count, set.SetId);
            }
        });

        return allCards;
    }

    // Riftcodex returns no prices; pricing is out of scope this pass.
    public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Riftbound price refresh skipped — pricing is not wired for Riftbound yet");
        progress?.Report(new PriceUpdateProgress(CardGame.Riftbound, null, 0, 0, "Riftbound pricing not available"));
        return Task.CompletedTask;
    }

    internal static RiftboundCard MapCard(RiftboundApiCard c) => new()
    {
        Id = c.Id,
        RiftboundId = c.RiftboundId,
        TcgplayerId = c.TcgplayerId,
        CollectorNumber = c.CollectorNumber,
        Name = c.Name,
        CleanName = c.Metadata.CleanName,
        SetId = c.Set.SetId,
        SetName = c.Set.Label,
        Rarity = c.Classification.Rarity ?? "",
        CardType = c.Classification.Type,
        Supertype = c.Classification.Supertype,
        Domain = string.Join("/", c.Classification.Domain),
        Energy = c.Attributes.Energy,
        Might = c.Attributes.Might,
        Power = c.Attributes.Power,
        CardText = c.Text.Plain,
        Flavour = c.Text.Flavour,
        Artist = c.Media.Artist,
        Orientation = c.Orientation,
        AlternateArt = c.Metadata.AlternateArt,
        Overnumbered = c.Metadata.Overnumbered,
        Signature = c.Metadata.Signature,
        CardImageUri = c.Media.ImageUrl,
        DateScraped = DateTime.UtcNow.ToString("o"),
    };

    // === Image hashing ===
    public async Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Riftbound image hash computation (forceAll: {ForceAll})", forceAll);
        var sw = Stopwatch.StartNew();

        await using var context = _dbContextFactory.CreateDbContext();
        var query = context.Cards.Where(c => c.CardImageUri != null);
        if (!forceAll)
            query = query.Where(c => c.ImageHash == null || c.EdgeHash == null);

        var cards = await query.Select(c => new { c.Id, c.CardImageUri }).ToListAsync(ct);
        _logger.LogInformation("Found {Count} Riftbound cards requiring hash computation", cards.Count);
        progress?.Report($"Computing hashes for {cards.Count} cards...");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        using var throttle = new SemaphoreSlim(8);
        var completed = 0;
        var failed = 0;
        var results = new List<(string Id, ulong Hash, ulong EdgeHash)>();
        var saveLock = new object();

        await Parallel.ForEachAsync(cards, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct
        }, async (card, token) =>
        {
            if (card.CardImageUri is null) { Interlocked.Increment(ref failed); return; }
            try
            {
                await throttle.WaitAsync(token);
                try
                {
                    var artFullPath = GetLocalArtFullPath(card.Id);
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
                        Directory.CreateDirectory(Path.GetDirectoryName(artFullPath)!);
                        await File.WriteAllBytesAsync(artFullPath, imageBytes, token);
                    }

                    using var buffer = new MemoryStream(imageBytes);
                    var hash = _hashService.ComputeHash(buffer);
                    buffer.Position = 0;
                    var edgeHash = _hashService.ComputeEdgeHash(buffer);
                    lock (saveLock) results.Add((card.Id, hash, edgeHash));
                }
                finally { throttle.Release(); await Task.Delay(50, token); }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to compute hash for Riftbound card {Id}", card.Id);
                Interlocked.Increment(ref failed);
            }

            var done = Interlocked.Increment(ref completed);
            if (done % 100 == 0)
                progress?.Report($"Hashed {done}/{cards.Count} cards ({failed} failed)...");

            List<(string Id, ulong Hash, ulong EdgeHash)>? toSave = null;
            lock (saveLock)
            {
                if (results.Count >= 200) { toSave = [.. results]; results.Clear(); }
            }
            if (toSave is not null) await SaveHashBatchAsync(toSave, ct);
        });

        if (results.Count > 0) await SaveHashBatchAsync(results, ct);

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("Riftbound hash computation complete: {Hashed} hashed, {Failed} failed in {Sec:F1}s", completed - failed, failed, sw.Elapsed.TotalSeconds);
        progress?.Report($"Done — hashed {completed - failed} cards ({failed} failed).");
    }

    private async Task SaveHashBatchAsync(List<(string Id, ulong Hash, ulong EdgeHash)> batch, CancellationToken ct)
    {
        await using var context = _dbContextFactory.CreateDbContext();
        foreach (var (id, hash, edgeHash) in batch)
        {
            var rel = GetLocalArtRelativePath(id);
            await context.Cards.Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.ImageHash, hash)
                    .SetProperty(c => c.EdgeHash, edgeHash)
                    .SetProperty(c => c.LocalImagePath, rel), ct);
        }
    }

    // === Matching ===

    // Parses OCR collector text of the form "{SET}-{number}" (e.g. "OGN-310"). The printed
    // "/total" has already been stripped by the OCR detector.
    internal static bool TryParseOcrCollectorNumber(string? ocr, out string setId, out int number)
    {
        setId = ""; number = 0;
        if (string.IsNullOrWhiteSpace(ocr)) return false;
        var dash = ocr.LastIndexOf('-');
        if (dash <= 0 || dash == ocr.Length - 1) return false;
        setId = ocr[..dash].ToUpperInvariant();
        return int.TryParse(ocr[(dash + 1)..], out number);
    }

    private RiftboundCard? LookupById(string id)
        => _readContext.Cards.AsNoTracking().FirstOrDefault(c => c.Id == id);

    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null)
    {
        LastMatchDiagnostics = new MatchDiagnostics { SetFilterActive = setFilter is not null };

        // Phase 0: OCR collector-number lookup. Set+number can map to multiple printings
        // (alt arts share a collector number) — disambiguate the candidate set by pHash.
        if (ocrResult?.CollectorNumber is not null && ocrResult.CollectorNumberConfidence >= 0.5
            && TryParseOcrCollectorNumber(ocrResult.CollectorNumber, out var ocrSet, out var ocrNum))
        {
            var candidates = _readContext.Cards.AsNoTracking()
                .Where(c => c.SetId == ocrSet && c.CollectorNumber == ocrNum)
                .ToList();

            if (setFilter is not null)
                candidates = candidates.Where(c => setFilter.Contains(c.SetId)).ToList();

            if (candidates.Count == 1)
            {
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ToMatch(candidates[0], confidence: 100);
            }
            if (candidates.Count > 1)
            {
                var hashed = candidates.Where(c => c.ImageHash != null).ToList();
                RiftboundCard best;
                if (hashed.Count > 0)
                {
                    best = hashed
                        .OrderBy(c => PerceptualHashService.HammingDistance(imageHash, c.ImageHash!.Value))
                        .First();
                }
                else
                {
                    best = candidates[0];
                }
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ToMatch(best, confidence: 100);
            }
        }

        // Foil path: match on the color-robust edge hash instead of the luminance pHash.
        if (scanEdgeHash is ulong scanEdge)
        {
            _edgeHashCache ??= _readContext.Cards
                .Where(c => c.EdgeHash != null)
                .Select(c => new { c.Id, Edge = c.EdgeHash!.Value, c.SetId })
                .AsNoTracking().AsEnumerable()
                .Select(c => (c.Id, c.Edge, c.SetId)).ToList();

            string bestEdgeId = "";
            int bestEdgeDist = int.MaxValue;
            foreach (var (id, edge, setId) in _edgeHashCache)
            {
                if (setFilter is not null && !setFilter.Contains(setId)) continue;
                var dist = PerceptualHashService.HammingDistance(scanEdge, edge);
                if (dist < bestEdgeDist) { bestEdgeDist = dist; bestEdgeId = id; }
            }
            if (bestEdgeId.Length > 0 && bestEdgeDist <= maxDistance)
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

        // Build caches.
        if (_hashCache is null)
        {
            var entries = _readContext.Cards
                .Where(c => c.ImageHash != null)
                .Select(c => new { c.Id, Hash = c.ImageHash!.Value, c.SetId })
                .AsNoTracking().AsEnumerable().ToList();
            _hashCache = entries.Select(c => (c.Id, c.Hash)).ToList();
            _hashSetLookup = entries.ToDictionary(c => c.Id, c => c.SetId);
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
        if (exact.CorrectCardId is not null)
        {
            var corrected = LookupById(exact.CorrectCardId);
            if (corrected is not null && (setFilter is null || setFilter.Contains(corrected.SetId)))
            {
                LastMatchDiagnostics.DecisionPhase = "ExactCorrection";
                return ToMatch(corrected, confidence: 100);
            }
        }

        if (_hashCache.Count == 0)
        {
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

        // Phase 2: nearest pHash + fuzzy corrections.
        string bestId = "";
        int bestDist = int.MaxValue;
        foreach (var (id, hash) in _hashCache)
        {
            if (setFilter is not null && !setFilter.Contains(_hashSetLookup![id])) continue;
            var dist = PerceptualHashService.HammingDistance(imageHash, hash);
            if (dist < bestDist) { bestDist = dist; bestId = id; }
        }

        string? bestCorrId = null;
        int bestCorrAdjusted = int.MaxValue;
        foreach (var (scanHash, correctCardId) in _correctionsCache)
        {
            if (setFilter is not null)
            {
                var corrSet = _hashSetLookup!.GetValueOrDefault(correctCardId);
                if (corrSet is null || !setFilter.Contains(corrSet)) continue;
            }
            var dist = PerceptualHashService.HammingDistance(imageHash, scanHash);
            if (dist <= maxDistance)
            {
                var adjusted = Math.Max(0, dist - CorrectionTrustBonus);
                if (adjusted < bestCorrAdjusted) { bestCorrAdjusted = adjusted; bestCorrId = correctCardId; }
            }
        }

        if (bestCorrId is not null && bestCorrAdjusted <= bestDist)
        {
            var conf = Math.Max(0, 1.0 - (double)bestCorrAdjusted / maxDistance) * 100;
            var corrected = LookupById(bestCorrId);
            if (corrected is not null)
            {
                LastMatchDiagnostics.DecisionPhase = "PHashConfident";
                LastMatchDiagnostics.PHashDistance = bestCorrAdjusted;
                return ToMatch(corrected, conf);
            }
        }

        if (bestDist > maxDistance)
        {
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }

        if (setFilter is not null)
        {
            var bestSet = _hashSetLookup!.GetValueOrDefault(bestId);
            if (bestSet is null || !setFilter.Contains(bestSet))
            {
                LastMatchDiagnostics.DecisionPhase = "NoMatch";
                return null;
            }
        }

        var bestCard = LookupById(bestId);
        if (bestCard is null)
        {
            LastMatchDiagnostics.DecisionPhase = "NoMatch";
            return null;
        }
        LastMatchDiagnostics.DecisionPhase = "PHashConfident";
        LastMatchDiagnostics.PHashDistance = bestDist;
        var confidence = Math.Max(0, 1.0 - (double)bestDist / maxDistance) * 100;
        return ToMatch(bestCard, confidence);
    }

    // === Query surface ===
    public IReadOnlyList<SetInfo> GetAvailableSets()
        => _readContext.Cards.AsNoTracking()
            .Select(c => new { c.SetId, c.SetName }).Distinct()
            .OrderBy(s => s.SetName).AsEnumerable()
            .Select(s => new SetInfo(s.SetId, s.SetName)).ToList();

    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
    {
        var setTotals = _readContext.Cards.AsNoTracking()
            .Select(c => new { c.SetId, c.SetName, c.CollectorNumber }).Distinct()
            .AsEnumerable()
            .GroupBy(c => new { c.SetId, c.SetName })
            .Select(g => new { g.Key.SetId, g.Key.SetName, Total = g.Count() })
            .ToDictionary(s => s.SetId, s => (s.SetName, s.Total));

        var ownedPerSet = ownedCards
            .GroupBy(c => c.SetCode)
            .ToDictionary(g => g.Key, g => (Distinct: g.Select(c => c.Number).Distinct().Count(), Physical: g.Count()));

        var results = new List<SetCompletionSummary>();
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
                Game = CardGame.Riftbound,
            });
        }
        return Task.FromResult(results);
    }

    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers)
    {
        var ownedSet = ownedCollectorNumbers.ToHashSet();
        return _readContext.Cards.AsNoTracking()
            .Where(c => c.SetId == setCode).AsEnumerable()
            .Where(c => !ownedSet.Contains(c.CollectorNumber.ToString()))
            .GroupBy(c => c.CollectorNumber)
            .Select(g => g.OrderBy(c => c.AlternateArt).First())
            .Select(c => new MissingCard
            {
                Name = c.Name,
                CollectorNumber = c.CollectorNumber.ToString(),
                SetCode = c.SetId,
                Rarity = c.Rarity,
                ImageUri = c.CardImageUri,
                TypeLine = c.CardType,
                OracleText = c.CardText,
                Power = c.Power?.ToString(),
                CardColor = c.Domain,
                CardCost = c.Energy?.ToString(),
            })
            .OrderBy(m => m.CollectorNumber).ToList();
    }

    public List<CardMatch> SearchCards(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        IQueryable<RiftboundCard> cards = _readContext.Cards.AsNoTracking();
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = term;
            if (t.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[4..];
                cards = cards.Where(c => EF.Functions.Like(c.SetId, $"%{val}%") || EF.Functions.Like(c.SetName, $"%{val}%"));
            }
            else if (t.StartsWith("type:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t[(t.IndexOf(':') + 1)..];
                cards = cards.Where(c => EF.Functions.Like(c.CardType, $"%{val}%"));
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

    // Riftcodex provides no prices.
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => null;
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => [];

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
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().FirstOrDefault(c => c.Id == gameCardId);
    }

    internal CardMatch ToMatch(RiftboundCard c, double? confidence = null) => new()
    {
        Name = c.Name,
        SetCode = c.SetId,
        SetName = c.SetName,
        CollectorNumber = c.CollectorNumber.ToString(),
        Rarity = c.Rarity,
        ImageUri = c.CardImageUri,
        GameSpecificId = c.Id,
        LocalImagePath = ResolveLocalArtPath(c.LocalImagePath),
        Confidence = confidence,
        Source = c
    };

    internal static string GetLocalArtRelativePath(string id) => $"riftbound-art/{id}.png";
    internal string GetLocalArtFullPath(string id) => Path.Combine(_dataDirectory, "riftbound-art", $"{id}.png");
    private string? ResolveLocalArtPath(string? relativePath)
    {
        if (relativePath is null) return null;
        var full = Path.Combine(_dataDirectory, relativePath);
        return File.Exists(full) ? full : null;
    }

    public void Dispose() => _readContext.Dispose();
}
