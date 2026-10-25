using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Kiosk;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Staff side of the customer kiosk (Sales ▸ Kiosk): the queue of pick lists customers sent to the counter,
/// claiming one to pull the cards, trimming it, and turning it into an order. Like the rest of Sales, not
/// site-scoped — the kiosk's browsable locations are chosen by an admin in Settings ▸ Kiosk.
/// </summary>
[ApiController]
[ApiAuth]
[Route("api/kiosk-queue")]
public sealed class KioskQueueController(KioskService kiosk) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.SalesKioskView)]
    public ActionResult<IReadOnlyList<KioskQueueEntryDto>> Get() => kiosk.GetQueue();

    /// <summary>Lists waiting to be claimed (and customers browsing) — polled for the Sales nav badge.</summary>
    [HttpGet("count")]
    [RequirePermission(Permissions.SalesKioskView)]
    public ActionResult<KioskQueueCountDto> Count() => kiosk.GetQueueCount();

    [HttpPost("{id:int}/claim")]
    [RequirePermission(Permissions.SalesKioskFulfill)]
    public IActionResult Claim(int id) => Run(() =>
        kiosk.Claim(id, AppAuthGate.CurrentUserId(HttpContext) ?? 0, AppAuthGate.CurrentUsername(HttpContext) ?? ""));

    [HttpPost("{id:int}/unclaim")]
    [RequirePermission(Permissions.SalesKioskFulfill)]
    public IActionResult Unclaim(int id) => Run(() => kiosk.Unclaim(id));

    /// <summary>Drops copies of one held lot from the list (all of them when <paramref name="quantity"/> is omitted).</summary>
    [HttpDelete("{id:int}/holds/{holdId:int}")]
    [RequirePermission(Permissions.SalesKioskFulfill)]
    public IActionResult RemoveHold(int id, int holdId, [FromQuery] int? quantity) =>
        Run(() => kiosk.RemoveQueueHold(id, holdId, quantity));

    [HttpPost("{id:int}/cancel")]
    [RequirePermission(Permissions.SalesKioskFulfill)]
    public IActionResult Cancel(int id) => Run(() => kiosk.Cancel(id));

    [HttpPost("{id:int}/order")]
    [RequirePermission(Permissions.SalesKioskFulfill)]
    public ActionResult<KioskCreateOrderResultDto> CreateOrder(int id, [FromBody] KioskCreateOrderRequest request)
    {
        try
        {
            return kiosk.CreateOrder(id, request.CustomerId, request.CustomerName);
        }
        catch (KioskException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message, code = ex.Code });
        }
    }

    private IActionResult Run(Action action)
    {
        try
        {
            action();
            return NoContent();
        }
        catch (KioskException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message, code = ex.Code });
        }
    }
}
