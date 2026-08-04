using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;

namespace OmniCard.Tests.Services;

public class TagServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public TagServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ITagService CreateService() => new TagService(new MockFactory(_options));

    [Fact]
    public void SetTagsForLot_CreatesNewTagsAndPreservesCasing()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["Foil", "PSA-Worthy"]);

        var tags = service.GetTagsForLot(1);
        Assert.Equal(["Foil", "PSA-Worthy"], tags.OrderBy(t => t));
    }

    [Fact]
    public void SetTagsForLot_ReusesExistingTagCaseInsensitively()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["Foil"]);
        service.SetTagsForLot(2, ["foil"]);

        var allTags = service.GetAllTags();
        var foilTag = Assert.Single(allTags);
        Assert.Equal("Foil", foilTag.Name); // first-seen casing preserved
        Assert.Equal(2, foilTag.UsageCount);
    }

    [Fact]
    public void SetTagsForLot_RemovesNoLongerWantedTags()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["A", "B"]);
        service.SetTagsForLot(1, ["B", "C"]);

        Assert.Equal(["B", "C"], service.GetTagsForLot(1).OrderBy(t => t));
    }

    [Fact]
    public void GetTagsByLots_BatchesAndOmitsUntaggedLots()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["A"]);
        service.SetTagsForLot(2, ["B"]);

        var result = service.GetTagsByLots([1, 2, 3]);

        Assert.Equal(["A"], result[1]);
        Assert.Equal(["B"], result[2]);
        Assert.False(result.ContainsKey(3));
    }

    [Fact]
    public void AddTagToLots_CreatesTagAndSkipsAlreadyTagged()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["Existing"]);

        service.AddTagToLots([1, 2], "Existing");

        Assert.Equal(["Existing"], service.GetTagsForLot(1));
        Assert.Equal(["Existing"], service.GetTagsForLot(2));
        var tag = Assert.Single(service.GetAllTags());
        Assert.Equal(2, tag.UsageCount);
    }

    [Fact]
    public void RenameTag_UpdatesNameForAllTaggedLots()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["Old"]);
        var tagId = service.GetAllTags().Single().Id;

        service.RenameTag(tagId, "New");

        Assert.Equal(["New"], service.GetTagsForLot(1));
    }

    [Fact]
    public void DeleteTag_RemovesFromEveryLot()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["Gone"]);
        service.SetTagsForLot(2, ["Gone"]);
        var tagId = service.GetAllTags().Single().Id;

        service.DeleteTag(tagId);

        Assert.Empty(service.GetTagsForLot(1));
        Assert.Empty(service.GetTagsForLot(2));
        Assert.Empty(service.GetAllTags());
    }

    [Fact]
    public void MergeTags_ReassignsLotsAndDeletesSource()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["Dupe"]);
        service.SetTagsForLot(2, ["Canonical"]);
        var tags = service.GetAllTags();
        var sourceId = tags.Single(t => t.Name == "Dupe").Id;
        var targetId = tags.Single(t => t.Name == "Canonical").Id;

        service.MergeTags(sourceId, targetId);

        Assert.Equal(["Canonical"], service.GetTagsForLot(1));
        Assert.Equal(["Canonical"], service.GetTagsForLot(2));
        var remaining = Assert.Single(service.GetAllTags());
        Assert.Equal("Canonical", remaining.Name);
    }

    [Fact]
    public void MergeTags_DropsDuplicateWhenLotAlreadyHasTarget()
    {
        var service = CreateService();
        service.SetTagsForLot(1, ["Dupe", "Canonical"]);
        var tags = service.GetAllTags();
        var sourceId = tags.Single(t => t.Name == "Dupe").Id;
        var targetId = tags.Single(t => t.Name == "Canonical").Id;

        service.MergeTags(sourceId, targetId);

        Assert.Equal(["Canonical"], service.GetTagsForLot(1));
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
