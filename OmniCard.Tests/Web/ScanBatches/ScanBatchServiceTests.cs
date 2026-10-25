using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Inventory;
using OmniCard.Collection.Settings;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;
using OmniCard.Web.Services;
using OmniCard.Web.Services.ScanBatches;
using Xunit;

namespace OmniCard.Tests.Web.ScanBatches;

public sealed class ScanBatchServiceTests : IDisposable
{
    private const int Alice = 1;
    private const int Bob = 2;

    private readonly ScanBatchFixture _f = new();
    private readonly Mock<ICardService> _cardService = new();
    private readonly Mock<ITagService> _tags = new();
    private readonly Mock<ICardGameService> _mtg = new();
    private readonly ScanBatchService _svc;
    private readonly ScanFolderSettingsService _folderSettings;
    private readonly int _location;

    public ScanBatchServiceTests()
    {
        _cardService.Setup(c => c.GetGameService(CardGame.Mtg)).Returns(_mtg.Object);
        var binder = new WebBinderCardService(_f.Factory, _f.Paths);
        var commits = new ScanCommitService(_cardService.Object, binder, _tags.Object, NullLogger<ScanCommitService>.Instance);
        _folderSettings = new ScanFolderSettingsService(_f.Paths);
        _svc = new ScanBatchService(_f.Factory, commits, _f.Storage, _folderSettings, _f.Clock, NullLogger<ScanBatchService>.Instance);
        _location = new StorageContainerService(_f.Factory).Create("Box", ContainerType.Box).Id;
    }

    public void Dispose() => _f.Dispose();

    private static string MatchJson(string id, string name = "Bolt") => JsonSerializer.Serialize(
        new ScanMatchDto { Matched = true, Game = "Mtg", GameCardId = id, Name = name, SetCode = "lea", SetName = "Alpha",
            CollectorNumber = "161", Rarity = "common", ScanHash = "123" }, ScanBatchStorage.Json);

    /// <summary>A Ready batch with matched items (one per card id); returns (batchId, itemIds).</summary>
    private (int Batch, List<int> Items) Seed(ScanBatchStatus status = ScanBatchStatus.Ready, params string[] cardIds)
    {
        if (cardIds.Length == 0) cardIds = ["c1", "c2"];
        using var db = _f.Db();
        var now = _f.Clock.GetUtcNow().UtcDateTime;
        var batch = new ScanBatch { Game = CardGame.Mtg, Name = "Box 12", FolderKey = "Box 12", Status = status, CreatedUtc = now, LastFileUtc = now };
        db.ScanBatches.Add(batch);
        db.SaveChanges();
        var items = new List<int>();
        var seq = 0;
        foreach (var id in cardIds)
        {
            var item = new ScanBatchItem
            {
                ScanBatchId = batch.Id, Sequence = ++seq, OriginalFileName = id + ".jpg", StoredFileName = id + ".jpg",
                Status = ScanBatchItemStatus.Matched, State = ScanBatchItemState.Open, MatchJson = MatchJson(id), Include = true,
            };
            db.ScanBatchItems.Add(item);
            db.SaveChanges();
            Directory.CreateDirectory(_f.Storage.BatchDirectory(batch.Id));
            File.WriteAllBytes(_f.Storage.ItemPath(batch.Id, item.StoredFileName), [1]);
            items.Add(item.Id);
        }
        return (batch.Id, items);
    }

    private ScanBatch Batch(int id)
    {
        using var db = _f.Db();
        return db.ScanBatches.Single(b => b.Id == id);
    }

    private static ScanBatchException Throws(Action act) => Assert.Throws<ScanBatchException>(act);

    // --- claim ---

    [Fact]
    public void Claim_IsExclusive()
    {
        var (id, _) = Seed();
        _svc.Claim(id, Alice, "alice", force: false);

        var ex = Throws(() => _svc.Claim(id, Bob, "bob", force: false));
        Assert.Equal(ScanBatchErrorKind.Conflict, ex.Kind);
        Assert.Equal("alice", ex.ClaimedBy);
    }

