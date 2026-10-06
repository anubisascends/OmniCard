using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Security;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;
using OmniCard.Web.Services.ScanBatches;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Watched-folder scan batches: list them (and the nav-badge count), claim one for review, save the
/// reviewer's edits, and commit/remove/rematch/discard. Batches aren't site-scoped until commit, where
/// the target location's site write access is enforced like the interactive scan commit.
/// </summary>
[ApiController]
[ApiAuth]
[Route("api/scan/batches")]
public sealed class ScanBatchesController(ScanBatchService batches) : ControllerBase
{
    private int? UserId => AppAuthGate.CurrentUserId(HttpContext);
    private bool IsAdmin => AppAuthGate.IsAdmin(HttpContext);

    [HttpGet]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<IReadOnlyList<ScanBatchSummaryDto>> List() => Ok(batches.List(UserId));

    [HttpGet("count")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<ScanBatchCountDto> Count() => new ScanBatchCountDto(batches.CountUnclaimed());

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<ScanBatchDto> Get(int id) => Run(() => batches.Get(id, UserId));

    /// <summary>The stored scan for one item (its JPEG preview for TIFFs). Served here rather than as a
    /// static file so it stays behind sign-in and the scan permission.</summary>
    [HttpGet("{id:int}/items/{itemId:int}/image")]
    [RequirePermission(Permissions.ScanView)]
    public IActionResult Image(int id, int itemId)
    {
        if (batches.ImageFile(id, itemId) is not { } file)
            return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(file.Path, file.ContentType);
    }

    /// <summary>Claim the batch for review. <paramref name="force"/> (admin only) takes it over from
    /// another reviewer.</summary>
    [HttpPost("{id:int}/claim")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<ScanBatchSummaryDto> Claim(int id, [FromQuery] bool force = false)
    {
        if (UserId is not { } userId)
            return Unauthorized();
        if (force && !IsAdmin)
            return Forbid();
        var name = AppAuthGate.CurrentUsername(HttpContext) ?? $"User {userId}";
        return Run(() => batches.Claim(id, userId, name, force));
    }

    [HttpPost("{id:int}/release")]
    [RequirePermission(Permissions.ScanView)]
    public IActionResult Release(int id) =>
        RunVoid(() => batches.Release(id, UserId ?? 0, IsAdmin));

    [HttpPut("{id:int}/items")]
    [RequirePermission(Permissions.ScanView)]
    public IActionResult SaveItems(int id, [FromBody] IReadOnlyList<ScanBatchItemEdit> edits) =>
        RunVoid(() => batches.SaveItems(id, UserId ?? 0, edits ?? []));

    [HttpPost("{id:int}/items/remove")]
    [RequirePermission(Permissions.ScanView)]
    public IActionResult Remove(int id, [FromBody] ScanBatchItemIdsRequest request) =>
        Run(() => new { batchClosed = batches.RemoveItems(id, UserId ?? 0, request.ItemIds) });

    [HttpPost("{id:int}/items/rematch")]
    [RequirePermission(Permissions.ScanView)]
    public IActionResult Rematch(int id, [FromBody] ScanBatchItemIdsRequest request) =>
        RunVoid(() => batches.Rematch(id, UserId ?? 0, request.ItemIds));

    [HttpPost("{id:int}/commit")]
    [RequirePermission(Permissions.ScanCommit)]
    [RequireSiteAccess(SiteAccessLevel.Write, Location = "ContainerId")]
    public ActionResult<ScanBatchCommitResultDto> Commit(int id, [FromBody] ScanBatchCommitRequest request)
    {
        try
        {
            return Run(() => batches.Commit(id, UserId ?? 0, request.ContainerId, request.ItemIds));
        }
        catch (UnknownScanGameException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (DeckBoxGameMismatchException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/discard")]
    [RequirePermission(Permissions.ScanCommit)]
    public IActionResult Discard(int id) =>
        RunVoid(() => batches.Discard(id, UserId ?? 0, IsAdmin));

    private ActionResult Run<T>(Func<T> action)
    {
        try
        {
            return Ok(action());
        }
        catch (ScanBatchException ex)
        {
            return ToResult(ex);
        }
    }

    private IActionResult RunVoid(Action action)
    {
        try
        {
            action();
            return NoContent();
        }
        catch (ScanBatchException ex)
        {
            return ToResult(ex);
        }
    }

    private ActionResult ToResult(ScanBatchException ex) => ex.Kind switch
    {
        ScanBatchErrorKind.NotFound => NotFound(new { error = ex.Message }),
        ScanBatchErrorKind.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message }),
        ScanBatchErrorKind.BadRequest => BadRequest(new { error = ex.Message }),
        _ => Conflict(new { error = ex.Message, claimedBy = ex.ClaimedBy }),
    };
}
