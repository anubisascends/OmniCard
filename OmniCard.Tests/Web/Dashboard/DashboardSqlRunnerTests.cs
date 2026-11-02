using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Tags;
using OmniCard.Web.Services.Dashboard;

namespace OmniCard.Tests.Web.Dashboard;

/// <summary>
/// SQL widgets' queries run against a chosen database and come back as a typed table, capped at
/// <see cref="DashboardSqlRunner.MaxRows"/>; refused or failing queries surface as a readable error.
/// </summary>
public class DashboardSqlRunnerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DashboardSqlRunner _runner;

    public DashboardSqlRunnerTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(opts);
        ctx.Database.EnsureCreated();
        ctx.Tags.AddRange(Enumerable.Range(1, 5).Select(i => new Tag { Name = $"tag{i}" }));
        ctx.SaveChanges();
        _runner = new DashboardSqlRunner([new DashboardSqlTarget(DashboardSqlRunner.MainDatabase, "OmniCard", () => _conn)]);
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task Select_ReturnsTypedColumnsAndRows()
    {
        var result = await _runner.RunAsync("main", "SELECT Name, LENGTH(Name) AS Len, Name, NULL FROM Tags ORDER BY Name", default);

        Assert.Equal(["Name", "Len", "Name_2", "NULL"], result.Columns.Select(c => c.Key));
        Assert.Equal(("text", "number"), (result.Columns[0].Kind, result.Columns[1].Kind));
        Assert.Equal(5, result.Rows.Count);
        Assert.Equal("tag1", result.Rows[0][0]);
        Assert.Equal(4L, result.Rows[0][1]);
        Assert.Null(result.Rows[0][3]);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task LargeResults_AreCapped()
    {
        var sql = "WITH n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1500) SELECT i FROM n";

        var result = await _runner.RunAsync(null, sql, default);

        Assert.Equal(DashboardSqlRunner.MaxRows, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task RefusedAndFailingQueries_AreBadRequests_AndChangeNothing()
    {
        var refused = await Assert.ThrowsAsync<DashboardException>(() => _runner.RunAsync("main", "DELETE FROM Tags", default));
        var failing = await Assert.ThrowsAsync<DashboardException>(() => _runner.RunAsync("main", "SELECT Nope FROM Tags", default));
        var unknownDb = await Assert.ThrowsAsync<DashboardException>(() => _runner.RunAsync("Mtg", "SELECT 1", default));

        Assert.All([refused, failing, unknownDb], e => Assert.Equal(DashboardErrorKind.BadRequest, e.Kind));
        Assert.Equal(5, (await _runner.RunAsync("main", "SELECT COUNT(*) FROM Tags", default)).Rows[0][0] is long n ? n : -1);
    }

    [Fact]
    public async Task Schema_ListsTables_ButNotUsers()
    {
        var schema = await _runner.SchemaAsync("main", default);

        Assert.Contains(schema.Tables, t => t.Name == "Lots" && t.Columns.Any(c => c.Name == "Quantity"));
        Assert.DoesNotContain(schema.Tables, t => t.Name == "Users");
    }

    [Fact]
    public void Values_AreJsonSafe()
    {
        Assert.Equal("0x0102", DashboardSqlRunner.ToJsonValue(new byte[] { 1, 2 }));
        Assert.Null(DashboardSqlRunner.ToJsonValue(double.NaN));
        Assert.Equal("00000000-0000-0000-0000-000000000000", DashboardSqlRunner.ToJsonValue(Guid.Empty));
    }
}
