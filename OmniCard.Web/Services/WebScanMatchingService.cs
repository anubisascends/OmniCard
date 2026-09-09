using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.CardMatching.Games;

namespace OmniCard.Web.Services;

/// <summary>
/// Server-side card-image matching for the web app. This is the port of the desktop
/// <c>CardService.AddFromStream</c> pipeline (pHash + MTG art hash + foil edge hash + per-game OCR
/// refinement + 180° rotation retry) with the desktop coupling (ScannedCards, temp files,
/// diagnostics, background task hand-off) stripped out — the OCR stages run inline here.
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

    // The per-game ICardGameService instances are singletons that each share ONE read-only DbContext
    // (their `_readContext`), which EF Core forbids using concurrently. The SPA uploads a whole scan
    // batch at once, so without this gate concurrent requests race that shared context into
    // "a second operation was started on this context instance" 500s. Serialize just the catalog
    // match — it's fast (in-memory hash caches + a few small queries); the expensive OCR stays
    // parallel, so batch throughput is barely affected.
    private readonly SemaphoreSlim _matchGate = new(1, 1);

    // The user's "Sets (art fallback)" selection is a HARD filter on the candidate pool — a scan that
    // doesn't resolve inside those sets gets no match. The one exception: OCR that reads the card's
    // printed identity (set code + collector number) at or above this confidence is trusted as ground
    // truth and is allowed to resolve to a printing OUTSIDE the chosen sets. Below it, the set filter
    // still binds, so an uncertain read can't drag in a wrong-set match.
    private const double OcrSetOverrideConfidence = 0.95;

    /// <summary>The effective set constraint for an OCR lookup: the user's chosen sets normally, but
    /// unconstrained (null) once the OCR read is confident enough (<see cref="OcrSetOverrideConfidence"/>)
    /// to override the "Sets (art fallback)" filter.</summary>
    private static IReadOnlySet<string>? EffectiveFilter(IReadOnlySet<string>? setFilter, double ocrConfidence)
        => ocrConfidence >= OcrSetOverrideConfidence ? null : setFilter;

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
    /// <param name="setCodes">Optional set(s) to constrain matching to — the user tells us which set(s)
    /// they're scanning, which bounds the pHash/artwork fallback (and every other match path) to their
    /// union. Empty/null ⇒ no set constraint.</param>
    public async Task<ScanMatchDto> MatchAsync(byte[] imageBytes, CardGame game, bool isFoil, IReadOnlyCollection<string>? setCodes = null, CancellationToken ct = default)
    {
        if (!_gameServices.TryGetValue(game, out var gameService))
            return new ScanMatchDto { Matched = false, Game = game.ToString(), Error = $"Game {game} is not available" };

        EnsureSymbolHashes();

        // User-chosen set filter (hard constraint on the candidate pool, all match paths).
        var chosenSets = setCodes?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? [];
        IReadOnlySet<string>? setFilter = chosenSets.Length == 0
            ? null
            : new HashSet<string>(chosenSets, StringComparer.OrdinalIgnoreCase);

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
        var match = await FindMatchAsync(() => gameService.FindClosestMatch(hash, artHashes, null, setFilter, detectedSets, scanEdgeHash: edgeHash));

        // 6. OCR refinement — for MTG the printed (set, collector) is ground truth and overrides even
        //    a confident pHash guess; the other games use the collector number to pin the printing.
        match = await RefineWithOcrAsync(imageBytes, game, gameService, hash, artHashes, edgeHash, detectedSets, setFilter, match);

        // 7. If still nothing, retry rotated 180° (cards are often fed upside down).
        if (match is null)
            (match, hash) = await RetryRotatedAsync(imageBytes, game, gameService, isFoil, setFilter, hash);

        _logger.LogInformation(
            match is null ? "Scan produced no match for {Game} (pHash {Hash:X16})"
                          : "Scan matched \"{Name}\" ({Set} #{Num}) for {Game}",
            match?.Name, match?.SetCode, match?.CollectorNumber, game, hash);

        return ToDto(match, game, hash);
    }

    /// <summary>Run one catalog match under the shared-context gate (see <see cref="_matchGate"/>).</summary>
    private async Task<CardMatch?> FindMatchAsync(Func<CardMatch?> find)
    {
        await _matchGate.WaitAsync();
        try { return find(); }
        finally { _matchGate.Release(); }
    }

    private async Task<CardMatch?> RefineWithOcrAsync(
        byte[] imageBytes, CardGame game, ICardGameService gameService, ulong hash,
        ulong[]? artHashes, ulong? edgeHash, IReadOnlySet<string>? detectedSets,
        IReadOnlySet<string>? setFilter, CardMatch? current)
    {
        try
        {
            switch (game)
            {
                case CardGame.OnePiece:
                    {
                        var (cn, conf) = await _ocrService.DetectOptcgCollectorNumberAsync(imageBytes);
                        return await ApplyCollectorOcrAsync(gameService, hash, artHashes, edgeHash, setFilter, cn, conf, current);
                    }
                case CardGame.Riftbound:
                    return await RefineRiftboundAsync(imageBytes, gameService, hash, edgeHash, setFilter, current);
                case CardGame.Pokemon or CardGame.YuGiOh or CardGame.FinalFantasy:
                    {
                        var spec = game switch
                        {
                            CardGame.Pokemon => PokemonService.OcrSpec,
                            CardGame.YuGiOh => YugiohService.OcrSpec,
                            _ => FinalFantasyService.OcrSpec,
                        };
                        var (cn, conf) = await _ocrService.DetectCollectorNumberAsync(imageBytes, spec);
                        return await ApplyCollectorOcrAsync(gameService, hash, artHashes, edgeHash, setFilter, cn, conf, current);
                    }
                default: // MTG
                    {
                        // Ground truth: bottom-left (set, collector) uniquely identifies a Scryfall printing.
                        var (ocrSet, ocrNumber, conf) = await _ocrService.DetectMtgSetAndNumberAsync(imageBytes);
                        if (ocrSet is not null && ocrNumber is not null && conf >= 0.5)
                        {
                            var gt = new OcrMatchResult { SetCode = ocrSet, CollectorNumber = ocrNumber, CollectorNumberConfidence = conf };
                            // A very confident printed (set, collector) read overrides the set filter (see
                            // EffectiveFilter); a weaker read stays bound to the user's chosen sets.
                            var gtMatch = await FindMatchAsync(() => gameService.FindClosestMatch(hash, artHashes, gt, EffectiveFilter(setFilter, conf), detectedSets, scanEdgeHash: edgeHash));
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
                            var ocrMatch = await FindMatchAsync(() => gameService.FindClosestMatch(hash, artHashes, ocr, setFilter, preferred, scanEdgeHash: edgeHash));
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

    private async Task<CardMatch?> ApplyCollectorOcrAsync(
        ICardGameService gameService, ulong hash, ulong[]? artHashes, ulong? edgeHash,
        IReadOnlySet<string>? setFilter, string? collectorNumber, double conf, CardMatch? current)
    {
        if (collectorNumber is null || conf < 0.5)
            return current;
        var ocr = new OcrMatchResult { CollectorNumber = collectorNumber, CollectorNumberConfidence = conf };
        // A very confident collector-number read overrides the set filter (see EffectiveFilter).
        var ocrMatch = await FindMatchAsync(() => gameService.FindClosestMatch(hash, artHashes, ocr, EffectiveFilter(setFilter, conf), null, scanEdgeHash: edgeHash));
        return ocrMatch is not null && (current is null || ocrMatch.GameSpecificId != current.GameSpecificId)
            ? ocrMatch
            : current;
    }

    // Riftbound orientations to try, in order. The printed collector line ("{SET} • {n}/{total}",
    // lower-left) uniquely identifies the printing and is the source of truth, so we lead with it.
    // Portrait cards (Units/Spells/Legends) read at 0°; landscape Battlefield cards are fed sideways,
    // so we then try both 90° rotations (the collector line only lands in the lower-left crop region
    // under the correct one) and finally 180° for upside-down feeds.
    private static readonly (System.Drawing.RotateFlipType Rot, string Label)[] RiftboundOrientations =
    [
        (System.Drawing.RotateFlipType.RotateNoneFlipNone, "0"),
        (System.Drawing.RotateFlipType.Rotate90FlipNone, "90cw"),
        (System.Drawing.RotateFlipType.Rotate270FlipNone, "90ccw"),
        (System.Drawing.RotateFlipType.Rotate180FlipNone, "180"),
    ];

    /// <summary>
    /// Resolve a Riftbound scan OCR-first: read the printed set code + collector number across
    /// orientations and match the exact printing. This is what recognizes and auto-rotates landscape
    /// Battlefield cards (fed sideways) without any UI — the orientation under which the collector line
    /// reads is the card's true rotation. Falls back to <paramref name="current"/> (the pHash guess)
    /// only when the line can't be read in any orientation.
    /// </summary>
    private async Task<CardMatch?> RefineRiftboundAsync(
        byte[] imageBytes, ICardGameService gameService, ulong hash, ulong? edgeHash,
        IReadOnlySet<string>? setFilter, CardMatch? current)
    {
        // Best landscape-artwork match found across rotated orientations, used as the Battlefield
        // fallback when OCR can't read the (small, often holofoil) collector line at any rotation.
        CardMatch? bestLandscapeArt = null;

        foreach (var (rot, label) in RiftboundOrientations)
        {
            var atZero = rot == System.Drawing.RotateFlipType.RotateNoneFlipNone;
            var bytes = atZero ? imageBytes : RotateImage(imageBytes, rot);
            var rotHash = atZero ? hash : _hashService.ComputeHash(new MemoryStream(bytes));

            // OCR is the source of truth: feed the printed collector number to Phase 0 (exact
            // set+number lookup, confidence 100). A confident read wins outright, in any orientation.
            var (cn, conf) = await _ocrService.DetectRiftboundCollectorNumberAsync(bytes);
            if (cn is not null && conf >= 0.5)
            {
                var ocr = new OcrMatchResult { CollectorNumber = cn, CollectorNumberConfidence = conf };
                // A very confident printed collector-line read overrides the set filter (see EffectiveFilter).
                var match = await FindMatchAsync(() => gameService.FindClosestMatch(rotHash, null, ocr, EffectiveFilter(setFilter, conf), null, scanEdgeHash: atZero ? edgeHash : null));
                if (match is not null)
                {
                    if (!atZero)
                        _logger.LogInformation(
                            "Riftbound scan matched after {Rot} rotation (\"{Name}\" {Set} #{Num}) — landscape Battlefield card fed sideways",
                            label, match.Name, match.SetCode, match.CollectorNumber);
                    return match;
                }
            }

            // Battlefield artwork fallback: if the collector line is unreadable, still try to match the
            // landscape ART once rotated upright. Only rotated orientations (0° is the step-5 `current`),
            // and only ACCEPT a landscape/Battlefield card — a portrait card must never be matched from a
            // rotated scan. pHash distance is minimized at the card's true orientation, so keeping the
            // highest-confidence landscape hit picks the correct rotation (incl. a 180°-flipped feed).
            if (!atZero)
            {
                var art = await FindMatchAsync(() => gameService.FindClosestMatch(rotHash, null, null, setFilter, null, scanEdgeHash: null));
                if (art is not null
                    && (art.Source as RiftboundCard)?.Orientation == "landscape"
                    && (bestLandscapeArt is null || (art.Confidence ?? 0) > (bestLandscapeArt.Confidence ?? 0)))
                    bestLandscapeArt = art;
            }
        }

        // Trust a strong existing portrait match; otherwise fall back to the best rotated Battlefield art.
        if (bestLandscapeArt is not null && (current is null || (current.Confidence ?? 0) < 50))
        {
            _logger.LogInformation(
                "Riftbound scan matched by rotated Battlefield artwork (\"{Name}\" {Set} #{Num}, conf {Conf:F0}) — collector line unreadable",
                bestLandscapeArt.Name, bestLandscapeArt.SetCode, bestLandscapeArt.CollectorNumber, bestLandscapeArt.Confidence ?? 0);
            return bestLandscapeArt;
        }
        return current;
    }

    /// <summary>Render a downscaled JPEG <c>data:</c> URI so the SPA can preview an uploaded scan whose
    /// own format a browser can't display in an <c>&lt;img&gt;</c> (TIFF). GDI+ decodes the source, so
    /// this works for any format <see cref="System.Drawing.Bitmap"/> reads. Returns null on failure —
    /// the caller falls back to a placeholder; matching itself is unaffected.</summary>
    public static string? RenderPreviewDataUri(byte[] imageBytes, int maxDim = 1400)
    {
        try
        {
            using var src = new System.Drawing.Bitmap(new MemoryStream(imageBytes));
            var scale = Math.Min(1.0, (double)maxDim / Math.Max(src.Width, src.Height));
            var w = Math.Max(1, (int)Math.Round(src.Width * scale));
            var h = Math.Max(1, (int)Math.Round(src.Height * scale));
            using var dst = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            var encoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            using var ep = new System.Drawing.Imaging.EncoderParameters(1);
            ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, 85L);
            using var ms = new MemoryStream();
            dst.Save(ms, encoder, ep);
            return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    // Rotate/flip an encoded image and re-encode as PNG. Used to try alternate scan orientations.
    private static byte[] RotateImage(byte[] imageBytes, System.Drawing.RotateFlipType rot)
    {
        using var bmp = new System.Drawing.Bitmap(new MemoryStream(imageBytes));
        bmp.RotateFlip(rot);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private async Task<(CardMatch? Match, ulong Hash)> RetryRotatedAsync(
        byte[] imageBytes, CardGame game, ICardGameService gameService, bool isFoil,
        IReadOnlySet<string>? setFilter, ulong originalHash)
    {
        try
        {
            var rotatedBytes = RotateImage(imageBytes, System.Drawing.RotateFlipType.Rotate180FlipNone);
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

            var match = await FindMatchAsync(() => gameService.FindClosestMatch(rotatedHash, null, ocr, setFilter, null, scanEdgeHash: rotatedEdge));
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
