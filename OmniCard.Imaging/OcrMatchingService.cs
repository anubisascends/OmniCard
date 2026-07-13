using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OmniCard.Imaging;

public sealed class OcrMatchingService : IOcrMatchingService
{
    private readonly IPerceptualHashService _hashService;
    private readonly ILogger<OcrMatchingService> _logger;
    private readonly OcrEngine? _ocrEngine;

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

    // OPTCG collector number crop region — bottom-right of the card (e.g., "OP15-043")
    internal static readonly (double X, double Y, double W, double H) OptcgCollectorNumberRegion =
        (0.40, 0.93, 0.45, 0.06);

    public Dictionary<string, ulong> SymbolHashes { get; set; } = [];

    public OcrMatchingService(IPerceptualHashService hashService, ILogger<OcrMatchingService> logger)
    {
        _hashService = hashService;
        _logger = logger;

        try
        {
            _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (_ocrEngine is null)
                _logger.LogWarning("Windows OCR engine unavailable — OCR matching disabled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Windows OCR engine");
        }
    }

    public async Task<OcrMatchResult> AnalyzeCardAsync(byte[] imageData)
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
            if (_ocrEngine is not null)
            {
                foreach (var region in NameCropRegions)
                {
                    var rect = ToPixelRect(region, width, height);
                    if (rect.Width < 10 || rect.Height < 5) continue;

                    var (text, confidence) = await OcrCroppedRegionAsync(bitmap, rect);
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

    private async Task<(string Text, double Confidence)> OcrCroppedRegionAsync(Bitmap source, Rectangle cropRect)
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

        try
        {
            // Convert System.Drawing.Bitmap to SoftwareBitmap for Windows.Media.Ocr
            // Use InMemoryRandomAccessStream to avoid needing WindowsRuntimeStreamExtensions
            using var ras = new InMemoryRandomAccessStream();
            using (var ms = new MemoryStream())
            {
                toOcr.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var bytes = ms.ToArray();
                using var writer = new DataWriter(ras);
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            ras.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(ras);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            var result = await _ocrEngine!.RecognizeAsync(softwareBitmap);

            var text = result.Text;

            // Use text length as a proxy for confidence — real card names are 2+ chars
            var confidence = string.IsNullOrWhiteSpace(text) ? 0.0
                : text.Trim().Length >= 3 ? 0.8
                : 0.3;

            return (text, confidence);
        }
        finally
        {
            if (needsDispose) toOcr.Dispose();
        }
    }

    public async Task<(string? CollectorNumber, double Confidence)> DetectOptcgCollectorNumberAsync(byte[] imageData)
    {
        if (_ocrEngine is null)
            return (null, 0);

        try
        {
            using var bitmap = new Bitmap(new MemoryStream(imageData));
            var rect = ToPixelRect(OptcgCollectorNumberRegion, bitmap.Width, bitmap.Height);
            if (rect.Width < 10 || rect.Height < 5)
                return (null, 0);

            var (text, confidence) = await OcrCroppedRegionAsync(bitmap, rect);
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
                _logger.LogInformation("OPTCG collector number detected: {Number} (raw: {Raw})", collectorNumber, text);
                return (collectorNumber, 0.95);
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
        resized.Save(ms, ImageFormat.Png);
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
}
