using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>Active for-sale listings, unlisting, and the printable pick list.</summary>
public sealed class ListingsController(
    IListingService listings,
    IPickListPdfExporter pickListPdf) : ApiControllerBase
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

    /// <summary>Printable pick list (cards to pull for active listings), optionally game-filtered.</summary>
    [HttpGet("picklist.pdf")]
    public IActionResult PickListPdf([FromQuery] string? game)
    {
        var entries = listings.GetPickList(LocationsController.ParseGame(game));
        var bytes = TempFile.Produce(".pdf", p => pickListPdf.Export(entries, p));
        return File(bytes, "application/pdf", "pick-list.pdf");
    }
}
