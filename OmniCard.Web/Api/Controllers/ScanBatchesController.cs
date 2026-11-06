using System.Text.Json;
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
/// reviewer's edits, and commit/remove/rematch/discard. The Scan page can also save its scans as a
/// batch (<c>upload</c> → one <c>{id}/upload</c> per image → <c>{id}/upload/finish</c>). Batches aren't site-scoped until commit, where
/// the target location's site write access is enforced like the interactive scan commit.
/// </summary>
[ApiController]
[ApiAuth]
[Route("api/scan/batches")]
public sealed class ScanBatchesController(
    ScanBatchService batches, ScanBatchUploadService uploads, RequestSiteAccess siteAccess) : ControllerBase
{
    private int? UserId => AppAuthGate.CurrentUserId(HttpContext);
    private bool IsAdmin => AppAuthGate.IsAdmin(HttpContext);

    [HttpGet]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<IReadOnlyList<ScanBatchSummaryDto>> List() => Ok(batches.List(UserId));

    [HttpGet("count")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<ScanBatchCountDto> Count() => new ScanBatchCountDto(batches.CountUnclaimed());

    /// <summary>Start matching Collecting batches now (all of them when no ids are given) — the Scan
    /// page's Run button, needed when auto-processing is off.</summary>
    [HttpPost("run")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<ScanBatchRunResultDto> RunBatches([FromBody] ScanBatchRunRequest? request) =>
        new ScanBatchRunResultDto(batches.Run(request?.BatchIds ?? []));

    /// <summary>Start saving the Scan page's scans as a batch. It stays hidden and claimed by the caller
    /// until <see cref="FinishUpload"/> releases it.</summary>
    [HttpPost("upload")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<ScanBatchUploadStartedDto> StartUpload([FromBody] ScanBatchUploadStartRequest request)
    {
        if (UserId is not { } userId)
            return Unauthorized();
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });
        var name = AppAuthGate.CurrentUsername(HttpContext) ?? $"User {userId}";
        return uploads.Start(game, request, userId, name);
    }

    /// <summary>One image of a batch being saved, with its <c>item</c> (a <see cref="ScanBatchUploadItem"/>
    /// as JSON). One image per request keeps each under the server's request size limit.</summary>
    [HttpPost("{id:int}/upload")]
    [RequestSizeLimit(CardScanController.MaxFileSize + 256 * 1024)]
    [RequirePermission(Permissions.ScanView)]
    public async Task<ActionResult<ScanBatchUploadItemResultDto>> UploadItem(
        int id, IFormFile image, [FromForm] string item, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "No image provided" });
        if (!CardScanController.IsAcceptedImage(image.ContentType, image.FileName))
            return BadRequest(new { error = "Only JPEG, PNG and TIFF images are accepted" });
        if (image.Length > CardScanController.MaxFileSize)
            return BadRequest(new { error = "Image exceeds 30 MB limit" });
        ScanBatchUploadItem? upload;
        try
        {
            upload = JsonSerializer.Deserialize<ScanBatchUploadItem>(item ?? "", ScanBatchStorage.Json);
        }
        catch (JsonException)
        {
            upload = null;
        }
        if (upload is null)
            return BadRequest(new { error = "Invalid scan details" });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);
        return Run(() => new ScanBatchUploadItemResultDto(
            uploads.AddItem(id, UserId ?? 0, ms.ToArray(), image.FileName, StoredExtension(image), upload)));
    }

    /// <summary>Release a saved batch for review (Matching, unclaimed).</summary>
    [HttpPost("{id:int}/upload/finish")]
    [RequirePermission(Permissions.ScanView)]
    public ActionResult<ScanBatchUploadFinishedDto> FinishUpload(int id) =>
        Run(() => uploads.Finish(id, UserId ?? 0));

    /// <summary>Throw away a batch whose save failed part-way.</summary>
    [HttpPost("{id:int}/upload/cancel")]
    [RequirePermission(Permissions.ScanView)]
    public IActionResult CancelUpload(int id) =>
        RunVoid(() => uploads.Cancel(id, UserId ?? 0));

    /// <summary>The stored file's extension: the upload's own when it's an accepted one, else from its
    /// content type (a camera capture may come up without a usable name).</summary>
    private static string StoredExtension(IFormFile image)
    {
        var ext = Path.GetExtension(image.FileName ?? "").ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff")
            return ext;
        return image.ContentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/tiff" or "image/tif" or "image/x-tiff" => ".tif",
            _ => ".jpg",
        };
    }

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
            return Run(() => batches.Commit(id, UserId ?? 0, request.ContainerId, request.ItemIds, siteAccess.CanWriteLocation));
        }
        catch (UnknownScanGameException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (LocationGameMismatchException ex)
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
