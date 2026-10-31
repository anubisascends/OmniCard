using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Intake;
using OmniCard.Shared.Security;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Intake;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Intake — buying singles over the counter (<c>/api/intake</c>): price scanned cards as a cash / trade
/// quote, buy them into the admin's intake location (paying cash or store credit), and review the
/// history. Settings ▸ Intake (PUT) is admin-only.
/// </summary>
public sealed class IntakeController(
    IntakeService intake,
    IIntakeSettingsService settings,
    IStorageContainerService containers,
    RequestSiteAccess siteAccess) : ApiControllerBase
{
    /// <summary>The intake settings — the counter needs the percentages, the customer view and whether a
    /// location is set; Settings ▸ Intake edits the whole thing.</summary>
    [HttpGet("settings")]
    [RequirePermission(Permissions.IntakeQuote, Permissions.IntakeHistory, Permissions.SettingsView)]
    public ActionResult<IntakeSettingsDto> GetSettings()
    {
        var s = settings.Get();
        return IntakeService.ToDto(s, LocationName(s.LocationId));
    }

    [HttpPut("settings")]
    [ApiAuth(RequireAdmin = true)]
    public ActionResult<IntakeSettingsDto> PutSettings([FromBody] IntakeSettingsDto request)
    {
        if (request.LocationId is int id && containers.GetAll().All(c => c.Id != id))
            return BadRequest(new { error = "That location doesn't exist." });
        settings.Save(IntakeService.FromDto(request));
        return GetSettings();
    }

    [HttpPost("quote")]
    [RequirePermission(Permissions.IntakeQuote)]
    public ActionResult<IntakeQuoteDto> Quote([FromBody] IntakeQuoteRequest request)
    {
        try
        {
            return intake.Quote(request);
        }
        catch (IntakeException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("accept")]
    [RequirePermission(Permissions.IntakeBuy)]
    public ActionResult<IntakeAcceptResultDto> Accept([FromBody] IntakeAcceptRequest request)
    {
        // The cards go into the admin's intake location, so the buyer must be able to write to its site.
        if (settings.Get().LocationId is int locationId && !siteAccess.CanWriteLocation(locationId))
            return StatusCode(403, new { error = "You can't add cards to the intake location's site." });
        try
        {
            return intake.Accept(request, AppAuthGate.CurrentUserId(HttpContext), AppAuthGate.CurrentUsername(HttpContext) ?? "");
        }
        catch (IntakeException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message, quote = ex.Quote });
        }
    }

    [HttpGet]
    [RequirePermission(Permissions.IntakeHistory)]
    public ActionResult<IntakePageDto> List(
        [FromQuery] int page = 0, [FromQuery] int pageSize = 50, [FromQuery] int? customerId = null,
        [FromQuery] int? userId = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null) =>
        intake.List(page, pageSize, customerId, userId, from, to);

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.IntakeHistory)]
    public ActionResult<IntakeDetailDto> Get(int id) =>
        intake.Get(id) is { } detail ? detail : NotFound();

    private string? LocationName(int? id) => id is int value ? containers.GetAll().FirstOrDefault(c => c.Id == value)?.Name : null;
}
