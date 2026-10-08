using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Security;
using OmniCard.Shared.Views;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Saved views of the Collection and Location pages: list the views offered on a page (and the one it
/// opens with), save / rename / delete them, set the user's or everyone's default, and copy a location's
/// view to other locations. The data rules (ownership, shared-view admin rights, default order) live in
/// <see cref="SavedViewService"/>; this layer adds the page's section permission (<c>collection.view</c>
/// or <c>locations.view</c>) and site access — a location the user can't read hides its views (404).
/// </summary>
public sealed class ViewsController(
    SavedViewService views,
    PermissionService permissions,
    RequestSiteAccess siteAccess) : ApiControllerBase
{
    private int? UserId => AppAuthGate.CurrentUserId(HttpContext);
    private bool IsAdmin => AppAuthGate.IsAdmin(HttpContext);

    /// <summary>The views offered on <paramref name="page"/> ("Collection" or "Location" +
    /// <paramref name="containerId"/>) with <paramref name="game"/> selected (none = All Games).</summary>
    [HttpGet]
    public Task<IActionResult> List([FromQuery] string page, [FromQuery] int? containerId, [FromQuery] string? game) =>
        Run(async userId =>
        {
            var p = SavedViewService.ParsePage(page);
            if (await DenyPage(userId, p, containerId) is { } denied) return denied;
            return Ok(views.List(userId, IsAdmin, p, containerId, SavedViewService.ParseSelectedGame(game)));
        });

    [HttpGet("{id:int}")]
    public Task<IActionResult> Get(int id) =>
        Run(async userId =>
        {
            if (await DenyView(userId, id) is { } denied) return denied;
            return Ok(views.Get(id, userId, IsAdmin));
        });

    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateSavedViewRequest request) =>
        Run(async userId =>
        {
            var p = SavedViewService.ParsePage(request.Page);
            if (await DenyPage(userId, p, request.ContainerId) is { } denied) return denied;
            return Ok(views.Create(userId, IsAdmin, request));
        });

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] UpdateSavedViewRequest request) =>
        Run(async userId =>
        {
            if (await DenyView(userId, id) is { } denied) return denied;
            return Ok(views.Update(id, userId, IsAdmin, request));
        });

    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id) =>
        Run(async userId =>
        {
            if (await DenyView(userId, id) is { } denied) return denied;
            views.Delete(id, userId, IsAdmin);
            return NoContent();
        });

    /// <summary>Make the view the user's default on the page they're on (<paramref name="page"/> /
    /// <paramref name="containerId"/>), or — <paramref name="everyone"/>, administrators only —
    /// everyone's default on the view's own page. <paramref name="game"/> is the game on screen (none =
    /// All Games): an any-game default replaces that game's default so it's what the page opens with.</summary>
    [HttpPut("{id:int}/default")]
    public Task<IActionResult> SetDefault(int id, [FromQuery] string page, [FromQuery] int? containerId,
        [FromQuery] string? game, [FromQuery] bool everyone = false) =>
        Run(async userId =>
        {
            var p = SavedViewService.ParsePage(page);
            if (await DenyView(userId, id) is { } denied) return denied;
            if (await DenyPage(userId, p, containerId) is { } deniedPage) return deniedPage;
            views.SetDefault(id, userId, IsAdmin, everyone, p, containerId, SavedViewService.ParseSelectedGame(game));
            return NoContent();
        });

    [HttpDelete("{id:int}/default")]
    public Task<IActionResult> ClearDefault(int id, [FromQuery] string page, [FromQuery] int? containerId,
        [FromQuery] bool everyone = false) =>
        Run(async userId =>
        {
            var p = SavedViewService.ParsePage(page);
            if (await DenyView(userId, id) is { } denied) return denied;
            if (await DenyPage(userId, p, containerId) is { } deniedPage) return deniedPage;
            views.ClearDefault(id, userId, IsAdmin, everyone, p, containerId);
            return NoContent();
        });

    /// <summary>Copy a location's view to other locations. Locations the user can't read are skipped.</summary>
    [HttpPost("{id:int}/copy")]
    public Task<IActionResult> Copy(int id, [FromBody] CopySavedViewRequest request) =>
        Run(async userId =>
        {
            if (await DenyView(userId, id) is { } denied) return denied;
            var targets = (request.ContainerIds ?? []).Distinct().Where(siteAccess.CanReadLocation).ToList();
            return Ok(new CopySavedViewResultDto(views.Copy(id, userId, IsAdmin, targets, request.SetDefault)));
        });

    // --- helpers ------------------------------------------------------------------------------------

    /// <summary>Non-null when the user may not see <paramref name="page"/>: they lack its section's view
    /// permission (403) or can't read the location's site (404).</summary>
    private async Task<IActionResult?> DenyPage(int userId, SavedViewPage page, int? containerId)
    {
        var permission = page == SavedViewPage.Collection ? Permissions.CollectionView : Permissions.LocationsView;
        if (!await permissions.HasPermissionAsync(userId, permission))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "You don't have permission to do that." });
        if (page == SavedViewPage.Location && containerId is int cid && !siteAccess.CanReadLocation(cid))
            return NotFound(new { error = "Location not found." });
        return null;
    }

    /// <summary><see cref="DenyPage"/> for the page a view belongs to (404 when it doesn't exist).</summary>
    private async Task<IActionResult?> DenyView(int userId, int id) =>
        views.ScopeOf(id) is { } scope
            ? await DenyPage(userId, scope.Page, scope.ContainerId)
            : NotFound(new { error = "View not found." });

    private async Task<IActionResult> Run(Func<int, Task<IActionResult>> action)
    {
        if (UserId is not { } userId)
            return Unauthorized(new { error = "Not authenticated. Please sign in." });
        try
        {
            return await action(userId);
        }
        catch (SavedViewException ex)
        {
            return ex.Kind switch
            {
                SavedViewErrorKind.NotFound => NotFound(new { error = ex.Message }),
                SavedViewErrorKind.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message }),
                SavedViewErrorKind.BadRequest => BadRequest(new { error = ex.Message }),
                _ => Conflict(new { error = ex.Message }),
            };
        }
    }
}
