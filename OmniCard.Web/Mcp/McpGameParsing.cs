using OmniCard.Shared.Cards;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Web.Mcp;

/// <summary>Game-string parsing for the MCP tools, reusing the exact same parser the SPA controllers
/// use (<see cref="LocationsController.ParseGame"/>) so the accepted identifiers (<c>mtg</c>,
/// <c>pokemon</c>, <c>optcg</c>, <c>riftbound</c>, <c>yugioh</c>, <c>finalfantasy</c>) stay in sync.</summary>
internal static class McpGameParsing
{
    /// <summary>Optional game filter — null/blank/unknown yields <c>null</c> (all games).</summary>
    public static CardGame? ParseGame(string? game) => LocationsController.ParseGame(game);

    /// <summary>Required game — throws a client-visible error when the identifier is missing/unknown.</summary>
    public static CardGame RequireGame(string? game) =>
        LocationsController.ParseGame(game)
        ?? throw new ArgumentException(
            $"Unknown game '{game}'. Use one of: mtg, pokemon, optcg, riftbound, yugioh, finalfantasy.");
}
