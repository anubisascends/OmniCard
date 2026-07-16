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
            // Tesseract's real mean confidence (0..1) — far better than the previous
            // text-length proxy, and it flows into the downstream match scoring.
            var confidence = string.IsNullOrWhiteSpace(text) ? 0.0 : page.GetMeanConfidence();

            return (text.Trim(), confidence);
        }
        finally
        {
            engine.SetVariable("tessedit_char_whitelist", string.Empty);
            ReturnEngine(engine);
            if (needsDispose) toOcr.Dispose();
        }
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

            // Extract collector number pattern: 2-4 letters + 2 digits + dash + 3 digits
            // e.g., OP15-043, EB01-021, ST01-001
            var match = System.Text.RegularExpressions.Regex.Match(
                text, @"([A-Za-z]{2,4}\d{2})\s*[-—]\s*(\d{2,3})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
