using System.ComponentModel;
using ModelContextProtocol.Server;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Sets;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Mcp.Tools;

/// <summary>Read-only MCP tools over the per-game reference catalogs (independent of ownership):
/// look up printings, set checklists, and current market prices.</summary>
[McpServerToolType]
public sealed class CatalogTools(
    ISetChecklistService setChecklist,
    ICardService cardService,
    IEnumerable<ICardGameService> gameServices)
{
    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _gameServices =
        gameServices.ToDictionary(s => s.Game);

    [McpServerTool(Name = "lookup_card_catalog")]
    [Description("Search a game's card catalog (all printings, regardless of ownership). Returns name, " +
        "set, collector number, rarity, and image for each match.")]
    public IReadOnlyList<CatalogCardResult> LookupCardCatalog(
        [Description("Game: mtg, pokemon, optcg, riftbound, yugioh, finalfantasy.")] string game,
        [Description("Card name or query text.")] string query,
        [Description("Max results. Default 20, max 100.")] int maxResults = 20)
    {
        var g = McpGameParsing.RequireGame(game);
        if (!_gameServices.TryGetValue(g, out var svc))
            throw new ArgumentException($"Game '{game}' is not available.");

        maxResults = Math.Clamp(maxResults, 1, 100);
        return svc.SearchCards(query ?? "", maxResults)
            .Select(m => new CatalogCardResult(
                m.GameSpecificId, m.Name, m.SetCode, m.SetName, m.CollectorNumber, m.Rarity, m.ImageUri))
            .ToList();
    }

    [McpServerTool(Name = "set_checklist")]
    [Description("The full checklist for one set in a game: every printing annotated with owned quantity " +
        "and current prices, sorted by collector number.")]
    public async Task<SetChecklistDto> SetChecklist(
        [Description("Game: mtg, pokemon, optcg, riftbound, yugioh, finalfantasy.")] string game,
        [Description("Set code, e.g. 'MH3' (MTG) or 'OP01' (One Piece).")] string setCode)
    {
        var g = McpGameParsing.RequireGame(game);
        var checklist = await setChecklist.BuildAsync(g, setCode);
        return DtoMapping.ToDto(checklist);
    }

    [McpServerTool(Name = "card_price")]
    [Description("Current catalog market price for a specific printing, by its game-specific card id.")]
    public decimal? CardPrice(
        [Description("Game: mtg, pokemon, optcg, riftbound, yugioh, finalfantasy.")] string game,
        [Description("The game-specific card id (GameCardId), as returned by lookup_card_catalog or search_collection.")]
        string gameCardId,
        [Description("Whether to price the foil finish. Default false.")] bool foil = false)
    {
        var g = McpGameParsing.RequireGame(game);
        var prices = cardService.GetCurrentPrices(g, [gameCardId], foil);
        return prices.TryGetValue(gameCardId, out var price) ? price : null;
    }
}

/// <summary>A single catalog match returned by <c>lookup_card_catalog</c> — a projection of
/// <c>CardMatch</c> that drops the internal game-specific <c>Source</c> object.</summary>
public sealed record CatalogCardResult(
    string GameCardId,
    string Name,
    string SetCode,
    string SetName,
    string CollectorNumber,
    string Rarity,
    string? ImageUri);
