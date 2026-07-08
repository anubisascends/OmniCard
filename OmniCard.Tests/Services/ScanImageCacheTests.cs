using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Imaging;
using Xunit;

namespace OmniCard.Tests.Services;

public class ScanImageCacheTests
{
    private static string CreateTestImage(string directory, string filename = "test.png")
    {
        var path = Path.Combine(directory, filename);
        var bmp = new RenderTargetBitmap(100, 100, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create);
        encoder.Save(fs);
        return path;
    }

    private static ScanImageCache CreateCache(string tempDir, int capacity = 20)
    {
        var pathService = new DataPathService(tempDir);
        return new ScanImageCache(pathService, NullLogger<ScanImageCache>.Instance, capacity);
    }

    [StaFact]
    public void GetImage_LoadsFromDisk_ReturnsFrozenBitmap()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cache = CreateCache(dir);
        try
        {
            var path = CreateTestImage(dir);
            var result = cache.GetImage(path);
            Assert.NotNull(result);
            Assert.True(result!.IsFrozen);
            Assert.Equal(1, cache.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [StaFact]
    public void GetImage_ReturnsCachedInstance_OnSecondCall()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cache = CreateCache(dir);
        try
        {
            var path = CreateTestImage(dir);
            var first = cache.GetImage(path);
            var second = cache.GetImage(path);
            Assert.Same(first, second);
            Assert.Equal(1, cache.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [StaFact]
    public void GetImage_ReturnsNull_WhenFileDoesNotExist()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cache = CreateCache(dir);
        try
        {
            var result = cache.GetImage(@"C:\nonexistent\fake.png");
            Assert.Null(result);
            Assert.Equal(0, cache.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [StaFact]
    public void Evict_RemovesEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cache = CreateCache(dir);
        try
        {
            var path = CreateTestImage(dir);
            cache.GetImage(path);
            Assert.Equal(1, cache.Count);
            cache.Evict(path);
            Assert.Equal(0, cache.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [StaFact]
    public void Clear_RemovesAllEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cache = CreateCache(dir);
        try
        {
            var p1 = CreateTestImage(dir, "a.png");
            var p2 = CreateTestImage(dir, "b.png");
            cache.GetImage(p1);
            cache.GetImage(p2);
            Assert.Equal(2, cache.Count);
            cache.Clear();
            Assert.Equal(0, cache.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [StaFact]
    public void GetImage_EvictsOldest_WhenOverCapacity()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cache = CreateCache(dir, capacity: 2);
        try
        {
            var p1 = CreateTestImage(dir, "1.png");
            var p2 = CreateTestImage(dir, "2.png");
            var p3 = CreateTestImage(dir, "3.png");
            cache.GetImage(p1);
            cache.GetImage(p2);
            Assert.Equal(2, cache.Count);
            cache.GetImage(p3);
            Assert.Equal(2, cache.Count); // oldest evicted
        }
        finally { Directory.Delete(dir, true); }
    }
}
