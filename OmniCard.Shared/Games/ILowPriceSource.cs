namespace OmniCard.Shared.Games;

/// <summary>
/// A game whose catalog also stores TCGplayer's <b>low</b> listing price beside the market price (from
/// TCGCSV's <c>lowPrice</c>; MTG fetches it from TCGCSV by TCGplayer id during its price refresh).
/// Optional alongside <see cref="ICardGameService"/>: callers that want a low price fall back to the market
/// price when a game doesn't implement it or has no low for a card.
/// </summary>
public interface ILowPriceSource
{
    /// <summary>Low prices keyed by game card id, foil-aware like
    /// <see cref="ICardGameService.GetCurrentPrices"/> (falling back to the other finish). Cards without a low
    /// price are left out.</summary>
    Dictionary<string, decimal> GetLowPrices(IEnumerable<string> gameCardIds, bool isFoil);
}
