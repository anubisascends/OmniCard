using OmniCard.Models;

namespace OmniCard.Web;

/// <summary>
/// Builds a TCGPlayer URL for a card. When a real TCGplayer product id is known it deep-links to
/// the exact product page; otherwise it falls back to a name search scoped to the game's product
/// line.
///
/// For the TCGCSV-backed games (Pokémon, Yu-Gi-Oh!, FFTCG, Riftbound) the card's
/// <see cref="Product.GameCardId"/> <em>is</em> the TCGplayer product id, so no extra lookup is
/// needed. MTG stores the Scryfall id in <c>GameCardId</c>, so its real product id is resolved
/// separately (from scryfall.db) and passed in via <paramref name="resolvedProductId"/>. One Piece
/// stores a set code (e.g. "OP01-001") with no TCGplayer id, so it always falls back to a search.
/// </summary>
public static class TcgPlayerLink
{
    public static string Build(CardGame game, string? gameCardId, string name, string? setName, int? resolvedProductId = null)
    {
        var productId = resolvedProductId
            ?? (int.TryParse(gameCardId, out var p) ? p : (int?)null);

        if (productId is int id)
            return $"https://www.tcgplayer.com/product/{id}";

        var query = string.IsNullOrWhiteSpace(setName) ? name : $"{name} {setName}";
        return $"https://www.tcgplayer.com/search/{LineSlug(game)}/product?q={Uri.EscapeDataString(query)}";
    }

    private static string LineSlug(CardGame game) => game switch
    {
        CardGame.Mtg => "magic",
        CardGame.OnePiece => "one-piece-card-game",
        CardGame.Riftbound => "riftbound",
        CardGame.Pokemon => "pokemon",
        CardGame.YuGiOh => "yugioh",
        CardGame.FinalFantasy => "final-fantasy-tcg",
        _ => "all",
    };
}
