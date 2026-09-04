using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>Active for-sale listings, and unlisting.</summary>
public sealed class ListingsController(IListingService listings) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<ActiveListingDto>> Get([FromQuery] string? game) =>
        listings.GetActiveListings(LocationsController.ParseGame(game)).Select(DtoMapping.ToDto).ToList();

    /// <summary>Cancel the active listing on a lot (returns it to not-listed).</summary>
    [HttpDelete("lot/{lotId:int}")]
    public IActionResult Unlist(int lotId)
    {
        listings.Unlist([lotId]);
        return NoContent();
    }
}
