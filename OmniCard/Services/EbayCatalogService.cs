using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IEbayCatalogService
{
    Task<List<EbayCatalogMatch>> SearchCatalogAsync(string cardName, string setName, string? collectorNumber);
    Task<EbayMarketPrice?> GetMarketPriceAsync(string searchQuery, string condition, bool isFoil);
}

public class EbayCatalogService : IEbayCatalogService
{
    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _ebayAuthService;
    private readonly ILogger<EbayCatalogService> _logger;

    public EbayCatalogService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService ebayAuthService,
        ILogger<EbayCatalogService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _ebayAuthService = ebayAuthService;
        _logger = logger;
    }

    public async Task<List<EbayCatalogMatch>> SearchCatalogAsync(string cardName, string setName, string? collectorNumber)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return [];

        var query = $"{cardName} {setName}";
        if (collectorNumber is not null)
            query += $" {collectorNumber}";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{_settings.ApiBaseUrl}/buy/browse/v1/item_summary/search?q={encodedQuery}&category_ids=38292&limit=10";

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("eBay catalog search failed: {Status}", response.StatusCode);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var results = new List<EbayCatalogMatch>();
        if (!doc.RootElement.TryGetProperty("itemSummaries", out var summaries))
            return results;

        foreach (var item in summaries.EnumerateArray())
        {
            var match = new EbayCatalogMatch
            {
                ItemId = item.GetProperty("itemId").GetString() ?? "",
                Title = item.GetProperty("title").GetString() ?? "",
            };

            if (item.TryGetProperty("price", out var price))
            {
                if (decimal.TryParse(price.GetProperty("value").GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var priceValue))
                    match.Price = priceValue;
                match.Currency = price.TryGetProperty("currency", out var curr) ? curr.GetString() : null;
            }

            if (item.TryGetProperty("condition", out var cond))
                match.Condition = cond.GetString();

            if (item.TryGetProperty("image", out var img) && img.TryGetProperty("imageUrl", out var imgUrl))
                match.ImageUrl = imgUrl.GetString();

            if (item.TryGetProperty("categories", out var cats))
            {
                foreach (var cat in cats.EnumerateArray())
                {
                    if (cat.TryGetProperty("categoryId", out var catId))
                    {
                        match.CategoryId = catId.GetString();
                        break;
                    }
                }
            }

            results.Add(match);
        }

        _logger.LogInformation("eBay catalog search for '{Query}' returned {Count} results", query, results.Count);
        return results;
    }

    public async Task<EbayMarketPrice?> GetMarketPriceAsync(string searchQuery, string condition, bool isFoil)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return null;

        var query = searchQuery;
        if (isFoil)
            query += " foil";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{_settings.ApiBaseUrl}/buy/browse/v1/item_summary/search?q={encodedQuery}&category_ids=38292&limit=50&filter=buyingOptions:{{FIXED_PRICE}}";

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("eBay market price search failed: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("itemSummaries", out var summaries))
            return null;

        var prices = new List<decimal>();
        foreach (var item in summaries.EnumerateArray())
        {
            if (item.TryGetProperty("price", out var price)
                && decimal.TryParse(price.GetProperty("value").GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                prices.Add(val);
            }
        }

        if (prices.Count == 0)
            return null;

        prices.Sort();
        var median = prices.Count % 2 == 0
            ? (prices[prices.Count / 2 - 1] + prices[prices.Count / 2]) / 2
            : prices[prices.Count / 2];

        return new EbayMarketPrice
        {
            MedianPrice = median,
            LowPrice = prices[0],
            HighPrice = prices[^1],
            SampleCount = prices.Count,
        };
    }
}
