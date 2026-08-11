namespace OmniCard.Models;

/// <summary>
/// Extracts a display image URL from a game-database card returned by
/// <c>ICardGameService.FindCardById</c>. Used to resolve art for collection cards whose stored
/// <see cref="CollectionCard.ImageUri"/> is null (e.g. imported cards, or legacy rows committed
/// before a printing's art was captured).
/// </summary>
public static class CardImageUriResolver
{
    public static string? From(object? gameCard) => gameCard switch
    {
        Card scryfall => scryfall.ImageUris?.Normal ?? scryfall.ImageUris?.Large,
        OptcgCard optcg => optcg.CardImageUri,
        RiftboundCard riftbound => riftbound.CardImageUri,
        TcgCsvCard tcgCsv => tcgCsv.ImageUrl,
        _ => null
    };
}
