using OmniCard.Api.Contracts;
using OmniCard.CardMatching;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Services;

/// <summary>
/// Server-side card-image matching for the web app. This is the port of the desktop
/// <c>CardService.AddFromStream</c> pipeline (pHash + MTG art hash + foil edge hash + per-game OCR
/// refinement + 180° rotation retry) with the WPF coupling (Dispatcher, ScannedCards, temp files,
/// diagnostics) stripped out. Because there is no UI message pump to avoid deadlocking, the OCR
/// stages that the desktop defers to a background <c>Dispatcher.BeginInvoke</c> run inline here.
///
/// Matching stays single-game — the caller picks the game (the desktop's "never fall back across
/// games" rule). All catalog reads go through the already-registered read-only game services.
/// </summary>
public sealed class WebScanMatchingService
{
    private readonly IPerceptualHashService _hashService;
    private readonly IOcrMatchingService _ocrService;
    private readonly Dictionary<CardGame, ICardGameService> _gameServices;
    private readonly ILogger<WebScanMatchingService> _logger;
    private readonly object _symbolLock = new();
    private bool _symbolsLoaded;

    public WebScanMatchingService(
        IPerceptualHashService hashService,
        IOcrMatchingService ocrService,
        IEnumerable<ICardGameService> gameServices,
        ILogger<WebScanMatchingService> logger)
    {
        _hashService = hashService;
        _ocrService = ocrService;
        _gameServices = gameServices.ToDictionary(s => s.Game);
        _logger = logger;
    }

    /// <summary>Match a single uploaded card image against <paramref name="game"/>'s catalog.</summary>
    public async Task<ScanMatchDto> MatchAsync(byte[] imageBytes, CardGame game, bool isFoil, CancellationToken ct = default)
    {
        if (!_gameServices.TryGetValue(game, out var gameService))
            return new ScanMatchDto { Matched = false, Game = game.ToString(), Error = $"Game {game} is not available" };

        EnsureSymbolHashes();

        // 1. pHash from the full image.
        ulong hash = _hashService.ComputeHash(new MemoryStream(imageBytes));

        // 2. Art-region hashes (MTG only — its art crop is stable enough to disambiguate reprints).
        ulong[]? artHashes = game == CardGame.Mtg
            ? _hashService.ComputeArtHash(new MemoryStream(imageBytes), ScryfallService.ArtCropRegions)
            : null;

        // 3. Foil edge hash — a holographic color shift corrupts the luminance pHash, so foils of the
        //    color-shifting games get a color-robust edge hash for matching to fall back on.
        ulong? edgeHash = isFoil && IsEdgeHashGame(game)
            ? _hashService.ComputeEdgeHash(new MemoryStream(imageBytes))
            : null;

        // 4. MTG set-symbol detection — a soft set preference to break ties among reprints.
        IReadOnlySet<string>? detectedSets = null;
        if (game == CardGame.Mtg)
        {
            var (symbolSets, symbolConf) = _ocrService.DetectSetSymbol(imageBytes);
            if (symbolConf >= 0.5 && symbolSets.Count > 0)
                detectedSets = new HashSet<string>(symbolSets, StringComparer.OrdinalIgnoreCase);
        }

        // 5. Initial pHash/art/edge match.
        var match = gameService.FindClosestMatch(hash, artHashes, null, null, detectedSets, scanEdgeHash: edgeHash);

        // 6. OCR refinement — for MTG the printed (set, collector) is ground truth and overrides even
        //    a confident pHash guess; the other games use the collector number to pin the printing.
        match = await RefineWithOcrAsync(imageBytes, game, gameService, hash, artHashes, edgeHash, detectedSets, match);

        // 7. If still nothing, retry rotated 180° (cards are often fed upside down).
        if (match is null)
            (match, hash) = await RetryRotatedAsync(imageBytes, game, gameService, isFoil, hash);

        _logger.LogInformation(
            match is null ? "Scan produced no match for {Game} (pHash {Hash:X16})"
                          : "Scan matched \"{Name}\" ({Set} #{Num}) for {Game}",
            match?.Name, match?.SetCode, match?.CollectorNumber, game, hash);

        return ToDto(match, game, hash);
    }

