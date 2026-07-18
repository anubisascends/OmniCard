namespace OmniCard.Models;

/// <summary>
/// Extracts a display image URL from a game-database card returned by
/// <c>ICardGameService.FindCardById</c> (a Scryfall <see cref="Card"/> or an
/// <see cref="OptcgCard"/>). Used to resolve art for collection cards whose stored
/// <see cref="CollectionCard.ImageUri"/> is null (e.g. imported cards).
/// </summary>
public static class CardImageUriResolver
{
    public static string? From(object? gameCard) => gameCard switch
    {
        Card scryfall => scryfall.ImageUris?.Normal ?? scryfall.ImageUris?.Large,
        OptcgCard optcg => optcg.CardImageUri,
        _ => null
    };
}