    [Fact]
    public void Claim_ByOwnerAgain_Succeeds_AndForceTakesOver()
    {
        var (id, _) = Seed();
        _svc.Claim(id, Alice, "alice", force: false);
        Assert.True(_svc.Claim(id, Alice, "alice", force: false).ClaimedByMe);

        var taken = _svc.Claim(id, Bob, "bob", force: true);
        Assert.True(taken.ClaimedByMe);
        Assert.Equal("bob", Batch(id).ClaimedByName);
    }

    [Fact]
    public void Claim_ClosedBatch_Conflicts()
    {
        var (id, _) = Seed(ScanBatchStatus.Committed);
        Assert.Equal(ScanBatchErrorKind.Conflict, Throws(() => _svc.Claim(id, Alice, "alice", false)).Kind);
    }

    [Fact]
    public void Release_OwnerOrAdminOnly()
    {
        var (id, _) = Seed();
        _svc.Claim(id, Alice, "alice", false);

        Assert.Equal(ScanBatchErrorKind.Forbidden, Throws(() => _svc.Release(id, Bob, isAdmin: false)).Kind);
        _svc.Release(id, Bob, isAdmin: true);
        Assert.Null(Batch(id).ClaimedByUserId);
    }

    [Fact]
    public void CountUnclaimed_CountsOpenUnclaimedBatches()
    {
        var (a, _) = Seed();
        Seed(ScanBatchStatus.Collecting);
        Seed(ScanBatchStatus.Matching);
        Seed(ScanBatchStatus.Committed);
        _svc.Claim(a, Alice, "alice", false);

        Assert.Equal(2, _svc.CountUnclaimed());
    }

    // --- edits ---

    [Fact]
    public void SaveItems_RequiresClaim()
    {
        var (id, items) = Seed();
        var edit = new ScanBatchItemEdit { Id = items[0], Condition = "LP" };

        Assert.Equal(ScanBatchErrorKind.Conflict, Throws(() => _svc.SaveItems(id, Alice, [edit])).Kind);
        _svc.Claim(id, Bob, "bob", false);
        Assert.Equal(ScanBatchErrorKind.Conflict, Throws(() => _svc.SaveItems(id, Alice, [edit])).Kind);
    }

    [Fact]
    public void SaveItems_PatchesProperties_NeverTheMatch()
    {
        var (id, items) = Seed();
        _svc.Claim(id, Alice, "alice", false);
        var correction = new ScanSearchResultDto("fixed", "Shock", "m10", "M10", "150", "common", null);

        _svc.SaveItems(id, Alice, [new ScanBatchItemEdit
        {
            Id = items[0], Include = true, Verified = true, Override = correction, Condition = "LP", Language = "JP",
            IsFoil = true, FoilType = "Etched", Quantity = 3, PurchasePrice = 1.5m, Tags = ["trade", " ", "trade"], Note = " signed ",
        }]);

        var dto = _svc.Get(id, Alice).Items.Single(i => i.Id == items[0]);
        Assert.True(dto.Verified);
        Assert.Equal("Shock", dto.Override!.Name);
        Assert.Equal("Bolt", dto.Match!.Name); // untouched
        Assert.Equal("LP", dto.Condition);
        Assert.Equal("ja", dto.Language);
        Assert.Equal("Etched", dto.FoilType);
        Assert.Equal(3, dto.Quantity);
        Assert.Equal(["trade"], dto.Tags);
        Assert.Equal("signed", dto.Note);
    }

    // --- commit ---

    [Fact]
    public void Commit_Partial_CreatesLots_AndLeavesBatchOpen()
    {
        var (id, items) = Seed();
        _svc.Claim(id, Alice, "alice", false);

        var result = _svc.Commit(id, Alice, _location, [items[0]]);

        Assert.Equal(1, result.Imported);
        Assert.False(result.BatchClosed);
        using var db = _f.Db();
        Assert.Single(db.Lots.Where(l => l.LocationId == _location));
        Assert.Equal(ScanBatchStatus.Ready, Batch(id).Status);
        Assert.Single(_svc.Get(id, Alice).Items);
        _mtg.Verify(g => g.RecordCorrection(123UL, "c1", null), Times.Once);
    }

    [Fact]
    public void Commit_Last_ClosesBatch_AndClearsClaim()
    {
        var (id, items) = Seed();
        _svc.Claim(id, Alice, "alice", false);

        var result = _svc.Commit(id, Alice, _location, items);

        Assert.True(result.BatchClosed);
        var batch = Batch(id);
        Assert.Equal(ScanBatchStatus.Committed, batch.Status);
        Assert.NotNull(batch.ClosedUtc);
        Assert.Null(batch.ClaimedByUserId);
    }

