using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Kiosk;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services.Kiosk;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Settings ▸ Kiosk (admin only): what customers can browse, the idle timeout, and the enrolled
/// kiosk devices (revoke / delete).</summary>
[ApiController]
[ApiAuth(RequireAdmin = true)]
[Route("api/kiosk-settings")]
public sealed class KioskSettingsController(IKioskSettingsService settings, KioskService kiosk) : ControllerBase
{
    [HttpGet]
    public ActionResult<KioskSettingsDto> Get() => ToDto(settings.Get());

    [HttpPut]
    public ActionResult<KioskSettingsDto> Put([FromBody] KioskSettingsDto request)
    {
        settings.Save(new KioskSettings
        {
            Enabled = request.Enabled,
            LocationIds = request.LocationIds?.ToList() ?? [],
            IdleTimeoutMinutes = request.IdleTimeoutMinutes,
            ShowPrices = request.ShowPrices,
            WelcomeMessage = request.WelcomeMessage,
        });
        return ToDto(settings.Get());
    }

    [HttpGet("devices")]
    public ActionResult<IReadOnlyList<KioskDeviceDto>> Devices() =>
        kiosk.ListDevices()
            .Select(d => new KioskDeviceDto(d.Id, d.Name, d.CreatedAt, d.CreatedBy, d.LastSeenAt, d.RevokedAt is not null))
            .ToList();

    [HttpPost("devices/{id:int}/revoke")]
    public IActionResult Revoke(int id) => kiosk.Revoke(id) ? NoContent() : NotFound();

    [HttpDelete("devices/{id:int}")]
    public IActionResult Delete(int id)
    {
        try
        {
            return kiosk.Delete(id) ? NoContent() : NotFound();
        }
        catch (KioskException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message, code = ex.Code });
        }
    }

    private static KioskSettingsDto ToDto(KioskSettings s) => new()
    {
        Enabled = s.Enabled,
        LocationIds = s.LocationIds,
        IdleTimeoutMinutes = s.IdleTimeoutMinutes,
        ShowPrices = s.ShowPrices,
        WelcomeMessage = s.WelcomeMessage,
    };
}
