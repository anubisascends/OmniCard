using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Settings;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Tags;
using OmniCard.Tests.Fakes;
using OmniCard.Web.Services;
using OmniCard.Web.Services.ScanBatches;
using Xunit;

namespace OmniCard.Tests.Web.ScanBatches;

public sealed class ScanBatchUploadServiceTests : IDisposable
{
    private const int Alice = 1;
    private const int Bob = 2;

    private readonly ScanBatchFixture _f = new();
    private readonly ScanBatchUploadService _uploads;
    private readonly ScanBatchService _batches;

    public ScanBatchUploadServiceTests()
    {
        _uploads = new ScanBatchUploadService(_f.Factory, _f.Storage, _f.Clock, NullLogger<ScanBatchUploadService>.Instance);
        var commits = new ScanCommitService(new Mock<ICardService>().Object, new WebBinderCardService(_f.Factory, _f.Paths),
            new Mock<ITagService>().Object, BuyListServices.Create(_f.Factory), NullLogger<ScanCommitService>.Instance);
        _batches = new ScanBatchService(_f.Factory, commits, _f.Storage, new ScanFolderSettingsService(_f.Paths), _f.Clock,
            NullLogger<ScanBatchService>.Instance);
    }

    public void Dispose() => _f.Dispose();

    private int Start(string? name = "Phone run", int user = Alice) =>
        _uploads.Start(CardGame.Mtg, new ScanBatchUploadStartRequest
        {
            Name = name, Game = "Mtg", IsFoil = true, Condition = "LP", Language = "ja", SetCodes = ["lea", " LEA ", "leb"],
            DefaultContainerId = 7,
        }, user, user == Alice ? "alice" : "bob").Id;

    private static ScanMatchDto Match(string id) => new()
    {
        Matched = true, Game = "Mtg", GameCardId = id, Name = "Bolt", SetCode = "lea", SetName = "Alpha",
        CollectorNumber = "161", Rarity = "common", ScanHash = "123", ScanPreviewDataUri = "data:image/jpeg;base64,AAAA",
    };

    private static ScanBatchUploadItem Item(int sequence, ScanMatchDto? match = null) => new()
    {
        Sequence = sequence,
        Match = match,
        Props = new ScanBatchItemEdit
        {
            Include = true, Verified = true, Condition = "MP", Language = "de", IsFoil = true, FoilType = "Etched",
            Quantity = 3, PurchasePrice = 1.5m, Tags = ["trade"], Note = " binder ",
        },
    };

    private bool Add(int batch, byte[] bytes, ScanBatchUploadItem item, int user = Alice, string ext = ".jpg") =>
        _uploads.AddItem(batch, user, bytes, "IMG_0001" + ext, ext, item);

    private ScanBatch Batch(int id)
    {
        using var db = _f.Db();
        return db.ScanBatches.Single(b => b.Id == id);
    }

    private List<ScanBatchItem> Items(int id)
    {
        using var db = _f.Db();
        return db.ScanBatchItems.Where(i => i.ScanBatchId == id).OrderBy(i => i.Id).ToList();
    }

