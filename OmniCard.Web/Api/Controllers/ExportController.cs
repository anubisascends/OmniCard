using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.ImportExport;
using OmniCard.Web.Helpers;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Collection CSV export in the app-native, TCGplayer, Moxfield, and Manabox formats.</summary>
public sealed class ExportController(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ICardService cardService,
    ICsvExportImportService csv,
    WebBinderCardService binderCards,
    IEnumerable<ICardGameService> gameServices) : ApiControllerBase
{
    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _gameServices = gameServices.ToDictionary(s => s.Game);

    /// <summary>Request body for <see cref="Selection"/>: the lot ids to export and the CSV format.</summary>
    public sealed record SelectionRequest(IReadOnlyList<int> Ids, string Format = "appnative");

    /// <summary>Export the (optionally filtered) collection as CSV. <paramref name="format"/> =
    /// appnative | tcgplayer | moxfield | manabox.</summary>
    [HttpGet("collection")]
    public IActionResult Collection(
        [FromQuery] string? game, [FromQuery] string? q, [FromQuery] string format = "appnative")
    {
        var gameFilter = LocationsController.ParseGame(game);
        using var ctx = dbFactory.CreateDbContext();
        var cards = CollectionQueryBuilder
            .BuildFilteredQuery(ctx, q ?? "", gameFilter, containerFilter: null, filterPreset: null, _gameServices)
            .OrderBy(c => c.Name).ThenBy(c => c.SetCode).ThenBy(c => c.Number)
            .ToList();

        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        MarketPriceHydrator.Populate(cardService, cards);

        return WriteCsv(cards, format, "collection");
    }

    /// <summary>Export a specific set of selected cards (by lot id) as CSV — e.g. the checked rows in a
    /// location's card table. <paramref name="request"/> carries the lot ids and the format
    /// (appnative | tcgplayer | moxfield | manabox). POST (not GET) so large selections don't overrun
    /// the query-string limit.</summary>
    [HttpPost("selection")]
    public IActionResult Selection([FromBody] SelectionRequest request)
    {
        if (request.Ids is null || request.Ids.Count == 0)
            return BadRequest(new { error = "No cards were selected to export." });

        var cards = binderCards.GetCollectionCards(request.Ids)
            .OrderBy(c => c.Name).ThenBy(c => c.SetCode).ThenBy(c => c.Number)
            .ToList();

        if (cards.Count == 0)
            return BadRequest(new { error = "None of the selected cards could be exported." });

        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        MarketPriceHydrator.Populate(cardService, cards);

        return WriteCsv(cards, request.Format, "selection");
    }

    private IActionResult WriteCsv(IReadOnlyList<CollectionCard> cards, string format, string namePrefix)
    {
        var fmt = (format ?? "appnative").ToLowerInvariant();
        Action<string> writer = fmt switch
        {
            "tcgplayer" => p => csv.ExportTcgPlayer(p, cards),
            "moxfield" => p => csv.ExportMoxfield(p, cards),
            "manabox" => p => csv.ExportManabox(p, cards),
            "ticker" => p => csv.ExportPriceTicker(p, cards),
            _ => p => csv.ExportAppNative(p, cards),
        };

        var bytes = TempFile.Produce(".csv", writer);
        return File(bytes, "text/csv", $"{namePrefix}-{fmt}.csv");
    }
}
