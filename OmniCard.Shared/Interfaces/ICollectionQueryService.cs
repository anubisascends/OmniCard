using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICollectionQueryService
{
    Task<List<LocationTileSummary>> GetLocationOverviewsAsync(CardGame? gameFilter = null);

    /// <summary>The <paramref name="take"/> highest market-value unlisted singles across every
    /// game, ranked descending. Excludes traded lots, sealed/non-single product, and lots with
    /// an active listing.</summary>
    List<CollectionCard> GetTopValueCards(int take);
}
