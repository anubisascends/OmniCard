using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>
/// Server-side catalog maintenance: refresh the per-game SQLite catalog caches (download bulk data,
/// update prices, recompute image hashes) without the desktop app. One job runs at a time; the SPA
/// polls <see cref="Status"/> for progress.
/// </summary>
public sealed class CatalogController(CatalogRefreshService refresh) : ApiControllerBase
{
    [HttpGet("status")]
    public ActionResult<CatalogStatusDto> Status()
    {
        var s = refresh.Status();
        return new CatalogStatusDto(Map(s.Running), s.Recent.Select(Map!).ToList());
    }

    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] CatalogRefreshRequest request)
    {
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });

        if (!refresh.TryStart(game, request.Operation, out var error))
        {
            // A busy refresh is a conflict; anything else (bad operation/game) is a bad request.
            return error!.Contains("already running")
                ? Conflict(new { error })
                : BadRequest(new { error });
        }
        return Ok(new { started = true });
    }

    private static CatalogJobDto? Map(CatalogRefreshService.JobSnapshot? j) =>
        j is null ? null : new CatalogJobDto(j.Game, j.Operation, j.State, j.Message, j.StartedAt, j.FinishedAt);
}
