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

        // NOTE: intentionally not auto-invoking ComputeImageHashesAsync here yet — it's still a
        // NotImplementedException stub in this task. Task 5 (which implements it for real) must
        // reinstate `if (inserted > 0) await ComputeImageHashesAsync(forceAll: false, progress, ct);`
        // here, matching OptcgService/RiftboundService's pattern.
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
    public Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task ComputeImageHashesAsync(bool forceAll = false, IProgress<string>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
    public CardMatch? FindClosestMatch(ulong imageHash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, int maxDistance = 14, ulong? scanEdgeHash = null) => throw new NotImplementedException();
    public List<CardMatch> SearchCards(string query, int maxResults = 20) => throw new NotImplementedException();
    public List<CardMatch> GetPrintings(string cardName) => throw new NotImplementedException();
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil) => throw new NotImplementedException();
    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil) => throw new NotImplementedException();
    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null) => throw new NotImplementedException();
    public IReadOnlyList<SetInfo> GetAvailableSets() => throw new NotImplementedException();
    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null) => throw new NotImplementedException();
    public List<MissingCard> GetMissingCards(string setCode, IEnumerable<string> ownedCollectorNumbers) => throw new NotImplementedException();
    public object? FindCardById(string gameCardId) => throw new NotImplementedException();
}
