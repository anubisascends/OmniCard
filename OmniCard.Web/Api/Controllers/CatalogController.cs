using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Security;
using OmniCard.Shared.Settings;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Services;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Server-side catalog maintenance: refresh the per-game SQLite catalog caches (download bulk data,
/// update prices, recompute image hashes) without the desktop app. One job runs at a time; the SPA
/// polls <see cref="Status"/> for progress. Also owns the per-game "languages to download" selection
/// (<see cref="Languages"/>), which the next bulk download applies.
/// </summary>
public sealed class CatalogController(CatalogRefreshService refresh, ICatalogLanguageSettingsService languages) : ApiControllerBase
{
    /// <summary>Every game's language options: what its source can download, what's selected, and
    /// what an owned copy can be tagged with (used by the scan/edit language pickers too).</summary>
    [HttpGet("languages")]
    [RequirePermission(Permissions.CatalogView)]
    public ActionResult<IReadOnlyList<CatalogLanguagesDto>> Languages() =>
        Enum.GetValues<CardGame>().Select(ToLanguagesDto).ToList();

    /// <summary>Saves which languages <c>request.Game</c>'s next bulk download fetches. English is always
    /// kept; unsupported codes are dropped. Un-ticked languages are pruned by that download.</summary>
    [HttpPut("languages")]
    [RequirePermission(Permissions.CatalogRefresh)]
    public ActionResult<CatalogLanguagesDto> SetLanguages([FromBody] SetCatalogLanguagesRequest request)
    {
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });
        languages.SetLanguages(game, request.Languages);
        return ToLanguagesDto(game);
    }

    private CatalogLanguagesDto ToLanguagesDto(CardGame game) => new(
        DtoMapping.GameId(game),
        CardLanguages.DownloadableFor(game),
        languages.GetLanguages(game),
        CardLanguages.ForGame(game));

    [HttpGet("status")]
    [RequirePermission(Permissions.CatalogView)]
    public ActionResult<CatalogStatusDto> Status()
    {
        var s = refresh.Status();
        return new CatalogStatusDto(Map(s.Running), s.Recent.Select(j => Map(j)!).ToList());
    }

    [HttpPost("refresh")]
    [RequirePermission(Permissions.CatalogRefresh)]
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
