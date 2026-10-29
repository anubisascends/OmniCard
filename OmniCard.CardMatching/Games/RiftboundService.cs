using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Imaging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Sets;
using OmniCard.Shared.Settings;
using OmniCard.CardMatching.Search;
using OmniCard.Data.Catalogs;

namespace OmniCard.CardMatching.Games;

public sealed class RiftboundService : ICardGameService, ILowPriceSource, IGameFieldResolver, IDisposable
{
    private const int CorrectionTrustBonus = 5;
    private const int RiftboundCategoryId = 89;

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
        // SQLite (desktop) bootstraps the file + schema here. On SQL Server (web) the DB/schema are
        // owned by EnsureCreated at web startup, and the connection string is not a file path.
        if (_readContext.Database.IsSqlite())
        {
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
        _logger.LogInformation("Starting Riftbound card data download from TCGCSV");
        var sw = Stopwatch.StartNew();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        progress?.Report("Fetching Riftbound set list...");
        var groups = await TcgCsvCatalogClient.FetchGroupsAsync(client, RiftboundCategoryId, ct);
        var catalog = await TcgCsvCatalogClient.FetchProductsAsync(client, RiftboundCategoryId, groups, _logger,
            (done, total) => progress?.Report($"Fetched {done}/{total} sets..."), ct);
        var incoming = MapTcgCsvCatalog(catalog);
        _logger.LogInformation("Fetched {Count} Riftbound printings from TCGCSV", incoming.Count);
        progress?.Report($"Fetched {incoming.Count} cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingRows = await importContext.Cards.AsNoTracking()
            .Select(c => new ExistingRow(c.Id, c.TcgplayerId, c.RiftboundId))
            .ToListAsync(ct);
        LinkToExistingRows(incoming, existingRows);
        var existingIds = existingRows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var inserted = 0;
        var updated = 0;
        var gainedArt = 0;
        foreach (var batch in incoming.Chunk(500))
        {
            var existingCardIds = batch.Where(c => existingIds.Contains(c.Id)).Select(c => c.Id).ToList();
            if (existingCardIds.Count > 0)
            {
                var tracked = await importContext.Cards
                    .Where(c => existingCardIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, ct);
                foreach (var card in batch)
                    if (tracked.TryGetValue(card.Id, out var existing) && ApplyTcgCsvFields(existing, card))
                        gainedArt++;

                await importContext.SaveChangesAsync(ct);
                importContext.ChangeTracker.Clear();
                updated += existingCardIds.Count;
            }

            var newCards = batch.Where(c => !existingIds.Contains(c.Id)).ToList();
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

        if (incoming.Count > 0)
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

        if (inserted > 0 || gainedArt > 0)
            await ComputeImageHashesAsync(forceAll: false, progress, ct);
    }

    // === TCGCSV catalog ===
    // The catalog comes from TCGCSV (TCGplayer), refreshed daily. Rows imported before TCGCSV became the
    // source carry Riftcodex ids; a TCGCSV printing updates such a row in place (matched by TcgplayerId,
    // then by riftbound id) so owned copies keep their id. Printings with no existing row are added under
    // a stable "tcgcsv-{productId}" id. Rows TCGCSV doesn't list are left as they are, never deleted.
    internal const string TcgCsvIdPrefix = "tcgcsv-";

    internal sealed record ExistingRow(string Id, string? TcgplayerId, string RiftboundId);

    // Every card product (sealed products have no "Number") mapped to a row, one per TCGplayer product.
    internal static List<RiftboundCard> MapTcgCsvCatalog(IEnumerable<(TcgCsvGroup Group, List<TcgCsvProduct> Products)> catalog)
    {
        var cards = new List<RiftboundCard>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (group, products) in catalog)
        {
            var setId = group.Abbreviation?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(setId)) continue;
            foreach (var product in products)
                if (MapTcgCsvProduct(product, setId, group.Name) is { } card && seen.Add(card.TcgplayerId!))
                    cards.Add(card);
        }
        return cards;
    }