    [Fact]
    public void Commit_UsesCorrectionOverMatch()
    {
        var (id, items) = Seed(cardIds: "c1");
        _svc.Claim(id, Alice, "alice", false);
        _svc.SaveItems(id, Alice, [new ScanBatchItemEdit
        {
            Id = items[0], Include = true, Override = new ScanSearchResultDto("fixed", "Shock", "m10", "M10", "150", "common", null),
        }]);

        _svc.Commit(id, Alice, _location, items);

        using var db = _f.Db();
        var lot = db.Lots.Single(l => l.LocationId == _location);
        var product = db.Products.Single(p => p.Id == lot.ProductId);
        Assert.Equal("fixed", product.GameCardId);
    }

    [Fact]
    public void Commit_Twice_DoesNotDuplicateLots()
    {
        var (id, items) = Seed();
        _svc.Claim(id, Alice, "alice", false);
        _svc.Commit(id, Alice, _location, [items[0]]);

        Assert.Equal(ScanBatchErrorKind.Conflict, Throws(() => _svc.Commit(id, Alice, _location, [items[0]])).Kind);
        using var db = _f.Db();
        Assert.Single(db.Lots.Where(l => l.LocationId == _location));
    }

    [Fact]
    public void Commit_UnmatchedItem_IsRejected()
    {
        var (id, items) = Seed(cardIds: "c1");
        using (var db = _f.Db())
        {
            var item = db.ScanBatchItems.Single();
            item.MatchJson = null;
            item.Status = ScanBatchItemStatus.Error;
            db.SaveChanges();
        }
        _svc.Claim(id, Alice, "alice", false);

        Assert.Equal(ScanBatchErrorKind.BadRequest, Throws(() => _svc.Commit(id, Alice, _location, items)).Kind);
    }

    [Fact]
    public void Commit_WithoutClaim_Conflicts()
    {
        var (id, items) = Seed();
        Assert.Equal(ScanBatchErrorKind.Conflict, Throws(() => _svc.Commit(id, Alice, _location, items)).Kind);
    }

    // --- remove / rematch / discard ---

    [Fact]
    public void RemoveItems_DeletesFiles_AndClosesEmptyBatchAsDiscarded()
    {
        var (id, items) = Seed();
        _svc.Claim(id, Alice, "alice", false);

        Assert.False(_svc.RemoveItems(id, Alice, [items[0]]));
        Assert.False(File.Exists(_f.Storage.ItemPath(id, "c1.jpg")));
        Assert.True(_svc.RemoveItems(id, Alice, [items[1]]));
        Assert.Equal(ScanBatchStatus.Discarded, Batch(id).Status);
    }

    [Fact]
    public void Rematch_ResetsItems_AndReopensReadyBatch()
    {
        var (id, items) = Seed();
        _svc.Claim(id, Alice, "alice", false);

        _svc.Rematch(id, Alice, [items[0]]);

        Assert.Equal(ScanBatchStatus.Matching, Batch(id).Status);
        var dto = _svc.Get(id, Alice).Items.Single(i => i.Id == items[0]);
        Assert.Equal("Pending", dto.Status);
        Assert.Null(dto.Match);
    }

    [Fact]
    public void Discard_ByOtherUser_Conflicts_ButAdminCan()
    {
        var (id, _) = Seed();
        _svc.Claim(id, Alice, "alice", false);

        Assert.Equal(ScanBatchErrorKind.Conflict, Throws(() => _svc.Discard(id, Bob, isAdmin: false)).Kind);
        _svc.Discard(id, Bob, isAdmin: true);

        Assert.Equal(ScanBatchStatus.Discarded, Batch(id).Status);
        Assert.False(File.Exists(_f.Storage.ItemPath(id, "c1.jpg")));
    }

    // --- list / purge ---

    [Fact]
    public void List_ShowsOpen_AndRecentlyClosed_WithCounts()
    {
        var (open, _) = Seed();
        var (closed, _) = Seed(ScanBatchStatus.Committed);
        using (var db = _f.Db())
        {
            db.ScanBatches.Single(b => b.Id == closed).ClosedUtc = _f.Clock.GetUtcNow().UtcDateTime;
            db.SaveChanges();
        }
        _f.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, _svc.List(Alice).Count);

