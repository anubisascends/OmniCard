using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace OmniCard.Services;

public sealed class ScanImageCache
{
    public static ScanImageCache? Instance { get; private set; }

    public static void Initialize(ScanImageCache instance) => Instance = instance;

    private readonly ILogger<ScanImageCache> _logger;
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Path, BitmapImage Image)>> _map = new();
    private readonly LinkedList<(string Path, BitmapImage Image)> _order = new();

    public string TempScansDirectory { get; }

    public ScanImageCache(IDataPathService dataPathService, ILogger<ScanImageCache> logger, int capacity = 200)
    {
        TempScansDirectory = dataPathService.TempScansDirectory;
        _logger = logger;
        _capacity = capacity;
    }

    public int Count => _map.Count;

    public BitmapImage? GetImage(string imagePath)
    {
        if (_map.TryGetValue(imagePath, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Image;
        }

        if (!File.Exists(imagePath))
        {
            _logger.LogWarning("Scan image not found: {Path}", imagePath);
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 500;
            bmp.StreamSource = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            bmp.EndInit();
            bmp.StreamSource.Dispose();
            bmp.Freeze();

            var newNode = _order.AddFirst((imagePath, bmp));
            _map[imagePath] = newNode;

            if (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _map.Remove(last.Value.Path);
                _order.RemoveLast();
                _logger.LogDebug("Evicted oldest cached image: {Path}", last.Value.Path);
            }

            return bmp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load scan image: {Path}", imagePath);
            return null;
        }
    }

    public void Evict(string imagePath)
    {
        if (_map.Remove(imagePath, out var node))
            _order.Remove(node);
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }
}