    private async Task<CardMatch?> RefineWithOcrAsync(
        byte[] imageBytes, CardGame game, ICardGameService gameService, ulong hash,
        ulong[]? artHashes, ulong? edgeHash, IReadOnlySet<string>? detectedSets, CardMatch? current)
    {
        try
        {
            switch (game)
            {
                case CardGame.OnePiece:
                {
                    var (cn, conf) = await _ocrService.DetectOptcgCollectorNumberAsync(imageBytes);
                    return ApplyCollectorOcr(gameService, hash, artHashes, edgeHash, cn, conf, current);
                }
                case CardGame.Riftbound:
                {
                    var (cn, conf) = await _ocrService.DetectRiftboundCollectorNumberAsync(imageBytes);
                    return ApplyCollectorOcr(gameService, hash, artHashes, edgeHash, cn, conf, current);
                }
                case CardGame.Pokemon or CardGame.YuGiOh or CardGame.FinalFantasy:
                {
                    var spec = game switch
                    {
                        CardGame.Pokemon => PokemonService.OcrSpec,
                        CardGame.YuGiOh => YugiohService.OcrSpec,
                        _ => FinalFantasyService.OcrSpec,
                    };
                    var (cn, conf) = await _ocrService.DetectCollectorNumberAsync(imageBytes, spec);
                    return ApplyCollectorOcr(gameService, hash, artHashes, edgeHash, cn, conf, current);
                }
                default: // MTG
                {
                    // Ground truth: bottom-left (set, collector) uniquely identifies a Scryfall printing.
                    var (ocrSet, ocrNumber, conf) = await _ocrService.DetectMtgSetAndNumberAsync(imageBytes);
                    if (ocrSet is not null && ocrNumber is not null && conf >= 0.5)
                    {
                        var gt = new OcrMatchResult { SetCode = ocrSet, CollectorNumber = ocrNumber, CollectorNumberConfidence = conf };
                        var gtMatch = gameService.FindClosestMatch(hash, artHashes, gt, null, detectedSets, scanEdgeHash: edgeHash);
                        if (gtMatch is not null)
                            return gtMatch;
                    }

                    // Fallback: name + set-symbol recognition, with pHash still primary.
                    var ocr = await _ocrService.AnalyzeCardAsync(imageBytes);
                    if (ocr?.RecognizedName is not null)
                    {
                        var preferred = detectedSets is null ? null : new HashSet<string>(detectedSets, StringComparer.OrdinalIgnoreCase);
                        if (ocr.SymbolConfidence >= 0.5 && ocr.CandidateSetCodes is { Count: > 0 })
                        {
                            preferred ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var code in ocr.CandidateSetCodes)
                                preferred.Add(code);
                        }
                        var ocrMatch = gameService.FindClosestMatch(hash, artHashes, ocr, null, preferred, scanEdgeHash: edgeHash);
                        if (ocrMatch is not null && (current is null || ocrMatch.GameSpecificId != current.GameSpecificId))
                            return ocrMatch;
                    }
                    return current;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR refinement failed for {Game}", game);
            return current;
        }
    }

    private static CardMatch? ApplyCollectorOcr(
        ICardGameService gameService, ulong hash, ulong[]? artHashes, ulong? edgeHash,
        string? collectorNumber, double conf, CardMatch? current)
    {
        if (collectorNumber is null || conf < 0.5)
            return current;
        var ocr = new OcrMatchResult { CollectorNumber = collectorNumber, CollectorNumberConfidence = conf };
        var ocrMatch = gameService.FindClosestMatch(hash, artHashes, ocr, null, null, scanEdgeHash: edgeHash);
        return ocrMatch is not null && (current is null || ocrMatch.GameSpecificId != current.GameSpecificId)
            ? ocrMatch
            : current;
    }

    private async Task<(CardMatch? Match, ulong Hash)> RetryRotatedAsync(
        byte[] imageBytes, CardGame game, ICardGameService gameService, bool isFoil, ulong originalHash)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(new MemoryStream(imageBytes));
            bmp.RotateFlip(System.Drawing.RotateFlipType.Rotate180FlipNone);
            using var rotated = new MemoryStream();
            bmp.Save(rotated, System.Drawing.Imaging.ImageFormat.Png);
            var rotatedBytes = rotated.ToArray();

            ulong rotatedHash = _hashService.ComputeHash(new MemoryStream(rotatedBytes));

            OcrMatchResult? ocr = null;
            switch (game)
            {
                case CardGame.OnePiece:
                {
                    var (cn, conf) = await _ocrService.DetectOptcgCollectorNumberAsync(rotatedBytes);
                    if (cn is not null && conf >= 0.5) ocr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = conf };
                    break;
                }
                case CardGame.Riftbound:
                {
                    var (cn, conf) = await _ocrService.DetectRiftboundCollectorNumberAsync(rotatedBytes);
                    if (cn is not null && conf >= 0.5) ocr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = conf };
                    break;
                }
                case CardGame.Pokemon or CardGame.YuGiOh or CardGame.FinalFantasy:
                {
                    var spec = game switch
                    {
                        CardGame.Pokemon => PokemonService.OcrSpec,
                        CardGame.YuGiOh => YugiohService.OcrSpec,
                        _ => FinalFantasyService.OcrSpec,
                    };
                    var (cn, conf) = await _ocrService.DetectCollectorNumberAsync(rotatedBytes, spec);
                    if (cn is not null && conf >= 0.5) ocr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = conf };
                    break;
                }
            }

            ulong? rotatedEdge = isFoil && IsEdgeHashGame(game)
                ? _hashService.ComputeEdgeHash(new MemoryStream(rotatedBytes))
                : null;

            var match = gameService.FindClosestMatch(rotatedHash, null, ocr, null, null, scanEdgeHash: rotatedEdge);
            return match is not null ? (match, rotatedHash) : (null, originalHash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rotated-retry match failed for {Game}", game);
            return (null, originalHash);
        }
    }

    private static bool IsEdgeHashGame(CardGame game) =>
        game is CardGame.OnePiece or CardGame.Riftbound or CardGame.Pokemon or CardGame.YuGiOh or CardGame.FinalFantasy;

    /// <summary>Lazily loads the MTG set-symbol hashes into the OCR service (needed for symbol
    /// detection). Cheap no-op after the first call.</summary>
    private void EnsureSymbolHashes()
    {
        if (_symbolsLoaded)
            return;
        lock (_symbolLock)
        {
            if (_symbolsLoaded)
                return;
            try
            {
                if (_ocrService.SymbolHashes.Count == 0 &&
                    _gameServices.TryGetValue(CardGame.Mtg, out var mtg) && mtg is ScryfallService scryfall)
                {
                    _ocrService.SymbolHashes = scryfall.GetSymbolHashes();
                    _logger.LogInformation("Loaded {Count} MTG symbol hashes into OCR service", _ocrService.SymbolHashes.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load MTG symbol hashes");
            }
            _symbolsLoaded = true;
        }
    }

    internal static ScanMatchDto ToDto(CardMatch? match, CardGame game, ulong hash) => new()
    {
        Matched = match is not null,
        Game = game.ToString(),
        GameCardId = match?.GameSpecificId,
        Name = match?.Name,
        SetName = match?.SetName,
        SetCode = match?.SetCode,
        CollectorNumber = match?.CollectorNumber,
        Rarity = match?.Rarity,
        ImageUri = match?.ImageUri,
        Confidence = match?.Confidence,
        ScanHash = hash.ToString(),
    };
}
