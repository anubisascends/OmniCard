using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICollectionQueryService
{
    Task<List<LocationTileSummary>> GetLocationOverviewsAsync(CardGame? gameFilter = null);
}
