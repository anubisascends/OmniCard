using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Settings;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>App/sales settings the SPA can read and edit — the for-sale location that picked cards are
/// moved to, and the scan page's value-tier badge configuration.</summary>
public sealed class SettingsController(
    ISalesSettingsService settings,
    IScanBadgeSettingsService scanBadges) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<SalesSettingsDto> Get() =>
        new SalesSettingsDto(settings.ForSaleLocationId);

    [HttpPut]
    public IActionResult Update([FromBody] UpdateSalesSettingsRequest req)
    {
        settings.SetForSaleLocationId(req.ForSaleLocationId);
        return NoContent();
    }

    /// <summary>The scan page's value-tier badge config (currency + price thresholds). Readable by any
    /// signed-in user — the scan page needs it to render badges.</summary>
    [HttpGet("scan-badges")]
    public ActionResult<ScanBadgeSettingsDto> GetScanBadges()
    {
        var s = scanBadges.Get();
        return new ScanBadgeSettingsDto(s.CurrencyCode, s.Thresholds);
    }

    /// <summary>Update the value-tier badge config. Admin-only — it's an administration setting.</summary>
    [HttpPut("scan-badges")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult UpdateScanBadges([FromBody] UpdateScanBadgeSettingsRequest req)
    {
        scanBadges.Save(req.CurrencyCode, req.Thresholds);
        return NoContent();
    }
}
