using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public interface ICardService
{
    ObservableCollection<ScannedCard> ScannedCards { get; }
    CardGame SelectedGame { get; set; }
    HashSet<string>? SelectedSetFilter { get; set; }
    bool DefaultIsFoil { get; set; }
    decimal? DefaultPurchasePrice { get; set; }
    IReadOnlyList<CardGame> AvailableGames { get; }
    ICardGameService ActiveGameService { get; }
    Action<HashStageResult>? OnHashStage { get; set; }
    ulong LastComputedHash { get; }
    ICardGameService GetGameService(CardGame game);
    void AddFromStream(Stream stream);
    void ReprocessScans();
    void CommitScans(IEnumerable<ScannedCard> scannedCards);
    void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null);
    void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results);
    void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results);
    void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results);
    void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null);
    void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update);
    List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds);
    void UpdateCollectionCard(CollectionCard card);
    void DeleteCollectionCard(int id);
    Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null);
    List<string> GetDistinctFieldValues(string field, CardGame game);
    List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode);
    void RemoveTempFile(ScannedCard card);
    void ClearTempFiles();
    (int FlagResolutions, int MismatchLogs) ClearDiagnosticLogs();
}

public sealed class CardSevice : ICardService
{
    private readonly IPerceptualHashService _hashService;
    private readonly Dictionary<CardGame, ICardGameService> _gameServices;
    private readonly IDbContextFactory<CollectionDbContext> _collectionDbContextFactory;
    private readonly IOcrMatchingService _ocrService;
    private readonly ScanImageCache _imageCache;
    private readonly ILogger<CardSevice> _logger;
    private readonly string _tempScansDir;
    private readonly IDataPathService _dataPathService;

    public CardSevice(
        IPerceptualHashService hashService,
        IEnumerable<ICardGameService> gameServices,
        IDbContextFactory<CollectionDbContext> collectionDbContextFactory,
        IOcrMatchingService ocrService,
        ScanImageCache imageCache,
        ILogger<CardSevice> logger,
        IDataPathService dataPathService)
    {
        _hashService = hashService;
        _gameServices = gameServices.ToDictionary(s => s.Game);
        _collectionDbContextFactory = collectionDbContextFactory;
        _ocrService = ocrService;
        _imageCache = imageCache;
        _tempScansDir = imageCache.TempScansDirectory;
        _logger = logger;
        _dataPathService = dataPathService;

        // Ensure collection DB exists
        using var ctx = _collectionDbContextFactory.CreateDbContext();
        var dbPath = ctx.Database.GetConnectionString();
        if (dbPath is not null)
        {
            var dataSource = dbPath.Replace("Data Source=", "");
            var dir = Path.GetDirectoryName(dataSource);
            if (dir is not null && dir.Length > 0)
                Directory.CreateDirectory(dir);
        }
        ctx.Database.EnsureCreated();

        // Manual migration: create MismatchLogs table on existing databases
        ctx.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "MismatchLogs" (
                "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "ScanHash" INTEGER NOT NULL,
                "ScanImagePath" TEXT,
                "OriginalCardId" TEXT NOT NULL DEFAULT '',
                "OriginalName" TEXT NOT NULL DEFAULT '',
                "OriginalSetCode" TEXT NOT NULL DEFAULT '',
                "OriginalNumber" TEXT NOT NULL DEFAULT '',
                "OriginalConfidence" REAL NOT NULL DEFAULT 0,
                "CorrectedCardId" TEXT NOT NULL DEFAULT '',
                "CorrectedName" TEXT NOT NULL DEFAULT '',
                "CorrectedSetCode" TEXT NOT NULL DEFAULT '',
                "CorrectedNumber" TEXT NOT NULL DEFAULT '',
                "CreatedAt" TEXT NOT NULL DEFAULT ''
            );
            """);

        ctx.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FlagResolutions" (
                "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "CollectionCardId" INTEGER NOT NULL,
                "FlagReason" TEXT NOT NULL DEFAULT '',
                "FixType" TEXT NOT NULL DEFAULT '',
                "OriginalData" TEXT NOT NULL DEFAULT '',
                "ResolvedData" TEXT NOT NULL DEFAULT '',
                "ScanHash" INTEGER NOT NULL DEFAULT 0,
                "Confidence" REAL,
                "FixedAt" TEXT NOT NULL DEFAULT '',
                "CreatedAt" TEXT NOT NULL DEFAULT '',
                FOREIGN KEY ("CollectionCardId") REFERENCES "Cards"("Id") ON DELETE CASCADE
            );
            """);

