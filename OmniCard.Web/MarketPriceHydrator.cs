using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web;

/// <summary>
/// Fills in <see cref="CollectionCard.MarketPrice"/> for owned singles by looking up the current
/// price in each game's read-only catalog DB. Mirrors the desktop's price pass
/// (OmniCard/Views/Root/CollectionViewModel.cs — batched GetCurrentPrices grouped by Game+IsFoil).
///
/// Necessary because singles do NOT persist a price on the row: <see cref="Product.LastMarketPrice"/>
/// is only populated for sealed products, so without this pass every single tile renders with no
/// price. Prices are looked up live and display-only (not persisted). Traded cards keep 0.
/// Shared by the Index (search), Location, Binder, and Card pages.
/// </summary>
public static class MarketPriceHydrator
{
    public static void Populate(ICardService cardService, IReadOnlyCollection<CollectionCard> cards)
    {
        foreach (var gameGroup in cards.Where(c => !c.IsTraded).GroupBy(c => c.Game))
        {
            ICardGameService gameService;
            try { gameService = cardService.GetGameService(gameGroup.Key); }
            catch { continue; } // game catalog not registered/available — leave price at 0

            foreach (var foilGroup in gameGroup.GroupBy(c => c.IsFoil))
            {
                var ids = foilGroup
                    .Select(c => c.GameCardId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct();

                IReadOnlyDictionary<string, decimal> prices;
                try { prices = gameService.GetCurrentPrices(ids, foilGroup.Key); }
                catch { continue; } // catalog DB missing/locked — leave these at 0

                foreach (var card in foilGroup)
                    if (prices.TryGetValue(card.GameCardId, out var price))
                        card.MarketPrice = price;
            }
        }
    }
}
