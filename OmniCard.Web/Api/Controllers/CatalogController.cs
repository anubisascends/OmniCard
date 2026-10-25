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
/// (<see cref="Languages"/>), which the next bulk download applies, and the automatic update schedule
/// (<see cref="Schedule"/>; anyone who can view the catalog sees it, only admins change or run it).
/// </summary>
public sealed class CatalogController(
    CatalogRefreshService refresh,
    ICatalogLanguageSettingsService languages,
    ICatalogScheduleSettingsService schedule,
    CatalogScheduleHostedService scheduler,
    TimeProvider clock) : ApiControllerBase
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

    [HttpGet("schedule")]
    [RequirePermission(Permissions.CatalogView)]
    public ActionResult<CatalogScheduleResponse> Schedule() => ToScheduleResponse(schedule.Get());

    /// <summary>Saves the automatic update schedule. Admin only.</summary>
    [HttpPut("schedule")]
    [ApiAuth(RequireAdmin = true)]
    public ActionResult<CatalogScheduleResponse> SetSchedule([FromBody] CatalogScheduleDto request)
    {
        var errors = new List<string>();
        var days = new List<DayOfWeek>();
        foreach (var d in request.Days)
        {
            if (Enum.TryParse<DayOfWeek>(d, ignoreCase: true, out var day) && Enum.IsDefined(day)) days.Add(day);
            else errors.Add($"Unknown day '{d}'.");
        }
        var games = new List<CardGame>();
        foreach (var g in request.Games)
        {
            if (LocationsController.ParseGame(g) is { } game) games.Add(game);
            else errors.Add($"Unknown game '{g}'.");
        }

        var settings = new CatalogScheduleSettings
        {
            Enabled = request.Enabled,
            Times = [.. request.Times],
            Days = days,
            Games = games,
            UpdateCards = request.UpdateCards,
            UpdatePrices = request.UpdatePrices,
        };
        if (errors.Count == 0)
            errors.AddRange(schedule.Save(settings));
        if (errors.Count > 0)
            return BadRequest(new { error = string.Join(" ", errors), errors });
        return ToScheduleResponse(schedule.Get());
    }

    /// <summary>Runs the scheduled update now (the saved games and operations), whether or not the
    /// schedule is on. Admin only.</summary>
    [HttpPost("schedule/run")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult RunScheduleNow()
    {
        var settings = schedule.Get();
        if (settings.Games.Count == 0 || (!settings.UpdateCards && !settings.UpdatePrices))
            return BadRequest(new { error = "Pick at least one game and card data and/or prices, then save, before running." });
        if (!scheduler.RequestRun())
            return Conflict(new { error = "A scheduled catalog update is already running." });
        return Accepted(new { started = true });
    }

    private CatalogScheduleResponse ToScheduleResponse(CatalogScheduleSettings s)
    {
        var state = schedule.GetRunState();
        var zone = clock.LocalTimeZone;
        var next = s.Enabled ? s.NextSlotAfter(clock.GetUtcNow().UtcDateTime, zone) : null;
        return new CatalogScheduleResponse(
            new CatalogScheduleDto
            {
                Enabled = s.Enabled,
                Times = s.Times,
                Days = s.Days.Select(d => d.ToString()).ToList(),
                Games = s.Games.Select(DtoMapping.GameId).ToList(),
                UpdateCards = s.UpdateCards,
                UpdatePrices = s.UpdatePrices,
            },
            Iso(next),
            Iso(state.LastRunStartedUtc),
            Iso(state.LastRunFinishedUtc),
            state.JobCount,
            state.FailedCount,
            state.Failures,
            scheduler.IsRunning,
            zone.DisplayName,
            AppAuthGate.IsAdmin(HttpContext));
    }

    private static string? Iso(DateTime? utc) =>
        utc is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("o") : null;

    private static CatalogJobDto? Map(CatalogRefreshService.JobSnapshot? j) =>
        j is null ? null : new CatalogJobDto(j.Game, j.Operation, j.State, j.Message, j.StartedAt, j.FinishedAt, j.Scheduled);
}
