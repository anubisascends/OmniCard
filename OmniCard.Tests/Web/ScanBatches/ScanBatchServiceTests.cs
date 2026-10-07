using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Inventory;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Scanning;
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
    private readonly int _location;

    public ScanBatchServiceTests()
    {
        _cardService.Setup(c => c.GetGameService(CardGame.Mtg)).Returns(_mtg.Object);
        var binder = new WebBinderCardService(_f.Factory, _f.Paths);
        var commits = new ScanCommitService(_cardService.Object, binder, _tags.Object, NullLogger<ScanCommitService>.Instance);
        _svc = new ScanBatchService(_f.Factory, commits, _f.Storage, _f.Clock, NullLogger<ScanBatchService>.Instance);
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
}
