using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace OmniCard.Imaging;

public sealed class CardArtCache
{
    public static CardArtCache? Instance { get; private set; }

    public static void Initialize(CardArtCache instance) => Instance = instance;

    private readonly ILogger<CardArtCache> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Image)>> _map = new();
    private readonly LinkedList<(string Key, BitmapImage Image)> _order = new();

    public CardArtCache(ILogger<CardArtCache> logger, IHttpClientFactory httpClientFactory, int capacity = 100)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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
        var client = _httpClientFactory.CreateClient();
        var bytes = client.GetByteArrayAsync(uri).GetAwaiter().GetResult();
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
