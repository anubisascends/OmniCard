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

    /// <summary>Drop a file with unique content — like a real scan, no two drops share bytes (identical
    /// bytes are what duplicate detection keys on; use <see cref="DropBytes"/> for that).</summary>
    private string Drop(string relative, int bytes = 64)
    {
        var content = new byte[Math.Max(bytes, 16)];
        Guid.NewGuid().TryWriteBytes(content);
        Array.Fill(content, (byte)7, 16, content.Length - 16);
        return DropBytes(relative, content);
    }

    private string DropBytes(string relative, byte[] content)
    {
        // Relative paths are written Windows-style ("Box 12\a.jpg"); map them to this OS's separator.
        var path = Path.Combine(_mtg, relative.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
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

    // --- Exact duplicates (capture software re-saving a run) ---

    private static byte[] Scan(int n)
    {
        var bytes = new byte[256];
        new Random(n).NextBytes(bytes);
        return bytes;
    }

    /// <summary>Drop scans 1..count as 0001.png… into <paramref name="folder"/> and pick them up.</summary>
    private void DropRun(string folder, int count, Func<int, byte[]>? content = null)
    {
        for (var n = 1; n <= count; n++)
            DropBytes($@"{folder}\{n:0000}.png", (content ?? Scan)(n));
        Settle();
    }

    [Fact]
    public void ResavedRun_IsSetAsideAsDuplicates_OfTheOriginals()
    {
        DropRun("Tray", 5);
        DropRun("Tray", 5); // the capture software writes the same files out again

        using var db = _f.Db();
        var batch = Assert.Single(db.ScanBatches);
        var items = db.ScanBatchItems.OrderBy(i => i.Sequence).ToList();
        Assert.Equal(10, items.Count);
        var originals = items.Take(5).ToList();
        var copies = items.Skip(5).ToList();
        Assert.All(originals, i => Assert.Equal(ScanBatchItemState.Open, i.State));
        Assert.All(copies, i => Assert.Equal(ScanBatchItemState.Duplicate, i.State));
        Assert.Equal(originals.Select(i => i.Id), copies.Select(i => i.DuplicateOfItemId!.Value));
        Assert.Equal(originals.Select(i => i.ContentHash), copies.Select(i => i.ContentHash));
        Assert.All(originals, i => Assert.Equal(64, i.ContentHash!.Length));

        // Copies keep no stored image, and their files still leave the watched folder.
        Assert.All(copies, i => Assert.Equal("", i.StoredFileName));
        Assert.Equal(5, Directory.GetFiles(_f.Storage.BatchDirectory(batch.Id)).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_mtg, "Tray")));
        Assert.True(File.Exists(Path.Combine(_mtg, "_processed", "Tray", "0001 (2).png")));
    }

    [Fact]
    public void SameNameDifferentBytes_IsARealScan()
    {
        DropRun("Tray", 3);
        DropRun("Tray", 3, n => Scan(n + 100)); // rescanned: same file names, new captures

        using var db = _f.Db();
        Assert.Equal(6, db.ScanBatchItems.Count(i => i.State == ScanBatchItemState.Open));
        Assert.Equal(0, db.ScanBatchItems.Count(i => i.State == ScanBatchItemState.Duplicate));
    }

    [Fact]
    public void OneByteDifference_IsNotADuplicate()
    {
        var bytes = Scan(1);
        DropBytes(@"Tray\a.png", bytes);
        Settle();
        var changed = (byte[])bytes.Clone();
        changed[^1] ^= 1;
        DropBytes(@"Tray\b.png", changed);
        Settle();

        using var db = _f.Db();
        Assert.Equal(2, db.ScanBatchItems.Count(i => i.State == ScanBatchItemState.Open));
    }

    [Fact]
    public void CopyUnderADifferentName_IsStillADuplicate()
    {
        DropBytes(@"Tray\0001.png", Scan(1));
        Settle();
        DropBytes(@"Tray\IMG_9999.png", Scan(1));
        Settle();

        using var db = _f.Db();
        var copy = db.ScanBatchItems.Single(i => i.OriginalFileName == "IMG_9999.png");
        Assert.Equal(ScanBatchItemState.Duplicate, copy.State);
    }

    [Fact]
    public void Duplicate_DoesNotReopenAReadyBatch()
    {
        DropRun("Tray", 2);
        using (var db = _f.Db())
        {
            var b = db.ScanBatches.Single();
            b.Status = ScanBatchStatus.Ready;
            b.ReadyUtc = DateTime.UtcNow;
            db.SaveChanges();
        }

        DropRun("Tray", 2);

        using var check = _f.Db();
        var batch = check.ScanBatches.Single();
        Assert.Equal(ScanBatchStatus.Ready, batch.Status);
        Assert.NotNull(batch.ReadyUtc);
    }

    [Fact]
    public void CopyOfACommittedBatch_LandsInANewBatchOfOnlyDuplicates()
    {
        DropRun("Tray", 3);
        using (var db = _f.Db())
        {
            db.ScanBatchItems.ToList().ForEach(i => i.State = ScanBatchItemState.Committed);
            db.ScanBatches.Single().Status = ScanBatchStatus.Committed;
            db.SaveChanges();
        }

        DropRun("Tray", 3);

        using var check = _f.Db();
        var second = check.ScanBatches.OrderBy(b => b.Id).Last();
        Assert.Equal("Tray (2)", second.Name);
        var items = check.ScanBatchItems.Where(i => i.ScanBatchId == second.Id).ToList();
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal(ScanBatchItemState.Duplicate, i.State));
    }

    [Fact]
    public void CopiesOfRemovedOrDiscardedItems_ArePickedUpAgain()
    {
        DropRun("Tray", 2);
        using (var db = _f.Db())
        {
            var items = db.ScanBatchItems.OrderBy(i => i.Sequence).ToList();
            items[0].State = ScanBatchItemState.Removed;
            items[1].State = ScanBatchItemState.Removed;
            db.ScanBatches.Single().Status = ScanBatchStatus.Discarded;
            db.SaveChanges();
        }

        DropRun("Tray", 2);

        using var check = _f.Db();
        Assert.Equal(2, check.ScanBatchItems.Count(i => i.State == ScanBatchItemState.Open));
        Assert.Equal(0, check.ScanBatchItems.Count(i => i.State == ScanBatchItemState.Duplicate));
    }

    [Fact]
    public void SameBytesInAnotherGamesFolder_IsNotADuplicate()
    {
        var pokemon = Path.Combine(_f.ScanRoot, "Pokemon");
        Directory.CreateDirectory(Path.Combine(pokemon, "Tray"));
        _settings.Folders.Add(new() { Game = CardGame.Pokemon, Path = pokemon });
        DropRun("Tray", 1);
        File.WriteAllBytes(Path.Combine(pokemon, "Tray", "0001.png"), Scan(1));
        Settle();

        using var db = _f.Db();
        Assert.Equal(2, db.ScanBatchItems.Count(i => i.State == ScanBatchItemState.Open));
    }

    [Fact]
    public void ContentHash_IsSha256Hex()
    {
        var path = DropBytes("x.png", "abc"u8.ToArray());
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ScanFolderIngestor.ContentHash(path));
    }
}
