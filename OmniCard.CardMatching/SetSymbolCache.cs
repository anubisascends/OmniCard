using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Svg.Skia;
using OmniCard.Interfaces;

namespace OmniCard.CardMatching;

public class SetSymbolCache(IHttpClientFactory httpClientFactory, IDataPathService dataPathService, ILogger<SetSymbolCache> logger)
{
    private readonly string _cacheDir = dataPathService.SymbolsCacheDirectory;

    private static readonly Dictionary<string, string> RarityToFile = new(StringComparer.OrdinalIgnoreCase)
    {
        ["common"] = "C",
        ["uncommon"] = "U",
        ["rare"] = "R",
        ["mythic"] = "M",
    };

    // Caches the in-flight (and completed) load task per symbol, not just the resolved value, so
    // the many card rows/tiles that render at once and request the same symbol simultaneously all
    // await one shared task instead of each firing its own network request (a cache stampede that
    // previously fetched each set symbol ~8× on startup). Lazy guarantees the factory runs once.
    // The cached value is the on-disk SVG path (null when the symbol doesn't exist upstream).
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _cache = [];
    private readonly ConcurrentDictionary<string, string> _setNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a set code → set name mapping for tooltip display.</summary>
    public void RegisterSetName(string setCode, string setName) =>
        _setNames[setCode.ToUpperInvariant()] = setName;

    /// <summary>Look up set name by code, returns null if unknown.</summary>
    public string? GetSetName(string setCode) =>
        _setNames.TryGetValue(setCode.ToUpperInvariant(), out var name) ? name : null;

    public static string FormatRarityDisplay(string rarity) => rarity?.ToLowerInvariant() switch
    {
        "common" => "Common",
        "uncommon" => "Uncommon",
        "rare" => "Rare",
        "mythic" => "Mythic Rare",
        _ => rarity ?? ""
    };

    /// <summary>
    /// Resolve the on-disk path of a set-symbol SVG, downloading (and disk-caching) it on first use.
    /// Returns null for unsupported rarities or symbols that don't exist upstream. Concurrent callers
    /// for the same symbol coalesce onto a single download.
    /// </summary>
    public Task<string?> GetSymbolSvgPathAsync(string setCode, string rarity)
    {
        if (!RarityToFile.TryGetValue(rarity, out var rarityFile))
            return Task.FromResult<string?>(null);

        var code = setCode.ToUpperInvariant();
        var cacheKey = $"{code}_{rarityFile}";

        // GetOrAdd + Lazy: concurrent callers for the same symbol share one load task, so a screen
        // full of cards requesting the same set symbol triggers a single network request, not one
        // per card.
        return _cache.GetOrAdd(cacheKey,
            _ => new Lazy<Task<string?>>(() => LoadOrDownloadAsync(code, rarityFile))).Value;
    }

    private async Task<string?> LoadOrDownloadAsync(string setCode, string rarityFile)
    {
        var dir = Path.Combine(_cacheDir, setCode);
        var filePath = Path.Combine(dir, $"{rarityFile}.svg");
        var missingMarkerPath = filePath + ".missing";

        // Try loading from disk cache first
        if (File.Exists(filePath))
            return filePath;

        // A prior 404 was recorded — this set/rarity has no symbol upstream. Don't hit the network
        // again. Many promo/special sets (TSB, SLD, PWAR, …) have no vector at all, so without this
        // every one of them was re-requested (and 404'd) on every single launch — the bulk of the
        // startup HTTP burst that froze the UI.
        if (File.Exists(missingMarkerPath))
            return null;

        // Download from mtg-vectors
        try
        {
            var url = $"https://raw.githubusercontent.com/Investigamer/mtg-vectors/main/svg/set/{setCode}/{rarityFile}.svg";
            var client = httpClientFactory.CreateClient();
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to download set symbol {SetCode}/{Rarity}: {Status}", setCode, rarityFile, response.StatusCode);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Persist the miss so future launches skip the network for this set/rarity.
                    Directory.CreateDirectory(dir);
                    await File.WriteAllBytesAsync(missingMarkerPath, []);
                }
                return null;
            }

            var svgContent = await response.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(filePath, svgContent);

            return filePath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error downloading set symbol {SetCode}/{Rarity}", setCode, rarityFile);
            return null;
        }
    }

    /// <summary>
    /// Bulk-download set symbol SVGs for all given set codes.
    /// Downloads the common rarity variant for each set that isn't already cached on disk.
    /// </summary>
    public async Task PreloadSymbolsAsync(IReadOnlyList<(string SetCode, string SetName)> sets, IProgress<string>? progress = null)
    {
        var client = httpClientFactory.CreateClient();
        int downloaded = 0, skipped = 0;

        foreach (var (setCode, setName) in sets)
        {
            RegisterSetName(setCode, setName);

            // Download all 4 rarity variants
            foreach (var (_, rarityFile) in RarityToFile)
            {
                var code = setCode.ToUpperInvariant();
                var dir = Path.Combine(_cacheDir, code);
                var filePath = Path.Combine(dir, $"{rarityFile}.svg");
                var missingMarkerPath = filePath + ".missing";

                // Already have the symbol, or already know it doesn't exist upstream — skip the network.
                if (File.Exists(filePath) || File.Exists(missingMarkerPath))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var url = $"https://raw.githubusercontent.com/Investigamer/mtg-vectors/main/svg/set/{code}/{rarityFile}.svg";
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        Directory.CreateDirectory(dir);
                        var content = await response.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(filePath, content);
                        downloaded++;
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Persist the miss so it's skipped on future preloads and lazy loads.
                        Directory.CreateDirectory(dir);
                        await File.WriteAllBytesAsync(missingMarkerPath, []);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to download symbol {SetCode}/{Rarity}", code, rarityFile);
                }
            }

            if ((downloaded + skipped) % 40 == 0)
                progress?.Report($"Downloading set symbols... {downloaded} new, {skipped} cached");
        }

        // Clear in-memory cache so fresh SVGs are loaded on next use
        _cache.Clear();
        logger.LogInformation("Set symbol preload complete: {Downloaded} downloaded, {Skipped} already cached", downloaded, skipped);
        progress?.Report($"Set symbols updated: {downloaded} new, {skipped} already cached");
    }

    /// <summary>
    /// Rasterize a set's common-rarity symbol SVG to a 32×32 PNG (shape only — color is irrelevant
    /// for the perceptual hash used to disambiguate MTG sets). Returns null when the symbol doesn't
    /// exist upstream. Rendering uses SkiaSharp (no WPF / STA dependency), so it runs on any thread.
    /// </summary>
    public async Task<byte[]?> RasterizeSymbolAsync(string setCode)
    {
        // Reuse the shared download/disk-cache/negative-marker path (common rarity → C.svg).
        var filePath = await GetSymbolSvgPathAsync(setCode, "common");
        if (filePath is null)
            return null;

        try
        {
            using var skSvg = new SKSvg();
            if (skSvg.Load(filePath) is not { } picture)
                return null;

            var bounds = picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            var info = new SKImageInfo(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            // Stretch the SVG's bounding box to fill the 32×32 target, matching the previous
            // DrawImage-into-Rect behavior so the shape hash stays comparable across sets.
            canvas.Scale(32f / bounds.Width, 32f / bounds.Height);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error rasterizing set symbol SVG for {SetCode}", setCode);
            return null;
        }
    }
}
