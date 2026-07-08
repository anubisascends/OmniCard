using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IEbaySyncService
{
    Task<int> SyncAllActiveAsync();
    Task SyncSingleAsync(EbayListing listing);
}
