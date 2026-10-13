using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Admin-only "Reset collection" (Settings ▸ Reset collection): <c>GET</c> previews what would be
/// removed, <c>POST</c> wipes the owned collection while keeping all sales data. See
/// <see cref="CollectionResetService"/> for exactly what is kept.
/// </summary>
[ApiController]
[ApiAuth(RequireAdmin = true)]
[Route("api/collection-reset")]
public sealed class CollectionResetController(CollectionResetService reset) : ControllerBase
{
    [HttpGet]
    public ActionResult<CollectionResetCountsDto> Preview() => ToDto(reset.Preview());

    [HttpPost]
    public ActionResult<CollectionResetCountsDto> Reset([FromBody] CollectionResetRequest req)
    {
        try
        {
            return ToDto(reset.Reset(req.Confirmation, User.Identity?.Name));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static CollectionResetCountsDto ToDto(CollectionResetCounts c) => new(
        c.Lots, c.Units, c.Products, c.Movements, c.Locations, c.Sites, c.Lists, c.Trades, c.TradeSessions,
        c.Tags, c.TagRules, c.KeptListedLots, c.KeptLocations, c.KeptOrders, c.KeptCustomers,
        CollectionResetService.ConfirmationPhrase);
}
