using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Storage-location overview tiles + create/rename/delete (the web equivalent of the
/// desktop Manage Storage Locations dialog). Writes go to the SQL Server unified store.</summary>
public sealed class LocationsController(
    ICollectionQueryService queryService,
    IStorageContainerService containers,
    IPriceSheetService priceSheets,
    IPriceSheetPdfExporter priceSheetPdf) : ApiControllerBase
{
    /// <summary>Printable price-sheet PDF for a location's cards.</summary>
    [HttpGet("{id:int}/pricesheet.pdf")]
    public IActionResult PriceSheet(int id)
    {
        var container = containers.GetAll().FirstOrDefault(c => c.Id == id);
        if (container is null) return NotFound();
        var report = priceSheets.BuildReport(id, container.Name);
        var bytes = TempFile.Produce(".pdf", p => priceSheetPdf.Export(report, p));
        return File(bytes, "application/pdf", $"pricesheet-{container.Name}.pdf");
    }

    /// <summary>All locations (optionally filtered to one game) with card counts and valuations.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationSummaryDto>>> Get([FromQuery] string? game)
    {
        var gameFilter = ParseGame(game);
        var summaries = await queryService.GetLocationOverviewsAsync(gameFilter);
        return summaries.Select(DtoMapping.ToDto).ToList();
    }

    /// <summary>One location's overview tile. Its cards come from
    /// <c>GET /api/collection?containerId={id}</c>.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<LocationSummaryDto>> GetOne(int id)
    {
        var summaries = await queryService.GetLocationOverviewsAsync();
        var match = summaries.FirstOrDefault(s => s.Container.Id == id);
        return match is null ? NotFound() : DtoMapping.ToDto(match);
    }

    internal static CardGame? ParseGame(string? game) =>
        string.IsNullOrWhiteSpace(game) ? null
        : Enum.TryParse<CardGame>(game, ignoreCase: true, out var g) ? g
        : null;

    // --- writes ---

    /// <summary>Whether a location name is free (case-insensitive, incl. the reserved Bulk name).
    /// Backs the live add/rename validation.</summary>
    [HttpGet("name-available")]
    public ActionResult<NameAvailableDto> NameAvailable([FromQuery] string name, [FromQuery] int? excludeId) =>
        new NameAvailableDto(!containers.NameExists(name ?? "", excludeId));

    /// <summary>Create a new location. 409 if the name is taken, 400 for an invalid/Bulk type.</summary>
    [HttpPost]
    public ActionResult<LocationSummaryDto> Create([FromBody] CreateLocationRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return BadRequest(new { error = "Name is required." });
        if (!Enum.TryParse<ContainerType>(req.Type, ignoreCase: true, out var type) || type == ContainerType.Bulk)
            return BadRequest(new { error = $"Invalid location type '{req.Type}'." });
        if (containers.NameExists(name))
            return Conflict(new { error = $"A location named \"{name}\" already exists." });

        var created = containers.Create(name, type, req.SlotsPerPage);
        return DtoMapping.ToDto(new LocationTileSummary { Container = created });
    }

    /// <summary>Rename a location. 409 if the new name is taken.</summary>
    [HttpPut("{id:int}")]
    public IActionResult Rename(int id, [FromBody] RenameRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return BadRequest(new { error = "Name is required." });
        if (containers.NameExists(name, excludeId: id))
            return Conflict(new { error = $"A location named \"{name}\" already exists." });
        containers.Rename(id, name);
        return NoContent();
    }

    /// <summary>Delete a location; <paramref name="moveToBulk"/> keeps its cards (moved to Bulk) or
    /// deletes them.</summary>
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id, [FromQuery] bool moveToBulk = true)
    {
        containers.Delete(id, moveToBulk);
        return NoContent();
    }

    [HttpPut("{id:int}/always-available")]
    public IActionResult SetAlwaysAvailable(int id, [FromBody] BoolValueRequest req)
    {
        containers.SetAlwaysAvailable(id, req.Value);
        return NoContent();
    }

    [HttpPut("{id:int}/exclude-deck-check")]
    public IActionResult SetExcludeFromDeckCheck(int id, [FromBody] BoolValueRequest req)
    {
        containers.SetExcludeFromDeckCheck(id, req.Value);
        return NoContent();
    }
}
