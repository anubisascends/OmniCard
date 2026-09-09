namespace OmniCard.Shared.Ebay;

public interface IEbayCatalogService
{
    Task<List<EbayCatalogMatch>> SearchCatalogAsync(string cardName, string setName, string? collectorNumber);
    Task<EbayMarketPrice?> GetMarketPriceAsync(string searchQuery, string condition, bool isFoil);
}
