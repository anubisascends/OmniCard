using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Settings;
using OmniCard.Web.Services.Dashboard;

namespace OmniCard.Tests.Web.Dashboard;

/// <summary>
/// Dashboard layouts: a user sees their own, else everyone's default, else the built-in layout (the
/// classic Dashboard rebuilt from widgets); layouts are sanitized; SQL widgets only carry SQL the user
/// may write, or SQL from everyone's default.
/// </summary>
public class DashboardLayoutServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly DashboardLayoutService _layouts;
    private readonly int _alice;
    private readonly int _bob;

    public DashboardLayoutServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();
        _layouts = new DashboardLayoutService(new Factory(_opts), TimeProvider.System);
        _alice = AddUser("alice");
        _bob = AddUser("bob");
    }

    public void Dispose() => _conn.Dispose();

    private int AddUser(string name)
    {
        using var db = new OmniCardDbContext(_opts);
        var u = new User { Username = name, PasswordHash = "x" };
        db.Users.Add(u);
        db.SaveChanges();
        return u.Id;
    }

    private static DashboardLayoutDto Layout(params DashboardWidgetDto[] widgets) => new() { Widgets = [.. widgets] };

    private static DashboardWidgetDto Note(string id, string text = "hi") => new() { Id = id, Type = "note", Text = text, W = 20, H = 4 };

    private static DashboardWidgetDto Sql(string id, string sql) => new() { Id = id, Type = "sql", Sql = sql, W = 20, H = 6 };

    [Fact]
    public void BuiltIn_RecreatesTheClassicDashboard()
    {
        var (layout, source, _, defaultSource) = _layouts.Resolve(_alice);

        Assert.Equal(DashboardLayoutService.SourceBuiltIn, source);
        Assert.Equal(DashboardLayoutService.SourceBuiltIn, defaultSource);
        Assert.Equal(
            ["totalUnits", "totalCost", "totalMarket", "unrealized", "realizedProfit"],
            layout.Widgets.Where(w => w.Type == "metric").Select(w => w.Metric));
        Assert.Equal(["game", "category"], layout.Widgets.Where(w => w.Type == "breakdown").Select(w => w.GroupBy));
        Assert.All(layout.Widgets, w => Assert.Null(w.Title)); // titles come from the page, translated
        Assert.Equal([false, false, false, true, true], layout.Widgets.Where(w => w.Type == "metric").Select(w => w.ColorBySign));
        // The five totals share one row across the full grid width.
        Assert.Equal(DashboardLayoutDto.Columns, layout.Widgets.Where(w => w.Type == "metric").Sum(w => w.W));
    }

    [Fact]
    public void Resolve_PersonalBeatsDefault_BeatsBuiltIn_AndResetFallsBack()
    {
        _layouts.SaveDefault(Layout(Note("everyone")));
        _layouts.SavePersonal(_alice, Layout(Note("mine")), canWriteSql: false);

        var alice = _layouts.Resolve(_alice);
        var bob = _layouts.Resolve(_bob);
        Assert.Equal((DashboardLayoutService.SourcePersonal, "mine"), (alice.Source, alice.Layout.Widgets.Single().Id));
        Assert.Equal("everyone", alice.Default.Widgets.Single().Id);
        Assert.Equal((DashboardLayoutService.SourceDefault, "everyone"), (bob.Source, bob.Layout.Widgets.Single().Id));

        _layouts.ResetPersonal(_alice);
        Assert.Equal(DashboardLayoutService.SourceDefault, _layouts.Resolve(_alice).Source);

        _layouts.ResetDefault();
        Assert.Equal(DashboardLayoutService.SourceBuiltIn, _layouts.Resolve(_bob).Source);
    }

    [Fact]
    public void SavingAgain_ReplacesTheUsersOnlyLayout()
    {
        _layouts.SavePersonal(_alice, Layout(Note("a")), canWriteSql: false);
        _layouts.SavePersonal(_alice, Layout(Note("b")), canWriteSql: false);
        _layouts.SaveDefault(Layout(Note("d1")));
        _layouts.SaveDefault(Layout(Note("d2")));

        using var db = new OmniCardDbContext(_opts);
        Assert.Equal(2, db.DashboardLayouts.Count());
        Assert.Equal("b", _layouts.Resolve(_alice).Layout.Widgets.Single().Id);
        Assert.Equal("d2", _layouts.Resolve(_bob).Layout.Widgets.Single().Id);
    }

    [Fact]
    public void Sanitize_DropsUnknownTypes_FixesIdsAndClampsTheGrid()
    {
        var clean = DashboardLayoutService.Sanitize(Layout(
            new DashboardWidgetDto { Id = "x", Type = "bogus" },
            new DashboardWidgetDto { Id = "dup", Type = "metric", Metric = "nope", X = 55, W = 20, H = 999, Display = "weird", Chart = "radar" },
            new DashboardWidgetDto { Id = "dup", Type = "note", Text = "n", Display = "chart", Formats = new() { ["a"] = "money" } },
            new DashboardWidgetDto { Id = "<script>", Type = "search", Output = "groups", GroupBy = "RARITY", Limit = 5000, Sql = "SELECT 1" }));

        Assert.Equal(3, clean.Widgets.Count);
        var metric = clean.Widgets[0];
        Assert.Equal(("dup", "totalMarket", 40, 20, DashboardLayoutService.MaxHeight), (metric.Id, metric.Metric, metric.X, metric.W, metric.H));
        Assert.Equal(("stat", "bar"), (metric.Display, metric.Chart));
        var note = clean.Widgets[1];
        Assert.NotEqual("dup", note.Id);
        Assert.Null(note.Display);
        Assert.Null(note.Formats);
        var search = clean.Widgets[2];
        Assert.Matches("^[A-Za-z0-9_-]+$", search.Id);
        Assert.Equal(("rarity", DashboardLayoutService.MaxLimit, "value"), (search.GroupBy, search.Limit, search.Sort));
        Assert.Null(search.Sql); // fields the type doesn't use are cleared
    }

    [Fact]
    public void WithoutSqlPermission_OnlyTheDefaultsSqlCanBeKept()
    {
        const string adminSql = "SELECT COUNT(*) AS Cards FROM Lots";
        _layouts.SaveDefault(Layout(Sql("count", adminSql)));

        // Keeping (and moving / retitling) the administrator's widget is fine…
        var kept = _layouts.SavePersonal(_alice,
            Layout(Sql("count", "  " + adminSql + "\r\n") with { Title = "My count", X = 10 }), canWriteSql: false);
        Assert.Equal("My count", kept.Widgets.Single().Title);

        // …but not changing its query, or adding one.
        var ex = Assert.Throws<DashboardException>(() =>
            _layouts.SavePersonal(_alice, Layout(Sql("count", "SELECT * FROM Customers")), canWriteSql: false));
        Assert.Equal(DashboardErrorKind.Forbidden, ex.Kind);

        // With the permission, any valid query can be saved.
        _layouts.SavePersonal(_alice, Layout(Sql("mine", "SELECT * FROM Customers")), canWriteSql: true);
    }

    [Fact]
    public void QueriesAlreadyInTheUsersLayout_StillSave_AfterTheDefaultDropsThem()
    {
        const string sql = "SELECT COUNT(*) AS Cards FROM Lots";
        _layouts.SaveDefault(Layout(Sql("count", sql)));
        _layouts.SavePersonal(_alice, Layout(Sql("count", sql)), canWriteSql: false);
        _layouts.SaveDefault(Layout(Note("n")));

        Assert.DoesNotContain(DashboardLayoutService.NormalizeSql(sql), _layouts.DefaultSql());
        _layouts.SavePersonal(_alice, Layout(Sql("count", sql) with { Y = 4 }), canWriteSql: false);
    }

    [Fact]
    public void InvalidSql_IsRefusedOnSave()
    {
        var ex = Assert.Throws<DashboardException>(() => _layouts.SaveDefault(Layout(Sql("bad", "DELETE FROM Lots") with { Title = "Oops" })));
        Assert.Equal(DashboardErrorKind.BadRequest, ex.Kind);
        Assert.Contains("\"Oops\"", ex.Message);
    }

    [Fact]
    public void DeletingAUser_RemovesTheirLayout()
    {
        _layouts.SavePersonal(_alice, Layout(Note("a")), canWriteSql: false);
        using (var db = new OmniCardDbContext(_opts))
        {
            db.Users.Remove(db.Users.Single(u => u.Id == _alice));
            db.SaveChanges();
        }
        using var check = new OmniCardDbContext(_opts);
        Assert.Empty(check.DashboardLayouts);
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
