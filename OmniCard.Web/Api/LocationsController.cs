using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Storage-location overview tiles for the collection screen.</summary>
public sealed class LocationsController(ICollectionQueryService queryService) : ApiControllerBase
{
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
}
