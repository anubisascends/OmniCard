using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;

namespace OmniCard.Imaging;

public sealed class CardArtCache
{
    public static CardArtCache? Instance { get; private set; }

    public static void Initialize(CardArtCache instance) => Instance = instance;

    private readonly ILogger<CardArtCache> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly int _capacity;
    // Persistent on-disk cache of downloaded (remote-URI) art. The in-memory LRU is small and
    // volatile, so without this every launch re-downloaded a Scryfall image for each visible card
    // (~130 requests on a full collection view). With it, art is fetched from the network at most
    // once ever and served from disk thereafter. Null disables disk caching (e.g. in unit tests).
    private readonly string? _diskCacheDir;
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Image)>> _map = new();
    private readonly LinkedList<(string Key, BitmapImage Image)> _order = new();

    public CardArtCache(ILogger<CardArtCache> logger, IHttpClientFactory httpClientFactory,
        int capacity = 200, IDataPathService? dataPathService = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _diskCacheDir = dataPathService?.ArtCacheDirectory;
        _capacity = capacity;
    }

    public int Count => _map.Count;

    public BitmapImage? GetImage(string? localPath, string? imageUri)
    {
        // Determine cache key and source
        string? key = null;
        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            key = localPath;
        else if (!string.IsNullOrEmpty(imageUri))
            key = imageUri;

        if (key is null)
            return null;

        // Check cache
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Image;
        }

        // Load image
        try
        {
            BitmapImage bmp;
            if (key == localPath)
            {
                bmp = LoadFromFile(localPath!);
            }
            else
            {
                bmp = LoadFromUri(imageUri!);
            }

            var newNode = _order.AddFirst((key, bmp));
            _map[key] = newNode;

            if (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _map.Remove(last.Value.Key);
                _order.RemoveLast();
            }

            return bmp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load card art: {Key}", key);
            return null;
        }
    }

    private static BitmapImage LoadFromFile(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 500;
        bmp.StreamSource = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        bmp.EndInit();
        bmp.StreamSource.Dispose();
        bmp.Freeze();
        return bmp;
    }

    private BitmapImage LoadFromUri(string uri)
    {
        var diskPath = DiskPathFor(uri);
        if (TryReadDiskCache(diskPath) is { } cachedBytes)
            return LoadFromBytes(cachedBytes);

        var client = _httpClientFactory.CreateClient();
        var bytes = client.GetByteArrayAsync(uri).GetAwaiter().GetResult();
        WriteDiskCache(diskPath, bytes);
        return LoadFromBytes(bytes);
    }

    /// <summary>Disk-cache path for a remote art URI, or null if disk caching is disabled.</summary>
    private string? DiskPathFor(string uri)
    {
        if (_diskCacheDir is null) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri)));
        return Path.Combine(_diskCacheDir, hash + ".img");
    }

    private byte[]? TryReadDiskCache(string? diskPath)
    {
        if (diskPath is null) return null;
        try
        {
            return File.Exists(diskPath) ? File.ReadAllBytes(diskPath) : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read card art disk cache: {Path}", diskPath);
            return null;
        }
    }

    private async Task<byte[]?> TryReadDiskCacheAsync(string? diskPath)
    {
        if (diskPath is null || !File.Exists(diskPath)) return null;
        try
        {
            return await File.ReadAllBytesAsync(diskPath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read card art disk cache: {Path}", diskPath);
            return null;
        }
    }

    /// <summary>Persist downloaded art bytes to the disk cache (best-effort; failures are ignored).
    /// Writes to a temp file then moves into place so a crashed/partial write can't leave a corrupt
    /// cache entry that would fail to decode on the next launch.</summary>
    private void WriteDiskCache(string? diskPath, byte[] bytes)
    {
        if (diskPath is null || _diskCacheDir is null) return;
        try
        {
            Directory.CreateDirectory(_diskCacheDir);
            var tmp = diskPath + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, diskPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write card art disk cache: {Path}", diskPath);
        }
    }

    private static BitmapImage LoadFromBytes(byte[] bytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 500;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.StreamSource.Dispose();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Async twin of <see cref="GetImage"/>. The network fetch runs off the calling thread;
    /// the continuation resumes on the caller's synchronization context (the UI thread in the
    /// app), so cache mutation stays single-threaded. Call this from the UI thread only.
    /// </summary>
    public async Task<BitmapImage?> GetImageAsync(string? localPath, string? imageUri)
    {
        string? key = null;
        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            key = localPath;
        else if (!string.IsNullOrEmpty(imageUri))
            key = imageUri;

        if (key is null)
            return null;

        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Image;
        }

        try
        {
            BitmapImage bmp;
            if (key == localPath)
            {
                bmp = LoadFromFile(localPath!);
            }
            else
            {
                var diskPath = DiskPathFor(imageUri!);
                if (await TryReadDiskCacheAsync(diskPath).ConfigureAwait(true) is { } cachedBytes)
                {
                    bmp = LoadFromBytes(cachedBytes);
                }
                else
                {
                    var client = _httpClientFactory.CreateClient();
                    var bytes = await client.GetByteArrayAsync(imageUri!).ConfigureAwait(true);
                    WriteDiskCache(diskPath, bytes);
                    bmp = LoadFromBytes(bytes);
                }
            }

            var newNode = _order.AddFirst((key, bmp));
            _map[key] = newNode;

            if (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _map.Remove(last.Value.Key);
                _order.RemoveLast();
            }

            return bmp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load card art (async): {Key}", key);
            return null;
        }
    }

    public void Evict(string key)
    {
        if (_map.Remove(key, out var node))
            _order.Remove(node);
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }
}