    // Re-keys incoming cards onto the existing rows for the same printing: first by TcgplayerId, then (for
    // rows that never had a TCGplayer id) by riftbound id. Each existing row is taken at most once.
    internal static void LinkToExistingRows(List<RiftboundCard> incoming, IReadOnlyCollection<ExistingRow> existing)
    {
        var byTcgId = existing.Where(r => r.TcgplayerId is not null)
            .GroupBy(r => r.TcgplayerId!).ToDictionary(g => g.Key, g => g.First().Id);
        var byRiftboundId = existing.Where(r => r.TcgplayerId is null && r.RiftboundId.Length > 0)
            .GroupBy(r => r.RiftboundId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var unlinked = new List<RiftboundCard>();

        foreach (var card in incoming)
        {
            if (byTcgId.TryGetValue(card.TcgplayerId!, out var id) && taken.Add(id)) card.Id = id;
            else unlinked.Add(card);
        }
        foreach (var card in unlinked)
            if (byRiftboundId.TryGetValue(card.RiftboundId, out var id) && taken.Add(id))
                card.Id = id;
    }

    // Refreshes an existing row from its TCGCSV printing: TcgplayerId always. A row TCGCSV created
    // ("tcgcsv-" id) also takes every catalog field TCGCSV has, so presale corrections land. A row from
    // Riftcodex keeps its own data — it is richer than TCGplayer's listing (supertypes such as "Basic",
    // stats, rules text) — and only fills fields it lacks. Either way the id, set code and stored
    // art/hashes stay. Returns whether the row gained art (to hash).
    internal static bool ApplyTcgCsvFields(RiftboundCard existing, RiftboundCard card)
    {
        existing.TcgplayerId = card.TcgplayerId;
        existing.DateScraped = card.DateScraped;

        var overwrite = existing.Id.StartsWith(TcgCsvIdPrefix, StringComparison.Ordinal);
        static string? Pick(string? current, string? incoming, bool overwrite) =>
            string.IsNullOrWhiteSpace(incoming) || (!overwrite && !string.IsNullOrWhiteSpace(current)) ? current : incoming;
        static int? PickInt(int? current, int? incoming, bool overwrite) =>
            overwrite || current is null ? incoming ?? current : current;

        existing.RiftboundId = Pick(existing.RiftboundId, card.RiftboundId, overwrite)!;
        if (overwrite || existing.CollectorNumber == 0) existing.CollectorNumber = card.CollectorNumber;
        existing.Name = Pick(existing.Name, card.Name, overwrite)!;
        existing.CleanName = Pick(existing.CleanName, card.CleanName, overwrite);
        existing.SetName = Pick(existing.SetName, card.SetName, overwrite)!;
        existing.Rarity = Pick(existing.Rarity, card.Rarity, overwrite)!;
        if (overwrite || string.IsNullOrWhiteSpace(existing.CardType))
        {
            existing.CardType = Pick(existing.CardType, card.CardType, true)!;
            existing.Supertype = card.Supertype ?? existing.Supertype;
        }
        existing.Domain = Pick(existing.Domain, card.Domain, overwrite)!;
        existing.Energy = PickInt(existing.Energy, card.Energy, overwrite);
        existing.Might = PickInt(existing.Might, card.Might, overwrite);
        existing.Power = PickInt(existing.Power, card.Power, overwrite);
        existing.CardText = Pick(existing.CardText, card.CardText, overwrite);
        existing.Flavour = Pick(existing.Flavour, card.Flavour, overwrite);
        existing.Orientation = Pick(existing.Orientation, card.Orientation, overwrite);
        if (overwrite)
        {
            existing.AlternateArt = card.AlternateArt;
            existing.Overnumbered = card.Overnumbered;
            existing.Signature = card.Signature;
        }

        if (existing.CardImageUri is not null || card.CardImageUri is null) return false;
        existing.CardImageUri = card.CardImageUri;
        return true;
    }

    internal static RiftboundCard? MapTcgCsvProduct(TcgCsvProduct p, string setId, string setName)
    {
        string? Val(string name) => TcgCsvCatalogClient.Extended(p, name);

        var number = Val("Number");
        if (string.IsNullOrEmpty(number)) return null;

        var slash = number.IndexOf('/');
        var printed = (slash >= 0 ? number[..slash] : number).Trim();
        var total = slash >= 0 ? number[(slash + 1)..].Trim() : "";
        // Last digit run: "020a" → 20, "R01" → 1, "T1A 001" → 1.
        var collectorNumber = int.TryParse(
            System.Text.RegularExpressions.Regex.Match(printed, @"\d+(?=\D*$)").Value, out var cn) ? cn : 0;

        // "Champion Unit" / "Signature Spell" → type Unit/Spell + supertype; "Unit;Token" → Unit + Token.
        var typeParts = (Val("Card Type") ?? "")
            .Split([';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (cardType, supertype) = typeParts.Length switch
        {
            0 => ("", (string?)null),
            1 => (typeParts[0], null),
            _ => typeParts[0] is "Unit" or "Spell" or "Gear" or "Legend"
                ? (typeParts[0], typeParts[1])
                : (typeParts[1], typeParts[0]),
        };

        var isUnit = cardType == "Unit";
        var power = TcgCsvCatalogClient.LeadingInt(Val("Power Cost"));

        return new RiftboundCard
        {
            Id = TcgCsvIdPrefix + p.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RiftboundId = total.Length > 0
                ? $"{setId}-{printed}-{total}".ToLowerInvariant()
                : $"{setId}-{printed}".ToLowerInvariant(),
            TcgplayerId = p.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CollectorNumber = collectorNumber,
            Name = TcgCsvCatalogClient.CardName(p.Name),
            CleanName = p.CleanName,
            SetId = setId,
            SetName = setName,
            Rarity = Val("Rarity") ?? "",
            CardType = cardType,
            Supertype = supertype,
            // Battlefields carry "None"; store them domain-less.
            Domain = string.Join("/", (Val("Domain") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(d => !d.Equals("None", StringComparison.OrdinalIgnoreCase))),
            Energy = TcgCsvCatalogClient.LeadingInt(Val("Energy Cost")),
            Might = isUnit ? TcgCsvCatalogClient.LeadingInt(Val("Might")) : null,
            Power = power > 0 ? power : null,
            CardText = TcgCsvCatalogClient.StripHtml(Val("Description")),
            Flavour = TcgCsvCatalogClient.StripHtml(Val("Flavor Text")),
            Orientation = cardType == "Battlefield" ? "landscape" : "portrait",
            AlternateArt = p.Name.Contains("Alternate Art", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Special Art", StringComparison.OrdinalIgnoreCase)
                || (printed.Length > 0 && char.IsAsciiLetter(printed[^1])),
            Overnumbered = p.Name.Contains("Overnumbered", StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(total, out var t) && t > 0 && collectorNumber > t),
            Signature = printed.Contains('*') || p.Name.Contains("(Signature)", StringComparison.OrdinalIgnoreCase),
            CardImageUri = TcgCsvCatalogClient.ProductImage(p),
            DateScraped = DateTime.UtcNow.ToString("o"),
        };
    }

    public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        await using (var ctx = _dbContextFactory.CreateDbContext())
        {
            if (!await ctx.Cards.AnyAsync(ct))
            {
                _logger.LogInformation("Skipping Riftbound price refresh: card database is empty (run a full data download first)");
                return;
            }
        }

        _logger.LogInformation("Starting Riftbound price-only refresh via TCGCSV");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");

        var priceMap = await FetchTcgCsvPriceMapAsync(client, progress, ct);

        await using var context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();

        var now = DateTime.UtcNow;
        var updated = 0;

        // Only touch cards whose TcgplayerId parses and exists in the price map.
        var allCards = await context.Cards
            .Where(c => c.TcgplayerId != null)
            .Select(c => new { c.Id, c.TcgplayerId })
            .ToListAsync(ct);

        var targets = allCards
            .Select(c => (c.Id, Parsed: int.TryParse(c.TcgplayerId, out var pid) ? pid : (int?)null))
            .Where(c => c.Parsed is int pid && priceMap.ContainsKey(pid))
            .ToList();

        foreach (var batch in targets.Chunk(500))
        {
            var ids = batch.Select(c => c.Id).ToList();
            var tracked = await context.Cards
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

            foreach (var (id, parsed) in batch)
            {
                if (parsed is int pid && tracked.TryGetValue(id, out var existing)
                    && priceMap.TryGetValue(pid, out var prices))
                {
                    existing.MarketPrice = prices.Normal;
                    existing.FoilMarketPrice = prices.Foil;
                    existing.LowPrice = prices.NormalLow;
                    existing.FoilLowPrice = prices.FoilLow;
                    existing.PriceUpdatedAt = now;
                    updated++;
                }
            }

            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        _logger.LogInformation("Riftbound price refresh complete: {Updated} cards updated", updated);
        progress?.Report(new PriceUpdateProgress(CardGame.Riftbound, null, 0, 0,
            $"Riftbound prices updated ({updated} cards)"));
    }

    // Fetches all Riftbound group prices from TCGCSV and folds them into a
    // productId -> (normal, foil) market + low price map.
    private async Task<Dictionary<int, (decimal? Normal, decimal? Foil, decimal? NormalLow, decimal? FoilLow)>> FetchTcgCsvPriceMapAsync(
        HttpClient client, IProgress<PriceUpdateProgress>? progress, CancellationToken ct)
    {
        var groups = await TcgCsvCatalogClient.FetchGroupsAsync(client, RiftboundCategoryId, ct);
        var rows = await TcgCsvCatalogClient.FetchPriceRowsAsync(client, RiftboundCategoryId, groups, _logger,
            (done, total) => progress?.Report(new PriceUpdateProgress(CardGame.Riftbound, null, done, total,
                $"Riftbound prices: {done}/{total} groups")), ct);

        var map = new Dictionary<int, (decimal? Normal, decimal? Foil, decimal? NormalLow, decimal? FoilLow)>(rows.Count);
        foreach (var (productId, productRows) in rows)
        {
            (decimal? Normal, decimal? Foil, decimal? NormalLow, decimal? FoilLow) entry = default;
            foreach (var row in productRows)
            {
                if (string.Equals(row.SubTypeName, "Foil", StringComparison.OrdinalIgnoreCase))
                    (entry.Foil, entry.FoilLow) = (row.MarketPrice, row.LowPrice);
                else if (string.Equals(row.SubTypeName, "Normal", StringComparison.OrdinalIgnoreCase))
                    (entry.Normal, entry.NormalLow) = (row.MarketPrice, row.LowPrice);
            }
            map[productId] = entry;
        }
        return map;
    }

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
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking()
            .Select(c => new { c.SetId, c.SetName }).Distinct()
            .AsEnumerable()
            .Select(s => new SetInfo(s.SetId, s.SetName)).InNaturalOrder();
    }

    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        var setTotals = ctx.Cards.AsNoTracking()
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
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking()
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

    public List<SetCatalogCard> GetSetCards(string setCode)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking()
            .Where(c => c.SetId == setCode).AsEnumerable()
            .GroupBy(c => c.CollectorNumber)
            .Select(g => g.OrderBy(c => c.AlternateArt).First())
            .Select(c => new SetCatalogCard
            {
                GameCardId = c.Id,
                Name = c.Name,
                CollectorNumber = c.CollectorNumber.ToString(),
                SetCode = c.SetId,
                SetName = c.SetName,
                Rarity = c.Rarity,
                ImageUri = c.CardImageUri,
                LocalImagePath = c.LocalImagePath,
                NormalPrice = c.MarketPrice,
                FoilPrice = c.FoilMarketPrice,
                HasFoil = c.FoilMarketPrice.HasValue,
            })
            .OrderBy(c => c.CollectorNumber, CollectorNumberComparer.Instance)
            .ToList();
    }

    // Riftbound searchable fields. Core name/set/cn/type/rarity map to columns; domain/energy/might/
    // power/supertype are game-specific (also resolvable for owned-card search).
    private SearchSchema? _searchSchema;
    public SearchSchema SearchSchema => _searchSchema ??= SharedSearchSchema.WithGameFields(
    [
        new SearchFieldDefinition { Canonical = "domain", Aliases = ["domain", "d"], SourceKey = nameof(RiftboundCard.Domain),
            Description = "Domain (Body, Order, …). Matches any of a card's domains.", Example = "domain:body" },
        new SearchFieldDefinition { Canonical = "energy", Aliases = ["energy"], SourceKey = nameof(RiftboundCard.Energy),
            SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
            Description = "Energy cost (supports <, >, <=, >=).", Example = "energy>=4" },
        new SearchFieldDefinition { Canonical = "might", Aliases = ["might", "m"], SourceKey = nameof(RiftboundCard.Might),
            SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
            Description = "Might (supports <, >, <=, >=).", Example = "might>=5" },
        new SearchFieldDefinition { Canonical = "power", Aliases = ["power", "pow"], SourceKey = nameof(RiftboundCard.Power),
            SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
            Description = "Power (supports <, >, <=, >=).", Example = "power>=3" },
        new SearchFieldDefinition { Canonical = "supertype", Aliases = ["supertype", "super"], SourceKey = nameof(RiftboundCard.Supertype),
            Description = "Supertype.", Example = "supertype:champion" },
    ]);

    private static readonly CatalogFieldMap<RiftboundCard> FieldMap = BuildFieldMap();

    private static CatalogFieldMap<RiftboundCard> BuildFieldMap()
    {
        var map = new CatalogFieldMap<RiftboundCard>
        {
            NamePredicate = (p, op, v) => CatalogSearchExpressionBuilder.StringPredicate(p, nameof(RiftboundCard.Name), op, v),
        };
        map.Str("name", nameof(RiftboundCard.Name))
           .Field("set", (p, op, v) => System.Linq.Expressions.Expression.OrElse(
               CatalogSearchExpressionBuilder.StringPredicate(p, nameof(RiftboundCard.SetId), op, v),
               CatalogSearchExpressionBuilder.StringPredicate(p, nameof(RiftboundCard.SetName), op, v)))
           .Num("cn", nameof(RiftboundCard.CollectorNumber))
           .Str("type", nameof(RiftboundCard.CardType))
           .Str("rarity", nameof(RiftboundCard.Rarity))
           .Str("domain", nameof(RiftboundCard.Domain))
           .Num("energy", nameof(RiftboundCard.Energy))
           .Num("might", nameof(RiftboundCard.Might))
           .Num("power", nameof(RiftboundCard.Power))
           .Str("supertype", nameof(RiftboundCard.Supertype));
        return map;
    }

    public List<CardMatch> SearchCards(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var node = ScryfallQueryParser.ParseFilter(query, SearchSchema);
        // Per-call context: DbContext is not thread-safe; the shared _readContext throws under
        // concurrent searches (fast typing fans out overlapping /api/scan/search requests).
        using var ctx = _dbContextFactory.CreateDbContext();
        IQueryable<RiftboundCard> cards = ctx.Cards.AsNoTracking();
        var lambda = CatalogSearchExpressionBuilder.Build(node, SearchSchema, FieldMap);
        if (lambda is not null) cards = cards.Where(lambda);
        return cards.OrderBy(c => c.Name).Take(maxResults).AsEnumerable().Select(c => ToMatch(c)).ToList();
    }

    public IReadOnlySet<string>? ResolveFieldCardIds(string field, ComparisonOp op, string value)
    {
        if (!SearchSchema.IsGameSpecific(field)) return null;
        var resolved = SearchSchema.ResolveValue(field, value);
        var lambda = CatalogSearchExpressionBuilder.SingleField(field, op, resolved, FieldMap);
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().Where(lambda).Select(c => c.Id).ToHashSet();
    }

    public List<CardMatch> GetPrintings(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return [];
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking()
            .Where(c => c.Name == cardName)
            .OrderBy(c => c.SetName).ThenBy(c => c.CollectorNumber)
            .AsEnumerable().Select(c => ToMatch(c)).ToList();
    }

    // Prices come from TCGCSV (see UpdatePricesAsync). Foil-aware with fallback to the
    // other subtype so a card resolves a price whenever any subtype has one.
    public decimal? GetCurrentPrice(string gameCardId, bool isFoil)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        var row = ctx.Cards.AsNoTracking()
            .Where(c => c.Id == gameCardId)
            .Select(c => new { c.MarketPrice, c.FoilMarketPrice })
            .FirstOrDefault();
        if (row is null) return null;
        return isFoil
            ? row.FoilMarketPrice ?? row.MarketPrice
            : row.MarketPrice ?? row.FoilMarketPrice;
    }

    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)
    {
        var ids = gameCardIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var result = new Dictionary<string, decimal>(ids.Count);

        using var ctx = _dbContextFactory.CreateDbContext();
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = ctx.Cards.AsNoTracking()
                .Where(c => chunk.Contains(c.Id))
                .Select(c => new { c.Id, c.MarketPrice, c.FoilMarketPrice })
                .ToList();

            foreach (var row in rows)
            {
                var price = isFoil
                    ? row.FoilMarketPrice ?? row.MarketPrice
                    : row.MarketPrice ?? row.FoilMarketPrice;
                if (price.HasValue)
                    result[row.Id] = price.Value;
            }
        }

        return result;
    }

    public Dictionary<string, decimal> GetLowPrices(IEnumerable<string> gameCardIds, bool isFoil)
    {
        var ids = gameCardIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var result = new Dictionary<string, decimal>(ids.Count);
        using var ctx = _dbContextFactory.CreateDbContext();
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = ctx.Cards.AsNoTracking()
                .Where(c => chunk.Contains(c.Id))
                .Select(c => new { c.Id, c.LowPrice, c.FoilLowPrice })
                .ToList();
            foreach (var row in rows)
            {
                var price = isFoil ? row.FoilLowPrice ?? row.LowPrice : row.LowPrice ?? row.FoilLowPrice;
                if (price.HasValue)
                    result[row.Id] = price.Value;
            }
        }
        return result;
    }

    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        // Provider-agnostic upsert (was SQLite-only INSERT OR REPLACE; failed on SQL Server catalogs).
        OmniCard.Data.Catalogs.HashCorrectionUpsert.Upsert(ctx, scanHash, correctCardId, artScanHash);
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