    [Fact]
    public void Start_CreatesHiddenUploadingBatchClaimedByUploader()
    {
        var id = Start();

        var batch = Batch(id);
        Assert.Equal(ScanBatchStatus.Uploading, batch.Status);
        Assert.Equal("Phone run", batch.Name);
        Assert.Equal(Alice, batch.ClaimedByUserId);
        Assert.True(batch.IsFoil);
        Assert.Equal("LP", batch.Condition);
        Assert.Equal("ja", batch.Language);
        Assert.Equal("lea,leb", batch.SetCodes);
        Assert.Equal(7, batch.DefaultContainerId);
        Assert.Empty(_batches.List(Alice));
        Assert.Equal(0, _batches.CountUnclaimed());
        var ex = Assert.Throws<ScanBatchException>(() => _batches.Claim(id, Bob, "bob", force: true));
        Assert.Equal(ScanBatchErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Start_BlankName_UsesUserAndTime_AndKeepsNamesUnique()
    {
        var first = Start(name: " ");
        var second = Start(name: null);

        Assert.Equal("alice 2026-10-06 12:00", Batch(first).Name);
        Assert.Equal("alice 2026-10-06 12:00 (2)", Batch(second).Name);
    }

    [Fact]
    public void AddItem_WithBrowserMatch_KeepsMatchAndEdits()
    {
        var id = Start();

        Assert.False(Add(id, [1, 2, 3], Item(4, Match("c1"))));

        var item = Assert.Single(Items(id));
        Assert.Equal(ScanBatchItemStatus.Matched, item.Status);
        Assert.Equal(ScanBatchItemState.Open, item.State);
        Assert.Equal(4, item.Sequence);
        Assert.Equal("IMG_0001.jpg", item.OriginalFileName);
        Assert.Equal($"{item.Id}.jpg", item.StoredFileName);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(_f.Storage.ItemPath(id, item.StoredFileName)));
        var match = JsonSerializer.Deserialize<ScanMatchDto>(item.MatchJson!, ScanBatchStorage.Json)!;
        Assert.Equal("c1", match.GameCardId);
        Assert.Null(match.ScanPreviewDataUri);
        Assert.True(item.Include);
        Assert.True(item.Verified);
        Assert.Equal("MP", item.Condition);
        Assert.Equal("de", item.Language);
        Assert.Equal("Etched", item.FoilType);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(1.5m, item.PurchasePrice);
        Assert.Equal("binder", item.Note);
        Assert.Contains("trade", item.TagsJson);
    }

    [Fact]
    public void AddItem_WithoutMatch_IsLeftForTheMatcher()
    {
        var id = Start();

        Add(id, [9], Item(1));

        var item = Assert.Single(Items(id));
        Assert.Equal(ScanBatchItemStatus.Pending, item.Status);
        Assert.Null(item.MatchJson);
        Assert.False(item.Include);
        Assert.False(item.Verified);
        Assert.Equal("MP", item.Condition);
    }

    [Fact]
    public void AddItem_SameBytesTwice_SetsTheCopyAside()
    {
        var id = Start();
        Add(id, [5, 5], Item(1, Match("c1")));

        Assert.True(Add(id, [5, 5], Item(2, Match("c1"))));

        var items = Items(id);
        Assert.Equal(ScanBatchItemState.Duplicate, items[1].State);
        Assert.Equal(items[0].Id, items[1].DuplicateOfItemId);
        Assert.Equal("", items[1].StoredFileName);
    }

    [Fact]
    public void AddItem_RefusesOtherUsersAndFinishedBatches()
    {
        var id = Start();
        Assert.Equal(ScanBatchErrorKind.Forbidden,
            Assert.Throws<ScanBatchException>(() => Add(id, [1], Item(1), user: Bob)).Kind);

        Add(id, [1], Item(1, Match("c1")));
        _uploads.Finish(id, Alice);

        Assert.Equal(ScanBatchErrorKind.Conflict,
            Assert.Throws<ScanBatchException>(() => Add(id, [2], Item(2))).Kind);
    }

    [Fact]
    public void Finish_ReleasesTheBatchForReview()
    {
        var id = Start();
        Add(id, [1], Item(1, Match("c1")));
        Add(id, [2], Item(2));
        Add(id, [2], Item(3));

        var result = _uploads.Finish(id, Alice);

        Assert.Equal(2, result.Items);
        Assert.Equal(1, result.Duplicates);
        Assert.Equal("Phone run", result.Name);
        var batch = Batch(id);
        Assert.Equal(ScanBatchStatus.Matching, batch.Status);
        Assert.Null(batch.ClaimedByUserId);
        Assert.Equal(1, _batches.CountUnclaimed());
        var listed = Assert.Single(_batches.List(Bob));
        Assert.Equal(2, listed.Total);
        Assert.Equal(1, listed.Pending);
        Assert.Equal(7, listed.DefaultContainerId);
        // Anyone can now pick it up.
        Assert.True(_batches.Claim(id, Bob, "bob", force: false).ClaimedByMe);
    }

    [Fact]
    public void Finish_OnlyDuplicates_ClosesAsDiscarded()
    {
        var first = Start();
        Add(first, [1], Item(1, Match("c1")));
        _uploads.Finish(first, Alice);
        var second = Start(name: "Again");
        Add(second, [1], Item(1, Match("c1")));

        var result = _uploads.Finish(second, Alice);

        Assert.Equal(0, result.Items);
        Assert.Equal(1, result.Duplicates);
        var batch = Batch(second);
        Assert.Equal(ScanBatchStatus.Discarded, batch.Status);
        Assert.NotNull(batch.ClosedUtc);
    }

    [Fact]
    public void Finish_NothingSent_DeletesTheBatch()
    {
        var id = Start();

        var ex = Assert.Throws<ScanBatchException>(() => _uploads.Finish(id, Alice));

        Assert.Equal(ScanBatchErrorKind.BadRequest, ex.Kind);
        using var db = _f.Db();
        Assert.False(db.ScanBatches.Any(b => b.Id == id));
    }

    [Fact]
    public void Cancel_DeletesRowsAndImages()
    {
        var id = Start();
        Add(id, [1], Item(1, Match("c1")));

        Assert.Equal(ScanBatchErrorKind.Forbidden,
            Assert.Throws<ScanBatchException>(() => _uploads.Cancel(id, Bob)).Kind);
        _uploads.Cancel(id, Alice);

        using var db = _f.Db();
        Assert.False(db.ScanBatches.Any(b => b.Id == id));
        Assert.False(db.ScanBatchItems.Any(i => i.ScanBatchId == id));
        Assert.False(Directory.Exists(_f.Storage.BatchDirectory(id)));
    }

    [Fact]
    public void Cancel_AfterFinish_LeavesTheBatch()
    {
        var id = Start();
        Add(id, [1], Item(1, Match("c1")));
        _uploads.Finish(id, Alice);

        _uploads.Cancel(id, Alice);

        Assert.Equal(ScanBatchStatus.Matching, Batch(id).Status);
    }

    [Fact]
    public void PurgeExpired_RemovesAbandonedUploads()
    {
        var abandoned = Start();
        Add(abandoned, [1], Item(1, Match("c1")));
        _f.Clock.Advance(TimeSpan.FromHours(23));
        var active = Start(name: "Still going");

        _f.Clock.Advance(TimeSpan.FromHours(2));
        _batches.PurgeExpired(retentionDays: 30);

        using var db = _f.Db();
        Assert.False(db.ScanBatches.Any(b => b.Id == abandoned));
        Assert.False(Directory.Exists(_f.Storage.BatchDirectory(abandoned)));
        Assert.True(db.ScanBatches.Any(b => b.Id == active));
    }
}