        _logger.LogInformation("Collection database ready at {DbPath}", dbPath);

        AvailableGames = _gameServices.Keys.OrderBy(g => g).ToList();
        SelectedGame = AvailableGames.FirstOrDefault();
    }

    public ObservableCollection<ScannedCard> ScannedCards { get; } = [];
    public CardGame SelectedGame { get; set; }
    public HashSet<string>? SelectedSetFilter { get; set; }
    public bool DefaultIsFoil { get; set; }
    public decimal? DefaultPurchasePrice { get; set; }
    public IReadOnlyList<CardGame> AvailableGames { get; }
    public ICardGameService ActiveGameService => _gameServices[SelectedGame];
    public Action<HashStageResult>? OnHashStage { get; set; }
    public ulong LastComputedHash { get; private set; }

    public ICardGameService GetGameService(CardGame game) => _gameServices[game];

    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null)
    {
        // Normalize empty set filter to null
        if (setFilter is { Count: 0 })
            setFilter = null;

        // Try selected game first
        if (_gameServices.TryGetValue(SelectedGame, out var primaryService))
        {
            var primaryMatch = primaryService.FindClosestMatch(hash, artHashes, ocrResult, setFilter, preferredSets);
            if (primaryMatch is not null)
                return (primaryMatch, SelectedGame);
        }

        // When a set filter is active, do not fall back to other games
        if (setFilter is not null)
            return (null, SelectedGame);

        // Fallback: try all other games (only accept high-confidence matches)
        foreach (var (game, service) in _gameServices)
        {
            if (game == SelectedGame)
                continue;

            var match = service.FindClosestMatch(hash, artHashes, ocrResult);
            if (match is not null && match.Confidence is null or >= 50)
            {
                _logger.LogInformation("Fallback match found in {Game} for hash {Hash:X16} (confidence {Confidence:F0}%)", game, hash, match.Confidence);
                return (match, game);
            }
        }

        return (null, SelectedGame);
    }

    public void ReprocessScans()
    {
        _logger.LogInformation("Reprocessing {Count} unmatched scanned cards", ScannedCards.Count(s => s.Match is null));

        foreach (var scan in ScannedCards)
        {
            if (scan.Match is not null)
                continue;

            var (match, game) = FindBestMatch(scan.Hash, scan.ArtHashes, null, SelectedSetFilter);
            if (match is not null)
            {
                scan.Match = match;
                scan.Game = game;
                _logger.LogInformation("Reprocess matched \"{CardName}\" in {Game}", match.Name, game);
            }
        }
    }

    private DateTime _lastTransferTime;
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    public void AddFromStream(Stream stream)
    {
        var now = DateTime.UtcNow;
        if (now - _lastTransferTime < DebounceWindow)
        {
            _logger.LogDebug("Duplicate transfer debounced ({Elapsed}ms since last)", (now - _lastTransferTime).TotalMilliseconds);
            return;
        }
        _lastTransferTime = now;

        var game = SelectedGame;
        _logger.LogInformation("Processing scanned card image for {Game}", game);
        var sw = Stopwatch.StartNew();

        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        _logger.LogDebug("Buffered {Bytes} bytes from scanner stream", buffer.Length);

        buffer.Position = 0;
        var hash = _hashService.ComputeHash(buffer, OnHashStage);
        LastComputedHash = hash;
        _logger.LogInformation("Computed pHash {Hash:X16} for scanned image", hash);

        // Compute art-region hashes for the selected game
        buffer.Position = 0;
        ulong[]? artHashes = null;
        if (SelectedGame == CardGame.Mtg)
        {
            artHashes = _hashService.ComputeArtHash(buffer, ScryfallService.ArtCropRegions);
        }

        // Capture raw bytes for temp file and OCR
        buffer.Position = 0;
        var rawBytes = buffer.ToArray();
        buffer.Dispose();

        // Write to temp file
        Directory.CreateDirectory(_tempScansDir);
        var tempPath = Path.Combine(_tempScansDir, $"{Guid.NewGuid()}.png");
        try
        {
            File.WriteAllBytes(tempPath, rawBytes);
            _logger.LogDebug("Wrote temp scan image to {Path}", tempPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write temp scan image to {Path}", tempPath);
        }

        // Ensure OCR service has current symbol hashes
        if (_ocrService.SymbolHashes.Count == 0 && _gameServices.TryGetValue(CardGame.Mtg, out var mtgService) && mtgService is ScryfallService scryfall)
        {
            try { _ocrService.SymbolHashes = scryfall.GetSymbolHashes(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load symbol hashes into OCR service"); }
        }

        // Detect set symbol synchronously — use as a soft preference to break ties among reprints
        HashSet<string>? detectedSets = null;
        if (SelectedGame == CardGame.Mtg)
        {
            var (symbolSets, symbolConf) = _ocrService.DetectSetSymbol(rawBytes);
            if (symbolConf >= 0.5 && symbolSets.Count > 0)
            {
                detectedSets = new HashSet<string>(symbolSets, StringComparer.OrdinalIgnoreCase);
                _logger.LogInformation("Symbol detection: {Sets} (confidence {Conf:F2})", string.Join(",", detectedSets), symbolConf);
            }
        }

        // pHash match with user set filter (hard) and detected sets (soft preference)
        var (match, matchedGame) = FindBestMatch(hash, artHashes, null, SelectedSetFilter, detectedSets);
        game = matchedGame;
        if (match is not null)
            _logger.LogInformation("Matched scanned card to \"{CardName}\" ({SetCode} #{Number}) in {Game}", match.Name, match.SetCode, match.CollectorNumber, game);
        else
            _logger.LogWarning("No matching card found for pHash {Hash:X16} in any game", hash);

        var flagReason = match is null
            ? FlagReason.NoMatch
            : match.Confidence is not null and < 20
                ? FlagReason.VeryLowConfidence
                : FlagReason.None;

        var scannedCard = new ScannedCard
        {
            TempImagePath = tempPath,
            Hash = hash,
            ArtHashes = artHashes,
            Game = game,
            Match = match,
            IsFoil = DefaultIsFoil,
            PurchasePrice = DefaultPurchasePrice,
            FlagReason = flagReason,
        };

        // Use BeginInvoke (non-blocking) for ALL UI thread work.
        // Dispatcher.Invoke deadlocks because TWAIN's message pump runs on the UI thread.
        var capturedHash = hash;
        var capturedSetFilter = SelectedSetFilter;
        Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            ScannedCards.Add(scannedCard);

            // Run OCR after card is in the queue
            try
            {
                var ocrResult = await _ocrService.AnalyzeCardAsync(rawBytes);
                if (ocrResult?.RecognizedName is not null)
                {
                    _logger.LogInformation("OCR recognized: \"{Name}\" (confidence: {Conf:F2})", ocrResult.RecognizedName, ocrResult.NameConfidence);

                    // Merge set preferences from initial symbol detection and async OCR
                    var mergedPreferredSets = detectedSets;
                    if (ocrResult.SymbolConfidence >= 0.5 && ocrResult.CandidateSetCodes is { Count: > 0 })
                    {
                        mergedPreferredSets ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var code in ocrResult.CandidateSetCodes)
                            mergedPreferredSets.Add(code);
                    }

                    // Re-match with combined scoring and set preferences
                    var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, mergedPreferredSets);
                    if (ocrMatch is not null && (scannedCard.Match is null || ocrMatch.GameSpecificId != scannedCard.Match?.GameSpecificId))
                    {
                        scannedCard.Match = ocrMatch;
                        scannedCard.Game = ocrGame;
                        _logger.LogInformation("OCR improved match to \"{CardName}\" ({SetCode} #{Number})", ocrMatch.Name, ocrMatch.SetCode, ocrMatch.CollectorNumber);

                        // Clear auto-flag if OCR improved the match above the threshold
                        if (scannedCard.FlagReason is FlagReason.NoMatch or FlagReason.VeryLowConfidence)
                        {
                            if (ocrMatch.Confidence is null or >= 20)
                            {
                                scannedCard.FlagReason = FlagReason.None;
                                _logger.LogInformation("Auto-flag cleared after OCR improvement");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR analysis failed");
            }
        });

        sw.Stop();
        _logger.LogInformation("Card scan processed in {ElapsedMs}ms (total scanned: {Count})", sw.ElapsedMilliseconds, ScannedCards.Count);
    }

    public void CommitScans(IEnumerable<ScannedCard> scannedCards)
        => CommitScans(scannedCards, null, null, null, null);

    public void CommitScans(IEnumerable<ScannedCard> scannedCards, StorageContainer? activeContainer, int? page, int? slot, string? section, IProgress<string>? progress = null)
    {
        _logger.LogInformation("Committing scanned cards to collection");
        using var context = _collectionDbContextFactory.CreateDbContext();

        var committed = new List<(CollectionCard Card, ScannedCard Scan)>();
        var skipped = 0;
        progress?.Report("Preparing cards for collection...");
        foreach (var scan in scannedCards)
        {
            if (scan.Match is null)
            {
                skipped++;
                continue;
            }

            // Use per-card override if set, otherwise use toolbar defaults
            var container = scan.OverrideContainer ?? activeContainer;

            var card = new CollectionCard
            {
                Game = scan.Game,
                Name = scan.Match.Name,
                SetCode = scan.Match.SetCode,
                SetName = scan.Match.SetName,
                Number = scan.Match.CollectorNumber,
                Rarity = scan.Match.Rarity,
                ImageUri = scan.Match.ImageUri,
                GameCardId = scan.Match.GameSpecificId,
                Condition = scan.Condition,
                IsFoil = scan.IsFoil,
                PurchasePrice = scan.PurchasePrice,
                ContainerId = container?.Id,
            };

            card.Color = CardAttributeExtractor.ExtractColor(scan.Match, scan.Game);
            card.CardType = CardAttributeExtractor.ExtractCardType(scan.Match, scan.Game);

            if (container?.ContainerType == ContainerType.Binder)
            {
                card.Page = scan.OverridePage ?? page;
                card.Slot = scan.OverrideSlot ?? slot;
            }
            else if (container?.ContainerType == ContainerType.Box)
            {
                card.Section = scan.OverrideSection ?? section;
            }

            context.Cards.Add(card);
            committed.Add((card, scan));
        }

        // First save to get auto-generated IDs
        progress?.Report($"Saving {committed.Count} cards to database...");
        context.SaveChanges();

        // Persist flag resolution records for flagged cards that were fixed
        foreach (var (card, scan) in committed)
        {
            if (scan.FlagFix is not null)
            {
                context.FlagResolutions.Add(new FlagResolution
                {
                    CollectionCardId = card.Id,
                    FlagReason = scan.FlagFix.OriginalFlagReason.ToString(),
                    FixType = scan.FlagFix.FixType,
                    OriginalData = scan.FlagFix.OriginalData,
                    ResolvedData = scan.FlagFix.ResolvedData,
                    ScanHash = scan.Hash,
                    Confidence = scan.Match?.Confidence,
                    FixedAt = scan.FlagFix.FixedAt,
                });
            }
        }

        // Save scan images to disk
        var scansDir = _dataPathService.ScansDirectory;
        Directory.CreateDirectory(scansDir);

        var saved = 0;
        foreach (var (card, scan) in committed)
        {
            var fileName = $"{card.Id}.png";
            var filePath = Path.Combine(scansDir, fileName);

            try
            {
                File.Copy(scan.TempImagePath, filePath, overwrite: true);
                card.ScanImagePath = $"scans/{fileName}";

                // Delete temp file after successful copy
                try
                {
                    File.Delete(scan.TempImagePath);
                    _imageCache.Evict(scan.TempImagePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file after commit: {Path}", scan.TempImagePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy scan image for card {Id}", card.Id);
            }

            saved++;
            if (saved % 5 == 0 || saved == committed.Count)
                progress?.Report($"Saving scan images... {saved}/{committed.Count}");
        }

        // Second save to persist ScanImagePath values
        progress?.Report("Finalizing...");
        context.SaveChanges();
        _logger.LogInformation("Committed {Committed} cards to collection ({Skipped} skipped)", committed.Count, skipped);
    }

    public void RemoveTempFile(ScannedCard card)
    {
        try
        {
            if (File.Exists(card.TempImagePath))
            {
                File.Delete(card.TempImagePath);
                _logger.LogDebug("Deleted temp scan image: {Path}", card.TempImagePath);
            }
            _imageCache.Evict(card.TempImagePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp scan image: {Path}", card.TempImagePath);
        }
    }

    public void ClearTempFiles()
    {
        foreach (var card in ScannedCards)
        {
            try
            {
                if (File.Exists(card.TempImagePath))
                    File.Delete(card.TempImagePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp scan image: {Path}", card.TempImagePath);
            }
        }
        _imageCache.Clear();
        _logger.LogInformation("Cleared temp scan files and image cache");
    }

    public (int FlagResolutions, int MismatchLogs) ClearDiagnosticLogs()
    {
        using var context = _collectionDbContextFactory.CreateDbContext();
        var flagCount = context.FlagResolutions.Count();
        var mismatchCount = context.MismatchLogs.Count();

        context.FlagResolutions.RemoveRange(context.FlagResolutions);
        context.MismatchLogs.RemoveRange(context.MismatchLogs);
        context.SaveChanges();

        _logger.LogInformation("Cleared diagnostic logs: {FlagResolutions} flag resolutions, {MismatchLogs} mismatch logs", flagCount, mismatchCount);
        return (flagCount, mismatchCount);
    }

    public void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results)
        => SearchCollection(query, gameFilter, null, null, null, results);

    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results)
        => SearchCollection(query, gameFilter, containerFilter, null, null, results);

    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results)
    {
        _logger.LogDebug("Searching collection: query={Query}, game={Game}, container={Container}", query ?? "(all)", gameFilter?.ToString() ?? "all", containerFilter?.ToString() ?? "all");
        results.Clear();

        using var context = _collectionDbContextFactory.CreateDbContext();
        IQueryable<CollectionCard> cards = context.Cards.AsNoTracking().Include(c => c.Container);

        if (gameFilter.HasValue)
            cards = cards.Where(c => c.Game == gameFilter.Value);

        if (containerFilter.HasValue)
            cards = cards.Where(c => c.ContainerId == containerFilter.Value);

        // Apply Scryfall syntax from the search box
        if (!string.IsNullOrWhiteSpace(query))
            cards = ApplyScryfallFilter(cards, query);

        // Apply Scryfall syntax from the filter preset
        if (filterPreset is not null && !string.IsNullOrWhiteSpace(filterPreset.Query))
            cards = ApplyScryfallFilter(cards, filterPreset.Query);

        // Apply sort preset (or default to Name)
        var sorted = ApplySortPreset(cards, sortPreset);

        foreach (var card in sorted)
            results.Add(card);

        _logger.LogDebug("Collection search returned {Count} results", results.Count);
    }

    public List<string> GetDistinctFieldValues(string field, CardGame game)
    {
        using var context = _collectionDbContextFactory.CreateDbContext();
        var cards = context.Cards.AsNoTracking().Where(c => c.Game == game);

        IQueryable<string> values = field switch
        {
            "Name" => cards.Select(c => c.Name),
            "Color" => cards.Where(c => c.Color != null).Select(c => c.Color!),
            "CardType" => cards.Where(c => c.CardType != null).Select(c => c.CardType!),
            "SetName" => cards.Select(c => c.SetName),
            "Rarity" => cards.Select(c => c.Rarity),
            "Condition" => cards.Select(c => c.Condition),
            "IsFoil" => cards.Select(c => c.IsFoil ? "True" : "False"),
            _ => Enumerable.Empty<string>().AsQueryable()
        };

        return values.Distinct().OrderBy(v => v).ToList();
    }

    private static IQueryable<CollectionCard> ApplyScryfallFilter(IQueryable<CollectionCard> cards, string query)
    {
        var filters = ScryfallQueryParser.Parse(query);
        foreach (var (field, value) in filters)
        {
            var v = value;
            cards = field switch
            {
                "name" => cards.Where(c => EF.Functions.Like(c.Name, $"%{v}%")),
                "set" => cards.Where(c => EF.Functions.Like(c.SetCode, $"%{v}%")
                                       || EF.Functions.Like(c.SetName, $"%{v}%")),
                "cn" => cards.Where(c => c.Number == v),
                "type" => cards.Where(c => c.CardType != null && EF.Functions.Like(c.CardType, $"%{v}%")),
                "rarity" => cards.Where(c => EF.Functions.Like(c.Rarity, v)),
                "color" => cards.Where(c => c.Color != null && c.Color == ScryfallQueryParser.ExpandColor(v)),
                "foil" => cards.Where(c => c.IsFoil == (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v == "1")),
                "condition" or "cond" => cards.Where(c => EF.Functions.Like(c.Condition, $"%{v}%")),
                "location" or "loc" => cards.Where(c => c.Container != null && EF.Functions.Like(c.Container.Name, $"%{v}%")),
                _ => cards.Where(c => EF.Functions.Like(c.Name, $"%{v}%")),
            };
        }
        return cards;
    }

    private static IEnumerable<CollectionCard> ApplySortPreset(IQueryable<CollectionCard> cards, SortPreset? preset)
    {
        if (preset is null || preset.SortLevels.Count == 0)
            return cards.OrderBy(c => c.Name);

        // Custom orders require in-memory sorting since SQLite can't do CASE WHEN with parameter lists
        var list = cards.ToList();

        IOrderedEnumerable<CollectionCard>? ordered = null;
        foreach (var level in preset.SortLevels)
        {
            Func<CollectionCard, object> keySelector = level.CustomOrder is not null
                ? c => GetCustomOrderIndex(GetFieldValue(c, level.Field), level.CustomOrder)
                : c => GetFieldValue(c, level.Field);

            if (ordered is null)
            {
                ordered = level.Direction == SortDirection.Ascending
                    ? list.OrderBy(keySelector)
                    : list.OrderByDescending(keySelector);
            }
            else
            {
                ordered = level.Direction == SortDirection.Ascending
                    ? ordered.ThenBy(keySelector)
                    : ordered.ThenByDescending(keySelector);
            }
        }

        return ordered ?? list.OrderBy(c => c.Name);
    }

    private static string GetFieldValue(CollectionCard card, string field)
    {
        return field switch
        {
            "Name" => card.Name,
            "Color" => card.Color ?? "",
            "CardType" => card.CardType ?? "",
            "SetName" => card.SetName,
            "SetCode" => card.SetCode,
            "Rarity" => card.Rarity,
            "Condition" => card.Condition,
            "IsFoil" => card.IsFoil.ToString(),
            "PurchasePrice" => card.PurchasePrice?.ToString("0000000000.00") ?? "",
            "DateAdded" => card.DateAdded.ToString("o"),
            "Number" => card.Number,
            _ => ""
        };
    }

    private static int GetCustomOrderIndex(string value, List<string> customOrder)
    {
        var index = customOrder.IndexOf(value);
        if (index >= 0)
            return index;

        // Multi-color handling: if value has 2+ chars and "Multi" is in the list, use Multi's index
        if (value.Length >= 2 && customOrder.IndexOf("Multi") is var multiIndex and >= 0)
            return multiIndex;

        // Unmatched values sort to the end
        return customOrder.Count;
    }

    public void MoveCardsToContainer(IEnumerable<int> cardIds, int containerId, string? section = null)
    {
        using var context = _collectionDbContextFactory.CreateDbContext();
        var ids = cardIds.ToList();
        var cards = context.Cards.Where(c => ids.Contains(c.Id)).ToList();
        foreach (var card in cards)
        {
            card.ContainerId = containerId;
            card.Page = null;
            card.Slot = null;
            card.Section = section;
        }
        context.SaveChanges();
    }

    public void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update)
    {
        using var context = _collectionDbContextFactory.CreateDbContext();
        var ids = cardIds.ToList();
        var cards = context.Cards.Where(c => ids.Contains(c.Id)).ToList();
        foreach (var card in cards)
            update(card);
        context.SaveChanges();
    }

    public List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds)
    {
        using var context = _collectionDbContextFactory.CreateDbContext();
        var ids = cardIds.ToList();
        return context.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToList();
    }

    public void UpdateCollectionCard(CollectionCard card)
    {
        _logger.LogInformation("Updating collection card {Id}: {Name}", card.Id, card.Name);
        using var context = _collectionDbContextFactory.CreateDbContext();
        context.Cards.Update(card);
        context.SaveChanges();
    }

    public void DeleteCollectionCard(int id)
    {
        _logger.LogInformation("Deleting collection card {Id}", id);
        using var context = _collectionDbContextFactory.CreateDbContext();
        var card = context.Cards.Find(id);
        if (card is null)
            return;

        context.Cards.Remove(card);
        context.SaveChanges();

        // Delete scan image if it exists
        if (card.ScanImagePath is not null)
        {
            var fullPath = Path.Combine(_dataPathService.DataDirectory, card.ScanImagePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogDebug("Deleted scan image {Path}", fullPath);
            }
        }
    }

    public async Task<List<SetCompletionSummary>> CalculateSetCompletionAsync(CardGame game, IProgress<string>? progress = null)
    {
        _logger.LogInformation("Calculating set completion for {Game}", game);

        using var context = _collectionDbContextFactory.CreateDbContext();
        var ownedCards = context.Cards
            .AsNoTracking()
            .Where(c => c.Game == game)
            .ToList();

        var service = _gameServices[game];
        return await service.GetSetCompletionAsync(ownedCards, progress);
    }

    public List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode)
    {
        _logger.LogDebug("Getting missing cards for {Game} set {SetCode}", game, setCode);

        using var context = _collectionDbContextFactory.CreateDbContext();
        var ownedNumbers = context.Cards
            .AsNoTracking()
            .Where(c => c.Game == game && c.SetCode == setCode)
            .Select(c => c.Number)
            .Distinct()
            .ToList();

        return _gameServices[game].GetMissingCards(setCode, ownedNumbers);
    }
}
