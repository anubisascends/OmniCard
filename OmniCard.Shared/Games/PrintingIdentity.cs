using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Games;

/// <summary>
/// A language-neutral identity for a printing, so a Japanese copy of a card counts as "the same printing"
/// as its English row. Catalog ids differ per language (Scryfall gives every language its own id; One Piece
/// suffixes <c>@ja</c>), so the plain game card id can't be compared across languages.
/// </summary>
public static class PrintingIdentity
{
    /// <summary>
    /// MTG: set code + collector number (Scryfall's language siblings share both). One Piece: the card id
    /// without its <c>@lang</c> suffix (parallel arts share a collector number, so set + number would merge
    /// them). Other games: the card id itself (their non-English printings are separate sets, or the
    /// catalog is English-only).
    /// </summary>
    public static string Key(CardGame game, string? gameCardId, string? setCode, string? collectorNumber)
    {
        if (game == CardGame.Mtg && !string.IsNullOrWhiteSpace(setCode) && !string.IsNullOrWhiteSpace(collectorNumber))
            return $"{setCode.Trim().ToLowerInvariant()}|{collectorNumber.Trim().ToLowerInvariant()}";

        var id = gameCardId ?? "";
        if (game == CardGame.OnePiece && id.IndexOf('@') is var at and >= 0)
            id = id[..at];
        return id;
    }
}
