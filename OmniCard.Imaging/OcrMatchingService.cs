using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;
using Tesseract;

namespace OmniCard.Imaging;

public sealed class OcrMatchingService : IOcrMatchingService, IDisposable
{
    private readonly IPerceptualHashService _hashService;
    private readonly ILogger<OcrMatchingService> _logger;

    // Tesseract engines are not thread-safe and are expensive to construct, so we keep a
    // small pool that grows to the actual OCR concurrency. OCR runs off the UI thread
    // (Task.Run below) because the TWAIN message pump owns the UI thread; a pool lets
    // multiple scanned cards OCR in parallel without sharing an engine.
    private readonly ConcurrentBag<TesseractEngine> _enginePool = [];
    private readonly string _tessdataPath;
    private readonly bool _ocrAvailable;

    // Restrict OCR to the characters that appear in an OPTCG collector number (e.g. "OP15-043").
    // A whitelist massively reduces misreads (0→O, 1→I, etc.) feeding the pattern regex below.
    private const string CollectorNumberWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-";

    // Collector-number pattern: 2-4 letters + 2 digits + dash + 2-3 digits (e.g. OP15-043, EB01-021).
    // Compiled once and reused — this runs on the OCR hot path (once per scanned One Piece card).
    private static readonly System.Text.RegularExpressions.Regex CollectorNumberPattern =
        new(@"([A-Za-z]{2,4}\d{2})\s*[-—]\s*(\d{2,3})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Compiled-regex cache for per-game OcrCollectorSpec patterns (Pokémon, Yu-Gi-Oh!, FFTCG, etc.).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.RegularExpressions.Regex> _specRegexCache = new();

    // Name crop regions as percentage of card image: (X%, Y%, Width%, Height%)
    internal static readonly (double X, double Y, double W, double H)[] NameCropRegions =
    [
        (0.07, 0.03, 0.75, 0.07), // Modern frame (post-2003)
        (0.05, 0.02, 0.80, 0.08), // Borderless / full art
        (0.10, 0.05, 0.70, 0.07), // Retro (pre-8th edition)
    ];

    // Set symbol crop region (MTG)
    internal static readonly (double X, double Y, double W, double H) SymbolCropRegion =
        (0.82, 0.43, 0.12, 0.07);

    // OPTCG collector number crop region — bottom-right of the card (e.g., "OP15-043").
    // Kept to the right of center so it isolates the collector number and excludes the
    // centered subtype banner (e.g., "Straw Hat Crew") that shares the same row; a wider
    // region caused OCR to read the subtype instead and never match the number pattern.
    internal static readonly (double X, double Y, double W, double H) OptcgCollectorNumberRegion =
        (0.68, 0.925, 0.24, 0.055);

    // Riftbound collector line — lower-LEFT: "{SET} • {n}/{total}" (e.g. "UNL • 150/219").
    // Portrait cards (Units/Spells/Legends) vs landscape cards (Battlefields) place it
    // differently, so we pick a region by the scanned card's aspect ratio.
    internal static readonly (double X, double Y, double W, double H) RiftboundPortraitRegion =
        (0.02, 0.945, 0.40, 0.05);
    internal static readonly (double X, double Y, double W, double H) RiftboundLandscapeRegion =
        (0.02, 0.93, 0.30, 0.06);

    // Restrict OCR to characters that appear in a Riftbound collector line.
    private const string RiftboundWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789•·./- ";

    // "{SET} [sep] {collector}/{total}". Captures set code + collector number; the /total is
    // matched only to anchor the pattern and is discarded.
    private static readonly System.Text.RegularExpressions.Regex RiftboundPattern =
        new(@"([A-Za-z]{2,4})\s*[•·.\-]{0,2}\s*(\d{1,3})\s*/\s*\d{1,3}",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public Dictionary<string, ulong> SymbolHashes { get; set; } = [];

    public OcrMatchingService(IPerceptualHashService hashService, ILogger<OcrMatchingService> logger)
    {
        _hashService = hashService;
        _logger = logger;

        _tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

        // Validate the engine can be constructed (native libs + language data present).
        // Mirror the previous behaviour: if OCR is unavailable, log a warning and degrade
        // gracefully — scanning still works via perceptual-hash matching.
        try
        {
            if (!File.Exists(Path.Combine(_tessdataPath, "eng.traineddata")))
            {
                _logger.LogWarning("Tesseract language data not found at {Path} — OCR matching disabled", _tessdataPath);
            }
            else
            {
                // Construct one engine up front both to validate and to prime the pool.
                _enginePool.Add(CreateEngine());
                _ocrAvailable = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Tesseract OCR engine — OCR matching disabled");
        }
    }

    private TesseractEngine CreateEngine() => new(_tessdataPath, "eng", EngineMode.Default);

    private TesseractEngine RentEngine() => _enginePool.TryTake(out var engine) ? engine : CreateEngine();

    private void ReturnEngine(TesseractEngine engine) => _enginePool.Add(engine);

    public Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData) => Task.Run(() => AnalyzeCard(imageData));

    private OcrMatchResult AnalyzeCard(byte[] imageData)
    {
        string? bestName = null;
        double bestConfidence = 0;
        var candidateSetCodes = new List<string>();
        double symbolConfidence = 0;

        if (SymbolHashes.Count == 0)
            _logger.LogWarning("AnalyzeCardAsync: SymbolHashes is empty — symbol detection will be skipped");

        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            var width = bitmap.Width;
            var height = bitmap.Height;

            // OCR card name — try multiple crop regions
            if (_ocrAvailable)
            {
                foreach (var region in NameCropRegions)
                {
                    var rect = ToPixelRect(region, width, height);
                    if (rect.Width < 10 || rect.Height < 5) continue;

                    var (text, confidence) = OcrCroppedRegion(bitmap, rect, PageSegMode.SingleLine, whitelist: null);
                    if (confidence > bestConfidence && !string.IsNullOrWhiteSpace(text))
                    {
                        bestName = text.Trim();
                        bestConfidence = confidence;
                    }
                }
            }

            // Set symbol pHash comparison
            if (SymbolHashes.Count > 0)
            {
                var symbolRect = ToPixelRect(SymbolCropRegion, width, height);
                if (symbolRect.Width >= 5 && symbolRect.Height >= 5)
                {
                    var (codes, conf) = MatchSymbol(bitmap, symbolRect);
                    candidateSetCodes = codes;
                    symbolConfidence = conf;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR analysis failed");
        }

        return new OcrMatchResult
        {
            RecognizedName = bestName,
            NameConfidence = bestConfidence,
            CandidateSetCodes = candidateSetCodes,
            SymbolConfidence = symbolConfidence,
        };
    }

    public (List<string> SetCodes, double Confidence) DetectSetSymbol(byte[] imageData)
    {
        if (SymbolHashes.Count == 0)
        {
            _logger.LogWarning("DetectSetSymbol called with empty SymbolHashes dictionary — no set detection possible");
            return ([], 0);
        }

        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            var symbolRect = ToPixelRect(SymbolCropRegion, bitmap.Width, bitmap.Height);
            if (symbolRect.Width < 5 || symbolRect.Height < 5)
                return ([], 0);

            return MatchSymbol(bitmap, symbolRect);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Symbol detection failed");
            return ([], 0);
        }
    }

    internal static Rectangle ToPixelRect((double X, double Y, double W, double H) pct, int imgWidth, int imgHeight)
    {
        var x = (int)(pct.X * imgWidth);
        var y = (int)(pct.Y * imgHeight);
        var w = Math.Min((int)(pct.W * imgWidth), imgWidth - x);
        var h = Math.Min((int)(pct.H * imgHeight), imgHeight - y);
        return new Rectangle(x, y, w, h);
    }

    private (string Text, double Confidence) OcrCroppedRegion(Bitmap source, Rectangle cropRect, PageSegMode psm, string? whitelist)
    {
        // Crop
        using var cropped = source.Clone(cropRect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        // Upscale if too small (OCR works better with larger text)
        Bitmap toOcr = cropped;
        bool needsDispose = false;
        if (cropped.Width < 200)
        {
            var scale = 200.0 / cropped.Width;
            var newWidth = (int)(cropped.Width * scale);
            var newHeight = (int)(cropped.Height * scale);
            toOcr = new Bitmap(newWidth, newHeight);
            needsDispose = true;
            using var g = Graphics.FromImage(toOcr);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(cropped, 0, 0, newWidth, newHeight);
        }

        try { return RunOcr(toOcr, psm, whitelist); }
        finally { if (needsDispose) toOcr.Dispose(); }
    }

    // Runs Tesseract on an already-prepared bitmap. Extracted so the plain crop path and the
    // binarized collector-number path share one engine-pool + whitelist discipline.
    private (string Text, double Confidence) RunOcr(Bitmap toOcr, PageSegMode psm, string? whitelist)
    {
        var engine = RentEngine();
        try
        {
            // Whitelist is per-recognition state on the shared engine; set it for this call
            // and clear it afterward so a pooled engine doesn't leak the restriction.
            engine.SetVariable("tessedit_char_whitelist", whitelist ?? string.Empty);

            using var ms = new MemoryStream();
            toOcr.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            using var pix = Pix.LoadFromMemory(ms.ToArray());
            using var page = engine.Process(pix, psm);

            var text = page.GetText() ?? string.Empty;
            var confidence = string.IsNullOrWhiteSpace(text) ? 0.0 : page.GetMeanConfidence();
            return (text.Trim(), confidence);
        }
        finally
        {
            engine.SetVariable("tessedit_char_whitelist", string.Empty);
            ReturnEngine(engine);
        }
    }

    // Upscale a crop to targetW and convert to high-contrast grayscale. Small holofoil set-code
    // text (Yu-Gi-Oh!) reads far better enlarged and desaturated than at native size in colour.
    private static Bitmap UpscaleGray(Bitmap crop, int targetW, float contrast)
    {
        var scale = (double)targetW / crop.Width;
        int nw = targetW, nh = Math.Max(1, (int)(crop.Height * scale));
        var outBmp = new Bitmap(nw, nh);
        using var g = Graphics.FromImage(outBmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        float t = 0.5f - 0.5f * contrast;
        var cm = new System.Drawing.Imaging.ColorMatrix(
        [
            [0.299f * contrast, 0.299f * contrast, 0.299f * contrast, 0, 0],
            [0.587f * contrast, 0.587f * contrast, 0.587f * contrast, 0, 0],
            [0.114f * contrast, 0.114f * contrast, 0.114f * contrast, 0, 0],
            [0, 0, 0, 1, 0],
            [t, t, t, 0, 1],
        ]);
        using var ia = new System.Drawing.Imaging.ImageAttributes();
        ia.SetColorMatrix(cm);
        g.DrawImage(crop, new Rectangle(0, 0, nw, nh), 0, 0, crop.Width, crop.Height, GraphicsUnit.Pixel, ia);
        return outBmp;
    }

    // Upscale + grayscale + Otsu threshold to a clean black-on-white binary, auto-picking polarity
    // so the code reads whether it's dark-on-light or light-on-dark against the card border.
    private static Bitmap BinarizeOtsu(Bitmap crop, int targetW)
    {
        using var gray = UpscaleGray(crop, targetW, 1.0f);
        int w = gray.Width, h = gray.Height, total = w * h;
        var lum = new byte[total];
        var hist = new int[256];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = gray.GetPixel(x, y);
                int l = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                lum[y * w + x] = (byte)l; hist[l]++;
            }

        double sum = 0; for (int i = 0; i < 256; i++) sum += i * hist[i];
        double sumB = 0; int wB = 0; double maxVar = 0; int thr = 127;
        for (int i = 0; i < 256; i++)
        {
            wB += hist[i]; if (wB == 0) continue;
            int wF = total - wB; if (wF == 0) break;
            sumB += i * hist[i];
            double mB = sumB / wB, mF = (sum - sumB) / wF;
            double between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > maxVar) { maxVar = between; thr = i; }
        }

        int dark = 0; for (int i = 0; i < total; i++) if (lum[i] <= thr) dark++;
        bool textIsDark = dark <= total / 2; // ink = the minority tone

        var outBmp = new Bitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool isDark = lum[y * w + x] <= thr;
                bool ink = textIsDark ? isDark : !isDark;
                outBmp.SetPixel(x, y, ink ? Color.Black : Color.White);
            }
        return outBmp;
    }

    public Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] imageData)
        => Task.Run(() => DetectOptcgCollectorNumber(imageData));

    private (string? CollectorNumber, double Confidence) DetectOptcgCollectorNumber(byte[] imageData)
    {
        if (!_ocrAvailable)
            return (null, 0);

        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            var rect = ToPixelRect(OptcgCollectorNumberRegion, bitmap.Width, bitmap.Height);
            if (rect.Width < 10 || rect.Height < 5)
                return (null, 0);

            var (text, confidence) = OcrCroppedRegion(bitmap, rect, PageSegMode.SingleLine, CollectorNumberWhitelist);
            if (string.IsNullOrWhiteSpace(text))
                return (null, 0);

            // Extract collector number (e.g. OP15-043, EB01-021, ST01-001) using the shared pattern.
            var match = CollectorNumberPattern.Match(text);

            if (match.Success)
            {
                var collectorNumber = $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}";
                // A successful structured match is strong evidence on its own; floor the
                // confidence so it always clears the downstream lookup gate, but never
                // report below Tesseract's actual reading confidence.
                var reportedConfidence = Math.Max(0.9, confidence);
                _logger.LogInformation("OPTCG collector number detected: {Number} (raw: {Raw}, ocrConf: {Conf:F2})",
                    collectorNumber, text, confidence);
                return (collectorNumber, reportedConfidence);
            }

            _logger.LogDebug("OPTCG collector number OCR text did not match pattern: {Text}", text);
            return (null, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OPTCG collector number detection failed");
            return (null, 0);
        }
    }

    // Extracts "{SET}-{collector}" from an OCR'd Riftbound collector line, or false if no match.
    internal static bool TryExtractRiftboundNumber(string ocrText, out string? formatted)
    {
        formatted = null;
        if (string.IsNullOrWhiteSpace(ocrText)) return false;
        var m = RiftboundPattern.Match(ocrText);
        if (!m.Success) return false;
        formatted = $"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}";
        return true;
    }

    public Task<(string? CollectorNumber, double Confidence)> DetectRiftboundCollectorNumberAsync(byte[] imageData)
        => Task.Run(() => DetectRiftboundCollectorNumber(imageData));

    private (string? CollectorNumber, double Confidence) DetectRiftboundCollectorNumber(byte[] imageData)
    {
        if (!_ocrAvailable) return (null, 0);
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            // Landscape cards (Battlefields) are wider than tall; portrait cards ~0.72 ratio.
            var region = bitmap.Width > bitmap.Height ? RiftboundLandscapeRegion : RiftboundPortraitRegion;
            var rect = ToPixelRect(region, bitmap.Width, bitmap.Height);
            if (rect.Width < 10 || rect.Height < 5) return (null, 0);

            var (text, confidence) = OcrCroppedRegion(bitmap, rect, PageSegMode.SingleLine, RiftboundWhitelist);
            if (string.IsNullOrWhiteSpace(text)) return (null, 0);

            if (TryExtractRiftboundNumber(text, out var formatted))
            {
                var reported = Math.Max(0.9, confidence);
                _logger.LogInformation("Riftbound collector detected: {Number} (raw: {Raw}, ocrConf: {Conf:F2})",
                    formatted, text, confidence);
                return (formatted, reported);
            }
            _logger.LogDebug("Riftbound collector OCR text did not match pattern: {Text}", text);
            return (null, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Riftbound collector number detection failed");
            return (null, 0);
        }
    }

    // Applies a spec's regex to OCR text; returns the first capture group, whitespace-stripped and upper-cased.
    internal static bool TryExtractCollectorNumber(string ocrText, string pattern, out string? formatted)
    {
        formatted = null;
        if (string.IsNullOrWhiteSpace(ocrText)) return false;
        var rx = _specRegexCache.GetOrAdd(pattern, p =>
            new System.Text.RegularExpressions.Regex(p,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled));
        var m = rx.Match(ocrText);
        if (!m.Success) return false;
        var raw = (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value);
        formatted = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", "").ToUpperInvariant();
        return formatted.Length > 0;
    }

    public Task<(string? CollectorNumber, double Confidence)> DetectCollectorNumberAsync(byte[] imageData, OcrCollectorSpec spec)
        => Task.Run(() => DetectCollectorNumber(imageData, spec));

    private const int CollectorBinarizeTargetWidth = 700;

    private (string? CollectorNumber, double Confidence) DetectCollectorNumber(byte[] imageData, OcrCollectorSpec spec)
    {
        if (!_ocrAvailable) return (null, 0);
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            var landscape = bitmap.Width > bitmap.Height;
            var regions = spec.RegionsFor(landscape);

            string? bestToken = null;
            double bestConfidence = 0;
            int bestDigits = -1;
            bool bestShaped = false;

            foreach (var region in regions)
            {
                var rect = ToPixelRect(region, bitmap.Width, bitmap.Height);
                if (rect.Width < 10 || rect.Height < 5) continue;

                // Try each preprocessing variant; the same holofoil crop reads under one but not the
                // other. Prefer a token that looks like a set code (letters then digits) over a merely
                // digit-rich one, so a stray read of the passcode/ATK line can't outrank the real code.
                foreach (var (text, confidence) in ReadRegion(bitmap, rect, spec))
                {
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    string? token = spec.LooseExtraction
                        ? ExtractLooseToken(text)
                        : (TryExtractCollectorNumber(text, spec.RegexPattern, out var f) ? f : null);
                    if (token is null) continue;

                    var shaped = LooksLikeSetCode(token);
                    var digits = token.Count(char.IsDigit);
                    bool better = (shaped, digits, confidence).CompareTo((bestShaped, bestDigits, bestConfidence)) > 0;
                    if (better)
                    {
                        bestToken = token; bestConfidence = confidence; bestDigits = digits; bestShaped = shaped;
                    }
                }
            }

            if (bestToken is null)
            {
                _logger.LogDebug("Collector OCR found no usable token across {Count} region(s)", regions.Count);
                return (null, 0);
            }

            // Floor the reported confidence so it clears the caller's gate — the real gate for the
            // fuzzy path is the catalog edit-distance + pHash agreement downstream, not Tesseract's
            // (unreliable, often near-zero) mean confidence on small holofoil text.
            var reported = Math.Max(0.9, bestConfidence);
            _logger.LogInformation("Collector token detected: {Token} (ocrConf: {Conf:F2})", bestToken, bestConfidence);
            return (bestToken, reported);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Collector number detection failed");
            return (null, 0);
        }
    }

    // Yields OCR (text, confidence) for a region: plain when Binarize is off, else both the Otsu
    // binarization and a high-contrast grayscale pass (each wins on different card finishes).
    private IEnumerable<(string Text, double Confidence)> ReadRegion(Bitmap bitmap, Rectangle rect, OcrCollectorSpec spec)
    {
        var psm = spec.MultiLine ? PageSegMode.SingleBlock : PageSegMode.SingleLine;
        if (!spec.Binarize)
        {
            yield return OcrCroppedRegion(bitmap, rect, psm, spec.Whitelist);
            yield break;
        }

        using var crop = bitmap.Clone(rect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        // For block/multi-line crops (FFTCG), add a plain upscaled pass: on codes printed over
        // artwork (no light footer, e.g. FFTCG full-art) the raw crop often reads cleaner than a
        // binarization the busy background corrupts. Scoring keeps whichever pass is best-shaped.
        // Skipped for single-line specs (Yu-Gi-Oh!) to preserve their tuned two-pass behaviour.
        if (spec.MultiLine)
            yield return OcrCroppedRegion(bitmap, rect, psm, spec.Whitelist);
        using var otsu = BinarizeOtsu(crop, CollectorBinarizeTargetWidth);
        yield return RunOcr(otsu, psm, spec.Whitelist);
        using var gray = UpscaleGray(crop, CollectorBinarizeTargetWidth, 1.7f);
        yield return RunOcr(gray, psm, spec.Whitelist);
    }

    // Best code-like token from noisy OCR text: split on non-code characters, then take the run
    // carrying both letters and digits (a set code always has both; a copyright year or a plain
    // word does not) with the most digits.
    // A set code reads as some letters/prefix, an optional region code, then a run of digits at the
    // end (e.g. "GRCR-EN049", or a mis-read "3RCR-ENO49"). Passcodes and ATK/DEF are pure digits;
    // copyright words have no trailing digit group — neither looks like this.
    internal static bool LooksLikeSetCode(string token) =>
        System.Text.RegularExpressions.Regex.IsMatch(token, "^[A-Z0-9]{2,6}-?[A-Z]{1,3}[A-Z0-9]{0,2}[0-9]{2,4}$");

    internal static string? ExtractLooseToken(string text)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(text.ToUpperInvariant(), "[^A-Z0-9-]", " ");
        return spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim('-'))
            .Where(v => v.Length >= 4 && v.Any(char.IsLetter) && v.Any(char.IsDigit))
            // No set code contains ATK/DEF — guards against a crop that catches a Monster's stat line.
            .Where(v => !v.Contains("ATK") && !v.Contains("DEF"))
            .OrderByDescending(v => v.Count(char.IsDigit))
            .ThenByDescending(v => v.Length)
            .FirstOrDefault();
    }

    private (List<string> SetCodes, double Confidence) MatchSymbol(Bitmap source, Rectangle symbolRect)
    {
        // Crop and hash the symbol region
        using var cropped = source.Clone(symbolRect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        // Resize to 32x32 for pHash (same as reference symbols)
        using var resized = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(cropped, 0, 0, 32, 32);
        }

        using var ms = new MemoryStream();
        resized.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var scanSymbolHash = _hashService.ComputeHash(ms);

        // Compare against all known symbol hashes
        var results = new List<(string SetCode, int Distance)>();
        foreach (var (setCode, refHash) in SymbolHashes)
        {
            var distance = PerceptualHashService.HammingDistance(scanSymbolHash, refHash);
            results.Add((setCode, distance));
        }

        // Return top 5 closest matches
        var topMatches = results.OrderBy(r => r.Distance).Take(5).ToList();
        var codes = topMatches.Select(r => r.SetCode).ToList();
        var bestDistance = topMatches.Count > 0 ? topMatches[0].Distance : 64;
        var confidence = Math.Max(0, 1.0 - (bestDistance / 20.0)); // 0 distance = 1.0, 20+ = 0.0

        return (codes, confidence);
    }

    public void Dispose()
    {
        while (_enginePool.TryTake(out var engine))
            engine.Dispose();
    }
}
