using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;
using OmniCard.Web.Services.BuyList;

namespace OmniCard.Web.Api.Controllers;

/// <summary>The per-game Buy Lists. Anyone signed in can read them (owned-copy counts stay within the sites
/// they can see); only administrators change them. The flag on scanned cards is served by
/// <see cref="CardScanController"/> and the customers' view by <see cref="KioskController"/>.</summary>
[ApiController]
[ApiAuth]
[Route("api/buy-list")]
public sealed class BuyListController(BuyListService buyList, RequestSiteAccess siteAccess) : ControllerBase
{
    [HttpGet]
    public ActionResult<BuyListPageDto> List([FromQuery] string? game, [FromQuery] bool fulfilled = false)
    {
        if (game is not null && LocationsController.ParseGame(game) is null)
            return BadRequest(new { error = $"Unknown game '{game}'" });
        return buyList.List(LocationsController.ParseGame(game), fulfilled, siteAccess.Current.ReadableSiteIds);
    }

    [HttpPost]
    [ApiAuth(RequireAdmin = true)]
    public ActionResult<BuyListEntryDto> Create([FromBody] BuyListEntryCreateRequest request)
    {
        try
        {
            return buyList.Add(request, AppAuthGate.CurrentUsername(HttpContext));
        }
        catch (BuyListException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [ApiAuth(RequireAdmin = true)]
    public ActionResult<BuyListEntryDto> Update(int id, [FromBody] BuyListEntryUpdateRequest request) =>
        buyList.Update(id, request) is { } dto ? dto : NotFound();

    [HttpDelete("{id:int}")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult Delete(int id) => buyList.Delete(id) ? NoContent() : NotFound();

    [HttpPut("set-aside")]
    [ApiAuth(RequireAdmin = true)]
    public IActionResult SetAside([FromBody] BuyListSetAsideRequest request)
    {
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });
        try
        {
            buyList.SetSetAside(game, request.LocationId);
            return NoContent();
        }
        catch (BuyListException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }
}
