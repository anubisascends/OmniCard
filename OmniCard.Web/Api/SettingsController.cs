using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>App/sales settings the SPA can read and edit — currently the for-sale location that picked
/// cards are moved to.</summary>
public sealed class SettingsController(ISalesSettingsService settings) : ApiControllerBase
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
}
