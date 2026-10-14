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
        _ingestor = NewIngestor();
        _mtg = Path.Combine(_f.ScanRoot, "Mtg");
        Directory.CreateDirectory(_mtg);
        _settings = new ScanFolderSettings
        {
            Enabled = true,
            QuietPeriodSeconds = 90,
            Folders = [new() { Game = CardGame.Mtg, Path = _mtg, IsFoil = true, Condition = "LP", Language = "ja", SetCodes = ["neo", "dmu"], DefaultContainerId = 3 }],
        };
    }

    public void Dispose() => _f.Dispose();

    private ScanFolderIngestor NewIngestor() =>
        new(_f.Factory, _f.Storage, _f.Clock, NullLogger<ScanFolderIngestor>.Instance);

    private TimeSpan Quiet => TimeSpan.FromSeconds(_settings.QuietPeriodSeconds);

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
        var path = Path.Combine(_mtg, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Notice what's in the folder, then let the quiet period run out and take it in.</summary>
    private int Settle()
    {
        var n = _ingestor.RunOnce(_settings);
        _f.Clock.Advance(Quiet + TimeSpan.FromSeconds(1));
        return n + _ingestor.RunOnce(_settings);
    }

    private ScanBatch SingleBatch()
    {
        using var db = _f.Db();
        return Assert.Single(db.ScanBatches);
    }

    private void SetStatus(int id, ScanBatchStatus status)
    {
        using var db = _f.Db();
        db.ScanBatches.Single(b => b.Id == id).Status = status;
        db.SaveChanges();
    }

    // --- waiting: hands off the files ---

    [Fact]
    public void NewFiles_WaitInTheFolder_UntilTheBatchStarts()
    {
        var a = Drop(@"Box 12\a.jpg");
        var b = Drop(@"Box 12\b.png");

        Assert.Equal(0, _ingestor.RunOnce(_settings));

        var batch = SingleBatch();
        Assert.Equal("Box 12", batch.Name);
        Assert.Equal(ScanBatchStatus.Collecting, batch.Status);
        Assert.Equal(2, batch.WaitingFiles);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.False(Directory.Exists(Path.Combine(_mtg, "_processed")));
        using var db = _f.Db();
        Assert.Empty(db.ScanBatchItems);
        Assert.True(_ingestor.HasWaitingBatches);
    }

    [Fact]
    public void WaitingFiles_AreNeverOpened()
    {
        // The scanner software holds the file exclusively; noticing it must not need any access.
        var path = Drop(@"Box 12\a.jpg");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromSeconds(30));
        _ingestor.RunOnce(_settings);

        Assert.Equal(1, SingleBatch().WaitingFiles);
        Assert.False(_ingestor.FolderErrors.ContainsKey(CardGame.Mtg));
    }

    [Fact]
    public void QuietPeriod_StartsTheBatch_WithFolderSettingsSnapshot()
    {
        var a = Drop(@"Box 12\a.jpg");
        Drop(@"Box 12\b.png");

        Assert.Equal(2, Settle());

        var batch = SingleBatch();
        Assert.Equal("Box 12", batch.FolderKey);
        Assert.Equal(CardGame.Mtg, batch.Game);
        Assert.Equal(ScanBatchStatus.Matching, batch.Status);
        Assert.Equal(0, batch.WaitingFiles);
        Assert.True(batch.IsFoil);
        Assert.Equal("LP", batch.Condition);
        Assert.Equal("ja", batch.Language);
        Assert.Equal(["neo", "dmu"], batch.SetCodeList);
        Assert.Equal(3, batch.DefaultContainerId);

        using var db = _f.Db();
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

        // Originals moved to _processed, and each item remembers where.
        Assert.False(File.Exists(a));
        var processed = Path.Combine(_mtg, "_processed", "Box 12", "a.jpg");
        Assert.True(File.Exists(processed));
        Assert.Equal(processed, items[0].ProcessedPath);
    }

    [Fact]
    public void AnyChange_RestartsTheBatchTimer_IncludingARename()
    {
        var path = Drop(@"Box 12\scan_tmp_0001.jpg");
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromSeconds(60));

        // The scanner software renames its files when it completes the run.
        File.Move(path, Path.Combine(_mtg, "Box 12", "0001.jpg"));
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromSeconds(60)); // 120s since the first file, 60s since the rename
        _ingestor.RunOnce(_settings);
        Assert.Equal(ScanBatchStatus.Collecting, SingleBatch().Status);
        Assert.True(File.Exists(Path.Combine(_mtg, "Box 12", "0001.jpg")));

        _f.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(1, _ingestor.RunOnce(_settings));
        Assert.Equal(ScanBatchStatus.Matching, SingleBatch().Status);
    }

    [Fact]
    public void EachBatch_CountsDownOnItsOwn()
    {
        Drop(@"Box 1\a.jpg");
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromSeconds(60));
        Drop(@"Box 2\a.jpg");
        _ingestor.RunOnce(_settings);

        _f.Clock.Advance(TimeSpan.FromSeconds(31)); // Box 1 is due, Box 2 has 59s to go
        _ingestor.RunOnce(_settings);
        using (var db = _f.Db())
        {
            var status = db.ScanBatches.ToDictionary(b => b.Name, b => b.Status);
            Assert.Equal(ScanBatchStatus.Matching, status["Box 1"]);
            Assert.Equal(ScanBatchStatus.Collecting, status["Box 2"]);
        }

        _f.Clock.Advance(TimeSpan.FromSeconds(60));
        _ingestor.RunOnce(_settings);
        using var check = _f.Db();
        Assert.All(check.ScanBatches, b => Assert.Equal(ScanBatchStatus.Matching, b.Status));
    }

    [Fact]
    public void AutoProcessOff_WaitsForRun_ThenStarts()
    {
        _settings.AutoProcess = false;
        var path = Drop(@"Box 12\a.jpg");
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, _ingestor.RunOnce(_settings));
        Assert.True(File.Exists(path));
        Assert.True(_ingestor.HasWaitingBatches);

        SetStatus(SingleBatch().Id, ScanBatchStatus.Starting); // Run pressed

        Assert.Equal(1, _ingestor.RunOnce(_settings));
        Assert.Equal(ScanBatchStatus.Matching, SingleBatch().Status);
        Assert.False(File.Exists(path));
        Assert.False(_ingestor.HasWaitingBatches);
    }

    [Fact]
    public void Run_StartsTheBatchBeforeItsTimer()
    {
        Drop(@"Box 12\a.jpg");
        _ingestor.RunOnce(_settings);
        SetStatus(SingleBatch().Id, ScanBatchStatus.Starting);

        Assert.Equal(1, _ingestor.RunOnce(_settings));
        Assert.Equal(ScanBatchStatus.Matching, SingleBatch().Status);
    }

    [Fact]
    public void FilesRemovedBeforeTheBatchStarts_DropTheBatch()
    {
        var path = Drop(@"Box 12\a.jpg");
        _ingestor.RunOnce(_settings);
        File.Delete(path);

        _ingestor.RunOnce(_settings);

        using var db = _f.Db();
        Assert.Empty(db.ScanBatches);
        Assert.False(_ingestor.HasWaitingBatches);
    }

    [Fact]
    public void Restart_StartsTheQuietPeriodOver()
    {
        Drop(@"Box 12\a.jpg");
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromSeconds(80));

        // A fresh ingestor (app restart) can't tell whether files changed while it was down.
        var restarted = NewIngestor();
        restarted.RunOnce(_settings);
        _f.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0, restarted.RunOnce(_settings));

        _f.Clock.Advance(Quiet);
        Assert.Equal(1, restarted.RunOnce(_settings));
    }

    // --- after a batch starts ---

    [Fact]
    public void LateFiles_StartANewBatch()
    {
        Drop(@"Box 12\a.jpg");
        Settle();

        Drop(@"Box 12\b.jpg");
        _ingestor.RunOnce(_settings);

        using var db = _f.Db();
        var batches = db.ScanBatches.OrderBy(b => b.Id).ToList();
        Assert.Equal(["Box 12", "Box 12 (2)"], batches.Select(b => b.Name));
        Assert.Equal(ScanBatchStatus.Matching, batches[0].Status);
        Assert.Equal(ScanBatchStatus.Collecting, batches[1].Status);
        Assert.Equal(1, db.ScanBatchItems.Count());
    }

    [Fact]
    public void LockedFileAtStart_IsLeftForTheNextBatch()
    {
        Drop(@"Box 12\a.jpg");
        var b = Drop(@"Box 12\b.jpg");
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(Quiet + TimeSpan.FromSeconds(1));

        using (new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(1, _ingestor.RunOnce(_settings));
        Assert.True(File.Exists(b));

        _ingestor.RunOnce(_settings);
        using var db = _f.Db();
        var next = db.ScanBatches.OrderBy(x => x.Id).Last();
        Assert.Equal("Box 12 (2)", next.Name);
        Assert.Equal(ScanBatchStatus.Collecting, next.Status);
    }

    [Fact]
    public void EveryFileLocked_BatchGoesBackToWaiting()
    {
        var path = Drop(@"Box 12\a.jpg");
        _ingestor.RunOnce(_settings);
        _f.Clock.Advance(Quiet + TimeSpan.FromSeconds(1));

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(0, _ingestor.RunOnce(_settings));

        var batch = SingleBatch();
        Assert.Equal(ScanBatchStatus.Collecting, batch.Status);
        Assert.Equal(_f.Clock.GetUtcNow().UtcDateTime, batch.LastFileUtc);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ProcessedNameCollision_GetsSuffix()
    {
        Directory.CreateDirectory(Path.Combine(_mtg, "_processed", "Box 12"));
        File.WriteAllText(Path.Combine(_mtg, "_processed", "Box 12", "a.jpg"), "earlier");
        Drop(@"Box 12\a.jpg");
        Settle();

        var renamed = Path.Combine(_mtg, "_processed", "Box 12", "a (2).jpg");
        Assert.True(File.Exists(renamed));
        using var db = _f.Db();
        Assert.Equal(renamed, db.ScanBatchItems.Single().ProcessedPath);
    }

    [Fact]
    public void RootFiles_GoToDateNamedBatch()
    {
        Drop("loose.jpg");
        Settle();

        var batch = SingleBatch();
        Assert.Equal("2026-10-06", batch.Name);
        Assert.Equal("", batch.FolderKey);
        Assert.True(File.Exists(Path.Combine(_mtg, "_processed", "2026-10-06", "loose.jpg")));
    }

    [Fact]
    public void NestedFolders_BelongToTopLevelBatch()
    {
        Drop(@"Box 12\page 1\a.jpg");
        Drop(@"Box 12\page 2\b.jpg");
        Settle();

        Assert.Equal("Box 12", SingleBatch().Name);
        using var db = _f.Db();
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
    public void DisabledFolder_IsIgnored()
    {
        _settings.Folders[0].Enabled = false;
        Drop(@"Box 12\a.jpg");
        Assert.Equal(0, Settle());
        using var db = _f.Db();
        Assert.Empty(db.ScanBatches);
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

    /// <summary>Drop scans 1..count as 0001.png… into <paramref name="folder"/> and take them in.</summary>
    private void DropRun(string folder, int count, Func<int, byte[]>? content = null, string suffix = "")
    {
        for (var n = 1; n <= count; n++)
            DropBytes($@"{folder}\{n:0000}{suffix}.png", (content ?? Scan)(n));
        Settle();
    }

    [Fact]
    public void ResavedRun_InOneBatch_IsSetAsideAsDuplicatesOfTheOriginals()
    {
        // The capture software writes the whole run out twice before the batch starts.
        for (var n = 1; n <= 5; n++)
            DropBytes($@"Tray\{n:0000}.png", Scan(n));
        DropRun("Tray", 5, suffix: "_copy");

        using var db = _f.Db();
        var batch = Assert.Single(db.ScanBatches);
        Assert.Equal(ScanBatchStatus.Matching, batch.Status);
        var items = db.ScanBatchItems.OrderBy(i => i.Sequence).ToList();
        Assert.Equal(10, items.Count);
        var originals = items.Where(i => !i.OriginalFileName.Contains("copy")).ToList();
        var copies = items.Where(i => i.OriginalFileName.Contains("copy")).ToList();
        Assert.All(originals, i => Assert.Equal(ScanBatchItemState.Open, i.State));
        Assert.All(copies, i => Assert.Equal(ScanBatchItemState.Duplicate, i.State));
        Assert.Equal(originals.Select(i => i.Id), copies.Select(i => i.DuplicateOfItemId!.Value));
        Assert.All(originals, i => Assert.Equal(64, i.ContentHash!.Length));

        // Copies keep no stored image, and their files still leave the watched folder.
        Assert.All(copies, i => Assert.Equal("", i.StoredFileName));
        Assert.All(copies, i => Assert.NotNull(i.ProcessedPath));
        Assert.Equal(5, Directory.GetFiles(_f.Storage.BatchDirectory(batch.Id)).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_mtg, "Tray")));
    }

    [Fact]
    public void ResaveAfterTheBatchStarted_IsClosedStraightAway()
    {
        DropRun("Tray", 3);
        DropRun("Tray", 3); // same names, same bytes, after the first run was taken in

        using var db = _f.Db();
        var batches = db.ScanBatches.OrderBy(b => b.Id).ToList();
        Assert.Equal(2, batches.Count);
        Assert.Equal(ScanBatchStatus.Matching, batches[0].Status);
        Assert.Equal("Tray (2)", batches[1].Name);
        Assert.Equal(ScanBatchStatus.Discarded, batches[1].Status);
        Assert.NotNull(batches[1].ClosedUtc);
        Assert.All(db.ScanBatchItems.Where(i => i.ScanBatchId == batches[1].Id),
            i => Assert.Equal(ScanBatchItemState.Duplicate, i.State));
        Assert.True(File.Exists(Path.Combine(_mtg, "_processed", "Tray", "0001 (2).png")));
    }

    [Fact]
    public void DuplicatesOnlyBatch_DeletesItsOriginals_WhenThatOptionIsOn()
    {
        _settings.DeleteOriginalsOnClose = true;
        DropRun("Tray", 2);
        DropRun("Tray", 2);

        var processed = Path.Combine(_mtg, "_processed", "Tray");
        Assert.True(File.Exists(Path.Combine(processed, "0001.png"))); // the open batch's originals stay
        Assert.False(File.Exists(Path.Combine(processed, "0001 (2).png")));
        Assert.False(File.Exists(Path.Combine(processed, "0002 (2).png")));
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
        DropBytes(@"Tray\IMG_9999.png", Scan(1));
        Settle();

        using var db = _f.Db();
        var copy = db.ScanBatchItems.Single(i => i.OriginalFileName == "IMG_9999.png");
        Assert.Equal(ScanBatchItemState.Duplicate, copy.State);
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
        DropBytes(@"Tray\0001.png", Scan(1));
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
