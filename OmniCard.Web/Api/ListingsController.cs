using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Active for-sale listings, unlisting, and the printable pick list.</summary>
public sealed class ListingsController(
    IListingService listings,
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IPickListPdfExporter pickListPdf) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<ActiveListingDto>> Get([FromQuery] string? game) =>
        listings.GetActiveListings(LocationsController.ParseGame(game)).Select(DtoMapping.ToDto).ToList();

    /// <summary>Full detail (including listing id and editable sale properties) for the Manage Listings screen.</summary>
    [HttpGet("details")]
    public ActionResult<IReadOnlyList<ListingDetailDto>> GetDetails([FromQuery] string? game) =>
        listings.GetListingDetails(LocationsController.ParseGame(game)).Select(DtoMapping.ToDto).ToList();

    /// <summary>Edit an active listing's sale properties (price, channel, quantity, note) in place.</summary>
    [HttpPut("{listingId:int}")]
    public IActionResult Update(int listingId, [FromBody] UpdateListingRequest req)
    {
        if (!Enum.TryParse<SalesChannel>(req.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Invalid channel '{req.Channel}'" });

        try
        {
            listings.UpdateListing(listingId, req.ListedPrice, channel, req.Quantity, req.Note);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        return NoContent();
    }

    /// <summary>List a single card lot for sale, splitting the lot when only part of a stack is listed.</summary>
    [HttpPost]
    public IActionResult Create([FromBody] CreateListingRequest req)
    {
        if (!Enum.TryParse<SalesChannel>(req.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Invalid channel '{req.Channel}'" });
        if (req.Quantity <= 0 || req.Price < 0)
            return BadRequest(new { error = "Quantity must be positive and price non-negative." });

        try
        {
            var listedLotId = listings.ListForSaleSplitting(req.LotId, channel, req.Price, req.Quantity, req.Note);
            if (listedLotId == 0)
                return NotFound(new { error = "Card lot not found." });
            return Ok(new { lotId = listedLotId });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>List several whole lots for sale at once, each at its own price.</summary>
    [HttpPost("bulk")]
    public IActionResult CreateBulk([FromBody] BulkListingRequest req)
    {
        if (!Enum.TryParse<SalesChannel>(req.Channel, ignoreCase: true, out var channel))
            return BadRequest(new { error = $"Invalid channel '{req.Channel}'" });

        var ids = req.Items.Select(i => i.LotId).Distinct().ToList();
        using var ctx = dbFactory.CreateDbContext();
        var quantities = ctx.Lots.Where(l => ids.Contains(l.Id)).ToDictionary(l => l.Id, l => l.Quantity);

        var listed = 0;
        foreach (var item in req.Items)
        {
            if (item.Price < 0 || !quantities.TryGetValue(item.LotId, out var qty)) continue;
            // Whole-lot listings never split; ListForSale already skips lots that are already listed.
            listed += listings.ListForSale([item.LotId], channel, item.Price, qty, req.Note) > 0 ? 1 : 0;
        }
        return Ok(new { listed });
    }

    /// <summary>Mark the given lots' active listings as picked — moves each lot to the configured
    /// for-sale location (a 400 is returned if no such location is configured).</summary>
    [HttpPost("pick")]
    public IActionResult Pick([FromBody] LotIdsRequest req)
    {
        try
        {
            var picked = listings.MarkPicked(req.LotIds);
            return Ok(new { picked });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

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
