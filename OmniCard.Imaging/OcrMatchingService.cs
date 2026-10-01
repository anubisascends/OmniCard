using System.Collections.Concurrent;
using System.Drawing;
using Microsoft.Extensions.Logging;
using Tesseract;
using OmniCard.Shared.Matching;

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

    // MTG bottom-left corner (modern frame, 2015+): two small lines that read as
    //   line 1: "{rarity} {collector}"        e.g. "R 0066"  (or older "066/281")
    //   line 2: "{setcode} • {lang} {artist}" e.g. "MKC • EN  SVETLIN VELINOV"
    // The (set code, collector number) pair uniquely identifies a Scryfall printing, so this is the
    // primary MTG match signal — see CardService's MTG scan branch and ScryfallService Phase 0. The
    // crop starts at the far left and is kept narrow so most of the artist credit on line 2 falls
    // outside it; the multi-line block read is robust to the two lines drifting vertically card-to-card.
    // Two-line bottom-left block: line 1 is rarity + collector number ("R 0172"), line 2 is
    // set code + language + artist ("SOC • EN · Artist"). The region must span BOTH lines because
    // TryExtractMtgSetAndNumber needs the collector number from line 1 and the set code from line 2.
    internal static readonly (double X, double Y, double W, double H) MtgCollectorRegion =
        (0.02, 0.912, 0.34, 0.066);

    // Whitelist: the codes are upper-case letters + digits; the bullet/separator and slash vary
    // (the • often OCRs as ., *, or nothing), so allow the common separators through for the regex.
    private const string MtgCollectorWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789/•·.*- ";

    // Language codes that print after the set code on line 2. Used to anchor the set-code parse
    // (the token immediately before the language is the set code) and to reject a language token
    // being mistaken for a set code.
    private static readonly HashSet<string> MtgLanguageCodes =
        new(StringComparer.OrdinalIgnoreCase) { "EN", "DE", "FR", "IT", "ES", "SP", "PT", "JA", "JP", "KO", "KR", "RU", "ZH", "CT", "CS", "PH" };

    // Matches "{setcode} {sep} {lang}" on line 2, e.g. "MKC • EN", "DMU•EN", "M21 . EN".
    // Group 1 is the set code (3-5 alphanumerics); the separator and language anchor it. At least one
    // separator (space and/or bullet/punctuation) is required between the set code and the language so
    // the language can't be matched as a substring of an ordinary word (e.g. "ES" inside "RULES").
    // A single stray letter may sit between the separator and the language code: the promo/special
    // "★" printed between set code and language OCRs as a whitelisted letter (e.g. "SOC ★ EN" → "SOC XEN").
    private static readonly System.Text.RegularExpressions.Regex MtgSetCodePattern =
        new(@"\b([A-Z0-9]{3,5})[\s•·.*\-]+[A-Z]?(EN|DE|FR|IT|ES|SP|PT|JA|JP|KO|KR|RU|ZH|CT|CS|PH)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches the collector number: a run of 1-4 digits, optionally "{collector}/{total}".
    // Group 1 is the collector number (the numerator). The loosest fallback — see TryExtractMtgSetAndNumber.
    private static readonly System.Text.RegularExpressions.Regex MtgCollectorNumberPattern =
        new(@"\b(\d{1,4})\s*(?:/\s*\d{1,4})?\b",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // "{collector}/{total}" (e.g. "040/277") — how 2015-2022 frames print line 1. Group 1 is the collector.
    private static readonly System.Text.RegularExpressions.Regex MtgCollectorFractionPattern =
        new(@"(?<!\d)(\d{1,4})\s*/\s*\d{2,4}(?!\d)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // A zero-padded 3-4 digit collector (e.g. "0012", "025") — how 2023+ frames print line 1, after the
    // rarity letter. Bounded by non-digits rather than \b, because OCR often glues the rarity letter to the
    // number ("C0012"), which a word boundary rejects.
    private static readonly System.Text.RegularExpressions.Regex MtgCollectorPaddedPattern =
        new(@"(?<!\d)(\d{3,4})(?!\d)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // How strongly a parsed collector number is anchored to where it's printed (line 1, directly above the
    // set-code line). Used to rank reads across OCR passes: stray digits from the rules-text box above the
    // corner or from border texture only ever reach the Loose tier.
    internal const int MtgEvidenceLoose = 0;             // first plausible digit run anywhere in the crop
    internal const int MtgEvidenceFractionElsewhere = 1; // "nnn/ttt" not on the line above the set code
    internal const int MtgEvidencePaddedAboveSet = 2;    // zero-padded "0012" on the line above the set code
    internal const int MtgEvidenceFractionAboveSet = 3;  // "nnn/ttt" on the line above the set code

    // Reported confidence when every OCR pass independently read the same anchored (set, collector).
    // Deliberately at/above WebScanMatchingService's set-filter override bar (0.95): three agreeing
    // reads of the printed identity are trusted over the user's "Sets (art fallback)" selection, whereas
    // a split or single read stays at the 0.9 floor and remains bound by that filter.
    private const double MtgUnanimousReadConfidence = 0.96;

    public Dictionary<string, ulong> SymbolHashes { get; set; } = [];

    // The List (plst) reprints print the MTG Planeswalker "hand" logo immediately left of the collector
    // number. We locate it with normalized cross-correlation (NCC) of a search window around that spot
    // against a template built from real reference crops. NCC normalizes out local brightness/contrast,
    // so the glyph is found the same on a blank dark border, a busy filigree border, or a bright foil —
    // where a plain pHash of the crop was swamped by border texture. Tuned on real scans: cards with the
    // glyph score ~0.69–0.88, cards without (regular printings, MB2, playtest) score ≤0.54. See
    // MtgListSymbolDetectionTests. The reference crops in Assets/ were extracted at TemplateRegion.
    internal static readonly (double X, double Y, double W, double H) MtgListSymbolTemplateRegion =
        (0.021, 0.905, 0.062, 0.070);
    // Wider window the template slides within, absorbing card-to-card drift of the collector line.
    internal static readonly (double X, double Y, double W, double H) MtgListSymbolSearchRegion =
        (0.005, 0.885, 0.110, 0.110);
    private const int ListTemplateW = 40, ListTemplateH = 48;   // template raster size
    private const int ListWindowW = 80, ListWindowH = 90;       // search-window raster size
    // Peak NCC at/above which the glyph counts as present. 0.60 sits in the wide gap between the two
    // clusters (~0.15 margin either side), so scanner/lighting variation doesn't flip the decision.
    private const double MtgListSymbolMinCorrelation = 0.60;

    // Zero-mean template built once from the embedded reference glyph crops, plus its L2 norm for NCC.
    private float[]? _listTemplate;
    private double _listTemplateNorm;

    public OcrMatchingService(IPerceptualHashService hashService, ILogger<OcrMatchingService> logger)
    {
        _hashService = hashService;
        _logger = logger;

        LoadListSymbolReferences();

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
                        ? ExtractLooseToken(text, spec.AllowLetterOnlyToken)
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

    // Yu-Gi-Oh! edition text location varies by frame era:
    //  • Modern (2020+): bottom line next to the password — "{8-digit password} 1st Edition".
    //  • Older reprints (e.g. Legendary Collection): just ABOVE the effect box on the LEFT.
    // Unlimited prints carry NO edition text in either spot, so ANY "Edition" word read here means the
    // card is 1st (or, rarely, Limited) Edition. Both bands are tried; the adjacent password/copyright
    // are harmless (we scan for the word "Edition", not a code). Validated on the 2026091401 batch.
    internal static readonly (double X, double Y, double W, double H)[] YugiohEditionRegions =
    [
        (0.12, 0.934, 0.34, 0.030), // modern: bottom line, right of the password
        (0.05, 0.703, 0.30, 0.034), // older: above the effect box, left side
    ];
    // Back-compat alias for the modern region (used by tests).
    internal static (double X, double Y, double W, double H) YugiohEditionRegion => YugiohEditionRegions[0];

    // The edition text is mixed-case ("1st Edition"), so the whitelist must include lowercase too —
    // an uppercase-only whitelist makes Tesseract misread the lowercase glyphs. ClassifyEdition
    // uppercases before matching.
    private const string EditionWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ";

    public Task<(string? Edition, double Confidence)> DetectYugiohEditionAsync(byte[] imageData)
        => Task.Run(() => DetectYugiohEdition(imageData));

    // Reads the lower-left edition line and classifies it. Returns "1st Edition" / "Limited Edition"
    // when the edition text is present, else null (Unlimited — no edition text is printed).
    private (string? Edition, double Confidence) DetectYugiohEdition(byte[] imageData)
    {
        if (!_ocrAvailable) return (null, 0);
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            foreach (var region in YugiohEditionRegions)
            {
                var rect = ToPixelRect(region, bitmap.Width, bitmap.Height);
                if (rect.Width < 10 || rect.Height < 5) continue;

                using var crop = bitmap.Clone(rect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                // Same cheap-to-aggressive passes as the collector-code path; the text is the same size/finish.
                IEnumerable<(string Text, double Confidence)> Passes()
                {
                    yield return OcrCroppedRegion(bitmap, rect, PageSegMode.SparseText, EditionWhitelist);
                    using var otsu = BinarizeOtsu(crop, CollectorBinarizeTargetWidth);
                    yield return RunOcr(otsu, PageSegMode.SparseText, EditionWhitelist);
                    using var gray = UpscaleGray(crop, CollectorBinarizeTargetWidth, 1.7f);
                    yield return RunOcr(gray, PageSegMode.SparseText, EditionWhitelist);
                }

                foreach (var (text, confidence) in Passes())
                {
                    var edition = ClassifyEdition(text);
                    if (edition is not null)
                    {
                        _logger.LogInformation("Yu-Gi-Oh! edition detected: {Edition} (raw: {Raw}, ocrConf: {Conf:F2})",
                            edition, text.Replace("\n", " ").Trim(), confidence);
                        return (edition, Math.Max(0.9, confidence));
                    }
                }
            }
            return (null, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yu-Gi-Oh! edition detection failed");
            return (null, 0);
        }
    }

    // Classifies an OCR'd edition line. Tolerant of the usual small-text confusions: matches the robust
    // core "EDITI" and "LIMITE" rather than the full words. Null when no edition marker is present.
    internal static string? ClassifyEdition(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return null;
        var upper = System.Text.RegularExpressions.Regex.Replace(ocrText.ToUpperInvariant(), "[^A-Z0-9]", "");
        bool hasEdition = upper.Contains("EDITI") || upper.Contains("DITION") || upper.Contains("EDITON");
        if (!hasEdition) return null;
        if (upper.Contains("LIMITE") || upper.Contains("LIMTE")) return "Limited Edition";
        return "1st Edition";
    }

    // Yields OCR (text, confidence) for a region: plain when Binarize is off, else both the Otsu
    // binarization and a high-contrast grayscale pass (each wins on different card finishes).
    private IEnumerable<(string Text, double Confidence)> ReadRegion(Bitmap bitmap, Rectangle rect, OcrCollectorSpec spec)
    {
        var psm = ResolvePsm(spec);
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

    // Maps the spec's provider-neutral page-seg mode to Tesseract's, preserving the legacy default
    // (SingleBlock for multi-line specs, SingleLine otherwise) when the spec doesn't override it.
    private static PageSegMode ResolvePsm(OcrCollectorSpec spec) => spec.PageSegMode switch
    {
        OcrPageSegMode.SingleLine => PageSegMode.SingleLine,
        OcrPageSegMode.SingleBlock => PageSegMode.SingleBlock,
        OcrPageSegMode.SparseText => PageSegMode.SparseText,
        _ => spec.MultiLine ? PageSegMode.SingleBlock : PageSegMode.SingleLine,
    };

    public async Task<(string? SetCode, string? CollectorNumber, double Confidence)> DetectMtgSetAndNumberAsync(byte[] imageData)
    {
        var (reads, confidence) = await DetectMtgSetAndNumberCandidatesAsync(imageData);
        return reads.Count == 0 ? (null, null, 0) : (reads[0].SetCode, reads[0].CollectorNumber, confidence);
    }

    public Task<(IReadOnlyList<MtgPrintedIdentity> Reads, double Confidence)> DetectMtgSetAndNumberCandidatesAsync(byte[] imageData)
        => Task.Run(() => DetectMtgSetAndNumber(imageData));

    private (IReadOnlyList<MtgPrintedIdentity> Reads, double Confidence) DetectMtgSetAndNumber(byte[] imageData)
    {
        if (!_ocrAvailable) return ([], 0);
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            // Only meaningful for portrait card scans; skip clearly non-card / landscape crops.
            if (bitmap.Width > bitmap.Height) return ([], 0);

            var rect = ToPixelRect(MtgCollectorRegion, bitmap.Width, bitmap.Height);
            if (rect.Width < 10 || rect.Height < 5) return ([], 0);

            // Read the two-line block with three preprocessing passes: plain crop → Otsu binarization →
            // high-contrast grayscale upscale (each wins on different finishes; the gray pass rescues foil
            // glare that the binarization crushes). All three always run: on real scans the first pass to
            // parse was often the wrong one (a stray digit from the rules box, or a misread digit that two
            // passes shared), so the passes vote and every anchored read is kept as a candidate.
            using var crop = bitmap.Clone(rect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            IEnumerable<(string Text, double Confidence)> Passes()
            {
                yield return OcrCroppedRegion(bitmap, rect, PageSegMode.SingleBlock, MtgCollectorWhitelist);
                using var otsu = BinarizeOtsu(crop, CollectorBinarizeTargetWidth);
                yield return RunOcr(otsu, PageSegMode.SingleBlock, MtgCollectorWhitelist);
                using var gray = UpscaleGray(crop, CollectorBinarizeTargetWidth, 1.7f);
                yield return RunOcr(gray, PageSegMode.SingleBlock, MtgCollectorWhitelist);
            }

            var parsed = new List<(string Set, string Number, int Evidence, double Confidence, string? Language)>();
            var raw = new List<string>();
            int passCount = 0;
            foreach (var (text, confidence) in Passes())
            {
                passCount++;
                raw.Add(text.Replace("\n", " | ").Trim());
                if (TryExtractMtgSetAndNumber(text, out var setCode, out var number, out var evidence, out var language))
                    parsed.Add((setCode!, number!, evidence, confidence, language));
            }

            if (parsed.Count == 0)
            {
                _logger.LogDebug("MTG set/number OCR found no usable (set, collector) pair (raw: {Raw})", string.Join(" || ", raw));
                return ([], 0);
            }

            var reads = RankMtgReads(parsed.Select(p => (p.Set, p.Number, p.Evidence, p.Language)).ToList());
            var best = reads[0];
            var bestEvidence = parsed.Where(p => p.Set == best.SetCode && p.Number == best.CollectorNumber).Max(p => p.Evidence);
            var bestConfidence = parsed.Where(p => p.Set == best.SetCode && p.Number == best.CollectorNumber).Max(p => p.Confidence);

            // Floor the reported confidence: the real gate is the exact (set, collector) DB lookup
            // downstream, which either resolves to a printing or it doesn't. A unanimous anchored read is
            // raised past the set-filter override bar (see MtgUnanimousReadConfidence).
            var unanimous = reads.Count == 1 && best.Votes == passCount && bestEvidence >= MtgEvidencePaddedAboveSet;
            var reported = Math.Max(unanimous ? MtgUnanimousReadConfidence : 0.9, bestConfidence);
            _logger.LogInformation("MTG set/number detected: {Set} #{Number} ({Votes}/{Passes} passes{Alts}; raw: {Raw})",
                best.SetCode, best.CollectorNumber, best.Votes, passCount,
                reads.Count > 1 ? "; alternates " + string.Join(", ", reads.Skip(1).Select(r => $"{r.SetCode} #{r.CollectorNumber}")) : "",
                string.Join(" || ", raw));
            return (reads, reported);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MTG set/number detection failed");
            return ([], 0);
        }
    }

    // Collapses per-pass reads into distinct (set, collector) candidates, best first: anchored reads
    // (evidence ≥ FractionElsewhere) outrank loose digit runs, then more agreeing passes, then stronger
    // anchoring. Loose reads are dropped as alternates (border noise would only offer random printings)
    // and survive only as the sole candidate when nothing anchored was read.
    internal static List<MtgPrintedIdentity> RankMtgReads(IReadOnlyList<(string Set, string Number, int Evidence)> parsed)
        => RankMtgReads(parsed.Select(p => (p.Set, p.Number, p.Evidence, (string?)null)).ToList());

    // As above, carrying each pass's printed-language read. A candidate's language is the one most of its
    // passes agreed on (passes that didn't read a language abstain).
    internal static List<MtgPrintedIdentity> RankMtgReads(IReadOnlyList<(string Set, string Number, int Evidence, string? Language)> parsed)
    {
        var ranked = parsed
            .GroupBy(p => (p.Set, p.Number))
            .Select(g => (g.Key.Set, g.Key.Number, Votes: g.Count(), Evidence: g.Max(p => p.Evidence),
                Language: g.Where(p => p.Language is not null).GroupBy(p => p.Language)
                    .OrderByDescending(l => l.Count()).Select(l => l.Key).FirstOrDefault()))
            .OrderByDescending(g => g.Evidence >= MtgEvidenceFractionElsewhere)
            .ThenByDescending(g => g.Votes)
            .ThenByDescending(g => g.Evidence)
            .ToList();
        return ranked
            .Where((g, i) => i == 0 || g.Evidence >= MtgEvidenceFractionElsewhere)
            .Select(g => new MtgPrintedIdentity(g.Set, g.Number, g.Votes, g.Language))
            .ToList();
    }

    public Task<(bool Present, double Confidence)> DetectMtgListSymbolAsync(byte[] imageData)
        => Task.Run(() => DetectMtgListSymbol(imageData));

    // Detects the Planeswalker "hand" glyph The List (plst) prints left of the collector number. We
    // pHash a small bottom-left box and compare against embedded reference crops of the glyph. Cards
    // drift a few pixels scan-to-scan, so we sample a small grid of offsets around the nominal box and
    // keep the closest match — the glyph only has to line up on one of them. Blank borders (any color)
    // stay far from every reference because the glyph's bright shape dominates the low-frequency DCT.
    internal (bool Present, double Confidence) DetectMtgListSymbol(byte[] imageData)
    {
        var peak = PeakListSymbolCorrelation(imageData);
        var present = peak >= MtgListSymbolMinCorrelation;
        if (present)
            _logger.LogInformation("MTG List (plst) Planeswalker glyph detected (NCC {Peak:F3})", peak);
        else
            _logger.LogDebug("MTG List glyph not detected (peak NCC {Peak:F3})", peak);
        // Report the peak correlation itself as the confidence — it already lives in [0,1].
        return (present, Math.Max(0, peak));
    }

    // Peak normalized cross-correlation between the glyph template and the bottom-left search window.
    // Exposed internal so the validation test can inspect the raw separation. Returns -1 when the
    // template is missing or the image isn't a usable portrait crop.
    internal double PeakListSymbolCorrelation(byte[] imageData)
    {
        if (_listTemplate is null) return -1;
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            // Portrait card scans only — the glyph position is defined for upright cards.
            if (bitmap.Width > bitmap.Height) return -1;

            var rect = ToPixelRect(MtgListSymbolSearchRegion, bitmap.Width, bitmap.Height);
            // The crop is resized up to the window raster regardless, so only reject degenerate crops.
            if (rect.Width < 8 || rect.Height < 8) return -1;

            var window = GrayResize(bitmap, rect, ListWindowW, ListWindowH);
            return MaxNcc(window, ListWindowW, ListWindowH, _listTemplate, _listTemplateNorm, ListTemplateW, ListTemplateH);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MTG List symbol detection failed");
            return -1;
        }
    }

    // Crop `rect`, resize to w×h, return row-major luminance in [0,255].
    private static float[] GrayResize(Bitmap source, Rectangle rect, int w, int h)
    {
        using var cropped = source.Clone(rect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var resized = new Bitmap(w, h);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(cropped, 0, 0, w, h);
        }
        var px = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = resized.GetPixel(x, y);
                px[y * w + x] = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
            }
        return px;
    }

    // Slide a zero-mean template over the window and return the highest Pearson correlation (ZNCC) over
    // all offsets. Template is pre-zero-meaned with L2 norm `tNorm`; each candidate patch is zero-meaned
    // per position (so the score is invariant to the local border's brightness and contrast).
    private static double MaxNcc(float[] win, int ww, int wh, float[] tmpl, double tNorm, int tw, int th)
    {
        if (tNorm <= 0) return -1;
        double best = -1;
        int n = tw * th;
        for (int oy = 0; oy <= wh - th; oy++)
        {
            for (int ox = 0; ox <= ww - tw; ox++)
            {
                double sum = 0;
                for (int y = 0; y < th; y++)
                {
                    int wrow = (oy + y) * ww + ox;
                    for (int x = 0; x < tw; x++)
                        sum += win[wrow + x];
                }
                double mean = sum / n;
                double num = 0, energy = 0;
                for (int y = 0; y < th; y++)
                {
                    int wrow = (oy + y) * ww + ox;
                    int trow = y * tw;
                    for (int x = 0; x < tw; x++)
                    {
                        double d = win[wrow + x] - mean;
                        num += d * tmpl[trow + x];
                        energy += d * d;
                    }
                }
                if (energy <= 0) continue;
                double ncc = num / (Math.Sqrt(energy) * tNorm);
                if (ncc > best) best = ncc;
            }
        }
        return best;
    }

    // Builds the zero-mean glyph template from the embedded reference crops. Each crop is grayscaled,
    // resized to the template raster, and per-crop contrast-normalized before averaging so all references
    // contribute equally regardless of their border brightness. Runs once at construction.
    private void LoadListSymbolReferences()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var accum = new double[ListTemplateW * ListTemplateH];
            int count = 0;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.Contains("mtg-list-glyph", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var bmp = new Bitmap(stream);
                var g = GrayResize(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), ListTemplateW, ListTemplateH);
                // Per-crop z-normalize.
                double mean = g.Average(v => (double)v);
                double var = g.Average(v => (v - mean) * (v - mean));
                double std = Math.Sqrt(var) + 1e-3;
                for (int i = 0; i < accum.Length; i++) accum[i] += (g[i] - mean) / std;
                count++;
            }
            if (count == 0)
            {
                _logger.LogWarning("No MTG List glyph reference templates found — List (plst) detection disabled");
                return;
            }
            // Average, then zero-mean the template and record its L2 norm for the NCC denominator.
            var tmpl = new float[accum.Length];
            for (int i = 0; i < accum.Length; i++) tmpl[i] = (float)(accum[i] / count);
            double tmean = tmpl.Average(v => (double)v);
            double norm = 0;
            for (int i = 0; i < tmpl.Length; i++) { tmpl[i] = (float)(tmpl[i] - tmean); norm += tmpl[i] * (double)tmpl[i]; }
            _listTemplate = tmpl;
            _listTemplateNorm = Math.Sqrt(norm);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load MTG List glyph references — detection disabled");
        }
    }

    // Parses the modern MTG bottom-left block into (set code, collector number). Both must be found
    // for a usable result. Exposed internal for the OCR tuning tests.
    internal static bool TryExtractMtgSetAndNumber(string ocrText, out string? setCode, out string? collectorNumber)
        => TryExtractMtgSetAndNumber(ocrText, out setCode, out collectorNumber, out _);

    // As above, also reporting how well-anchored the collector number is (MtgEvidence* constants).
    // The collector number is read from where it's printed — line 1, directly above the set-code line —
    // before falling back to looser matches. Taking the first digit run anywhere (the old behaviour) picked
    // up stray digits from the rules-text box above the corner ("5 2G…" before "040/277") and missed
    // numbers OCR'd with the rarity letter attached ("C0012"), landing on the wrong printing.
    internal static bool TryExtractMtgSetAndNumber(string ocrText, out string? setCode, out string? collectorNumber, out int evidence)
        => TryExtractMtgSetAndNumber(ocrText, out setCode, out collectorNumber, out evidence, out _);

    // As above, also returning the printed language marker that anchored the set code ("• JP" → "ja"),
    // normalized to a CardLanguages code.
    internal static bool TryExtractMtgSetAndNumber(string ocrText, out string? setCode, out string? collectorNumber, out int evidence, out string? language)
    {
        language = null;
        setCode = null;
        collectorNumber = null;
        evidence = MtgEvidenceLoose;
        if (string.IsNullOrWhiteSpace(ocrText)) return false;

        var upper = ocrText.ToUpperInvariant();
        var lines = upper.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        // Set code: the alphanumeric token immediately before the "• EN" language marker on line 2.
        // A language code can't be a set code (guards "EN • EN"-style misreads); a valid set code
        // carries at least one letter (pure-digit tokens are the collector/total, not a set).
        int setLine = -1, setIndex = 0;
        for (int i = 0; i < lines.Count && setCode is null; i++)
        {
            foreach (System.Text.RegularExpressions.Match m in MtgSetCodePattern.Matches(lines[i]))
            {
                var candidate = m.Groups[1].Value;
                if (MtgLanguageCodes.Contains(candidate) || !candidate.Any(char.IsLetter)) continue;
                setCode = candidate;
                language = OmniCard.Shared.Games.CardLanguages.Normalize(m.Groups[2].Value);
                setLine = i;
                setIndex = m.Index;
                break;
            }
        }

        // Collector number, strongest anchoring first. "Above the set" is the line before the set-code
        // line plus anything preceding the set code on its own line (OCR sometimes merges the two lines).
        string? number = null;
        if (setLine >= 0)
        {
            var aboveSet = (setLine > 0 ? lines[setLine - 1] : "") + " " + lines[setLine][..setIndex];
            number = FirstCollector(MtgCollectorFractionPattern, aboveSet);
            if (number is not null) evidence = MtgEvidenceFractionAboveSet;
            else if ((number = FirstCollector(MtgCollectorPaddedPattern, aboveSet)) is not null) evidence = MtgEvidencePaddedAboveSet;
        }
        if (number is null && (number = FirstCollector(MtgCollectorFractionPattern, upper)) is not null)
            evidence = MtgEvidenceFractionElsewhere;
        // Loosest: the first digit run anywhere (e.g. a set line read with no line above it).
        number ??= FirstCollector(MtgCollectorNumberPattern, upper);

        if (number is not null)
        {
            // Leading zeros stripped to match how Scryfall stores it ("0066" → "66").
            var normalized = number.TrimStart('0');
            collectorNumber = normalized.Length == 0 ? "0" : normalized;
        }

        return setCode is not null && collectorNumber is not null;
    }

    // First group-1 match of `pattern` in `text`, skipping 4-digit tokens in the 1990-2099 year range when
    // any other match exists (the copyright year prints on the same corner block on some frames).
    private static string? FirstCollector(System.Text.RegularExpressions.Regex pattern, string text)
    {
        var numbers = pattern.Matches(text).Select(m => m.Groups[1].Value).ToList();
        if (numbers.Count == 0) return null;
        var nonYear = numbers.Where(n => !(n.Length == 4 && int.TryParse(n, out var y) && y is >= 1990 and <= 2099)).ToList();
        return (nonYear.Count > 0 ? nonYear : numbers)[0];
    }

    // Best code-like token from noisy OCR text: split on non-code characters, then take the run
    // carrying both letters and digits (a set code always has both; a copyright year or a plain
    // word does not) with the most digits.
    // A set code reads as some letters/prefix, an optional region code, then a run of digits at the
    // end (e.g. "GRCR-EN049", or a mis-read "3RCR-ENO49"). Passcodes and ATK/DEF are pure digits;
    // copyright words have no trailing digit group — neither looks like this.
    internal static bool LooksLikeSetCode(string token) =>
        System.Text.RegularExpressions.Regex.IsMatch(token, "^[A-Z0-9]{2,6}-?[A-Z]{1,3}[A-Z0-9]{0,2}[0-9]{2,4}$");

    // When allowLetterOnly is set, tokens with letters but no digits are also eligible — a holofoil
    // read often turns the collector digits into letters ("DAMA-EN012" → "DAMA-ENULZ"), and the
    // confusion-aware fuzzy catalog matcher maps them back. Digit-bearing tokens still rank first, so
    // a clean read always wins; letter-only tokens only surface when nothing better was read.
    internal static string? ExtractLooseToken(string text, bool allowLetterOnly = false)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(text.ToUpperInvariant(), "[^A-Z0-9-]", " ");
        return spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim('-'))
            .Where(v => v.Length >= 4 && v.Any(char.IsLetter) && (allowLetterOnly || v.Any(char.IsDigit)))
            // No set code contains ATK/DEF — guards against a crop that catches a Monster's stat line.
            .Where(v => !v.Contains("ATK") && !v.Contains("DEF"))
            .OrderByDescending(v => v.Count(char.IsDigit))
            .ThenByDescending(v => v.Length)
            .FirstOrDefault();
    }

    // ── Old-frame (pre-2015) MTG printing evidence ─────────────────────────────────────────────────────
    // These frames print no set code / collector number bottom-left, and same-art reprints hash identically,
    // so the printing is told apart by physical cues instead (see MtgPrintEvidence). Regions are fractions
    // of the scan, calibrated on real 1993–2003 frame scans (Revised through Legions, plus MH2 retro).

    // Title bar, stopping short of the mana cost.
    internal static readonly (double X, double Y, double W, double H) OldFrameTitleRegion = (0.06, 0.04, 0.64, 0.065);
    // Illustrator credit + copyright line (+ "nnn/ttt" collector on 1999–2003 frames).
    internal static readonly (double X, double Y, double W, double H) OldFrameBottomRegion = (0.05, 0.915, 0.90, 0.07);
    // Rules/flavor text box.
    internal static readonly (double X, double Y, double W, double H) OldFrameTextBoxRegion = (0.10, 0.60, 0.80, 0.31);

    // Border classification by the median luminance of a band just inside the card edge. Measured on real
    // scans: white borders sit at ~230–240, black at ~25–100 (FEM's grainy black is the brightest).
    private const int WhiteBorderMinLuminance = 170;
    private const int BlackBorderMaxLuminance = 120;

    public Task<MtgPrintEvidence> ReadMtgPrintEvidenceAsync(byte[] imageData) => Task.Run(() => ReadMtgPrintEvidence(imageData));

    private MtgPrintEvidence ReadMtgPrintEvidence(byte[] imageData)
    {
        try
        {
            using var bitmap = CropToCard(new Bitmap(new MemoryStream(imageData)));
            var border = ClassifyBorder(bitmap);
            if (!_ocrAvailable)
                return new MtgPrintEvidence { BorderColor = border };

            var titles = new List<string>();
            var bottoms = new List<string>();
            using (var title = bitmap.Clone(ToPixelRect(OldFrameTitleRegion, bitmap.Width, bitmap.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                // Old-frame titles are white glyphs with a dark drop shadow on a coloured bar. Keeping only
                // bright, near-neutral pixels as ink isolates the glyphs from the frame texture — the plain
                // crop reads "Elvish Scout" as "Wei. |", the white-ink mask reads it cleanly. The plain read is
                // kept for the frames where it does better (light title bars, 2003 frame).
                using (var plain = UpscaleColor(title, 1400)) AddRead(titles, RunOcr(plain, PageSegMode.SingleLine, null).Text);
                // White-frame cards (white artifacts/lands, Chronicles white) put the white title on a cream bar,
                // which the white-ink mask can't separate; only the brightest pixels are glyph there. The mask
                // then also catches the card's white border (blanked as near-solid rows/columns) and speckle.
                // Each mask is read as scanned and auto-levelled: levelling rescues dim scans but can push a
                // well-exposed cream bar over the threshold, and the name lookup keeps the best read anyway.
                foreach (var levels in new[] { false, true })
                {
                    using (var ink = InkMask(title, 1400, IsWhiteInk, levels: levels)) AddRead(titles, RunOcr(ink, PageSegMode.SingleLine, null).Text);
                    using (var bright = InkMask(title, 1400, IsBrightInk, clean: true, levels: levels)) AddRead(titles, RunOcr(bright, PageSegMode.SingleLine, null).Text);
                }
            }
            using (var bottom = bitmap.Clone(ToPixelRect(OldFrameBottomRegion, bitmap.Width, bitmap.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                // The credit/copyright text is small and light-on-dark; each variant reads some frames the
                // others garble, so all are kept and the catalog side looks for its cues across them.
                using (var plain = UpscaleColor(bottom, 1800)) AddRead(bottoms, RunOcr(plain, PageSegMode.SingleBlock, null).Text);
                using (var light = InkMask(bottom, 1800, IsLightInk, levels: true)) AddRead(bottoms, RunOcr(light, PageSegMode.SingleBlock, null).Text);
                using (var white = InkMask(bottom, 1800, IsWhiteInk, levels: true)) AddRead(bottoms, RunOcr(white, PageSegMode.SingleBlock, null).Text);
            }
            return new MtgPrintEvidence { TitleReads = titles, BorderColor = border, BottomLineReads = bottoms };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Old-frame MTG print evidence read failed");
            return MtgPrintEvidence.Empty;
        }

        static void AddRead(List<string> reads, string text)
        {
            text = text.Replace('\n', ' ').Trim();
            if (text.Length > 0) reads.Add(text);
        }
    }

    public Task<string?> ReadMtgTextBoxAsync(byte[] imageData) => Task.Run(() => ReadMtgTextBox(imageData));

    private string? ReadMtgTextBox(byte[] imageData)
    {
        if (!_ocrAvailable) return null;
        try
        {
            using var bitmap = CropToCard(new Bitmap(new MemoryStream(imageData)));
            using var box = bitmap.Clone(ToPixelRect(OldFrameTextBoxRegion, bitmap.Width, bitmap.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var gray = UpscaleGray(box, 1400, 1.2f);
            var text = RunOcr(gray, PageSegMode.SingleBlock, null).Text.Replace('\n', ' ').Trim();
            return text.Length > 0 ? text : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Old-frame MTG text box read failed");
            return null;
        }
    }

    /// <summary>Takes ownership of <paramref name="scan"/> and returns it cropped to the card when it sits on
    /// a visible margin of scanner bed / mat (<see cref="FindCardBounds"/>), else unchanged. The old-frame
    /// regions are fractions of the card, so a margin would shift every crop.</summary>
    private static Bitmap CropToCard(Bitmap scan)
    {
        var bounds = FindCardBounds(scan);
        if (bounds.Width == scan.Width && bounds.Height == scan.Height)
            return scan;
        using (scan)
            return scan.Clone(bounds, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    /// <summary>
    /// The card's rectangle within a scan that has a uniform margin around it, or the whole image. The
    /// background colour is read from the corners (rounded card corners expose it even on tight scans). Only
    /// a background that can't be mistaken for a card border is trimmed — mid-grey or coloured, not
    /// near-white or near-black — since trimming a white lid off a white-bordered card would eat the border.
    /// </summary>
    internal static Rectangle FindCardBounds(Bitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        var full = new Rectangle(0, 0, w, h);
        if (w < 50 || h < 50) return full;
        using var argb = bitmap.Clone(full, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = argb.LockBits(full, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[] buf;
        int stride = data.Stride;
        try
        {
            buf = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        }
        finally { argb.UnlockBits(data); }

        (int R, int G, int B) Px(int x, int y) { var i = y * stride + x * 4; return (buf[i + 2], buf[i + 1], buf[i]); }

        // Background = median colour over four small corner patches.
        int patch = Math.Max(2, Math.Min(w, h) / 70);
        var rs = new List<int>(); var gs = new List<int>(); var bs = new List<int>();
        foreach (var (cx, cy) in new[] { (0, 0), (w - patch, 0), (0, h - patch), (w - patch, h - patch) })
            for (int y = cy; y < cy + patch; y++)
                for (int x = cx; x < cx + patch; x++)
                {
                    var (r, g, b) = Px(x, y); rs.Add(r); gs.Add(g); bs.Add(b);
                }
        rs.Sort(); gs.Sort(); bs.Sort();
        var bg = (R: rs[rs.Count / 2], G: gs[gs.Count / 2], B: bs[bs.Count / 2]);
        var bgLum = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
        var bgChroma = Math.Max(bg.R, Math.Max(bg.G, bg.B)) - Math.Min(bg.R, Math.Min(bg.G, bg.B));
        bool bgLooksLikeBorder = bgChroma < 40 && (bgLum > 215 || bgLum < 60);
        if (bgLooksLikeBorder) return full;

        const int Tolerance = 24;
        bool IsBg((int R, int G, int B) p) =>
            Math.Abs(p.R - bg.R) <= Tolerance && Math.Abs(p.G - bg.G) <= Tolerance && Math.Abs(p.B - bg.B) <= Tolerance;
        // A row/column is margin when nearly all of its middle 60% is background.
        bool RowIsBg(int y) { int n = 0, t = 0; for (int x = w / 5; x < w * 4 / 5; x += 2) { t++; if (IsBg(Px(x, y))) n++; } return n >= t * 0.9; }
        bool ColIsBg(int x) { int n = 0, t = 0; for (int y = h / 5; y < h * 4 / 5; y += 2) { t++; if (IsBg(Px(x, y))) n++; } return n >= t * 0.9; }

        int maxW = w * 15 / 100, maxH = h * 15 / 100;
        int top = 0; while (top < maxH && RowIsBg(top)) top++;
        int bottom = 0; while (bottom < maxH && RowIsBg(h - 1 - bottom)) bottom++;
        int left = 0; while (left < maxW && ColIsBg(left)) left++;
        int right = 0; while (right < maxW && ColIsBg(w - 1 - right)) right++;
        if (top + bottom + left + right == 0) return full;
        return new Rectangle(left, top, w - left - right, h - top - bottom);
    }

    /// <summary>"white"/"black" from the median luminance of a band 1–2.5% inside each edge (corners
    /// skipped — rounded corners show the scanner background), or null when it's neither clearly.</summary>
    internal static string? ClassifyBorder(Bitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        if (w < 50 || h < 50) return null;
        using var argb = bitmap.Clone(new Rectangle(0, 0, w, h), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = argb.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var buf = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            int Lum(int x, int y)
            {
                var i = y * stride + x * 4; // BGRA
                return (int)(0.114 * buf[i] + 0.587 * buf[i + 1] + 0.299 * buf[i + 2]);
            }
            var lums = new List<int>();
            for (double t = 0.15; t <= 0.85; t += 0.01)
                for (double d = 0.010; d <= 0.0251; d += 0.005)
                {
                    lums.Add(Lum((int)(t * w), (int)(d * h)));
                    lums.Add(Lum((int)(t * w), (int)((1 - d) * h)));
                    lums.Add(Lum((int)(d * w), (int)(t * h)));
                    lums.Add(Lum((int)((1 - d) * w), (int)(t * h)));
                }
            lums.Sort();
            var median = lums[lums.Count / 2];
            return median >= WhiteBorderMinLuminance ? "white" : median <= BlackBorderMaxLuminance ? "black" : null;
        }
        finally { argb.UnlockBits(data); }
    }

    // Bright and near-neutral: the white title/credit glyphs, not the coloured frame behind them.
    private static bool IsWhiteInk(byte r, byte g, byte b)
    {
        int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        return mn > 175 && mx - mn < 60;
    }

    private static bool IsLightInk(byte r, byte g, byte b) => 0.299 * r + 0.587 * g + 0.114 * b > 170;

    // Brighter and more neutral than the cream of a white frame's title bar.
    private static bool IsBrightInk(byte r, byte g, byte b)
    {
        int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        return mn > 228 && mx - mn < 35;
    }

    private static Bitmap UpscaleColor(Bitmap crop, int targetW)
    {
        var scale = (double)targetW / crop.Width;
        var outBmp = new Bitmap(targetW, Math.Max(1, (int)(crop.Height * scale)));
        using var g = Graphics.FromImage(outBmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(crop, 0, 0, outBmp.Width, outBmp.Height);
        return outBmp;
    }

    /// <summary>Upscales <paramref name="crop"/> and renders pixels passing <paramref name="isInk"/> black on
    /// white — Tesseract's preferred polarity. With <paramref name="levels"/> the crop is auto-levelled first
    /// (<see cref="AutoLevels"/>); with <paramref name="clean"/>, near-solid ink rows/columns (a border caught
    /// in the crop) are blanked and speckle is removed with a 3×3 majority filter.</summary>
    private static Bitmap InkMask(Bitmap crop, int targetW, Func<byte, byte, byte, bool> isInk, bool clean = false, bool levels = false)
    {
        var outBmp = UpscaleColor(crop, targetW); // new Bitmap(w, h) is 32bpp ARGB
        int w = outBmp.Width, h = outBmp.Height;
        var data = outBmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var buf = new byte[data.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            if (levels)
                AutoLevels(buf, data.Stride, w, h);
            var ink = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var i = y * data.Stride + x * 4; // BGRA
                    ink[y * w + x] = isInk(buf[i + 2], buf[i + 1], buf[i]);
                }
            if (clean)
                ink = CleanInk(ink, w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var i = y * data.Stride + x * 4;
                    byte v = ink[y * w + x] ? (byte)0 : (byte)255;
                    buf[i] = buf[i + 1] = buf[i + 2] = v;
                    buf[i + 3] = 255;
                }
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        }
        finally { outBmp.UnlockBits(data); }
        return outBmp;
    }

    /// <summary>Stretches a BGRA buffer's luminance range (1st–99.5th percentile) to full scale, all channels
    /// by the same factor so chroma is kept. The ink predicates use absolute thresholds, which a dim or
    /// low-contrast scan would otherwise fall under; a normally exposed crop is left nearly unchanged.</summary>
    private static void AutoLevels(byte[] buf, int stride, int w, int h)
    {
        var hist = new int[256];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var i = y * stride + x * 4;
                hist[(int)(0.114 * buf[i] + 0.587 * buf[i + 1] + 0.299 * buf[i + 2])]++;
            }
        int total = w * h, lo = 0, hi = 255;
        for (int acc = 0; lo < 255 && (acc += hist[lo]) < total * 0.01; lo++) { }
        for (int acc = 0; hi > 0 && (acc += hist[hi]) < total * 0.005; hi--) { }
        if (hi - lo < 40 || (lo <= 8 && hi >= 245)) return; // flat crop, or already full-range
        var lut = new byte[256];
        for (int v = 0; v < 256; v++)
            lut[v] = (byte)Math.Clamp((v - lo) * 255 / (hi - lo), 0, 255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var i = y * stride + x * 4;
                buf[i] = lut[buf[i]]; buf[i + 1] = lut[buf[i + 1]]; buf[i + 2] = lut[buf[i + 2]];
            }
    }

    private static bool[] CleanInk(bool[] ink, int w, int h)
    {
        const double SolidFraction = 0.6;
        for (int y = 0; y < h; y++)
        {
            int n = 0;
            for (int x = 0; x < w; x++) if (ink[y * w + x]) n++;
            if (n > w * SolidFraction) Array.Fill(ink, false, y * w, w);
        }
        for (int x = 0; x < w; x++)
        {
            int n = 0;
            for (int y = 0; y < h; y++) if (ink[y * w + x]) n++;
            if (n > h * SolidFraction) for (int y = 0; y < h; y++) ink[y * w + x] = false;
        }
        var outInk = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < w && yy < h && ink[yy * w + xx]) n++;
                    }
                outInk[y * w + x] = n >= 5;
            }
        return outInk;
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
