using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed class OptcgService : ICardGameService, IGameFieldResolver, ICatalogLanguageAware, IDisposable
{
    private const string ApiBaseUrl = "https://api.poneglyph.one";

    /// <summary>Separator between a variant uid and its language on non-English rows ("OP01-001@ja").</summary>
    internal const char LanguageSeparator = '@';

    private IReadOnlyCollection<string> _catalogLanguages = [CardLanguages.English];

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
        // SQLite (desktop) bootstraps the file + schema here. On SQL Server (web) the DB/schema are
        // owned by EnsureCreated at web startup, and the connection string is not a file path.
        if (_readContext.Database.IsSqlite())
        {
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
        _numberIndex = null;
        _correctionsCache = null;
        oldContext.Dispose();

        _logger.LogInformation("OPTCG migration wipe complete");
    }

    public CardGame Game => CardGame.OnePiece;

    // === ICatalogLanguageAware ===

    public IReadOnlyList<string> DownloadableLanguages => CardLanguages.DownloadableFor(CardGame.OnePiece);

    // TCGCSV is English-only, but the Japanese/French rows downloaded earlier from poneglyph are kept.
    public IReadOnlyList<string> SearchableLanguages { get; } = [CardLanguages.English, "ja", "fr"];

    public IReadOnlyCollection<string> CatalogLanguages
    {
        get => _catalogLanguages;
        set => _catalogLanguages = CardLanguages.SanitizeCatalogSelection(CardGame.OnePiece, value);
    }

    /// <summary>The row key for a variant in <paramref name="language"/>: English keeps the bare uid.</summary>
    internal static string LanguageCardSetId(string englishUid, string language) =>
        language == CardLanguages.English ? englishUid : $"{englishUid}{LanguageSeparator}{language}";

    /// <summary>The English uid a (possibly language-suffixed) row key belongs to.</summary>
    internal static string EnglishCardSetId(string cardSetId)
    {
        var at = cardSetId.IndexOf(LanguageSeparator);
        return at < 0 ? cardSetId : cardSetId[..at];
    }

    public string? GetCardLanguage(string gameCardId)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().Where(c => c.CardSetId == gameCardId).Select(c => c.Lang).FirstOrDefault();
    }

    /// <summary>The same variant in <paramref name="language"/>. poneglyph numbers each language's
    /// variants separately, so alt-art indices only line up approximately; the base printing (index 0)
    /// always corresponds.</summary>
    public CardMatch? FindLanguageVariant(string gameCardId, string language)
    {
        var target = LanguageCardSetId(EnglishCardSetId(gameCardId), language);
        if (target == gameCardId) return null;
        using var ctx = _dbContextFactory.CreateDbContext();
        var card = ctx.Cards.AsNoTracking().FirstOrDefault(c => c.CardSetId == target);
        return card is null ? null : ToMatch(card, confidence: null);
    }

    private CardMatch ToMatch(OptcgCard card, double? confidence) => new()
    {
        Name = card.CardName,
        SetCode = card.SetId,
        SetName = card.SetName,
        CollectorNumber = card.CardNumber,
        Rarity = card.Rarity,
        ImageUri = card.CardImageUri,
        GameSpecificId = card.CardSetId,
        Language = card.Lang,
        LocalImagePath = ResolveLocalArtPath(card.LocalImagePath),
        Confidence = confidence,
        Source = card,
    };

    public MatchDiagnostics? LastMatchDiagnostics { get; private set; }

    private List<(string CardSetId, ulong Hash)>? _hashCache;
    private List<(string CardSetId, ulong EdgeHash, string SetId)>? _edgeHashCache;
    private Dictionary<string, string>? _hashSetLookup;
    private List<(ulong ScanHash, string CorrectCardId)>? _correctionsCache;
    private const int CorrectionTrustBonus = 5;

    // === TCGCSV catalog ===
    // The catalog and its prices come from TCGCSV (TCGplayer category 68), refreshed daily. Rows are
    // found by TcgplayerId. Rows imported before TCGCSV became the source (poneglyph) are linked to their
    // TCGplayer product once, from poneglyph's tcgplayer_url, so they update in place and owned copies
    // keep their id. TCGCSV is English-only: non-English rows from earlier poneglyph downloads are kept
    // as they are (their prices fall back to the English printing), and nothing is ever deleted.
    private const int OnePieceCategoryId = 68;

    public async Task DownloadBulkDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting One Piece card data download from TCGCSV");
        var sw = Stopwatch.StartNew();
        var client = CreateClient();

        await EnsureTcgplayerLinksAsync(client, progress, ct);

        progress?.Report("Fetching One Piece set list...");
        var groups = await TcgCsvCatalogClient.FetchGroupsAsync(client, OnePieceCategoryId, ct);
        var catalog = await TcgCsvCatalogClient.FetchProductsAsync(client, OnePieceCategoryId, groups, _logger,
            (done, total) => progress?.Report($"Fetched {done}/{total} sets..."), ct);
        progress?.Report("Fetching One Piece prices...");
        var prices = MapTcgCsvPrices(await TcgCsvCatalogClient.FetchPriceRowsAsync(
            client, OnePieceCategoryId, groups, _logger, null, ct));

        var incoming = MapTcgCsvCatalog(catalog);
        _logger.LogInformation("Fetched {Count} One Piece printings from TCGCSV", incoming.Count);
        progress?.Report($"Fetched {incoming.Count} cards, importing...");

        await using var importContext = _dbContextFactory.CreateDbContext();
        importContext.Database.EnsureCreated();

        var existingRows = await importContext.Cards.AsNoTracking()
            .Select(c => new ExistingRow(c.CardSetId, c.Lang, c.TcgplayerId, c.CardNumber, c.VariantIndex, c.SetId, c.SetName))
            .ToListAsync(ct);
        var (updates, inserts) = PlanImport(incoming, existingRows);
        foreach (var card in incoming)
            if (prices.TryGetValue(card.TcgplayerId!.Value, out var price))
                (card.MarketPrice, card.InventoryPrice) = price;

        var updated = 0;
        var gainedArt = 0;
        foreach (var batch in updates.Chunk(500))
        {
            var ids = batch.Select(u => u.ExistingId).ToList();
            var tracked = await importContext.Cards.Where(c => ids.Contains(c.CardSetId)).ToDictionaryAsync(c => c.CardSetId, ct);
            foreach (var (existingId, card) in batch)
                if (tracked.TryGetValue(existingId, out var existing))
                {
                    if (ApplyTcgCsvFields(existing, card)) gainedArt++;
                    updated++;
                }
            await importContext.SaveChangesAsync(ct);
            importContext.ChangeTracker.Clear();
            progress?.Report($"Updated {updated} cards...");
        }

        var inserted = 0;
        foreach (var batch in inserts.Chunk(500))
        {
            importContext.Cards.AddRange(batch);
            await importContext.SaveChangesAsync(ct);
            importContext.ChangeTracker.Clear();
            inserted += batch.Length;
            progress?.Report($"Processed {inserted + updated} cards ({inserted} new, {updated} updated)...");
        }

        if (incoming.Count > 0)
            importContext.MarkMigrationComplete();

        var oldContext = _readContext;
        _readContext = _dbContextFactory.CreateDbContext();
        _hashCache = null;
        _edgeHashCache = null;
        _hashSetLookup = null;
        _numberIndex = null;
        oldContext.Dispose();

        sw.Stop();
        _logger.LogInformation("One Piece download complete: {Inserted} new, {Updated} updated in {ElapsedSec:F1}s", inserted, updated, sw.Elapsed.TotalSeconds);
        progress?.Report($"Download complete — {inserted} new, {updated} updated.");

        if (inserted > 0 || gainedArt > 0)
        {
            _logger.LogInformation("Auto-computing hashes for {Count} new cards and {Art} cards with new art", inserted, gainedArt);
            await ComputeImageHashesAsync(forceAll: false, progress, ct);
        }
    }

    public async Task UpdatePricesAsync(IProgress<PriceUpdateProgress>? progress = null, CancellationToken ct = default)
    {
        await using (var ctx = _dbContextFactory.CreateDbContext())
        {
            if (!await ctx.Cards.AnyAsync(ct))
            {
                _logger.LogInformation("Skipping One Piece price refresh: card database is empty (run a full data download first)");
                return;
            }
        }

        _logger.LogInformation("Starting One Piece price refresh via TCGCSV");
        var client = CreateClient();
        await EnsureTcgplayerLinksAsync(client,
            progress is null ? null : new Progress<string>(m => progress.Report(new PriceUpdateProgress(CardGame.OnePiece, null, 0, 0, m))), ct);

        var groups = await TcgCsvCatalogClient.FetchGroupsAsync(client, OnePieceCategoryId, ct);
        var prices = MapTcgCsvPrices(await TcgCsvCatalogClient.FetchPriceRowsAsync(client, OnePieceCategoryId, groups, _logger,
            (done, total) => progress?.Report(new PriceUpdateProgress(CardGame.OnePiece, null, done, total,
                $"One Piece prices: {done}/{total} groups")), ct));

        await using var context = _dbContextFactory.CreateDbContext();
        var targets = (await context.Cards.Where(c => c.TcgplayerId != null)
                .Select(c => new { c.CardSetId, c.TcgplayerId }).ToListAsync(ct))
            .Where(c => prices.ContainsKey(c.TcgplayerId!.Value))
            .ToList();

        var updated = 0;
        foreach (var batch in targets.Chunk(500))
        {
            var ids = batch.Select(c => c.CardSetId).ToList();
            var tracked = await context.Cards.Where(c => ids.Contains(c.CardSetId)).ToListAsync(ct);
            foreach (var card in tracked)
            {
                (card.MarketPrice, card.InventoryPrice) = prices[card.TcgplayerId!.Value];
                updated++;
            }
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        _logger.LogInformation("One Piece price refresh complete: {Updated} cards updated", updated);
        progress?.Report(new PriceUpdateProgress(CardGame.OnePiece, null, 0, 0,
            $"One Piece prices updated ({updated} cards)"));
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniCard/1.0");
        return client;
    }

    // One-time link for a catalog downloaded from poneglyph before TCGCSV became the source: poneglyph
    // lists each variant's TCGplayer product URL, which carries the id TCGCSV keys everything by. Runs
    // only while no row has a TcgplayerId yet; a failed fetch aborts the refresh (nothing changed) rather
    // than letting the TCGCSV import add a second copy of every printing.
    private async Task EnsureTcgplayerLinksAsync(HttpClient client, IProgress<string>? progress, CancellationToken ct)
    {
        await using (var ctx = _dbContextFactory.CreateDbContext())
        {
            if (!await ctx.Cards.AnyAsync(c => c.Lang == CardLanguages.English, ct)
                || await ctx.Cards.AnyAsync(c => c.TcgplayerId != null, ct))
                return;
        }

        _logger.LogInformation("Linking the existing One Piece catalog to TCGplayer products (one time)");
        progress?.Report("Linking existing One Piece cards to TCGplayer (one time)...");
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        List<OptcgCard> variants;
        try
        {
            variants = await FetchAllVariantsAsync(client, jsonOptions,
                (done, total, _) => progress?.Report($"Linking existing cards to TCGplayer: {done}/{total} sets..."), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Couldn't link the existing One Piece catalog to TCGplayer (poneglyph is unavailable). Nothing was changed; try again later.", ex);
        }

        // A product listed against several variants links to the lowest-numbered one only.
        var links = variants
            .Where(v => v.TcgplayerId is not null)
            .GroupBy(v => v.TcgplayerId!.Value)
            .Select(g => g.OrderBy(v => v.VariantIndex).First())
            .ToDictionary(v => v.CardSetId, v => v.TcgplayerId!.Value);
        if (links.Count == 0)
            throw new InvalidOperationException(
                "Couldn't link the existing One Piece catalog to TCGplayer (poneglyph returned no TCGplayer links). Nothing was changed; try again later.");

        await using var context = _dbContextFactory.CreateDbContext();
        var linked = 0;
        foreach (var batch in links.Keys.Chunk(500))
        {
            var tracked = await context.Cards.Where(c => batch.Contains(c.CardSetId)).ToListAsync(ct);
            foreach (var card in tracked)
            {
                card.TcgplayerId = links[card.CardSetId];
                linked++;
            }
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }
        _logger.LogInformation("Linked {Linked} One Piece printings to TCGplayer products", linked);
    }

    internal sealed record ExistingRow(
        string CardSetId, string Lang, int? TcgplayerId, string CardNumber, int VariantIndex, string SetId, string SetName);

    // Splits the TCGCSV printings into updates of existing rows (by TcgplayerId) and new rows. A new
    // printing of a number the catalog doesn't know yet takes the bare number as its id (its standard
    // printing first); further variants get "{number}_t{productId}" and the next variant index. New rows
    // reuse the catalog's existing name for their set, so a set keeps one name in the Sets view.
    internal static (List<(string ExistingId, OptcgCard Card)> Updates, List<OptcgCard> Inserts) PlanImport(
        IReadOnlyList<OptcgCard> incoming, IReadOnlyCollection<ExistingRow> existing)
    {
        var byTcgId = existing.Where(r => r.TcgplayerId is not null)
            .GroupBy(r => r.TcgplayerId!.Value).ToDictionary(g => g.Key, g => g.First().CardSetId);
        var usedIds = existing.Select(r => r.CardSetId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var maxIndex = existing.Where(r => r.Lang == CardLanguages.English && r.CardNumber.Length > 0)
            .GroupBy(r => r.CardNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(r => r.VariantIndex), StringComparer.OrdinalIgnoreCase);
        var setNames = existing.Where(r => r.SetName.Length > 0)
            .GroupBy(r => r.SetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SetName, StringComparer.OrdinalIgnoreCase);

        var updates = new List<(string, OptcgCard)>();
        var inserts = new List<OptcgCard>();
        var ordered = incoming
            .OrderBy(c => c.CardNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.VariantLabel == StandardLabel ? 0 : 1)
            .ThenBy(c => c.TcgplayerId);
        foreach (var card in ordered)
        {
            if (byTcgId.TryGetValue(card.TcgplayerId!.Value, out var existingId))
            {
                updates.Add((existingId, card));
                continue;
            }

            var known = maxIndex.TryGetValue(card.CardNumber, out var max);
            card.VariantIndex = known ? max + 1 : 0;
            maxIndex[card.CardNumber] = card.VariantIndex;
            card.CardSetId = usedIds.Contains(card.CardNumber)
                ? $"{card.CardNumber}_t{card.TcgplayerId}"
                : card.CardNumber;
            usedIds.Add(card.CardSetId);
            if (setNames.TryGetValue(card.SetId, out var setName)) card.SetName = setName;
            else setNames[card.SetId] = card.SetName;
            inserts.Add(card);
        }
        return (updates, inserts);
    }

    // Refreshes an existing row from its TCGCSV printing: TcgplayerId and prices always. A row TCGCSV
    // created (FromTcgCsv) also takes every catalog field TCGCSV has, so presale corrections land. A row
    // from poneglyph keeps its own data — it is more accurate than TCGplayer's listing (printed rarity
    // vs "PR", correct card types, cleaner rules text) — and only fills fields it lacks. Either way the id,
    // number, set, variant index and stored art/hashes stay. Returns whether the row gained art (to hash).
    internal static bool ApplyTcgCsvFields(OptcgCard existing, OptcgCard card)
    {
        existing.TcgplayerId = card.TcgplayerId;
        if (card.MarketPrice is not null || card.InventoryPrice is not null)
        {
            existing.MarketPrice = card.MarketPrice;
            existing.InventoryPrice = card.InventoryPrice;
        }
        existing.DateScraped = card.DateScraped;

        static string? Pick(string? current, string? incoming, bool overwrite) =>
            string.IsNullOrWhiteSpace(incoming) || (!overwrite && !string.IsNullOrWhiteSpace(current)) ? current : incoming;
        var overwrite = existing.FromTcgCsv;
        existing.CardName = Pick(existing.CardName, card.CardName, overwrite)!;
        existing.VariantLabel = Pick(existing.VariantLabel, card.VariantLabel, overwrite);
        existing.SetName = Pick(existing.SetName, card.SetName, false)!;
        existing.Rarity = Pick(existing.Rarity, card.Rarity, overwrite)!;
        existing.CardColor = Pick(existing.CardColor, card.CardColor, overwrite)!;
        existing.CardType = Pick(existing.CardType, card.CardType, overwrite)!;
        existing.CardCost = Pick(existing.CardCost, card.CardCost, overwrite);
        existing.CardPower = Pick(existing.CardPower, card.CardPower, overwrite);
        existing.Life = Pick(existing.Life, card.Life, overwrite);
        existing.CardText = Pick(existing.CardText, card.CardText, overwrite);
        existing.SubTypes = Pick(existing.SubTypes, card.SubTypes, overwrite);
        existing.Attribute = Pick(existing.Attribute, card.Attribute, overwrite);
        if (overwrite || existing.CounterAmount is null) existing.CounterAmount = card.CounterAmount ?? existing.CounterAmount;

        if (existing.CardImageUri is not null || card.CardImageUri is null) return false;
        existing.CardImageUri = card.CardImageUri;
        return true;
    }

    private const string StandardLabel = "Standard";

    // Every card product (sealed products and DON!! cards have no "Number"), one per TCGplayer product.
    // A card's set name is its own set's group name ("OP01" → "Romance Dawn") even when the printing comes
    // from another group (a release-event or reprint group).
    internal static List<OptcgCard> MapTcgCsvCatalog(IReadOnlyList<(TcgCsvGroup Group, List<TcgCsvProduct> Products)> catalog)
    {
        var setNames = catalog
            .Where(g => !string.IsNullOrWhiteSpace(g.Group.Abbreviation))
            .GroupBy(g => g.Group.Abbreviation!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Group.Name, StringComparer.OrdinalIgnoreCase);
        var cards = new List<OptcgCard>();
        var seen = new HashSet<int>();
        foreach (var (group, products) in catalog)
            foreach (var product in products)
                if (seen.Add(product.ProductId) && MapTcgCsvProduct(product, group) is { } card)
                {
                    if (setNames.TryGetValue(card.SetId, out var setName)) card.SetName = setName;
                    cards.Add(card);
                }
        return cards;
    }

    internal static OptcgCard? MapTcgCsvProduct(TcgCsvProduct p, TcgCsvGroup group)
    {
        string? Val(string name) => TcgCsvCatalogClient.Extended(p, name);

        var number = Val("Number")?.ToUpperInvariant();
        if (number is null) return null;
        var dash = number.IndexOf('-');
        var setId = dash > 0 ? number[..dash] : group.Abbreviation?.Trim().ToUpperInvariant() ?? "";

        var rarity = Val("Rarity") ?? "";
        return new OptcgCard
        {
            TcgplayerId = p.ProductId,
            FromTcgCsv = true,
            Lang = CardLanguages.English,
            CardNumber = number,
            VariantLabel = VariantLabelOf(p.Name, group.Name),
            CardName = TcgCsvCatalogClient.CardName(p.Name),
            SetId = setId,
            SetName = group.Name,
            // TCGplayer's promo rarity is "PR"; the catalog (and the printed card) uses "P".
            Rarity = rarity.Equals("PR", StringComparison.OrdinalIgnoreCase) ? "P" : rarity,
            CardColor = TcgCsvCatalogClient.JoinList(Val("Color")) ?? "",
            CardType = Val("CardType") ?? "",
            CardCost = Val("Cost"),
            CardPower = Val("Power"),
            Life = Val("Life"),
            CardText = TcgCsvCatalogClient.StripHtml(Val("Description")),
            SubTypes = TcgCsvCatalogClient.JoinList(Val("Subtypes")),
            Attribute = TcgCsvCatalogClient.JoinList(Val("Attribute")),
            CounterAmount = TcgCsvCatalogClient.LeadingInt(Val("Counterplus")),
            CardImageUri = TcgCsvCatalogClient.ProductImage(p),
            DateScraped = DateTime.UtcNow.ToString("o"),
        };
    }

    // The variant label from TCGplayer's name qualifiers, in poneglyph's vocabulary where they differ:
    // "Shanks (020) (Alternate Art)" → "Alternate Art", "(Manga)" → "Manga Art", "(Parallel)" →
    // "Alternate Art". Number qualifiers ("(020)", "(EB04-007)") aren't variants. No qualifier → a
    // release-event group's "Release Event", else "Standard".
    internal static string VariantLabelOf(string productName, string groupName)
    {
        var qualifier = TcgCsvCatalogClient.Qualifiers(productName)
            .LastOrDefault(q => !System.Text.RegularExpressions.Regex.IsMatch(q, @"^([A-Z]+\d*-)?\d+[A-Z]?$"));
        return qualifier?.ToLowerInvariant() switch
        {
            null => groupName.Contains("Release Event", StringComparison.OrdinalIgnoreCase) ? "Release Event" : StandardLabel,
            "alternate art" or "parallel" => "Alternate Art",
            "manga" => "Manga Art",
            _ => qualifier,
        };
    }

    // productId → (market, low) from a product's price rows: its Normal row, else Foil, else the first.
    internal static Dictionary<int, (decimal? Market, decimal? Low)> MapTcgCsvPrices(Dictionary<int, List<TcgCsvPrice>> rows)
    {
        var map = new Dictionary<int, (decimal?, decimal?)>(rows.Count);
        foreach (var (productId, productRows) in rows)
        {
            var row = productRows.FirstOrDefault(r => string.Equals(r.SubTypeName, "Normal", StringComparison.OrdinalIgnoreCase))
                ?? productRows.FirstOrDefault(r => string.Equals(r.SubTypeName, "Foil", StringComparison.OrdinalIgnoreCase))
                ?? productRows[0];
            map[productId] = (row.MarketPrice, row.LowPrice);
        }
        return map;
    }

    // Fetches poneglyph's set list and, for each set, its English card variants (mapped). Only used for
    // the one-time TCGplayer link (see EnsureTcgplayerLinksAsync). Invokes onSetCompleted(done, total,
    // setCode) after each set finishes.
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
                    .SelectMany(card => card.Variants.Select(v => MapVariant(card, v, detail.Data.Name)))
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

    /// <param name="card">The card's metadata source — for a non-English variant, the English card of the
    /// same number (so names/colours stay English), falling back to the language's own card.</param>
    internal static OptcgCard MapVariant(OptcgApiCard card, OptcgApiVariant variant, string? setName = null,
        string language = CardLanguages.English)
    {
        var uid = LanguageCardSetId(
            variant.Index == 0 ? card.CardNumber : $"{card.CardNumber}_p{variant.Index}", language);

        var imageUri = variant.Images.Scan.Display
            ?? variant.Images.Scan.Full
            ?? variant.Images.Stock.Full
            ?? variant.Images.Stock.Thumb;

        return new OptcgCard
        {
            CardSetId = uid,
            Lang = language,
            CardNumber = card.CardNumber,
            VariantIndex = variant.Index,
            VariantLabel = variant.Label,
            Artist = variant.Artist,
            CardName = card.Name,
            SetId = card.Set,
            SetName = !string.IsNullOrWhiteSpace(setName) ? setName : card.SetName,
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
            TcgplayerId = TcgplayerProductId(variant.Market.TcgplayerUrl),
            MarketPrice = ParsePrice(variant.Market.MarketPrice),
            InventoryPrice = ParsePrice(variant.Market.LowPrice),
            CardImageUri = imageUri,
            DateScraped = DateTime.UtcNow.ToString("o"),
        };
    }

    // "https://www.tcgplayer.com/product/454512/one-piece-…" → 454512.
    internal static int? TcgplayerProductId(string? url)
    {
        if (url is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(url, @"/product/(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
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
        _numberIndex = null;
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

        // Phase 0: the printed collector number (most reliable for OPTCG). The OCR reads are snapped to
        // catalog numbers; the image only settles disagreeing reads and which alt-art variant this is.
        if (ocrResult is not null)
        {
            var ocrMatch = MatchCollectorReads(ocrResult, imageHash, setFilter, maxDistance);
            if (ocrMatch is not null)
            {
                LastMatchDiagnostics.DecisionPhase = "OcrCollectorNumber";
                return ocrMatch;
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

        // A same-art variant out-hashes its watermarked base image; keep the base unless clearly nearer.
        (bestPHashId, bestPHashDistance) = PreferBasePrinting(bestPHashId, bestPHashDistance, imageHash, setFilter);

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
            Language = card.Lang,
            LocalImagePath = ResolveLocalArtPath(card.LocalImagePath),
            Confidence = confidence,
            Source = card
        };
    }

    public IReadOnlyList<SetInfo> GetAvailableSets()
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards
            .AsNoTracking()
            .Where(c => c.Lang == CardLanguages.English)
            .Select(c => new { c.SetId, c.SetName })
            .Distinct()
            .AsEnumerable()
            .Select(s => new SetInfo(s.SetId, s.SetName))
            .InNaturalOrder();
    }

    public Task<List<SetCompletionSummary>> GetSetCompletionAsync(IEnumerable<CollectionCard> ownedCards, IProgress<string>? progress = null)
    {
        _logger.LogInformation("Calculating set completion for OPTCG");

        // Total cards per set (counted by distinct printed CardNumber, so alt-art
        // variant rows do not inflate totals; SetId is the set code)
        using var ctx = _dbContextFactory.CreateDbContext();
        var setTotals = ctx.Cards
            .AsNoTracking()
            .Where(c => c.Lang == CardLanguages.English)
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

        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards
            .AsNoTracking()
            .Where(c => c.SetId == setCode && c.Lang == CardLanguages.English)
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

    public List<SetCatalogCard> GetSetCards(string setCode)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking()
            .Where(c => c.SetId == setCode && c.Lang == CardLanguages.English).AsEnumerable()
            .GroupBy(c => c.CardNumber)
            .Select(g => g.OrderBy(c => c.VariantIndex).First())
            .Select(c => new SetCatalogCard
            {
                GameCardId = c.CardSetId,
                Name = c.CardName,
                CollectorNumber = c.CardNumber,
                SetCode = c.SetId,
                SetName = c.SetName,
                Rarity = c.Rarity,
                ImageUri = c.CardImageUri,
                LocalImagePath = c.LocalImagePath,
                NormalPrice = c.MarketPrice,
                FoilPrice = null,
                HasFoil = false,
            })
            .OrderBy(c => c.CollectorNumber, CollectorNumberComparer.Instance)
            .ToList();
    }

    // At most this many voted numbers are weighed against the image.
    private const int MaxOcrCandidates = 3;
    // A lower-voted number displaces the top-voted one only when the scan's pHash is at least this many
    // bits closer to it — e.g. Tesseract dropping "09" from "OP09-110" reads "OP-11088", an exact hit on
    // the wrong card OP11-088, whose art is nowhere near the scan's.
    internal const int AlternateReadMinHashAdvantage = 6;
    // An alt-art variant is preferred over the base printing of the same number only when the image is
    // clearly nearer to it. Same-art variants (Release Event / anniversary stamps, reprints) still hash
    // 2–8 bits nearer a plain scan than the base does, because the catalog's base images carry a large
    // "SAMPLE" watermark the variants lack; a genuinely different alt-art measured 14 (24 vs 10).
    internal const int VariantMinHashAdvantage = 10;
    // Confidence of a number read only approximately (no pass read it exactly).
    private const double FuzzyReadConfidence = 80;

    private sealed record NumberedRow(string CardSetId, int VariantIndex, string SetId, ulong? Hash);
    private sealed record NumberIndex(OptcgCollectorNumberResolver Resolver, Dictionary<string, List<NumberedRow>> Rows, Dictionary<string, string> NumberById);
    private NumberIndex? _numberIndex;

    // English rows grouped by printed card number (variants share it), plus the OCR resolver over them.
    // Language rows are reached afterwards via FindLanguageVariant.
    private NumberIndex GetNumberIndex()
    {
        if (_numberIndex is not null) return _numberIndex;
        var separator = LanguageSeparator.ToString();
        var rows = _readContext.Cards
            .AsNoTracking()
            .Where(c => !c.CardSetId.Contains(separator))
            .Select(c => new { c.CardSetId, c.CardNumber, c.VariantIndex, c.SetId, c.ImageHash })
            .AsEnumerable()
            .Where(c => !string.IsNullOrWhiteSpace(c.CardNumber)) // unnumbered rows aren't variants of one another
            .GroupBy(c => c.CardNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(c => new NumberedRow(c.CardSetId, c.VariantIndex, c.SetId, c.ImageHash)).OrderBy(r => r.VariantIndex).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var numberById = rows.SelectMany(kv => kv.Value.Select(r => (r.CardSetId, Number: kv.Key)))
            .ToDictionary(x => x.CardSetId, x => x.Number, StringComparer.OrdinalIgnoreCase);
        _logger.LogInformation("OPTCG card-number index built: {Count} numbers", rows.Count);
        return _numberIndex = new NumberIndex(new OptcgCollectorNumberResolver(rows.Keys), rows, numberById);
    }

    private CardMatch? MatchCollectorReads(OcrMatchResult ocr, ulong imageHash, IReadOnlySet<string>? setFilter, int maxDistance)
    {
        var reads = ocr.CollectorTexts.AsEnumerable();
        if (ocr.CollectorNumber is not null && ocr.CollectorNumberConfidence >= 0.5)
            reads = reads.Append(ocr.CollectorNumber);
        var readList = reads.ToList();
        if (readList.Count == 0) return null;

        var index = GetNumberIndex();
        var picks = new List<(OptcgNumberCandidate Candidate, NumberedRow Row, int? Distance)>();
        foreach (var candidate in index.Resolver.Resolve(readList).Take(MaxOcrCandidates))
        {
            var rows = index.Rows[candidate.CardNumber].Where(r => setFilter is null || setFilter.Contains(r.SetId)).ToList();
            if (rows.Count == 0)
            {
                _logger.LogDebug("OPTCG OCR read {Number} rejected by set filter", candidate.CardNumber);
                continue;
            }
            var (row, distance) = PickVariant(rows, imageHash, maxDistance);
            picks.Add((candidate, row, distance));
        }
        if (picks.Count == 0)
        {
            _logger.LogDebug("OPTCG OCR reads resolved to no catalog number: {Reads}", string.Join(" | ", readList));
            return null;
        }

        // An unhashed top pick can't be compared; let only a near-certain image match displace it.
        var best = picks[0];
        foreach (var alternate in picks.Skip(1))
        {
            if (alternate.Distance is int d && d <= (best.Distance ?? maxDistance) - AlternateReadMinHashAdvantage)
            {
                _logger.LogInformation(
                    "OPTCG OCR reads disagreed: preferring {Alt} (pHash {AltDist}) over top read {Top} (pHash {TopDist})",
                    alternate.Row.CardSetId, d, best.Row.CardSetId, best.Distance);
                best = alternate;
            }
        }

        var confidence = best.Candidate.ExactVotes > 0 ? 100 : FuzzyReadConfidence;
        var match = LookupOptcgCard(best.Row.CardSetId, confidence);
        if (match is not null)
        {
            if (best.Distance is int distance) LastMatchDiagnostics!.PHashDistance = distance;
            _logger.LogInformation("OPTCG OCR match: {CardName} ({CardSetId}) from {Reads}", match.Name, best.Row.CardSetId, string.Join(" | ", readList));
        }
        return match;
    }

    // The pHash path's pick, moved to its number's base printing when that is about as near (see
    // VariantMinHashAdvantage) — the same rule the OCR path applies via PickVariant.
    private (string CardSetId, int Distance) PreferBasePrinting(string cardSetId, int distance, ulong imageHash, IReadOnlySet<string>? setFilter)
    {
        var index = GetNumberIndex();
        if (!index.NumberById.TryGetValue(cardSetId, out var number)) return (cardSetId, distance);
        var baseRow = index.Rows[number][0];
        if (baseRow.CardSetId == cardSetId || baseRow.Hash is not ulong baseHash
            || (setFilter is not null && !setFilter.Contains(baseRow.SetId)))
            return (cardSetId, distance);
        var baseDistance = PerceptualHashService.HammingDistance(imageHash, baseHash);
        return distance <= baseDistance - VariantMinHashAdvantage ? (cardSetId, distance) : (baseRow.CardSetId, baseDistance);
    }

    // Variants share the printed number, so the image picks among them. The base printing wins unless a
    // variant is clearly nearer (or the base is unhashed and the variant is a confident match).
    private (NumberedRow Row, int? Distance) PickVariant(List<NumberedRow> rows, ulong imageHash, int maxDistance)
    {
        var baseRow = rows[0];
        int? baseDistance = baseRow.Hash is ulong bh ? PerceptualHashService.HammingDistance(imageHash, bh) : null;
        var nearest = rows
            .Where(r => r.Hash is not null)
            .Select(r => (Row: r, Distance: PerceptualHashService.HammingDistance(imageHash, r.Hash!.Value)))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Row.VariantIndex)
            .FirstOrDefault();
        if (nearest.Row is null || nearest.Row == baseRow) return (baseRow, baseDistance);
        _logger.LogDebug("OPTCG variant choice: base {Base} pHash {BaseDist}, nearest {Variant} pHash {VariantDist}",
            baseRow.CardSetId, baseDistance, nearest.Row.CardSetId, nearest.Distance);
        var clearlyNearer = baseDistance is int b
            ? nearest.Distance <= b - VariantMinHashAdvantage
            : nearest.Distance <= maxDistance;
        return clearlyNearer ? (nearest.Row, nearest.Distance) : (baseRow, baseDistance);
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
            Language = card.Lang,
            LocalImagePath = ResolveLocalArtPath(card.LocalImagePath),
            Confidence = confidence,
            Source = card
        };
    }

    // One Piece searchable fields. Core (name/set/cn/type/color/rarity) map to columns below;
    // cost/power/counter/life/attribute/subtype are game-specific (also resolvable for owned-card search).
    private SearchSchema? _searchSchema;
    public SearchSchema SearchSchema => _searchSchema ??= SharedSearchSchema.WithGameFields(
    [
        new SearchFieldDefinition { Canonical = "cost", Aliases = ["cost"], SourceKey = nameof(OptcgCard.CardCost),
            Description = "Play cost.", Example = "cost:4" },
        new SearchFieldDefinition { Canonical = "power", Aliases = ["power", "pow"], SourceKey = nameof(OptcgCard.CardPower),
            Description = "Power.", Example = "power:5000" },
        new SearchFieldDefinition { Canonical = "counter", Aliases = ["counter", "ctr"], SourceKey = nameof(OptcgCard.CounterAmount),
            SupportedOps = [ComparisonOp.Contains, ComparisonOp.Exact, ComparisonOp.NotEqual,
                ComparisonOp.LessThan, ComparisonOp.GreaterThan, ComparisonOp.LessOrEqual, ComparisonOp.GreaterOrEqual],
            Description = "Counter value (supports <, >, <=, >=).", Example = "counter>=1000" },
        new SearchFieldDefinition { Canonical = "life", Aliases = ["life"], SourceKey = nameof(OptcgCard.Life),
            Description = "Leader life.", Example = "life:5" },
        new SearchFieldDefinition { Canonical = "attribute", Aliases = ["attribute", "attr"], SourceKey = nameof(OptcgCard.Attribute),
            Description = "Attribute (Slash, Strike, …).", Example = "attribute:slash" },
        new SearchFieldDefinition { Canonical = "subtype", Aliases = ["subtype", "sub", "trait"], SourceKey = nameof(OptcgCard.SubTypes),
            Description = "Subtype / trait.", Example = "subtype:straw" },
    ]);

    private static readonly CatalogFieldMap<OptcgCard> FieldMap = BuildFieldMap();

    private static CatalogFieldMap<OptcgCard> BuildFieldMap()
    {
        var map = new CatalogFieldMap<OptcgCard>
        {
            NamePredicate = (p, op, v) => CatalogSearchExpressionBuilder.StringPredicate(p, nameof(OptcgCard.CardName), op, v),
        };
        map.Str("name", nameof(OptcgCard.CardName))
           .Field("set", (p, op, v) => System.Linq.Expressions.Expression.OrElse(
               CatalogSearchExpressionBuilder.StringPredicate(p, nameof(OptcgCard.SetId), op, v),
               CatalogSearchExpressionBuilder.StringPredicate(p, nameof(OptcgCard.SetName), op, v)))
           .Str("cn", nameof(OptcgCard.CardNumber))
           .Str("type", nameof(OptcgCard.CardType))
           .Str("color", nameof(OptcgCard.CardColor))
           .Str("rarity", nameof(OptcgCard.Rarity))
           .Str("cost", nameof(OptcgCard.CardCost))
           .Str("power", nameof(OptcgCard.CardPower))
           .Num("counter", nameof(OptcgCard.CounterAmount))
           .Str("life", nameof(OptcgCard.Life))
           .Str("attribute", nameof(OptcgCard.Attribute))
           .Str("subtype", nameof(OptcgCard.SubTypes))
           .Field("lang", (p, op, v) => CatalogSearchExpressionBuilder.StringPredicate(p, nameof(OptcgCard.Lang), op,
               CardLanguages.Normalize(v) ?? v));
        return map;
    }

    public List<CardMatch> SearchCards(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        _logger.LogDebug("Searching OPTCG cards with query: {Query} (max: {MaxResults})", query, maxResults);
        var node = ScryfallQueryParser.ParseFilter(query, SearchSchema);
        // Per-call context: DbContext is not thread-safe; the shared _readContext throws under
        // concurrent searches (fast typing fans out overlapping /api/scan/search requests).
        using var ctx = _dbContextFactory.CreateDbContext();
        IQueryable<OptcgCard> cards = ctx.Cards.AsNoTracking();
        var lambda = CatalogSearchExpressionBuilder.Build(node, SearchSchema, FieldMap);
        if (lambda is not null) cards = cards.Where(lambda);

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
            Language = c.Lang,
            LocalImagePath = ResolveLocalArtPath(c.LocalImagePath),
            Source = c
        }).ToList();
    }

    public IReadOnlySet<string>? ResolveFieldCardIds(string field, ComparisonOp op, string value)
    {
        if (!SearchSchema.IsGameSpecific(field)) return null;
        var resolved = SearchSchema.ResolveValue(field, value);
        var lambda = CatalogSearchExpressionBuilder.SingleField(field, op, resolved, FieldMap);
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Cards.AsNoTracking().Where(lambda).Select(c => c.CardSetId).ToHashSet();
    }

    public List<CardMatch> GetPrintings(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName))
            return [];

        using var ctx = _dbContextFactory.CreateDbContext();
        var results = ctx.Cards
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
            Language = c.Lang,
            LocalImagePath = ResolveLocalArtPath(c.LocalImagePath),
            Source = c
        }).ToList();
    }

    public decimal? GetCurrentPrice(string gameCardId, bool isFoil)
        => GetCurrentPrices([gameCardId], isFoil).TryGetValue(gameCardId, out var price) ? price : null;

    public Dictionary<string, decimal> GetCurrentPrices(IEnumerable<string> gameCardIds, bool isFoil)
    {
        var requested = gameCardIds.Distinct().ToList();
        if (requested.Count == 0)
            return [];

        // Non-English rows carry no market price (poneglyph prices only the English listing), so they
        // are valued at their English variant's price — look both up in one pass.
        var ids = requested.Concat(requested.Select(EnglishCardSetId)).Distinct().ToList();
        var result = new Dictionary<string, decimal>(ids.Count);

        using var ctx = _dbContextFactory.CreateDbContext();
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = ctx.Cards.AsNoTracking()
                .Where(c => chunk.Contains(c.CardSetId))
                .Select(c => new { c.CardSetId, c.MarketPrice })
                .ToList();

            foreach (var row in rows)
            {
                if (row.MarketPrice.HasValue)
                    result[row.CardSetId] = row.MarketPrice.Value;
            }
        }

        var priced = new Dictionary<string, decimal>(requested.Count);
        foreach (var id in requested)
        {
            if (result.TryGetValue(id, out var own) || result.TryGetValue(EnglishCardSetId(id), out own))
                priced[id] = own;
        }
        return priced;
    }

    public void RecordCorrection(ulong scanHash, string correctCardId, ulong? artScanHash = null)
    {
        _logger.LogInformation("Recording OPTCG correction: hash {Hash:X16} → card {CardId}", scanHash, correctCardId);
        using var ctx = _dbContextFactory.CreateDbContext();
        // Provider-agnostic upsert (was SQLite-only INSERT OR REPLACE; failed on SQL Server catalogs).
        OmniCard.Data.Catalogs.HashCorrectionUpsert.Upsert(ctx, scanHash, correctCardId, artScanHash);
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
