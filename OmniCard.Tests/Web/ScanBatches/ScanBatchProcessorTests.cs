using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Scanning;
using OmniCard.Web.Services;
using OmniCard.Web.Services.ScanBatches;
using Xunit;

namespace OmniCard.Tests.Web.ScanBatches;

public sealed class ScanBatchProcessorTests : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(90);
    private readonly ScanBatchFixture _f = new();
    private readonly FakeMatcher _matcher = new();

    public void Dispose() => _f.Dispose();

    private ScanBatchProcessor NewProcessor() =>
        new(_f.Factory, _matcher, (_, id) => id == "new-card", _f.Storage, _f.Clock, NullLogger<ScanBatchProcessor>.Instance);

    /// <summary>A batch whose last file landed now, with one stored image per name.</summary>
    private int SeedBatch(ScanBatchStatus status, params string[] files)
    {
        using var db = _f.Db();
        var now = _f.Clock.GetUtcNow().UtcDateTime;
        var batch = new ScanBatch
        {
            Game = CardGame.Mtg, Name = "Box", FolderKey = "Box", Status = status,
            CreatedUtc = now, LastFileUtc = now, SetCodes = "neo", Language = "ja",
        };
        db.ScanBatches.Add(batch);
        db.SaveChanges();
        var seq = 0;
        foreach (var name in files)
        {
            var item = new ScanBatchItem
            {
                ScanBatchId = batch.Id, Sequence = ++seq, OriginalFileName = name, StoredFileName = "x",
                Status = ScanBatchItemStatus.Pending, State = ScanBatchItemState.Open,
            };
            db.ScanBatchItems.Add(item);
            db.SaveChanges();
            item.StoredFileName = item.Id + Path.GetExtension(name);
            Directory.CreateDirectory(_f.Storage.BatchDirectory(batch.Id));
            // The file holds its own name so the fake matcher can report which scan it was given.
            File.WriteAllBytes(_f.Storage.ItemPath(batch.Id, item.StoredFileName), Encoding.UTF8.GetBytes(name));
            db.SaveChanges();
        }
        return batch.Id;
    }

    private ScanBatch Batch(int id)
    {
        using var db = _f.Db();
        return db.ScanBatches.Single(b => b.Id == id);
    }

    private List<ScanBatchItem> Items(int id)
    {
        using var db = _f.Db();
        return db.ScanBatchItems.Where(i => i.ScanBatchId == id).OrderBy(i => i.Sequence).ToList();
    }

    [Fact]
    public async Task CollectingBatch_WaitsForQuietPeriod()
    {
        var id = SeedBatch(ScanBatchStatus.Collecting, "a.jpg");
        var processor = NewProcessor();

        Assert.False(await processor.ProcessNextAsync(Quiet));
        Assert.Equal(ScanBatchStatus.Collecting, Batch(id).Status);
        Assert.Empty(_matcher.Calls);

        _f.Clock.Advance(Quiet);
        Assert.True(await processor.ProcessNextAsync(Quiet));
        Assert.Equal(ScanBatchStatus.Matching, Batch(id).Status);
        Assert.Single(_matcher.Calls);
    }

    [Fact]
    public async Task MatchesInSequence_ThenBatchIsReady()
    {
        var id = SeedBatch(ScanBatchStatus.Matching, "a.jpg", "b.jpg");
        var processor = NewProcessor();

        while (await processor.ProcessNextAsync(Quiet)) { }

        Assert.Equal(["a.jpg", "b.jpg"], _matcher.Calls.Select(c => c.File));
        Assert.All(_matcher.Calls, c =>
        {
            Assert.Equal(CardGame.Mtg, c.Game);
            Assert.Equal(["neo"], c.Sets);
            Assert.Equal("ja", c.Language);
        });
        var batch = Batch(id);
        Assert.Equal(ScanBatchStatus.Ready, batch.Status);
        Assert.NotNull(batch.ReadyUtc);
        Assert.All(Items(id), i => Assert.Equal(ScanBatchItemStatus.Matched, i.Status));
    }

    [Fact]
    public async Task StoresMatch_SeedsIncludeLanguage_AndNewFlag()
    {
        _matcher.Result = new ScanMatchDto { Matched = true, Game = "Mtg", GameCardId = "new-card", Name = "Bolt", Language = "de", ScanHash = "42" };
        var id = SeedBatch(ScanBatchStatus.Matching, "a.jpg");

        await NewProcessor().ProcessNextAsync(Quiet);

        var item = Assert.Single(Items(id));
        Assert.True(item.Include);
        Assert.Equal("de", item.Language);
        var match = JsonSerializer.Deserialize<ScanMatchDto>(item.MatchJson!, ScanBatchStorage.Json)!;
        Assert.Equal("Bolt", match.Name);
        Assert.True(match.IsNew);
        Assert.Equal("42", match.ScanHash);
    }

    [Fact]
    public async Task MatcherFailure_MarksItemError_AndBatchStillFinishes()
    {
        _matcher.Throw = true;
        var id = SeedBatch(ScanBatchStatus.Matching, "a.jpg");
        var processor = NewProcessor();

        while (await processor.ProcessNextAsync(Quiet)) { }

        var item = Assert.Single(Items(id));
        Assert.Equal(ScanBatchItemStatus.Error, item.Status);
        Assert.Contains("boom", item.Error);
        Assert.Equal(ScanBatchStatus.Ready, Batch(id).Status);
    }

    [Fact]
    public async Task PendingItems_ArePickedUpByANewProcessor()
    {
        // Simulates a restart: nothing is held in memory, pending rows are simply matched.
        var id = SeedBatch(ScanBatchStatus.Matching, "a.jpg", "b.jpg");
        await NewProcessor().ProcessNextAsync(Quiet);

        var restarted = NewProcessor();
        while (await restarted.ProcessNextAsync(Quiet)) { }

        Assert.Equal(2, _matcher.Calls.Count);
        Assert.Equal(ScanBatchStatus.Ready, Batch(id).Status);
    }

    [Fact]
    public async Task RemovedItems_AreSkipped()
    {
        var id = SeedBatch(ScanBatchStatus.Matching, "a.jpg");
        using (var db = _f.Db())
        {
            db.ScanBatchItems.Single().State = ScanBatchItemState.Removed;
            db.SaveChanges();
        }

        Assert.False(await NewProcessor().ProcessNextAsync(Quiet));
        Assert.Empty(_matcher.Calls);
        Assert.Equal(ScanBatchStatus.Ready, Batch(id).Status);
    }

    [Fact]
    public async Task OldestBatch_IsMatchedFirst()
    {
        var first = SeedBatch(ScanBatchStatus.Matching, "first.jpg");
        SeedBatch(ScanBatchStatus.Matching, "second.jpg");

        await NewProcessor().ProcessNextAsync(Quiet);

        Assert.Equal("first.jpg", Assert.Single(_matcher.Calls).File);
        Assert.Equal(ScanBatchItemStatus.Matched, Items(first).Single().Status);
    }

    private sealed class FakeMatcher : IScanMatcher
    {
        public List<(string File, CardGame Game, IReadOnlyCollection<string>? Sets, string? Language)> Calls { get; } = [];
        public ScanMatchDto Result { get; set; } = new() { Matched = false, Game = "Mtg" };
        public bool Throw { get; set; }

        public Task<ScanMatchDto> MatchAsync(byte[] imageBytes, CardGame game, bool isFoil,
            IReadOnlyCollection<string>? setCodes = null, string? language = null, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("boom");
            Calls.Add((Encoding.UTF8.GetString(imageBytes), game, setCodes, language));
            return Task.FromResult(Result);
        }
    }
}