        _f.Clock.Advance(ScanBatchService.ClosedVisibleFor);
        var summary = Assert.Single(_svc.List(Alice));
        Assert.Equal(open, summary.Id);
        Assert.Equal(2, summary.Total);
        Assert.Equal(2, summary.Matched);
    }

    [Fact]
    public void PurgeExpired_RemovesOnlyOldClosedBatches()
    {
        var (old, _) = Seed();
        var (recent, _) = Seed();
        var (open, _) = Seed();
        foreach (var id in new[] { old, recent })
        {
            _svc.Claim(id, Alice, "alice", false);
            _svc.Discard(id, Alice, false);
            _f.Clock.Advance(TimeSpan.FromDays(10));
        }
        // old closed 20 days before "now", recent 10 days before.

        Assert.Equal(1, _svc.PurgeExpired(retentionDays: 14));

        using var db = _f.Db();
        Assert.Equal([recent, open], db.ScanBatches.OrderBy(b => b.Id).Select(b => b.Id).ToList());
        Assert.False(Directory.Exists(_f.Storage.BatchDirectory(old)));
    }

    [Fact]
    public void ImageFile_PrefersPreview()
    {
        var (id, items) = Seed(cardIds: "c1");
        using (var db = _f.Db())
        {
            db.ScanBatchItems.Single().PreviewFileName = "c1.preview.jpg";
            db.SaveChanges();
        }
        File.WriteAllBytes(_f.Storage.ItemPath(id, "c1.preview.jpg"), [1]);

        var file = _svc.ImageFile(id, items[0]);

        Assert.NotNull(file);
        Assert.EndsWith("c1.preview.jpg", file.Value.Path);
        Assert.Null(_svc.ImageFile(id + 99, items[0]));
    }

    // --- run (auto-processing off) ---

    [Fact]
    public void Run_MarksCollectingBatchesStarting_AllOrListed()
    {
        var (a, _) = Seed(ScanBatchStatus.Collecting);
        var (b, _) = Seed(ScanBatchStatus.Collecting);
        var (ready, _) = Seed(ScanBatchStatus.Ready);

        Assert.Equal(1, _svc.Run([b, ready]));
        Assert.Equal(ScanBatchStatus.Collecting, Batch(a).Status);
        Assert.Equal(ScanBatchStatus.Starting, Batch(b).Status);
        Assert.Equal(ScanBatchStatus.Ready, Batch(ready).Status);

        Assert.Equal(1, _svc.Run([]));
        Assert.Equal(ScanBatchStatus.Starting, Batch(a).Status);
        Assert.Equal(0, _svc.Run([]));
    }

    [Fact]
    public void AwaitingRun_OnlyForCollectingBatches_WhenAutoProcessIsOff()
    {
        var (collecting, _) = Seed(ScanBatchStatus.Collecting);
        var (matching, _) = Seed(ScanBatchStatus.Matching);
        Assert.All(_svc.List(Alice), b => Assert.False(b.AwaitingRun));

        Assert.Empty(_folderSettings.Save(new ScanFolderSettings { AutoProcess = false }));

        var list = _svc.List(Alice).ToDictionary(b => b.Id);
        Assert.True(list[collecting].AwaitingRun);
        Assert.False(list[matching].AwaitingRun);
        Assert.True(_svc.Get(collecting, Alice).Summary.AwaitingRun);
    }

    [Fact]
    public void Summary_GivesEachWaitingBatchItsOwnStartTime()
    {
        var (first, _) = Seed(ScanBatchStatus.Collecting);
        _f.Clock.Advance(TimeSpan.FromSeconds(30));
        var (second, _) = Seed(ScanBatchStatus.Collecting);
        var (matching, _) = Seed(ScanBatchStatus.Matching);
        Assert.Empty(_folderSettings.Save(new ScanFolderSettings { AutoProcess = true, QuietPeriodSeconds = 90 }));

        var list = _svc.List(Alice).ToDictionary(b => b.Id);
        var start = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(start.AddSeconds(90), list[first].StartsUtc);
        Assert.Equal(start.AddSeconds(120), list[second].StartsUtc);
        Assert.Null(list[matching].StartsUtc);

        Assert.Empty(_folderSettings.Save(new ScanFolderSettings { AutoProcess = false }));
        Assert.Null(_svc.List(Alice).Single(b => b.Id == first).StartsUtc);
    }

    [Fact]
    public void Discard_BatchThatHasNotStarted_Conflicts()
    {
        var (id, _) = Seed(ScanBatchStatus.Collecting, []);
        using (var db = _f.Db())
        {
            db.ScanBatchItems.RemoveRange(db.ScanBatchItems.Where(i => i.ScanBatchId == id));
            db.SaveChanges();
        }

        Assert.Equal(ScanBatchErrorKind.Conflict, Throws(() => _svc.Discard(id, Alice, isAdmin: true)).Kind);
        Assert.Equal(ScanBatchStatus.Collecting, Batch(id).Status);
    }

    // --- deleting originals when a batch closes ---

    /// <summary>Give each item of the batch an original under <c>_processed\Box 12</c>; returns the paths.</summary>
    private List<string> AddOriginals(int batchId)
    {
        var dir = Path.Combine(_f.ScanRoot, "Mtg", ScanFolderIngestor.ProcessedFolderName, "Box 12");
        Directory.CreateDirectory(dir);
        using var db = _f.Db();
        var paths = new List<string>();
        foreach (var item in db.ScanBatchItems.Where(i => i.ScanBatchId == batchId).ToList())
        {
            item.ProcessedPath = Path.Combine(dir, item.OriginalFileName);
            File.WriteAllBytes(item.ProcessedPath, [1]);
            paths.Add(item.ProcessedPath);
        }
        db.SaveChanges();
        return paths;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Commit_DeletesOriginals_OnlyWhenTheOptionIsOn(bool deleteOriginals)
    {
        Assert.Empty(_folderSettings.Save(new ScanFolderSettings { DeleteOriginalsOnClose = deleteOriginals }));
        var (id, items) = Seed();
        var originals = AddOriginals(id);
        var unrelated = Path.Combine(_f.ScanRoot, "Mtg", ScanFolderIngestor.ProcessedFolderName, "Box 12", "other.jpg");
        File.WriteAllBytes(unrelated, [1]);
        _svc.Claim(id, Alice, "alice", false);

        // A partial commit leaves the batch open, so nothing is deleted yet.
        _svc.Commit(id, Alice, _location, [items[0]]);
        Assert.All(originals, p => Assert.True(File.Exists(p)));

        _svc.Commit(id, Alice, _location, [items[1]]);
        Assert.All(originals, p => Assert.Equal(!deleteOriginals, File.Exists(p)));
        Assert.True(File.Exists(unrelated)); // another batch's file in the same folder is left alone
    }

    [Fact]
    public void Discard_DeletesOriginals_AndPrunesTheEmptyFolder()
    {
        Assert.Empty(_folderSettings.Save(new ScanFolderSettings { DeleteOriginalsOnClose = true }));
        var (id, _) = Seed();
        var originals = AddOriginals(id);

        _svc.Discard(id, Alice, isAdmin: false);

        Assert.All(originals, p => Assert.False(File.Exists(p)));
        var processed = Path.Combine(_f.ScanRoot, "Mtg", ScanFolderIngestor.ProcessedFolderName);
        Assert.False(Directory.Exists(Path.Combine(processed, "Box 12")));
        Assert.True(Directory.Exists(processed)); // _processed itself stays
        Assert.True(_svc.List(Alice).Single().DeletesOriginalsOnClose);
    }

    // --- duplicates set aside at pickup ---

    /// <summary>Add set-aside duplicates of <paramref name="originals"/> (in order) to a batch.</summary>
    private void AddDuplicates(int batchId, IEnumerable<int> originals)
    {
        using var db = _f.Db();
        var seq = db.ScanBatchItems.Where(i => i.ScanBatchId == batchId).Max(i => (int?)i.Sequence) ?? 0;
        foreach (var originalId in originals)
        {
            var original = db.ScanBatchItems.Single(i => i.Id == originalId);
            db.ScanBatchItems.Add(new ScanBatchItem
            {
                ScanBatchId = batchId, Sequence = ++seq, OriginalFileName = original.OriginalFileName, StoredFileName = "",
                Status = ScanBatchItemStatus.Pending, State = ScanBatchItemState.Duplicate, DuplicateOfItemId = originalId,
            });
        }
        db.SaveChanges();
    }

    [Fact]
    public void Duplicates_AreCounted_ButNeverListedOrCommitted()
    {
        var (id, items) = Seed(ScanBatchStatus.Ready, "c1", "c2", "c3");
        AddDuplicates(id, items);

        var dto = _svc.Get(id, Alice);
        Assert.Equal(3, dto.Summary.Total);
        Assert.Equal(3, dto.Summary.Duplicates);
        Assert.Equal(items, dto.Items.Select(i => i.Id));

        // Committing every listed card closes the batch; the duplicates don't hold it open.
        _svc.Claim(id, Alice, "alice", false);
        Assert.True(_svc.Commit(id, Alice, _location, items).BatchClosed);
        using var db = _f.Db();
        Assert.Equal(3, db.Lots.Count(l => l.LocationId == _location));
    }

    [Fact]
    public void DuplicateRuns_FoldAResavedRunIntoOneLine()
    {
        var (id, items) = Seed(ScanBatchStatus.Ready, "0001", "0002", "0003", "0004");
        AddDuplicates(id, items);

        var run = Assert.Single(_svc.Get(id, Alice).DuplicateRuns);
        Assert.Equal(new ScanBatchDuplicateRunDto("0001.jpg", "0004.jpg", 4, "0001.jpg", "0004.jpg", null), run);
    }

    [Fact]
    public void DuplicateRuns_NameTheOriginalsBatch_WhenItIsAnotherOne()
    {
        var (first, items) = Seed(ScanBatchStatus.Committed, "0001", "0002");
        var (second, _) = Seed(ScanBatchStatus.Collecting, "x");
        AddDuplicates(second, items);

        var run = Assert.Single(_svc.Get(second, Alice).DuplicateRuns);
        Assert.Equal("Box 12", run.OriginalBatchName);
        Assert.Equal(2, run.Count);
        Assert.NotEqual(first, second);
    }

    private static ScanBatchService.DuplicatePair Pair(int seq, int originalSeq, int originalBatch = 1) =>
        new(seq, $"{seq:0000}.png", originalBatch, originalSeq, $"{originalSeq:0000}.png");

    [Fact]
    public void FoldRuns_BreaksWhereEitherSideSkips()
    {
        var runs = ScanBatchService.FoldRuns(
        [
            Pair(11, 1), Pair(12, 2), Pair(13, 3),  // run 1
            Pair(14, 5),                             // original skips 4
            Pair(16, 6),                             // copy sequence skips 15
            Pair(17, 7, originalBatch: 2),           // originals in another batch
        ], batchId: 1, new Dictionary<int, string> { [2] = "Earlier" });

        Assert.Equal([3, 1, 1, 1], runs.Select(r => r.Count));
        Assert.Equal(("0011.png", "0013.png", "0001.png", "0003.png"),
            (runs[0].FirstFile, runs[0].LastFile, runs[0].OriginalFirstFile, runs[0].OriginalLastFile));
        Assert.Null(runs[0].OriginalBatchName);
        Assert.Equal("Earlier", runs[3].OriginalBatchName);
    }

    [Fact]
    public void FoldRuns_ReorderedCopies_StillAllReported()
    {
        // Copies written in reverse: no two are a consecutive pattern, but every one is reported.
        var runs = ScanBatchService.FoldRuns([Pair(4, 3), Pair(5, 2), Pair(6, 1)], 1, new Dictionary<int, string>());
        Assert.Equal(3, runs.Sum(r => r.Count));
        Assert.Equal(3, runs.Count);
    }

    [Fact]
    public void FoldRuns_IsCapped()
    {
        // Every other copy breaks the pattern → one run each, capped.
        var pairs = Enumerable.Range(0, ScanBatchService.MaxDuplicateRuns + 50).Select(n => Pair(n * 2, n * 3)).ToList();
        Assert.Equal(ScanBatchService.MaxDuplicateRuns, ScanBatchService.FoldRuns(pairs, 1, new Dictionary<int, string>()).Count);
    }
}
