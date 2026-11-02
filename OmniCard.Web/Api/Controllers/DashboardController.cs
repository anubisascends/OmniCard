using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Dashboard;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// The Dashboard: its widget layout (the user's own, everyone's default — administrators only — or the
/// built-in one), each widget's data, and the SQL schema browser. Layout rules live in
/// <see cref="DashboardLayoutService"/> and the data in <see cref="DashboardDataService"/>; this layer
/// adds who may do what:
/// <list type="bullet">
/// <item>card-search widgets need <c>collection.view</c> and only see the user's readable sites;</item>
/// <item>SQL widgets run for users with <c>dashboard.sql</c>, or when the query is on everyone's
/// current default (so anyone can see the administrator's SQL widgets, but not write their own).</item>
/// </list>
/// <c>GET /api/dashboard</c> keeps serving the classic totals (the MCP tool uses them).
/// </summary>
[RequirePermission(Permissions.DashboardView)]
public sealed class DashboardController(
    IAnalyticsService analytics,
    DashboardLayoutService layouts,
    DashboardDataService data,
    DashboardSqlRunner sql,
    PermissionService permissions,
    RequestSiteAccess siteAccess) : ApiControllerBase
{
    private int? UserId => AppAuthGate.CurrentUserId(HttpContext);
    private bool IsAdmin => AppAuthGate.IsAdmin(HttpContext);

    [HttpGet]
    public ActionResult<DashboardDto> Get()
    {
        var holdings = analytics.GetHoldings();
        var realized = analytics.GetRealized();
        return DtoMapping.ToDto(holdings, realized);
    }

    /// <summary>The layout to show, everyone's default to edit from, and what the user may change.</summary>
    [HttpGet("layout")]
    public Task<IActionResult> Layout() =>
        Run(async userId =>
        {
            var (layout, source, defaultLayout, defaultSource) = layouts.Resolve(userId);
            return Ok(new DashboardStateDto
            {
                Layout = layout,
                Source = source,
                DefaultLayout = defaultLayout,
                DefaultSource = defaultSource,
                CanEditDefault = IsAdmin,
                CanWriteSql = await CanWriteSql(userId),
                CanSearchCollection = await permissions.HasPermissionAsync(userId, Permissions.CollectionView),
                SqlDatabases = sql.Targets.Select(t => new DashboardSqlDatabaseDto(t.Key, t.Label)).ToList(),
            });
        });

    /// <summary>Save the user's own Dashboard.</summary>
    [HttpPut("layout")]
    public Task<IActionResult> SaveLayout([FromBody] DashboardLayoutDto layout) =>
        Run(async userId => Ok(layouts.SavePersonal(userId, layout, await CanWriteSql(userId))));

    /// <summary>Forget the user's own Dashboard, going back to everyone's default.</summary>
    [HttpDelete("layout")]
    public Task<IActionResult> ResetLayout() =>
        Run(userId =>
        {
            layouts.ResetPersonal(userId);
            return Task.FromResult<IActionResult>(NoContent());
        });

    /// <summary>Save everyone's default Dashboard (administrators).</summary>
    [HttpPut("layout/default")]
    public Task<IActionResult> SaveDefault([FromBody] DashboardLayoutDto layout) =>
        Run(_ => Task.FromResult<IActionResult>(IsAdmin
            ? Ok(layouts.SaveDefault(layout))
            : Forbidden("Only administrators can change the default dashboard.")));

    /// <summary>Drop everyone's default, going back to the built-in Dashboard (administrators).</summary>
    [HttpDelete("layout/default")]
    public Task<IActionResult> ResetDefault() =>
        Run(_ =>
        {
            if (!IsAdmin)
                return Task.FromResult<IActionResult>(Forbidden("Only administrators can change the default dashboard."));
            layouts.ResetDefault();
            return Task.FromResult<IActionResult>(NoContent());
        });

    /// <summary>One widget's data — for a saved widget or one being edited. POST because a widget's
    /// settings (a SQL query, say) can outgrow a query string.</summary>
    [HttpPost("data")]
    public Task<IActionResult> Data([FromBody] DashboardWidgetDto widget, CancellationToken ct) =>
        Run(async userId =>
        {
            var clean = DashboardLayoutService.Sanitize(new DashboardLayoutDto { Widgets = [widget] }).Widgets.FirstOrDefault();
            if (clean is null)
                return BadRequest(new { error = "Unknown widget type." });

            if (clean.Type == "search" && !await permissions.HasPermissionAsync(userId, Permissions.CollectionView))
                return Forbidden("You don't have permission to search the collection.");
            if (clean.Type == "sql" && !await CanWriteSql(userId)
                && !layouts.DefaultSql().Contains(DashboardLayoutService.NormalizeSql(clean.Sql ?? "")))
                return Forbidden("You don't have permission to run your own SQL. Only the SQL widgets on the default dashboard run for you.");

            return Ok(await data.GetAsync(clean, siteAccess.Current.ReadableSiteIds, ct));
        });

    /// <summary>The tables and columns a SQL widget may read in <paramref name="database"/> (<c>main</c>
    /// or a game id), for the editor's schema browser.</summary>
    [HttpGet("sql/schema")]
    public Task<IActionResult> Schema([FromQuery] string? database, CancellationToken ct) =>
        Run(async userId => await CanWriteSql(userId)
            ? Ok(await sql.SchemaAsync(database, ct))
            : Forbidden("You don't have permission to write SQL widgets."));

    // --- helpers ------------------------------------------------------------------------------------

    private Task<bool> CanWriteSql(int userId) => permissions.HasPermissionAsync(userId, Permissions.DashboardSql);

    private ObjectResult Forbidden(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = message });

    private async Task<IActionResult> Run(Func<int, Task<IActionResult>> action)
    {
        if (UserId is not { } userId)
            return Unauthorized(new { error = "Not authenticated. Please sign in." });
        try
        {
            return await action(userId);
        }
        catch (DashboardException ex)
        {
            return ex.Kind switch
            {
                DashboardErrorKind.NotFound => NotFound(new { error = ex.Message }),
                DashboardErrorKind.Forbidden => Forbidden(ex.Message),
                _ => BadRequest(new { error = ex.Message }),
            };
        }
    }
}
