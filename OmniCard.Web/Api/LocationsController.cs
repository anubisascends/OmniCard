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

    internal static CardGame? ParseGame(string? game) =>
        string.IsNullOrWhiteSpace(game) ? null
        : Enum.TryParse<CardGame>(game, ignoreCase: true, out var g) ? g
        : null;
}
