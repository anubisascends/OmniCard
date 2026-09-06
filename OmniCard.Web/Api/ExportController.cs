using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>Collection CSV export in the app-native, TCGplayer, Moxfield, and Manabox formats.</summary>
public sealed class ExportController(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ICardService cardService,
    ICsvExportImportService csv) : ApiControllerBase
{
    /// <summary>Export the (optionally filtered) collection as CSV. <paramref name="format"/> =
    /// appnative | tcgplayer | moxfield | manabox.</summary>
    [HttpGet("collection")]
    public IActionResult Collection(
        [FromQuery] string? game, [FromQuery] string? q, [FromQuery] string format = "appnative")
    {
        var gameFilter = LocationsController.ParseGame(game);
        using var ctx = dbFactory.CreateDbContext();
        var cards = CollectionQueryBuilder
            .BuildFilteredQuery(ctx, q ?? "", gameFilter, containerFilter: null, filterPreset: null)
            .OrderBy(c => c.Name).ThenBy(c => c.SetCode).ThenBy(c => c.Number)
            .ToList();

        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        MarketPriceHydrator.Populate(cardService, cards);

        Action<string> writer = format.ToLowerInvariant() switch
        {
            "tcgplayer" => p => csv.ExportTcgPlayer(p, cards),
            "moxfield" => p => csv.ExportMoxfield(p, cards),
            "manabox" => p => csv.ExportManabox(p, cards),
            _ => p => csv.ExportAppNative(p, cards),
        };

        var bytes = TempFile.Produce(".csv", writer);
        return File(bytes, "text/csv", $"collection-{format.ToLowerInvariant()}.csv");
    }
}
