using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web;

/// <summary>
/// Fills in <see cref="CollectionCard.ImageUri"/> for cards that have none stored, by looking the
/// card up in its game catalog — mirrors the desktop's HydrateMissingImageUris
/// (OmniCard/Views/Binder/BinderViewModel.cs, CollectionViewModel) so the web companion renders the
/// same art as the main app instead of blank tiles. Read-only catalog lookups; display-only, not
/// persisted. Shared by the Location, Binder, Card, Trade, and Index (search) pages.
/// </summary>
public static class CardArtHydrator
{
    public static void HydrateMissingImageUris(ICardService cardService, IReadOnlyList<CollectionCard> cards)
    {
        foreach (var gameGroup in cards.Where(c => string.IsNullOrEmpty(c.ImageUri)).GroupBy(c => c.Game))
        {
            ICardGameService gameService;
            try { gameService = cardService.GetGameService(gameGroup.Key); }
            catch { continue; } // game catalog not registered/available — leave these blank

            foreach (var card in gameGroup)
            {
                if (string.IsNullOrEmpty(card.GameCardId)) continue;
                try
                {
                    card.ImageUri = CardImageUriResolver.From(gameService.FindCardById(card.GameCardId));
                }
                catch
                {
                    // Leave ImageUri null; the tile falls back to scan art or a placeholder.
                }
            }
        }
    }
}
