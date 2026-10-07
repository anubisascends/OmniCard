using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Settings;
using OmniCard.Web.Services.ScanBatches;
using Xunit;

namespace OmniCard.Tests.Web.ScanBatches;

public sealed class ScanFolderIngestorTests : IDisposable
{
    private readonly ScanBatchFixture _f = new();
    private readonly ScanFolderIngestor _ingestor;
    private readonly string _mtg;
    private readonly ScanFolderSettings _settings;

    public ScanFolderIngestorTests()
    {
        _ingestor = new ScanFolderIngestor(_f.Factory, _f.Storage, _f.Clock, NullLogger<ScanFolderIngestor>.Instance);
        _mtg = Path.Combine(_f.ScanRoot, "Mtg");
        Directory.CreateDirectory(_mtg);
        _settings = new ScanFolderSettings
        {
            Enabled = true,
            Folders = [new() { Game = CardGame.Mtg, Path = _mtg, IsFoil = true, Condition = "LP", Language = "ja", SetCodes = ["neo", "dmu"], DefaultContainerId = 3 }],
        };
    }

    public void Dispose() => _f.Dispose();

    private string Drop(string relative, int bytes = 64)
    {
        var path = Path.Combine(_mtg, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)7, bytes).ToArray());
        return path;
    }

    /// <summary>Two passes, the stability window apart — what it takes to pick up a finished file.</summary>
    private int Settle()
    {
        var n = _ingestor.RunOnce(_settings);
        _f.Clock.Advance(ScanFolderIngestor.StableFor + TimeSpan.FromSeconds(1));
        return n + _ingestor.RunOnce(_settings);
    }

    [Fact]
    public void Subfolder_BecomesBatch_WithFolderSettingsSnapshot()
    {
        Drop(@"Box 12\a.jpg");
        Drop(@"Box 12\b.png");

        Assert.Equal(2, Settle());

        using var db = _f.Db();
        var batch = Assert.Single(db.ScanBatches);
        Assert.Equal("Box 12", batch.Name);
        Assert.Equal("Box 12", batch.FolderKey);
        Assert.Equal(CardGame.Mtg, batch.Game);
        Assert.Equal(ScanBatchStatus.Collecting, batch.Status);
        Assert.True(batch.IsFoil);
        Assert.Equal("LP", batch.Condition);
        Assert.Equal("ja", batch.Language);
        Assert.Equal(["neo", "dmu"], batch.SetCodeList);
        Assert.Equal(3, batch.DefaultContainerId);

        var items = db.ScanBatchItems.OrderBy(i => i.Sequence).ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i =>
        {
            Assert.Equal(ScanBatchItemStatus.Pending, i.Status);
            Assert.Equal("LP", i.Condition);
            Assert.True(i.IsFoil);
            Assert.True(File.Exists(_f.Storage.ItemPath(batch.Id, i.StoredFileName)));
        });
        Assert.Equal([1, 2], items.Select(i => i.Sequence));
    }

    [Fact]
    public void Originals_MoveToProcessed()
    {
        var src = Drop(@"Box 12\a.jpg");
        Settle();

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(Path.Combine(_mtg, "_processed", "Box 12", "a.jpg")));
    }

    [Fact]
    public void ProcessedNameCollision_GetsSuffix()
    {
        Directory.CreateDirectory(Path.Combine(_mtg, "_processed", "Box 12"));
        File.WriteAllText(Path.Combine(_mtg, "_processed", "Box 12", "a.jpg"), "earlier");
        Drop(@"Box 12\a.jpg");
        Settle();

        Assert.True(File.Exists(Path.Combine(_mtg, "_processed", "Box 12", "a (2).jpg")));
    }

    [Fact]
    public void RootFiles_GoToDateNamedBatch()
    {
        Drop("loose.jpg");
        Settle();

        using var db = _f.Db();
        Assert.Equal("2026-10-06", Assert.Single(db.ScanBatches).Name);
    }

    [Fact]
    public void NestedFolders_BelongToTopLevelBatch()
    {
        Drop(@"Box 12\page 1\a.jpg");
        Drop(@"Box 12\page 2\b.jpg");
        Settle();

        using var db = _f.Db();
        Assert.Equal("Box 12", Assert.Single(db.ScanBatches).Name);
        Assert.Equal(2, db.ScanBatchItems.Count());
    }

    [Fact]
    public void IgnoresNonImages_UnderscoreFolders_AndTempFiles()
    {
        Drop(@"Box 12\notes.txt");
        Drop(@"Box 12\scan.tmp");
        Drop(@"_archive\old.jpg");
        Drop(@".hidden\x.jpg");

        Assert.Equal(0, Settle());
        using var db = _f.Db();
        Assert.Empty(db.ScanBatches);
    }

    [Fact]
    public void GrowingFile_WaitsUntilStable()
    {
        var path = Drop(@"Box 12\a.jpg", bytes: 10);
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromSeconds(5));
        File.AppendAllText(path, "more"); // still being written
        Assert.Equal(0, _ingestor.RunOnce(_settings));
        Assert.True(_ingestor.HasUnsettledFiles);

        _f.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, _ingestor.RunOnce(_settings));
    }

    [Fact]
    public void LockedFile_IsSkippedUntilReleased()
    {
        var path = Drop(@"Box 12\a.jpg");
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromSeconds(5));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(0, _ingestor.RunOnce(_settings));

        Assert.Equal(1, _ingestor.RunOnce(_settings));
    }

    [Fact]
    public void LateFile_AppendsToOpenBatch_AndReopensReadyBatch()
    {
        Drop(@"Box 12\a.jpg");
        Settle();
        using (var db = _f.Db())
        {
            var b = db.ScanBatches.Single();
            b.Status = ScanBatchStatus.Ready;
            b.ReadyUtc = DateTime.UtcNow;
            db.SaveChanges();
        }

        Drop(@"Box 12\b.jpg");
        Settle();

        using var check = _f.Db();
        var batch = Assert.Single(check.ScanBatches);
        Assert.Equal(ScanBatchStatus.Matching, batch.Status);
        Assert.Null(batch.ReadyUtc);
        Assert.Equal(2, check.ScanBatchItems.Count());
        Assert.Equal(2, check.ScanBatchItems.Max(i => i.Sequence));
    }

    [Fact]
    public void FileAfterCommit_StartsNumberedBatch()
    {
        Drop(@"Box 12\a.jpg");
        Settle();
        using (var db = _f.Db())
        {
            db.ScanBatches.Single().Status = ScanBatchStatus.Committed;
            db.SaveChanges();
        }

        Drop(@"Box 12\b.jpg");
        Settle();

        using var check = _f.Db();
        Assert.Equal(["Box 12", "Box 12 (2)"], check.ScanBatches.OrderBy(b => b.Id).Select(b => b.Name).ToList());
    }

    [Fact]
    public void DisabledFolder_IsIgnored()
    {
        _settings.Folders[0].Enabled = false;
        Drop(@"Box 12\a.jpg");
        Assert.Equal(0, Settle());
    }

    [Fact]
    public void MissingFolder_ReportsError()
    {
        _settings.Folders[0].Path = Path.Combine(_f.ScanRoot, "nope");
        _ingestor.RunOnce(_settings);
        Assert.Contains("not found", _ingestor.FolderErrors[CardGame.Mtg]);
    }
}
