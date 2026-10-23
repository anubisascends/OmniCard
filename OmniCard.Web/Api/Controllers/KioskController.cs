using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Kiosk;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Kiosk;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// The customer kiosk's API (the <c>/kiosk</c> app). Deliberately NOT an <see cref="Infrastructure.ApiControllerBase"/>:
/// kiosks carry no staff sign-in, only the kiosk device cookie (<see cref="KioskAuth"/>). <c>status</c>,
/// <c>enroll</c> and <c>unenroll</c> are reachable without it; everything else needs an enrolled,
/// unrevoked device and acts only on that device's own customer session.
/// </summary>
[ApiController]
[Route("api/kiosk")]
public sealed class KioskController(KioskService kiosk, IKioskSettingsService settings, UserService users) : ControllerBase
{
    private const string DeviceItem = "kiosk-device";

    private KioskDevice Device => (KioskDevice)HttpContext.Items[DeviceItem]!;

    /// <summary>What to draw: the enrollment screen, "closed", the welcome screen or the customer's session.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<KioskStatusDto>> Status()
    {
        var device = await ResolveDeviceAsync();
        return device is null ? new KioskStatusDto { Enrolled = false } : kiosk.GetStatus(device);
    }

    /// <summary>Turns this device into a kiosk. Needs an administrator's credentials.</summary>
    [HttpPost("enroll")]
    public async Task<ActionResult<KioskStatusDto>> Enroll([FromBody] KioskCredentialsRequest request)
    {
        var admin = await AuthenticateAdminAsync(request);
        if (admin is null)
            return Unauthorized(new { error = "An administrator's username and password are required.", code = "not-admin" });

        var device = kiosk.Enroll(request.DeviceName, admin.Username);
        await KioskAuth.SignInAsync(HttpContext, device);
        return kiosk.GetStatus(device);
    }

    /// <summary>Takes this device out of kiosk mode (revokes it). Needs an administrator's credentials.</summary>
    [HttpPost("unenroll")]
    public async Task<IActionResult> Unenroll([FromBody] KioskCredentialsRequest request)
    {
        var admin = await AuthenticateAdminAsync(request);
        if (admin is null)
            return Unauthorized(new { error = "An administrator's username and password are required.", code = "not-admin" });

        if (await KioskAuth.DeviceIdAsync(HttpContext) is int id)
            kiosk.Revoke(id);
        await KioskAuth.SignOutAsync(HttpContext);
        return NoContent();
    }

    [HttpPost("session")]
    [KioskDevice]
    public ActionResult<KioskSessionDto> StartSession([FromBody] KioskStartSessionRequest request) =>
        Run(() => kiosk.StartSession(Device.Id, request.Name));

    [HttpDelete("session")]
    [KioskDevice]
    public IActionResult EndSession()
    {
        kiosk.EndSession(Device.Id);
        return NoContent();
    }

    /// <summary>Keeps the customer's session alive while they browse; 204 when there's no session.</summary>
    [HttpPost("session/ping")]
    [KioskDevice]
    public ActionResult<KioskSessionDto> Ping() =>
        kiosk.Touch(Device.Id) is { } session ? session : NoContent();

    [HttpPost("session/holds")]
    [KioskDevice]
    public ActionResult<KioskSessionDto> AddHold([FromBody] KioskHoldRequest request) =>
        Run(() => kiosk.AddHold(Device.Id, request.Key, request.Quantity));

    [HttpPost("session/holds/remove")]
    [KioskDevice]
    public ActionResult<KioskSessionDto> RemoveHold([FromBody] KioskHoldRequest request) =>
        Run(() => kiosk.RemoveHold(Device.Id, request.Key, request.Quantity));

    [HttpPost("session/submit")]
    [KioskDevice]
    public ActionResult<KioskSubmitResultDto> Submit() => Run(() => kiosk.Submit(Device.Id));

    [HttpGet("games")]
    [KioskDevice]
    public ActionResult<IReadOnlyList<KioskGameDto>> Games() =>
        settings.Get().Enabled ? kiosk.GetGames() : [];

    [HttpGet("cards")]
    [KioskDevice]
    public ActionResult<PagedResult<KioskCardDto>> Cards(
        [FromQuery] string? game, [FromQuery] string? q, [FromQuery] string? sort,
        [FromQuery] int skip = 0, [FromQuery] int take = 48)
    {
        if (!settings.Get().Enabled)
            return new PagedResult<KioskCardDto>(0, skip, take, []);
        if (LocationsController.ParseGame(game) is not { } g)
            return BadRequest(new { error = "Pick a game.", code = "bad-game" });
        return kiosk.Search(Device.Id, g, q, sort, skip, take);
    }

    private ActionResult<T> Run<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (KioskException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message, code = ex.Code });
        }
    }

    private async Task<KioskDevice?> ResolveDeviceAsync() =>
        await KioskAuth.DeviceIdAsync(HttpContext) is int id ? kiosk.GetActiveDevice(id) : null;

    private async Task<Shared.Settings.User?> AuthenticateAdminAsync(KioskCredentialsRequest request)
    {
        var user = await users.AuthenticateAsync(request.Login ?? "", request.Password ?? "");
        return user is { } u && (u.IsAdmin || u.IsSystem) ? u : null;
    }

    /// <summary>Requires an enrolled, unrevoked kiosk device (else 401 <c>not-enrolled</c>) and stashes it
    /// for the action.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    private sealed class KioskDeviceAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var http = context.HttpContext;
            var device = await KioskAuth.DeviceIdAsync(http) is int id
                ? http.RequestServices.GetRequiredService<KioskService>().GetActiveDevice(id)
                : null;
            if (device is null)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "This device isn't set up as a kiosk.", code = "not-enrolled" });
                return;
            }
            http.Items[DeviceItem] = device;
        }
    }
}
