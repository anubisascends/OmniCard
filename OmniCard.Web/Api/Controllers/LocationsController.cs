using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Audit;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Storage-location overview tiles + create/rename/delete (the web equivalent of the
/// desktop Manage Storage Locations dialog). Writes go to the SQL Server unified store.</summary>
public sealed class LocationsController(
    ICollectionQueryService queryService,
    IStorageContainerService containers,
    IDeckTypeService deckTypes,
    IDeckLegalityService deckLegality,
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

    /// <summary>Create a new location. 409 if the name is taken, 400 for an invalid/Bulk type or a
    /// deck box without a valid game.</summary>
    [HttpPost]
    public ActionResult<LocationSummaryDto> Create([FromBody] CreateLocationRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return BadRequest(new { error = "Name is required." });
        if (!Enum.TryParse<ContainerType>(req.Type, ignoreCase: true, out var type) || type == ContainerType.Bulk)
            return BadRequest(new { error = $"Invalid location type '{req.Type}'." });

        CardGame? game = null;
        if (type == ContainerType.DeckBox)
        {
            game = ParseGame(req.Game);
            if (game is null)
                return BadRequest(new { error = "A deck box must be assigned a game." });
            if (req.DeckTypeId is int dt && !DeckTypeBelongsToGame(dt, game.Value))
                return BadRequest(new { error = "The selected deck type doesn't belong to that game." });
        }

        if (containers.NameExists(name))
            return Conflict(new { error = $"A location named \"{name}\" already exists." });

        var created = containers.Create(name, type, req.SlotsPerPage, game, type == ContainerType.DeckBox ? req.DeckTypeId : null);
        return DtoMapping.ToDto(new LocationTileSummary
        {
            Container = created,
            DeckTypeName = created.DeckTypeId is int id ? deckTypes.GetById(id)?.Name : null,
        });
    }

    /// <summary>Assign/reassign a deck box's game system and deck type. 400 if not a deck box or the
    /// game/deck type is invalid; 409 if the box already holds cards from a different game.</summary>
    [HttpPut("{id:int}/deck-box")]
    public IActionResult SetDeckBox(int id, [FromBody] SetDeckBoxRequest req)
    {
        var game = ParseGame(req.Game);
        if (game is null)
            return BadRequest(new { error = "A valid game is required." });
        if (req.DeckTypeId is int dt && !DeckTypeBelongsToGame(dt, game.Value))
            return BadRequest(new { error = "The selected deck type doesn't belong to that game." });
        try
        {
            containers.SetDeckBox(id, game.Value, req.DeckTypeId);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        return NoContent();
    }

    /// <summary>Deck boxes with no game assigned yet (legacy), each with the game inferred from the
    /// cards inside. Backs the "assign game" prompt on the Locations page.</summary>
    [HttpGet("deck-boxes/needs-game")]
    public ActionResult<IReadOnlyList<DeckBoxNeedsGameDto>> DeckBoxesNeedingGame() =>
        containers.GetDeckBoxesMissingGame().Select(DtoMapping.ToDto).ToList();

    /// <summary>Advisory deck-legality check for a deck box against its deck type's rules.</summary>
    [HttpGet("{id:int}/deck-legality")]
    public ActionResult<DeckLegalityDto> DeckLegality(int id) =>
        DtoMapping.ToDto(deckLegality.Check(id));

    private bool DeckTypeBelongsToGame(int deckTypeId, CardGame game) =>
        deckTypes.GetById(deckTypeId) is { } dt && dt.Game == game;

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
