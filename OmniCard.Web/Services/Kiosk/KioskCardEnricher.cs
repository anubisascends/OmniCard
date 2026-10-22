using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Web.Helpers;

namespace OmniCard.Web.Services.Kiosk;

/// <summary>Kiosk art + prices through the same helpers as the Collection page: catalog art for cards with
/// none stored, the locally cached copy when there is one, and live market prices.</summary>
public sealed class KioskCardEnricher(ICardService cardService, CardImageCacheService imageCache) : IKioskCardEnricher
{
    public void Enrich(IReadOnlyList<CollectionCard> cards, bool prices)
    {
        CardArtHydrator.HydrateMissingImageUris(cardService, cards);
        imageCache.PreferCached(cards);
        if (prices) MarketPriceHydrator.Populate(cardService, cards);
    }
}
