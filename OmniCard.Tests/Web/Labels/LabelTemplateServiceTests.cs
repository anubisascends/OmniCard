using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Tests.Services.Ebay;
using OmniCard.Web.Services.Labels;

namespace OmniCard.Tests.Web.Labels;

public class LabelTemplateServiceTests : IDisposable
{
    private const string Design = """{"schemas":[[{"name":"cardName","type":"text","position":{"x":1,"y":1},"width":30,"height":6}]],"basePdf":{"width":66.7,"height":25.4,"padding":[0,0,0,0]}}""";

    private readonly SqliteConnection _conn;
    private readonly LabelTemplateService _service;

    public LabelTemplateServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options);
        using (var ctx = factory.CreateDbContext()) ctx.Database.EnsureCreated();
        _service = new LabelTemplateService(factory, TimeProvider.System);
    }

    public void Dispose() => _conn.Dispose();

    /// <summary>A US-Letter sheet of 3 × 10 labels (the common 1" × 2⅝" address-label layout).</summary>
    private static LabelTemplateInput Letter30(string name = "Toploader") => new()
    {
        Name = name,
        PageWidthMm = 215.9, PageHeightMm = 279.4,
        MarginTopMm = 12.7, MarginBottomMm = 12.7, MarginLeftMm = 4.8, MarginRightMm = 4.8,
        Columns = 3, Rows = 10, ColumnGapMm = 3.2, RowGapMm = 0,
        Design = Design,
    };

    [Fact]
    public void Create_DerivesTheLabelSizeFromTheSheet()
    {
        var t = _service.Create(Letter30());

        Assert.Equal("Toploader", t.Name);
        Assert.Equal(66.63, t.LabelWidthMm, 2);  // (215.9 − 2×4.8 − 2×3.2) / 3
        Assert.Equal(25.4, t.LabelHeightMm, 2);  // (279.4 − 2×12.7) / 10
        Assert.Equal(Design, _service.Get(t.Id)!.Design);
    }

    [Fact]
    public void Create_RejectsADuplicateNameIgnoringCase()
    {
        _service.Create(Letter30("Toploader"));
        var ex = Assert.Throws<LabelTemplateException>(() => _service.Create(Letter30("TOPLOADER")));
        Assert.Contains(ex.Errors, e => e.Contains("already exists"));
    }

    [Fact]
    public void Update_PatchesTheTemplateAndKeepsItsOwnName()
    {
        var t = _service.Create(Letter30());
        var updated = _service.Update(t.Id, Letter30() with { Columns = 2, RowGapMm = 1 });

        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Columns);
        Assert.Equal(1, updated.RowGapMm);
        Assert.Single(_service.List());
    }

    [Fact]
    public void Update_UnknownTemplate_ReturnsNull() => Assert.Null(_service.Update(999, Letter30()));

    [Fact]
    public void Delete_RemovesTheTemplate()
    {
        var t = _service.Create(Letter30());
        Assert.True(_service.Delete(t.Id));
        Assert.False(_service.Delete(t.Id));
        Assert.Empty(_service.List());
    }

    [Fact]
    public void List_IsSortedByName()
    {
        _service.Create(Letter30("b labels"));
        _service.Create(Letter30("A labels"));
        Assert.Equal(["A labels", "b labels"], _service.List().Select(t => t.Name));
    }

    [Fact]
    public void Validate_RejectsMarginsThatLeaveNoRoom()
    {
        var errors = LabelTemplateService.Validate(Letter30() with { MarginLeftMm = 110, MarginRightMm = 110 });
        Assert.Contains(errors, e => e.Contains("no room"));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(3, 0)]
    [InlineData(51, 1)]
    public void Validate_RejectsAGridOutOfRange(int columns, int rows)
    {
        var errors = LabelTemplateService.Validate(Letter30() with { Columns = columns, Rows = rows });
        Assert.Contains(errors, e => e.Contains("Columns and rows"));
    }

    [Fact]
    public void Validate_RejectsNegativeSpacingAndBadPageSize()
    {
        var errors = LabelTemplateService.Validate(Letter30() with { RowGapMm = -1, PageWidthMm = 5 });
        Assert.Contains(errors, e => e.Contains("negative"));
        Assert.Contains(errors, e => e.Contains("page"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"basePdf":{}}""")]
    [InlineData("""{"schemas":[{}]}""")]
    public void Validate_RejectsADesignThatIsntAPdfmeTemplate(string design)
    {
        var errors = LabelTemplateService.Validate(Letter30() with { Design = design });
        Assert.Contains(errors, e => e.Contains("design"));
    }

    [Fact]
    public void Validate_RequiresAName()
    {
        var errors = LabelTemplateService.Validate(Letter30("  "));
        Assert.Contains("Enter a name.", errors);
    }
}
