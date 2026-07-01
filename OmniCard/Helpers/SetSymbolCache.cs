using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using OmniCard.Services;

namespace OmniCard.Helpers;

public class SetSymbolCache(IHttpClientFactory httpClientFactory, IDataPathService dataPathService, ILogger<SetSymbolCache> logger)
{
    private readonly string _cacheDir = dataPathService.SymbolsCacheDirectory;

    private static readonly WpfDrawingSettings SvgSettings = new()
    {
        IncludeRuntime = true,
        TextAsGeometry = false,
    };

    private static readonly Dictionary<string, string> RarityToFile = new(StringComparer.OrdinalIgnoreCase)
    {
        ["common"] = "C",
        ["uncommon"] = "U",
        ["rare"] = "R",
        ["mythic"] = "M",
    };

    private readonly ConcurrentDictionary<string, DrawingImage?> _cache = [];
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

    public async Task<DrawingImage?> GetSetSymbolAsync(string setCode, string rarity)
    {
        if (!RarityToFile.TryGetValue(rarity, out var rarityFile))
            return null;

        var cacheKey = $"{setCode.ToUpperInvariant()}_{rarityFile}";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var image = await LoadOrDownloadAsync(setCode.ToUpperInvariant(), rarityFile);
        _cache.TryAdd(cacheKey, image);
        return image;
    }

    private async Task<DrawingImage?> LoadOrDownloadAsync(string setCode, string rarityFile)
    {
        var dir = Path.Combine(_cacheDir, setCode);
        var filePath = Path.Combine(dir, $"{rarityFile}.svg");

        // Try loading from disk cache first
        if (File.Exists(filePath))
            return LoadSvgFromFile(filePath);

        // Download from mtg-vectors
        try
        {
            var url = $"https://raw.githubusercontent.com/Investigamer/mtg-vectors/main/svg/set/{setCode}/{rarityFile}.svg";
            var client = httpClientFactory.CreateClient();
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to download set symbol {SetCode}/{Rarity}: {Status}", setCode, rarityFile, response.StatusCode);
                return null;
            }

            var svgContent = await response.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(filePath, svgContent);

            return LoadSvgFromFile(filePath);
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

                if (File.Exists(filePath))
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

    private static DrawingImage? LoadSvgFromFile(string filePath)
    {
        try
        {
            using var reader = new FileSvgReader(SvgSettings);
            var drawing = reader.Read(filePath);
            if (drawing == null) return null;

            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public async Task<System.Drawing.Bitmap?> RasterizeSymbolAsync(string setCode)
    {
        var dir = Path.Combine(_cacheDir, setCode.ToUpperInvariant());
        var filePath = Path.Combine(dir, "C.svg"); // Common rarity — shape only, color irrelevant for pHash

        // Download if not cached
        if (!File.Exists(filePath))
        {
            try
            {
                var url = $"https://raw.githubusercontent.com/Investigamer/mtg-vectors/main/svg/set/{setCode.ToUpperInvariant()}/C.svg";
                var client = httpClientFactory.CreateClient();
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var svgContent = await response.Content.ReadAsByteArrayAsync();
                Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(filePath, svgContent);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error downloading set symbol SVG for {SetCode}", setCode);
                return null;
            }
        }

        // Rasterize SVG to 32x32 bitmap — must run on STA thread for WPF rendering
        try
        {
            System.Drawing.Bitmap? bmp = null;

            // WPF rendering requires an STA thread with a Dispatcher
            void RenderOnSta()
            {
                using var reader = new FileSvgReader(SvgSettings);
                var drawing = reader.Read(filePath);
                if (drawing is null) return;

                var drawingImage = new DrawingImage(drawing);
                drawingImage.Freeze();

                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(drawingImage, new System.Windows.Rect(0, 0, 32, 32));
                }
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(visual);

                var pixels = new byte[32 * 32 * 4];
                rtb.CopyPixels(pixels, 32 * 4, 0);

                bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bmpData = bmp.LockBits(
                    new System.Drawing.Rectangle(0, 0, 32, 32),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
                bmp.UnlockBits(bmpData);
            }

            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher &&
                !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(RenderOnSta);
            }
            else
            {
                RenderOnSta();
            }

            return bmp;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error rasterizing set symbol SVG for {SetCode}", setCode);
            return null;
        }
    }
}
