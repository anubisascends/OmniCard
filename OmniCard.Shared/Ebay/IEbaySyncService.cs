namespace OmniCard.Shared.Ebay;

public interface IEbaySyncService
{
    Task<int> SyncAllActiveAsync();
    Task SyncSingleAsync(EbayListing listing);
}
