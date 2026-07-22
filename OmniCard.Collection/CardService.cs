using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using LinqExpression = System.Linq.Expressions.Expression;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public sealed class CardService : ICardService
{
    private readonly IPerceptualHashService _hashService;
    private readonly Dictionary<CardGame, ICardGameService> _gameServices;
    private readonly IDbContextFactory<OmniCardDbContext> _omniDbContextFactory;
    private readonly IOcrMatchingService _ocrService;
    private readonly ScanImageCache _imageCache;
    private readonly ILogger<CardService> _logger;
    private readonly string _tempScansDir;
    private readonly IDataPathService _dataPathService;
    private readonly IScanDiagnosticService _diagnosticService;
    private readonly IAuditService _auditService;
    private string _currentSessionId = Guid.NewGuid().ToString();

    public CardService(
        IPerceptualHashService hashService,
        IEnumerable<ICardGameService> gameServices,
        IDbContextFactory<OmniCardDbContext> omniDbContextFactory,
        IOcrMatchingService ocrService,
        ScanImageCache imageCache,
        ILogger<CardService> logger,
        IDataPathService dataPathService,
        IScanDiagnosticService diagnosticService,
        IAuditService auditService)
    {
        _hashService = hashService;
        _gameServices = gameServices.ToDictionary(s => s.Game);
        _omniDbContextFactory = omniDbContextFactory;
        _ocrService = ocrService;
        _imageCache = imageCache;
        _tempScansDir = imageCache.TempScansDirectory;
        _logger = logger;
        _dataPathService = dataPathService;
        _diagnosticService = diagnosticService;
        _auditService = auditService;

        // Ensure the unified store exists. Schema completeness beyond a brand-new file
        // (missing tables/columns on a pre-existing inventory.db) is handled at startup by
        // UnifiedMigrationService.EnsureUnifiedSchema, which runs before any service is constructed.
        using var ctx = _omniDbContextFactory.CreateDbContext();
        var dbPath = ctx.Database.GetConnectionString();
        if (dbPath is not null)
        {
            var dataSource = dbPath.Replace("Data Source=", "");
            var dir = Path.GetDirectoryName(dataSource);
            if (dir is not null && dir.Length > 0)
                Directory.CreateDirectory(dir);
        }
        ctx.Database.EnsureCreated();

        _logger.LogInformation("Unified database ready at {DbPath}", dbPath);

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

    public void StartNewDiagnosticSession() => _currentSessionId = Guid.NewGuid().ToString();

    public (CardMatch? Match, CardGame Game) FindBestMatch(ulong hash, ulong[]? artHashes = null, OcrMatchResult? ocrResult = null, IReadOnlySet<string>? setFilter = null, IReadOnlySet<string>? preferredSets = null, ulong? scanEdgeHash = null)
    {
        // Normalize empty set filter to null
        if (setFilter is { Count: 0 })
            setFilter = null;

        // Match only within the selected game — never fall back to other games
        if (_gameServices.TryGetValue(SelectedGame, out var primaryService))
        {
            var primaryMatch = primaryService.FindClosestMatch(hash, artHashes, ocrResult, setFilter, preferredSets, scanEdgeHash: scanEdgeHash);
            if (primaryMatch is not null)
                return (primaryMatch, SelectedGame);
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

            if (_auditService.IsAuditActive)
            {
                var scopedMatch = _auditService.FindScopedMatch(scan.Hash, scan.ArtHashes);
                if (scopedMatch is not null)
                {
                    scan.Match = scopedMatch;
                    _logger.LogInformation("Reprocess (audit) matched \"{CardName}\"", scopedMatch.Name);
                }
            }
            else
            {
                var (match, game) = FindBestMatch(scan.Hash, scan.ArtHashes, null, SelectedSetFilter);
                if (match is not null)
                {
                    scan.Match = match;
                    scan.Game = game;
                    _logger.LogInformation("Reprocess matched \"{CardName}\" in {Game}", match.Name, game);
                }
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

        // Auto-crop oversized scans. Foil cards confuse the RS40's internal
        // edge detection, producing images much larger than the card itself.
        buffer = AutoCropScan(buffer);

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

        // Foil cards scan with a holographic color shift that corrupts the luminance pHash;
        // compute a color-robust edge hash so matching can use it (One Piece, Riftbound).
        ulong? scanEdgeHash = null;
        if (DefaultIsFoil && (SelectedGame == CardGame.OnePiece || SelectedGame == CardGame.Riftbound))
        {
            scanEdgeHash = _hashService.ComputeEdgeHash(new MemoryStream(rawBytes));
        }

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
            try
            {
                _ocrService.SymbolHashes = scryfall.GetSymbolHashes();
                _logger.LogInformation("Loaded {Count} symbol hashes into OCR service", _ocrService.SymbolHashes.Count);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load symbol hashes into OCR service"); }
        }

        // Game-specific detection (synchronous portion only — async OCR runs after card is queued)
        HashSet<string>? detectedSets = null;

        if (SelectedGame == CardGame.Mtg)
        {
            // Detect set symbol synchronously — use as a soft preference to break ties among reprints
            var (symbolSets, symbolConf) = _ocrService.DetectSetSymbol(rawBytes);
            if (symbolConf >= 0.5 && symbolSets.Count > 0)
            {
                detectedSets = new HashSet<string>(symbolSets, StringComparer.OrdinalIgnoreCase);
                _logger.LogInformation("Symbol detection: {Sets} (confidence {Conf:F2})", string.Join(",", detectedSets), symbolConf);
            }
        }

        // pHash match — branch on audit mode
        CardMatch? match;
        if (_auditService.IsAuditActive)
        {
            match = _auditService.FindScopedMatch(hash, artHashes);
            // Skip set symbol detection and OCR re-matching in audit mode
        }
        else
        {
            var (bestMatch, matchedGame) = FindBestMatch(hash, artHashes, null, SelectedSetFilter, detectedSets, scanEdgeHash);
            match = bestMatch;
            game = matchedGame;
        }
        if (match is not null)
            _logger.LogInformation("Matched scanned card to \"{CardName}\" ({SetCode} #{Number}) in {Game}", match.Name, match.SetCode, match.CollectorNumber, game);
        else
            _logger.LogWarning("No matching card found for pHash {Hash:X16} in any game", hash);

        var flagReason = match is null
            ? FlagReason.NoMatch
            : match.Confidence is not null and < 15
                ? FlagReason.VeryLowConfidence
                : FlagReason.None;

        var scannedCard = new ScannedCard
        {
            TempImagePath = tempPath,
            Hash = hash,
            ArtHashes = artHashes,
            ScanEdgeHash = scanEdgeHash,
            Game = game,
            Match = match,
            IsFoil = DefaultIsFoil,
            PurchasePrice = DefaultPurchasePrice,
            FlagReason = flagReason,
        };

        // Log diagnostic event
        try
        {
            var lastDiag = _gameServices.TryGetValue(game, out var gs) ? gs.LastMatchDiagnostics : null;
            _diagnosticService.LogScanCompleted(_currentSessionId, hash, match, lastDiag, artHashes, null, flagReason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log scan diagnostic event");
        }

        // Use BeginInvoke (non-blocking) for ALL UI thread work.
        // Dispatcher.Invoke deadlocks because TWAIN's message pump runs on the UI thread.
        var capturedHash = hash;
        var capturedSetFilter = SelectedSetFilter;
        Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            ScannedCards.Add(scannedCard);

            if (!_auditService.IsAuditActive)
            {
                // Run async OCR after card is in the queue
                OcrMatchResult? ocrResult = null;
                try
                {
                    if (game == CardGame.OnePiece)
                    {
                        // OPTCG: detect collector number for direct lookup
                        var (collectorNumber, conf) = await _ocrService.DetectOptcgCollectorNumberAsync(rawBytes);
                        if (collectorNumber is not null && conf >= 0.5)
                        {
                            ocrResult = new OcrMatchResult { CollectorNumber = collectorNumber, CollectorNumberConfidence = conf };
                            _logger.LogInformation("OPTCG collector number detected: {Number} (confidence {Conf:F2})", collectorNumber, conf);

                            var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, null, scannedCard.ScanEdgeHash);
                            if (ocrMatch is not null && (scannedCard.Match is null || ocrMatch.GameSpecificId != scannedCard.Match?.GameSpecificId))
                            {
                                scannedCard.Match = ocrMatch;
                                scannedCard.Game = ocrGame;
                                scannedCard.FlagReason = FlagReason.None;
                                _logger.LogInformation("OCR matched to \"{CardName}\" ({SetCode} #{Number})", ocrMatch.Name, ocrMatch.SetCode, ocrMatch.CollectorNumber);
                            }
                        }
                    }
                    else if (game == CardGame.Riftbound)
                    {
                        // Riftbound: detect "{SET}-{number}" for candidate lookup + pHash disambiguation
                        var (collectorNumber, conf) = await _ocrService.DetectRiftboundCollectorNumberAsync(rawBytes);
                        if (collectorNumber is not null && conf >= 0.5)
                        {
                            ocrResult = new OcrMatchResult { CollectorNumber = collectorNumber, CollectorNumberConfidence = conf };
                            _logger.LogInformation("Riftbound collector detected: {Number} (confidence {Conf:F2})", collectorNumber, conf);
                            var (ocrMatch, ocrGame) = FindBestMatch(capturedHash, scannedCard.ArtHashes, ocrResult, capturedSetFilter, null, scannedCard.ScanEdgeHash);
                            if (ocrMatch is not null && (scannedCard.Match is null || ocrMatch.GameSpecificId != scannedCard.Match?.GameSpecificId))
                            {
                                scannedCard.Match = ocrMatch;
                                scannedCard.Game = ocrGame;
                                scannedCard.FlagReason = FlagReason.None;
                                _logger.LogInformation("OCR matched to \"{CardName}\" ({SetCode} #{Number})", ocrMatch.Name, ocrMatch.SetCode, ocrMatch.CollectorNumber);
                            }
                        }
                    }
                    else
                    {
                        // MTG: name recognition + symbol detection
                        ocrResult = await _ocrService.AnalyzeCardAsync(rawBytes);
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

                                if (scannedCard.FlagReason is FlagReason.NoMatch or FlagReason.VeryLowConfidence)
                                {
                                    if (ocrMatch.Confidence is null or >= 15)
                                    {
                                        scannedCard.FlagReason = FlagReason.None;
                                        _logger.LogInformation("Auto-flag cleared after OCR improvement");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OCR analysis failed");
                }

                // Auto-rotate 180° and retry if still no match
                if (scannedCard.Match is null)
                {
                    try
                    {
                        _logger.LogInformation("No match found, attempting 180° rotation for hash {Hash:X16}", scannedCard.Hash);
                        using var bmp = new System.Drawing.Bitmap(new MemoryStream(rawBytes));
                        bmp.RotateFlip(System.Drawing.RotateFlipType.Rotate180FlipNone);

                        using var rotatedStream = new MemoryStream();
                        bmp.Save(rotatedStream, System.Drawing.Imaging.ImageFormat.Png);
                        rotatedStream.Position = 0;
                        var rotatedHash = _hashService.ComputeHash(rotatedStream);
                        rotatedStream.Position = 0;
                        var rotatedBytes = rotatedStream.ToArray();

                        // Try OCR on rotated image for One Piece / Riftbound
                        OcrMatchResult? rotatedOcr = null;
                        if (game == CardGame.OnePiece)
                        {
                            var (cn, cnConf) = await _ocrService.DetectOptcgCollectorNumberAsync(rotatedBytes);
                            if (cn is not null && cnConf >= 0.5)
                                rotatedOcr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = cnConf };
                        }
                        else if (game == CardGame.Riftbound)
                        {
                            var (cn, cnConf) = await _ocrService.DetectRiftboundCollectorNumberAsync(rotatedBytes);
                            if (cn is not null && cnConf >= 0.5)
                                rotatedOcr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = cnConf };
                        }

                        // Structure rotates too — recompute the edge hash on the rotated bytes for foil One Piece/Riftbound scans
                        ulong? rotatedEdgeHash = null;
                        if (scannedCard.IsFoil && (game == CardGame.OnePiece || game == CardGame.Riftbound))
                            rotatedEdgeHash = _hashService.ComputeEdgeHash(new MemoryStream(rotatedBytes));

                        var (rotatedMatch, rotatedGame) = FindBestMatch(rotatedHash, null, rotatedOcr, capturedSetFilter, null, rotatedEdgeHash);
                        if (rotatedMatch is not null)
                        {
                            scannedCard.Match = rotatedMatch;
                            scannedCard.Game = rotatedGame;
                            scannedCard.Hash = rotatedHash;
                            scannedCard.FlagReason = FlagReason.None;

                            // Overwrite temp file with rotated image
                            try { File.WriteAllBytes(scannedCard.TempImagePath, rotatedBytes); }
                            catch (Exception ex) { _logger.LogWarning(ex, "Failed to save rotated image"); }

                            _logger.LogInformation("180° rotation matched to \"{CardName}\" ({SetCode} #{Number})",
                                rotatedMatch.Name, rotatedMatch.SetCode, rotatedMatch.CollectorNumber);
                        }
                        else
                        {
                            _logger.LogDebug("180° rotation did not produce a match");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto-rotation failed");
                    }
                }

                // Upgrade NoMatch to MissingFromDatabase after all matching attempts exhausted
                if (scannedCard.Match is null && scannedCard.FlagReason == FlagReason.NoMatch)
                {
                    scannedCard.FlagReason = FlagReason.MissingFromDatabase;
                    _logger.LogInformation("Card flagged as missing from database (pHash + OCR exhausted)");
                }

                // Always log OCR results to diagnostics (even if OCR didn't change the match)
                try
                {
                    var currentGame = scannedCard.Game;
                    var ocrDiag = _gameServices.TryGetValue(currentGame, out var gs2) ? gs2.LastMatchDiagnostics : null;
                    _diagnosticService.LogScanCompleted(_currentSessionId, capturedHash, scannedCard.Match, ocrDiag, scannedCard.ArtHashes, ocrResult, scannedCard.FlagReason);
                }
                catch { }
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
        using var context = _omniDbContextFactory.CreateDbContext();
        var productCache = new Dictionary<(CardGame Game, string GameCardId, bool Foil), Product>();

        var committed = new List<(InventoryLot Lot, ScannedCard Scan)>();
        var skipped = 0;
        progress?.Report("Preparing cards for collection...");
        foreach (var scan in scannedCards)
        {
            // Use per-card override if set, otherwise use toolbar defaults
            var container = scan.OverrideContainer ?? activeContainer;

            InventoryLot lot;
            if (scan.Match is null)
            {
                // Commit as missing card if flagged MissingFromDatabase
                if (scan.FlagReason != FlagReason.MissingFromDatabase)
                {
                    skipped++;
                    continue;
                }

                var product = FindOrCreateProduct(context, productCache, scan.Game, "", scan.IsFoil,
                    "Unknown Card", "", "", "", "", null, null, null);

                lot = new InventoryLot
                {
                    Product = product,
                    Condition = scan.Condition,
                    UnitCost = scan.PurchasePrice,
                    LocationId = container?.Id,
                    IsMissing = true,
                    FlagReason = FlagReason.MissingFromDatabase,
                };
            }
            else
            {
                var product = FindOrCreateProduct(context, productCache, scan.Game, scan.Match.GameSpecificId, scan.IsFoil,
                    scan.Match.Name, scan.Match.SetCode, scan.Match.SetName, scan.Match.CollectorNumber, scan.Match.Rarity,
                    scan.Match.ImageUri,
                    CardAttributeExtractor.ExtractColor(scan.Match, scan.Game),
                    CardAttributeExtractor.ExtractCardType(scan.Match, scan.Game));

                lot = new InventoryLot
                {
                    Product = product,
                    Condition = scan.Condition,
                    UnitCost = scan.PurchasePrice,
                    LocationId = container?.Id,
                    FlagReason = scan.FlagReason != FlagReason.None ? scan.FlagReason : null,
                };
            }

            if (container?.ContainerType == ContainerType.Binder)
            {
                lot.Page = scan.OverridePage ?? page;
                lot.Slot = scan.OverrideSlot ?? slot;
            }
            else if (container?.ContainerType == ContainerType.Box)
            {
                lot.Section = scan.OverrideSection ?? section;
            }

            context.Lots.Add(lot);
            committed.Add((lot, scan));
        }

        // First save to get auto-generated IDs (and resolve any newly-created Products)
        progress?.Report($"Saving {committed.Count} cards to database...");
        context.SaveChanges();

        // Acquire movements + flag resolution records for flagged cards that were fixed
        foreach (var (lot, scan) in committed)
        {
            context.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Acquire,
                Quantity = 1,
                UnitValue = lot.UnitCost,
            });

            if (scan.FlagFix is not null)
            {
                context.FlagResolutions.Add(new FlagResolution
                {
                    LotId = lot.Id,
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
        foreach (var (lot, scan) in committed)
        {
            var fileName = $"{lot.Id}.jpg";
            var filePath = Path.Combine(scansDir, fileName);

            try
            {
                ConvertToJpeg(scan.TempImagePath, filePath, quality: 90);
                lot.ScanImagePath = $"scans/{fileName}";

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
                _logger.LogWarning(ex, "Failed to copy scan image for card {Id}", lot.Id);
            }

            saved++;
            if (saved % 5 == 0 || saved == committed.Count)
                progress?.Report($"Saving scan images... {saved}/{committed.Count}");
        }

        // Second save to persist movements, flag resolutions, and ScanImagePath values
        progress?.Report("Finalizing...");
        context.SaveChanges();
        _logger.LogInformation("Committed {Committed} cards to collection ({Skipped} skipped)", committed.Count, skipped);
    }

    /// <summary>
    /// Finds an existing catalog <see cref="Product"/> matching (Game, GameCardId, Foil) — the
    /// identity key for singles — or creates one. <paramref name="cache"/> lets callers dedupe
    /// across a batch of writes made in the same context/SaveChanges cycle (e.g. several scans
    /// of the same printing, or several bulk-edited siblings reassigned to the same new printing)
    /// without round-tripping to the database or creating duplicate rows for not-yet-saved Products.
    /// </summary>
    private static Product FindOrCreateProduct(
        OmniCardDbContext context,
        Dictionary<(CardGame Game, string GameCardId, bool Foil), Product> cache,
        CardGame game, string gameCardId, bool foil,
        string name, string? setCode, string? setName, string? number, string? rarity,
        string? imageUri, string? color, string? cardType)
    {
        var key = (game, gameCardId, foil);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var product = context.Products.Local.FirstOrDefault(p =>
                p.Category == ProductCategory.Single && p.Game == game && p.GameCardId == gameCardId && p.Foil == foil)
            ?? context.Products.FirstOrDefault(p =>
                p.Category == ProductCategory.Single && p.Game == game && p.GameCardId == gameCardId && p.Foil == foil);

        if (product is null)
        {
            product = new Product
            {
                Game = game,
                Category = ProductCategory.Single,
                GameCardId = gameCardId,
                Foil = foil,
                Name = name,
                SetCode = setCode,
                SetName = setName,
                CollectorNumber = number,
                Rarity = rarity,
                ImageUri = imageUri,
                Color = color,
                CardType = cardType,
            };
            context.Products.Add(product);
        }

        cache[key] = product;
        return product;
    }

    private static Product FindOrCreateProduct(
        OmniCardDbContext context,
        CardGame game, string gameCardId, bool foil,
        string name, string? setCode, string? setName, string? number, string? rarity,
        string? imageUri, string? color, string? cardType)
        => FindOrCreateProduct(context, [], game, gameCardId, foil, name, setCode, setName, number, rarity, imageUri, color, cardType);

    /// <summary>
    /// Applies a <see cref="CollectionCard"/> DTO's edits back onto its backing <see cref="InventoryLot"/>.
    /// Identity fields (Game/GameCardId/IsFoil/Name/SetCode/SetName/Number/Rarity) live on the shared
    /// <see cref="Product"/> catalog row, so a change to any of them moves the lot to a matching
    /// find-or-create Product rather than mutating the (possibly shared) Product in place. Copy
    /// attributes (condition, location, scan image, etc.) always apply directly to the Lot.
    /// </summary>
    private static void ApplyIdentityAndCopyAttrs(OmniCardDbContext context, InventoryLot lot, CollectionCard card,
        Dictionary<(CardGame Game, string GameCardId, bool Foil), Product>? productCache = null)
    {
        var product = lot.Product;
        var identityChanged =
            product.Game != card.Game ||
            (product.GameCardId ?? "") != card.GameCardId ||
            product.Foil != card.IsFoil ||
            product.Name != card.Name ||
            (product.SetCode ?? "") != card.SetCode ||
            (product.SetName ?? "") != card.SetName ||
            (product.CollectorNumber ?? "") != card.Number ||
            (product.Rarity ?? "") != card.Rarity;

        if (identityChanged)
        {
            var target = FindOrCreateProduct(context, productCache ?? [], card.Game, card.GameCardId, card.IsFoil,
                card.Name, card.SetCode, card.SetName, card.Number, card.Rarity, card.ImageUri, card.Color, card.CardType);
            lot.Product = target;
        }

        // Copy attributes always live on the Lot, independent of identity.
        lot.Condition = card.Condition;
        lot.UnitCost = card.PurchasePrice;
        lot.LocationId = card.ContainerId;
        lot.Page = card.Page;
        lot.Slot = card.Slot;
        lot.Section = card.Section;
        lot.ScanImagePath = card.ScanImagePath;
        lot.IsMissing = card.IsMissing;
        lot.FlagReason = card.FlagReason;
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

    public (int FlagResolutions, int MismatchLogs, int DiagnosticEvents) ClearDiagnosticLogs()
    {
        // MismatchLogs, ScanDiagnosticEvents, and FlagResolutions are all now written against
        // the unified OmniCardDbContext (Task 6 moved MismatchLogService/ScanDiagnosticService
        // off the Phase-1 CollectionDbContext).
        using var context = _omniDbContextFactory.CreateDbContext();
        var mismatchCount = context.MismatchLogs.Count();
        var diagnosticCount = context.ScanDiagnosticEvents.Count();

        context.MismatchLogs.RemoveRange(context.MismatchLogs);
        context.ScanDiagnosticEvents.RemoveRange(context.ScanDiagnosticEvents);
        context.SaveChanges();

        var flagCount = context.FlagResolutions.Count();
        context.FlagResolutions.RemoveRange(context.FlagResolutions);
        context.SaveChanges();

        _logger.LogInformation("Cleared diagnostic logs: {FlagResolutions} flag resolutions, {MismatchLogs} mismatch logs, {DiagnosticEvents} diagnostic events", flagCount, mismatchCount, diagnosticCount);
        return (flagCount, mismatchCount, diagnosticCount);
    }

    public (int Deleted, int Errors) DeleteOrphanedScans(IProgress<string>? progress = null)
    {
        var scansDir = _dataPathService.ScansDirectory;
        if (!Directory.Exists(scansDir))
            return (0, 0);

        progress?.Report("Scanning for orphaned images...");

        var scanFiles = Directory.GetFiles(scansDir);
        // Scan images now belong to Lots (Task 4) — not the Phase-1 Cards table.
        using var context = _omniDbContextFactory.CreateDbContext();
        var validPaths = context.Lots
            .AsNoTracking()
            .Where(l => l.ScanImagePath != null)
            .Select(l => l.ScanImagePath!)
            .ToHashSet();

        var deleted = 0;
        var errors = 0;

        foreach (var filePath in scanFiles)
        {
            var relativePath = $"scans/{Path.GetFileName(filePath)}";
            if (!validPaths.Contains(relativePath))
            {
                try
                {
                    File.Delete(filePath);
                    deleted++;
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogWarning(ex, "Failed to delete orphaned scan: {Path}", filePath);
                }
            }
        }

        _logger.LogInformation("Orphaned scan cleanup: {Deleted} deleted, {Errors} errors", deleted, errors);
        return (deleted, errors);
    }

    public void AddCardToCollection(CardMatch match, CardGame game, string condition, bool isFoil, decimal? purchasePrice, int quantity, StorageContainer? container, int? page, int? slot, string? section)
    {
        using var context = _omniDbContextFactory.CreateDbContext();

        var product = FindOrCreateProduct(context, game, match.GameSpecificId, isFoil,
            match.Name, match.SetCode, match.SetName, match.CollectorNumber, match.Rarity, match.ImageUri,
            CardAttributeExtractor.ExtractColor(match, game), CardAttributeExtractor.ExtractCardType(match, game));

        var lots = new List<InventoryLot>();
        for (var i = 0; i < quantity; i++)
        {
            var lot = new InventoryLot
            {
                Product = product,
                Condition = condition,
                UnitCost = purchasePrice,
                LocationId = container?.Id,
            };

            if (container?.ContainerType == ContainerType.Binder)
            {
                lot.Page = page;
                lot.Slot = slot;
            }
            else if (container?.ContainerType == ContainerType.Box)
            {
                lot.Section = section;
            }

            context.Lots.Add(lot);
            lots.Add(lot);
        }

        context.SaveChanges();

        foreach (var lot in lots)
        {
            context.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Acquire,
                Quantity = 1,
                UnitValue = purchasePrice,
            });
        }
        context.SaveChanges();

        _logger.LogInformation("Manually added {Quantity}x {Name} ({SetCode}) to collection", quantity, match.Name, match.SetCode);
    }

    /// <summary>
    /// Writes CSV-imported <see cref="CollectionCard"/> rows into the unified store as one
    /// Product (find-or-create by Game/GameCardId/Foil) + Lot per row. Callers (CsvExportImportService)
    /// are expected to have already resolved <c>card.ContainerId</c> (including creating any
    /// container referenced by name in app-native imports) before calling this.
    /// </summary>
    public int ImportCollectionCards(IEnumerable<CollectionCard> cards, bool skipDuplicates)
    {
        using var context = _omniDbContextFactory.CreateDbContext();
        var productCache = new Dictionary<(CardGame Game, string GameCardId, bool Foil), Product>();
        var imported = 0;
        var committed = new List<InventoryLot>();

        foreach (var card in cards)
        {
            if (skipDuplicates)
            {
                var exists = context.Lots
                    .Any(l => l.Product.Game == card.Game
                        && l.Product.GameCardId == card.GameCardId
                        && l.Product.Foil == card.IsFoil
                        && l.Condition == card.Condition);
                if (exists)
                    continue;
            }

            var product = FindOrCreateProduct(context, productCache, card.Game, card.GameCardId, card.IsFoil,
                card.Name, card.SetCode, card.SetName, card.Number, card.Rarity, card.ImageUri, card.Color, card.CardType);

            var lot = new InventoryLot
            {
                Product = product,
                Condition = card.Condition,
                UnitCost = card.PurchasePrice,
                AcquisitionDate = card.DateAdded,
                LocationId = card.ContainerId,
                Page = card.Page,
                Slot = card.Slot,
                Section = card.Section,
            };

            context.Lots.Add(lot);
            committed.Add(lot);
            imported++;
        }

        context.SaveChanges();

        foreach (var lot in committed)
        {
            context.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Acquire,
                Quantity = 1,
                UnitValue = lot.UnitCost,
            });
        }
        context.SaveChanges();

        _logger.LogInformation("Imported {Count} cards into collection", imported);
        return imported;
    }

    public ulong ComputeHashFromStream(Stream stream) => _hashService.ComputeHash(stream);

    public ulong ComputeEdgeHashFromStream(Stream stream) => _hashService.ComputeEdgeHash(stream);

    public IOcrMatchingService OcrService => _ocrService;

    public void SearchCollection(string query, CardGame? gameFilter, ObservableCollection<CollectionCard> results)
        => SearchCollection(query, gameFilter, null, null, null, results);

    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, ObservableCollection<CollectionCard> results)
        => SearchCollection(query, gameFilter, containerFilter, null, null, results);

    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, ObservableCollection<CollectionCard> results)
        => SearchCollection(query, gameFilter, containerFilter, sortPreset, filterPreset, false, results);

    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, ObservableCollection<CollectionCard> results)
        => SearchCollection(query, gameFilter, containerFilter, sortPreset, filterPreset, stacked, 0, int.MaxValue, results);

    public void SearchCollection(string query, CardGame? gameFilter, int? containerFilter, SortPreset? sortPreset, FilterPreset? filterPreset, bool stacked, int skip, int take, ObservableCollection<CollectionCard> results)
    {
        _logger.LogDebug("Searching collection: query={Query}, game={Game}, container={Container}, stacked={Stacked}, skip={Skip}, take={Take}", query ?? "(all)", gameFilter?.ToString() ?? "all", containerFilter?.ToString() ?? "all", stacked, skip, take);
        if (skip == 0)
            results.Clear();

        using var context = _omniDbContextFactory.CreateDbContext();
        var cards = BuildFilteredQuery(context, query, gameFilter, containerFilter, filterPreset);

        if (stacked)
        {
            // SQL GROUP BY: get representative ID + count per group
            var groups = cards
                .GroupBy(c => new { c.GameCardId, c.IsFoil, c.Condition })
                .Select(g => new
                {
                    g.Key,
                    RepId = g.Min(c => c.Id),
                    Count = g.Count(),
                })
                .ToList();

            // Collect all IDs per group for StackedIds (lightweight: just Id + group key)
            var allIdsByGroup = cards
                .Select(c => new { c.Id, c.GameCardId, c.IsFoil, c.Condition })
                .ToList()
                .GroupBy(c => (c.GameCardId, c.IsFoil, c.Condition))
                .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

            // Load only the representative cards
            var repIds = groups.Select(g => g.RepId).ToHashSet();
            var repCards = cards.Where(c => repIds.Contains(c.Id)).ToDictionary(c => c.Id);

            // Build stacked results
            var stackedResults = new List<CollectionCard>(groups.Count);
            foreach (var g in groups)
            {
                if (!repCards.TryGetValue(g.RepId, out var rep)) continue;
                rep.Quantity = g.Count;
                rep.StackedIds = allIdsByGroup.GetValueOrDefault((g.Key.GameCardId, g.Key.IsFoil, g.Key.Condition));
                stackedResults.Add(rep);
            }

            // Apply sort, then paginate
            var sorted = ApplySortPreset(stackedResults.AsQueryable(), sortPreset);
            var page = sorted.Skip(skip).Take(take).ToList();
            AttachEbayListings(context, page);
            foreach (var card in page)
                results.Add(card);
        }
        else
        {
            // Apply sort preset (or default to Name), then paginate
            var sorted = ApplySortPreset(cards, sortPreset);
            var page = sorted.Skip(skip).Take(take).ToList();
            AttachEbayListings(context, page);
            foreach (var card in page)
                results.Add(card);
        }

        _logger.LogDebug("Collection search returned {Count} results", results.Count);
    }

    public int GetSearchCount(string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset, bool stacked)
    {
        using var context = _omniDbContextFactory.CreateDbContext();
        var cards = BuildFilteredQuery(context, query, gameFilter, containerFilter, filterPreset);

        if (stacked)
        {
            return cards
                .GroupBy(c => new { c.GameCardId, c.IsFoil, c.Condition })
                .Count();
        }

        return cards.Count();
    }

    public HashSet<int> GetMatchingContainerIds(string query, CardGame? gameFilter)
    {
        using var context = _omniDbContextFactory.CreateDbContext();
        var cards = BuildFilteredQuery(context, query, gameFilter, containerFilter: null, filterPreset: null);
        return cards
            .Where(c => c.ContainerId != null)
            .Select(c => c.ContainerId!.Value)
            .Distinct()
            .ToHashSet();
    }

    /// <summary>
    /// Detects the card region in a scanned image and crops to it. When the
    /// scanner's internal edge detection fails (common with foil cards), the
    /// raw image is much larger than the card. This scans inward from each
    /// edge looking for non-background content.
    /// </summary>
    private MemoryStream AutoCropScan(MemoryStream input)
    {
        const int margin = 8;
        const int brightnessThreshold = 225;
        const double minCardFraction = 0.20;

        try
        {
            input.Position = 0;
            using var bmp = new Bitmap(input);

            var w = bmp.Width;
            var h = bmp.Height;

            // Lock pixels for fast access
            var data = bmp.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            int stride = data.Stride;
            var pixels = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(data);

            // A row/column counts as "card" when at least 10% of its pixels
            // are darker than the background threshold.
            int rowThreshold = (int)(w * 0.10);
            int colThreshold = (int)(h * 0.10);

            int top = 0, bottom = h - 1, left = 0, right = w - 1;

            for (int y = 0; y < h; y++)
            {
                if (CountDarkPixelsInRow(pixels, y, w, stride, brightnessThreshold) >= rowThreshold)
                { top = y; break; }
            }

            for (int y = h - 1; y >= top; y--)
            {
                if (CountDarkPixelsInRow(pixels, y, w, stride, brightnessThreshold) >= rowThreshold)
                { bottom = y; break; }
            }

            for (int x = 0; x < w; x++)
            {
                if (CountDarkPixelsInCol(pixels, x, top, bottom, stride, brightnessThreshold) >= colThreshold)
                { left = x; break; }
            }

            for (int x = w - 1; x >= left; x--)
            {
                if (CountDarkPixelsInCol(pixels, x, top, bottom, stride, brightnessThreshold) >= colThreshold)
                { right = x; break; }
            }

            // Add margin
            top = Math.Max(0, top - margin);
            left = Math.Max(0, left - margin);
            bottom = Math.Min(h - 1, bottom + margin);
            right = Math.Min(w - 1, right + margin);

            int cropW = right - left + 1;
            int cropH = bottom - top + 1;

            // Skip if the card already fills the frame (no crop needed)
            if (cropW >= w * 0.95 && cropH >= h * 0.95)
            {
                _logger.LogDebug("Auto-crop: card fills frame ({W}x{H}), no crop needed", w, h);
                input.Position = 0;
                return input;
            }

            // Skip if detected region is too small (noise, not a card)
            if (cropW < w * minCardFraction || cropH < h * minCardFraction)
            {
                _logger.LogWarning("Auto-crop: detected region too small ({CropW}x{CropH} in {W}x{H}), skipping",
                    cropW, cropH, w, h);
                input.Position = 0;
                return input;
            }

            var cropRect = new Rectangle(left, top, cropW, cropH);
            using var cropped = bmp.Clone(cropRect, bmp.PixelFormat);

            var output = new MemoryStream();
            cropped.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            output.Position = 0;

            _logger.LogInformation(
                "Auto-crop: {OrigW}x{OrigH} -> {CropW}x{CropH} (removed {Pct:F0}% border)",
                w, h, cropW, cropH,
                100.0 * (1.0 - (double)(cropW * cropH) / (w * h)));

            input.Dispose();
            return output;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-crop failed, using original image");
            input.Position = 0;
            return input;
        }
    }

    private static int CountDarkPixelsInRow(byte[] pixels, int y, int w, int stride, int threshold)
    {
        int count = 0;
        int rowStart = y * stride;
        for (int x = 0; x < w; x++)
        {
            int offset = rowStart + x * 3;
            int brightness = (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3;
            if (brightness < threshold) count++;
        }
        return count;
    }

    private static int CountDarkPixelsInCol(byte[] pixels, int x, int yStart, int yEnd, int stride, int threshold)
    {
        int count = 0;
        for (int y = yStart; y <= yEnd; y++)
        {
            int offset = y * stride + x * 3;
            int brightness = (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3;
            if (brightness < threshold) count++;
        }
        return count;
    }

    /// <summary>
    /// Builds the base read query by projecting Lots⋈Products (Category=Single) into the
    /// <see cref="CollectionCard"/> DTO shape, left-joined to StorageContainers so
    /// location-based filters keep working. Downstream filter/sort code (below) is written
    /// against <see cref="CollectionCard"/> and is unchanged from the Phase-1 CollectionDbContext
    /// version — only the source of the IQueryable moved.
    /// </summary>
    private IQueryable<CollectionCard> BuildFilteredQuery(OmniCardDbContext context, string query, CardGame? gameFilter, int? containerFilter, FilterPreset? filterPreset)
    {
        IQueryable<CollectionCard> cards =
            from l in context.Lots.AsNoTracking()
            join p in context.Products.AsNoTracking() on l.ProductId equals p.Id
            where p.Category == ProductCategory.Single
            join sc in context.StorageContainers.AsNoTracking() on l.LocationId equals sc.Id into containerJoin
            from sc in containerJoin.DefaultIfEmpty()
            select new CollectionCard
            {
                Id = l.Id,
                Game = p.Game,
                GameCardId = p.GameCardId ?? "",
                Name = p.Name,
                SetName = p.SetName ?? "",
                SetCode = p.SetCode ?? "",
                Number = p.CollectorNumber ?? "",
                Rarity = p.Rarity ?? "",
                ImageUri = p.ImageUri,
                ScanImagePath = l.ScanImagePath,
                Condition = l.Condition ?? "NM",
                IsFoil = p.Foil,
                PurchasePrice = l.UnitCost,
                DateAdded = l.AcquisitionDate,
                ContainerId = l.LocationId,
                Container = sc,
                Page = l.Page,
                Slot = l.Slot,
                Section = l.Section,
                Color = p.Color,
                CardType = p.CardType,
                IsMissing = l.IsMissing,
                FlagReason = l.FlagReason,
            };

        if (gameFilter.HasValue)
            cards = cards.Where(c => c.Game == gameFilter.Value);

        if (containerFilter.HasValue)
            cards = cards.Where(c => c.ContainerId == containerFilter.Value);

        if (!string.IsNullOrWhiteSpace(query))
            cards = ApplyScryfallFilter(cards, query);

        if (filterPreset is not null && !string.IsNullOrWhiteSpace(filterPreset.Query))
            cards = ApplyScryfallFilter(cards, filterPreset.Query);

        return cards;
    }

    /// <summary>
    /// Batch-loads <see cref="EbayListing"/> rows for the given (already-materialized) cards' Lot
    /// ids and attaches each to its owning <see cref="CollectionCard"/>. A single lot has at most
    /// one listing (unique index on LotId), so this is a dictionary lookup, not a join — avoids an
    /// N+1 query per card for UI/editor code that reads <c>card.EbayListing?.Status</c> etc.
    /// </summary>
    private static void AttachEbayListings(OmniCardDbContext context, IReadOnlyCollection<CollectionCard> cards)
    {
        if (cards.Count == 0)
            return;

        var lotIds = cards.Select(c => c.Id).ToList();
        var listingsByLotId = context.EbayListings
            .AsNoTracking()
            .Where(l => lotIds.Contains(l.LotId))
            .ToDictionary(l => l.LotId);

        foreach (var card in cards)
            card.EbayListing = listingsByLotId.GetValueOrDefault(card.Id);
    }

    public List<string> GetDistinctFieldValues(string field, CardGame game)
    {
        using var context = _omniDbContextFactory.CreateDbContext();
        var cards = BuildFilteredQuery(context, "", game, containerFilter: null, filterPreset: null);

        IQueryable<string> values = field switch
        {
            "Name" => cards.Select(c => c.Name),
            "Color" => cards.Where(c => c.Color != null).Select(c => c.Color!),
            "CardType" => cards.Where(c => c.CardType != null).Select(c => c.CardType!),
            "SetName" => cards.Select(c => c.SetName),
            "SetCode" => cards.Select(c => c.SetCode),
            "Rarity" => cards.Select(c => c.Rarity),
            "Condition" => cards.Select(c => c.Condition),
            "IsFoil" => cards.Select(c => c.IsFoil ? "True" : "False"),
            "Section" => cards.Where(c => c.Section != null).Select(c => c.Section!),
            _ => Enumerable.Empty<string>().AsQueryable()
        };

        return values.Distinct().OrderBy(v => v).ToList();
    }

    private static IQueryable<CollectionCard> ApplyScryfallFilter(IQueryable<CollectionCard> cards, string query)
    {
        var filter = ScryfallQueryParser.ParseFilter(query);
        if (filter is null)
            return cards;

        var param = LinqExpression.Parameter(typeof(CollectionCard), "c");
        var expr = BuildFilterExpression(param, filter);
        var lambda = LinqExpression.Lambda<Func<CollectionCard, bool>>(expr, param);
        return cards.Where(lambda);
    }

    private static readonly System.Reflection.MethodInfo LikeMethod =
        typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            [typeof(DbFunctions), typeof(string), typeof(string)])!;

    private static LinqExpression CallLike(LinqExpression property, string pattern)
    {
        return LinqExpression.Call(
            LikeMethod,
            LinqExpression.Property(null, typeof(EF), nameof(EF.Functions)),
            property,
            LinqExpression.Constant(pattern));
    }

    private static LinqExpression BuildFilterExpression(System.Linq.Expressions.ParameterExpression param, FilterNode node)
    {
        return node switch
        {
            FieldFilter f => BuildFieldExpression(param, f),
            AndFilter and => and.Children
                .Select(c => BuildFilterExpression(param, c))
                .Aggregate(LinqExpression.AndAlso),
            OrFilter or => or.Children
                .Select(c => BuildFilterExpression(param, c))
                .Aggregate(LinqExpression.OrElse),
            NotFilter not => LinqExpression.Not(BuildFilterExpression(param, not.Inner)),
            _ => LinqExpression.Constant(true),
        };
    }

    private static LinqExpression BuildFieldExpression(System.Linq.Expressions.ParameterExpression param, FieldFilter filter)
    {
        var expr = filter.Field switch
        {
            "name" => BuildNameExpression(param, filter.Op, filter.Value),
            "set" => BuildSetExpression(param, filter.Op, filter.Value),
            "cn" => BuildCnExpression(param, filter.Op, filter.Value),
            "type" => BuildNullableStringExpression(param, nameof(CollectionCard.CardType), filter.Op, filter.Value),
            "rarity" => BuildRarityExpression(param, filter.Op, filter.Value),
            "color" => BuildColorExpression(param, filter.Op, filter.Value),
            "is" => BuildIsExpression(param, filter.Value),
            "foil" => BuildLegacyFoilExpression(param, filter.Value),
            "condition" or "cond" => BuildStringExpression(param, nameof(CollectionCard.Condition), filter.Op, filter.Value),
            "location" or "loc" => BuildLocationExpression(param, filter.Op, filter.Value),
            _ => BuildNameExpression(param, filter.Op, filter.Value),
        };

        return filter.Negated ? LinqExpression.Not(expr) : expr;
    }

    private static LinqExpression BuildNameExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Name));
        return op switch
        {
            ComparisonOp.Exact => CallLike(prop, value),
            ComparisonOp.NotEqual => LinqExpression.Not(CallLike(prop, value)),
            _ => CallLike(prop, $"%{value}%"),
        };
    }

    private static LinqExpression BuildSetExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var codeProp = LinqExpression.Property(param, nameof(CollectionCard.SetCode));
        var nameProp = LinqExpression.Property(param, nameof(CollectionCard.SetName));

        return op switch
        {
            // set:xyz → exact match on set code (case-insensitive via LIKE)
            ComparisonOp.Contains => CallLike(codeProp, value),
            ComparisonOp.Exact => CallLike(codeProp, value),
            ComparisonOp.NotEqual => LinqExpression.Not(CallLike(codeProp, value)),
            _ => CallLike(codeProp, value),
        };
    }

    private static LinqExpression BuildCnExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Number));
        return op switch
        {
            ComparisonOp.NotEqual => LinqExpression.NotEqual(prop, LinqExpression.Constant(value)),
            _ => LinqExpression.Equal(prop, LinqExpression.Constant(value)),
        };
    }

    private static LinqExpression BuildStringExpression(System.Linq.Expressions.ParameterExpression param, string propertyName, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, propertyName);
        return op switch
        {
            ComparisonOp.Exact => CallLike(prop, value),
            ComparisonOp.NotEqual => LinqExpression.Not(CallLike(prop, value)),
            _ => CallLike(prop, $"%{value}%"),
        };
    }

    private static LinqExpression BuildNullableStringExpression(System.Linq.Expressions.ParameterExpression param, string propertyName, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, propertyName);
        var notNull = LinqExpression.NotEqual(prop, LinqExpression.Constant(null, typeof(string)));

        if (op == ComparisonOp.NotEqual)
        {
            return LinqExpression.OrElse(
                LinqExpression.Equal(prop, LinqExpression.Constant(null, typeof(string))),
                LinqExpression.Not(CallLike(prop, value)));
        }

        var pattern = op == ComparisonOp.Exact ? value : $"%{value}%";
        return LinqExpression.AndAlso(notNull, CallLike(prop, pattern));
    }

    private static LinqExpression BuildRarityExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Rarity));

        if (op == ComparisonOp.Contains || op == ComparisonOp.Exact)
            return CallLike(prop, value);

        var matching = ScryfallQueryParser.RaritiesMatching(op, value);
        if (matching.Count == 0)
            return LinqExpression.Constant(false);

        return matching
            .Select(r => CallLike(prop, r))
            .Aggregate(LinqExpression.OrElse);
    }

    private static LinqExpression BuildColorExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var prop = LinqExpression.Property(param, nameof(CollectionCard.Color));
        var notNull = LinqExpression.NotEqual(prop, LinqExpression.Constant(null, typeof(string)));

        // colorless: Color is null or empty
        if (value.Equals("colorless", StringComparison.OrdinalIgnoreCase))
        {
            var isColorless = LinqExpression.OrElse(
                LinqExpression.Equal(prop, LinqExpression.Constant(null, typeof(string))),
                LinqExpression.Equal(prop, LinqExpression.Constant("")));
            return op == ComparisonOp.NotEqual ? LinqExpression.Not(isColorless) : isColorless;
        }

        // multicolor: Color has 2+ characters
        if (value.Equals("multicolor", StringComparison.OrdinalIgnoreCase) || value.Equals("multi", StringComparison.OrdinalIgnoreCase))
        {
            var lengthProp = LinqExpression.Property(prop, nameof(string.Length));
            var isMulti = LinqExpression.AndAlso(notNull,
                LinqExpression.GreaterThanOrEqual(lengthProp, LinqExpression.Constant(2)));
            return op == ComparisonOp.NotEqual ? LinqExpression.Not(isMulti) : isMulti;
        }

        var normalized = ScryfallQueryParser.NormalizeColorValue(value);
        if (normalized.Length == 0)
            return LinqExpression.Constant(true);

        return op switch
        {
            // : and >= mean "includes at least these colors"
            ComparisonOp.Contains or ComparisonOp.GreaterOrEqual => BuildColorSuperset(prop, notNull, normalized),
            // = means "exactly these colors"
            ComparisonOp.Exact => LinqExpression.AndAlso(notNull, CallLike(prop, normalized)),
            // != means "not exactly these colors"
            ComparisonOp.NotEqual => LinqExpression.OrElse(
                LinqExpression.Equal(prop, LinqExpression.Constant(null, typeof(string))),
                LinqExpression.Not(CallLike(prop, normalized))),
            // <= means "at most these colors" (subset)
            ComparisonOp.LessOrEqual => BuildColorSubset(prop, notNull, normalized),
            // < means "strict subset"
            ComparisonOp.LessThan => LinqExpression.AndAlso(
                BuildColorSubset(prop, notNull, normalized),
                LinqExpression.Not(CallLike(prop, normalized))),
            // > means "strict superset"
            ComparisonOp.GreaterThan => LinqExpression.AndAlso(
                BuildColorSuperset(prop, notNull, normalized),
                LinqExpression.Not(CallLike(prop, normalized))),
            _ => BuildColorSuperset(prop, notNull, normalized),
        };
    }

    /// <summary>Card's colors include all of the specified colors (superset).</summary>
    private static LinqExpression BuildColorSuperset(LinqExpression prop, LinqExpression notNull, string colors)
    {
        LinqExpression expr = notNull;
        foreach (var c in colors)
            expr = LinqExpression.AndAlso(expr, CallLike(prop, $"%{c}%"));
        return expr;
    }

    /// <summary>Card's colors don't include any color NOT in the specified set (subset).</summary>
    private static LinqExpression BuildColorSubset(LinqExpression prop, LinqExpression notNull, string colors)
    {
        const string allColors = "WUBRG";
        LinqExpression expr = notNull;
        foreach (var c in allColors.Where(c => !colors.Contains(c)))
            expr = LinqExpression.AndAlso(expr, LinqExpression.Not(CallLike(prop, $"%{c}%")));
        return expr;
    }

    private static LinqExpression BuildIsExpression(System.Linq.Expressions.ParameterExpression param, string value)
    {
        return value.ToLowerInvariant() switch
        {
            "foil" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsFoil)),
                LinqExpression.Constant(true)),
            "missing" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.IsMissing)),
                LinqExpression.Constant(true)),
            "missingdb" => LinqExpression.Equal(
                LinqExpression.Property(param, nameof(CollectionCard.FlagReason)),
                LinqExpression.Constant((FlagReason?)FlagReason.MissingFromDatabase, typeof(FlagReason?))),
            _ => LinqExpression.Constant(true),
        };
    }

    private static LinqExpression BuildLegacyFoilExpression(System.Linq.Expressions.ParameterExpression param, string value)
    {
        var isFoil = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                  || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                  || value == "1";
        return LinqExpression.Equal(
            LinqExpression.Property(param, nameof(CollectionCard.IsFoil)),
            LinqExpression.Constant(isFoil));
    }

    private static LinqExpression BuildLocationExpression(System.Linq.Expressions.ParameterExpression param, ComparisonOp op, string value)
    {
        var containerProp = LinqExpression.Property(param, nameof(CollectionCard.Container));
        var notNull = LinqExpression.NotEqual(containerProp, LinqExpression.Constant(null, typeof(StorageContainer)));
        var nameProp = LinqExpression.Property(containerProp, nameof(StorageContainer.Name));

        if (op == ComparisonOp.NotEqual)
        {
            return LinqExpression.OrElse(
                LinqExpression.Equal(containerProp, LinqExpression.Constant(null, typeof(StorageContainer))),
                LinqExpression.Not(CallLike(nameProp, value)));
        }

        var pattern = op == ComparisonOp.Exact ? value : $"%{value}%";
        return LinqExpression.AndAlso(notNull, CallLike(nameProp, pattern));
    }

    private static IEnumerable<CollectionCard> ApplySortPreset(IQueryable<CollectionCard> cards, SortPreset? preset)
    {
        if (preset is null || preset.SortLevels.Count == 0)
            return cards.OrderBy(c => c.Name);

        // If no sort level uses CustomOrder, we can sort entirely in SQL
        // An empty CustomOrder ([]) is equivalent to no custom order: sort by natural field value.
        if (preset.SortLevels.All(l => l.CustomOrder is not { Count: > 0 }))
            return ApplySortPresetInSql(cards, preset.SortLevels);

        // Custom orders require in-memory sorting since SQLite can't do CASE WHEN with parameter lists
        var list = cards.ToList();

        IOrderedEnumerable<CollectionCard>? ordered = null;
        foreach (var level in preset.SortLevels)
        {
            Func<CollectionCard, object> keySelector = level.CustomOrder is { Count: > 0 } customOrder
                ? c => GetCustomOrderIndex(GetFieldValue(c, level.Field), customOrder)
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

    private static IQueryable<CollectionCard> ApplySortPresetInSql(IQueryable<CollectionCard> cards, List<SortLevel> levels)
    {
        IOrderedQueryable<CollectionCard>? ordered = null;
        foreach (var level in levels)
        {
            System.Linq.Expressions.Expression<Func<CollectionCard, object>> keySelector = level.Field switch
            {
                "Name" => c => c.Name,
                "Color" => c => c.Color ?? "",
                "CardType" => c => c.CardType ?? "",
                "SetName" => c => c.SetName,
                "SetCode" => c => c.SetCode,
                "Rarity" => c => c.Rarity,
                "Condition" => c => c.Condition,
                "IsFoil" => c => c.IsFoil,
                "PurchasePrice" => c => c.PurchasePrice ?? 0m,
                "DateAdded" => c => c.DateAdded,
                "Number" => c => c.Number,
                "Section" => c => c.Section ?? "",
                "Page" => c => c.Page ?? 0,
                "Slot" => c => c.Slot ?? 0,
                "IsMissing" => c => c.IsMissing,
                // MarketPrice and Quantity are not DB columns; they are sorted in-memory
                // after materialization (see CollectionViewModel), so fall back to Name here.
                _ => c => c.Name
            };

            if (ordered is null)
            {
                ordered = level.Direction == SortDirection.Ascending
                    ? cards.OrderBy(keySelector)
                    : cards.OrderByDescending(keySelector);
            }
            else
            {
                ordered = level.Direction == SortDirection.Ascending
                    ? ordered.ThenBy(keySelector)
                    : ordered.ThenByDescending(keySelector);
            }
        }

        return ordered ?? cards.OrderBy(c => c.Name);
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
            "MarketPrice" => card.MarketPrice.ToString("0000000000.00"),
            "Quantity" => card.Quantity.ToString("0000000000"),
            "DateAdded" => card.DateAdded.ToString("o"),
            "Number" => card.Number,
            "Section" => card.Section ?? "",
            "Page" => (card.Page ?? 0).ToString("0000000000"),
            "Slot" => (card.Slot ?? 0).ToString("0000000000"),
            "IsMissing" => card.IsMissing.ToString(),
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
        using var context = _omniDbContextFactory.CreateDbContext();
        var ids = cardIds.ToList();
        // Category=Single guard, defense-in-depth: matches GetCollectionCards' scoping so a stray
        // sealed-lot id passed in from elsewhere can never be mutated via the singles write path.
        var lots = context.Lots.Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single).ToList();
        foreach (var lot in lots)
        {
            lot.LocationId = containerId;
            lot.Page = null;
            lot.Slot = null;
            lot.Section = section;

            context.Movements.Add(new InventoryMovement
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                Type = MovementType.Move,
                Quantity = 1,
                Note = section,
            });
        }
        context.SaveChanges();
    }

    public void BulkUpdateField(IEnumerable<int> cardIds, Action<CollectionCard> update)
    {
        using var context = _omniDbContextFactory.CreateDbContext();
        var ids = cardIds.ToList();
        // Category=Single guard, defense-in-depth: matches GetCollectionCards' scoping so a stray
        // sealed-lot id passed in from elsewhere can never be mutated via the singles write path.
        var lots = context.Lots.Include(l => l.Product)
            .Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single)
            .ToList();
        var productCache = new Dictionary<(CardGame Game, string GameCardId, bool Foil), Product>();

        foreach (var lot in lots)
        {
            var dto = CollectionCardMapper.ToDto(lot, lot.Product, 0m);
            update(dto);
            ApplyIdentityAndCopyAttrs(context, lot, dto, productCache);
        }

        context.SaveChanges();
    }

    public List<CollectionCard> GetCollectionCards(IEnumerable<int> cardIds)
    {
        using var context = _omniDbContextFactory.CreateDbContext();
        var ids = cardIds.ToList();
        var dtos = context.Lots.AsNoTracking()
            .Include(l => l.Product)
            .Where(l => ids.Contains(l.Id) && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m))
            .ToList();
        AttachEbayListings(context, dtos);
        return dtos;
    }

    public void UpdateCollectionCard(CollectionCard card)
    {
        _logger.LogInformation("Updating collection card {Id}: {Name}", card.Id, card.Name);
        using var context = _omniDbContextFactory.CreateDbContext();
        // Category=Single guard, defense-in-depth: matches GetCollectionCards' scoping so a stray
        // sealed-lot id can never be mutated via the singles write path.
        var lot = context.Lots.Include(l => l.Product)
            .FirstOrDefault(l => l.Id == card.Id && l.Product.Category == ProductCategory.Single);
        if (lot is null)
            return;

        ApplyIdentityAndCopyAttrs(context, lot, card);
        context.SaveChanges();
    }

    public void DeleteCollectionCard(int id)
    {
        _logger.LogInformation("Deleting collection card {Id}", id);
        using var context = _omniDbContextFactory.CreateDbContext();
        // Category=Single guard, defense-in-depth: matches GetCollectionCards' scoping so a stray
        // sealed-lot id can never be deleted via the singles write path. Find() can't apply a filter,
        // so this uses an explicit query instead.
        var lot = context.Lots.FirstOrDefault(l => l.Id == id && l.Product.Category == ProductCategory.Single);
        if (lot is null)
            return;

        // Explicit cleanup, defense-in-depth: OmniCardDbContext models EbayListing/FlagResolution
        // with a cascade-delete FK to Lot, but a dev db that was renamed in place from an older
        // schema may not have that FK actually enforced at the SQLite level, which would silently
        // orphan these rows instead of removing them.
        context.EbayListings.RemoveRange(context.EbayListings.Where(l => l.LotId == id));
        context.FlagResolutions.RemoveRange(context.FlagResolutions.Where(f => f.LotId == id));
        context.Lots.Remove(lot);
        context.SaveChanges();

        // Delete scan image if it exists
        if (lot.ScanImagePath is not null)
        {
            var fullPath = Path.Combine(_dataPathService.DataDirectory, lot.ScanImagePath);
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

        using var context = _omniDbContextFactory.CreateDbContext();
        var ownedCards = context.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.Product.Category == ProductCategory.Single && l.Product.Game == game)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m))
            .ToList();

        var service = _gameServices[game];
        return await service.GetSetCompletionAsync(ownedCards, progress);
    }

    public List<MissingCard> GetMissingCardsForSet(CardGame game, string setCode)
    {
        _logger.LogDebug("Getting missing cards for {Game} set {SetCode}", game, setCode);

        using var context = _omniDbContextFactory.CreateDbContext();
        var ownedNumbers = context.Lots
            .AsNoTracking()
            .Where(l => l.Product.Category == ProductCategory.Single && l.Product.Game == game && l.Product.SetCode == setCode)
            .Select(l => l.Product.CollectorNumber ?? "")
            .Distinct()
            .ToList();

        return _gameServices[game].GetMissingCards(setCode, ownedNumbers);
    }

    private static void ConvertToJpeg(string sourcePath, string destPath, int quality)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(sourcePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }
}
