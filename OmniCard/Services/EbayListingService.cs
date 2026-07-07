using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public interface IEbayListingService
{
    Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options);
    Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options);
    Task<bool> EndListingAsync(EbayListing listing);
    Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType);
}

public class EbayListingService : IEbayListingService
{
    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _ebayAuthService;
    private readonly IDbContextFactory<CollectionDbContext> _dbContextFactory;
    private readonly ILogger<EbayListingService> _logger;

    private static readonly Dictionary<string, int> ConditionMap = new()
    {
        ["NM"] = 3000, // Near Mint
        ["LP"] = 4000, // Lightly Played
        ["MP"] = 5000, // Moderately Played
        ["HP"] = 6000, // Heavily Played
        ["D"] = 7000,  // Damaged
    };

    public EbayListingService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService ebayAuthService,
        IDbContextFactory<CollectionDbContext> dbContextFactory,
        ILogger<EbayListingService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _ebayAuthService = ebayAuthService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Step 1: Create inventory item
            var sku = $"omnicard-{card.Id}";
            var inventoryItem = BuildInventoryItem(card, options);
            var inventoryJson = JsonSerializer.Serialize(inventoryItem);

            var inventoryContent = new StringContent(inventoryJson, Encoding.UTF8, "application/json");
            inventoryContent.Headers.Add("Content-Language", "en-US");

            var inventoryResponse = await client.PutAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                inventoryContent);

            if (!inventoryResponse.IsSuccessStatusCode)
            {
                var error = await inventoryResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to create inventory item: {Status} — {Error}", inventoryResponse.StatusCode, error);
                await SaveListingError(card.Id, options, $"Inventory creation failed: {inventoryResponse.StatusCode}");
                return false;
            }

            // Step 2: Create offer
            var offer = BuildOffer(sku, options);
            var offerJson = JsonSerializer.Serialize(offer);

            var offerResponse = await client.PostAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer",
                new StringContent(offerJson, Encoding.UTF8, "application/json"));

            if (!offerResponse.IsSuccessStatusCode)
            {
                var error = await offerResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to create offer: {Status} — {Error}", offerResponse.StatusCode, error);
                await SaveListingError(card.Id, options, $"Offer creation failed: {offerResponse.StatusCode}");
                return false;
            }

            var offerResponseJson = await offerResponse.Content.ReadAsStringAsync();
            using var offerDoc = JsonDocument.Parse(offerResponseJson);

            // If the offer response already contains listingId, treat as published
            if (offerDoc.RootElement.TryGetProperty("listingId", out var directListingId))
            {
                var ebayItemIdDirect = directListingId.GetString() ?? "";
                await SaveActiveListing(card.Id, options, ebayItemIdDirect);
                _logger.LogInformation("Created eBay listing {ItemId} for card {CardId} ({CardName})",
                    ebayItemIdDirect, card.Id, card.Name);
                return true;
            }

            // Step 3: Publish offer
            var offerId = offerDoc.RootElement.TryGetProperty("offerId", out var offerIdEl)
                ? offerIdEl.GetString() ?? ""
                : "";

            var publishResponse = await client.PostAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer/{Uri.EscapeDataString(offerId)}/publish",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            string ebayItemId;
            if (publishResponse.IsSuccessStatusCode)
            {
                var publishJson = await publishResponse.Content.ReadAsStringAsync();
                using var publishDoc = JsonDocument.Parse(publishJson);
                ebayItemId = publishDoc.RootElement.TryGetProperty("listingId", out var listingIdEl)
                    ? listingIdEl.GetString() ?? ""
                    : "";
            }
            else
            {
                var error = await publishResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to publish offer: {Status} — {Error}", publishResponse.StatusCode, error);
                await SaveListingError(card.Id, options, $"Publish failed: {publishResponse.StatusCode}");
                return false;
            }

            await SaveActiveListing(card.Id, options, ebayItemId);
            _logger.LogInformation("Created eBay listing {ItemId} for card {CardId} ({CardName})",
                ebayItemId, card.Id, card.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create eBay listing for card {CardId}", card.Id);
            return false;
        }
    }

    public async Task<bool> ReviseListingAsync(EbayListing listing, EbayListingOptions options)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var sku = $"omnicard-{listing.CollectionCardId}";
            var inventoryItem = BuildInventoryItem(null, options);
            var inventoryJson = JsonSerializer.Serialize(inventoryItem);

            var response = await client.PutAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                new StringContent(inventoryJson, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to revise listing {ItemId}: {Status}", listing.EbayItemId, response.StatusCode);
                return false;
            }

            using var ctx = _dbContextFactory.CreateDbContext();
            var tracked = await ctx.EbayListings.FindAsync(listing.Id);
            if (tracked is not null)
            {
                tracked.ListedPrice = options.Price;
                tracked.LastSyncedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }

            _logger.LogInformation("Revised eBay listing {ItemId}", listing.EbayItemId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revise eBay listing {ItemId}", listing.EbayItemId);
            return false;
        }
    }

    public async Task<bool> EndListingAsync(EbayListing listing)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var sku = $"omnicard-{listing.CollectionCardId}";

            var response = await client.DeleteAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}");

            // 204 No Content = success, 404 = already ended — both are acceptable
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Failed to end listing {ItemId}: {Status}", listing.EbayItemId, response.StatusCode);
                return false;
            }

            using var ctx = _dbContextFactory.CreateDbContext();
            var tracked = await ctx.EbayListings.FindAsync(listing.Id);
            if (tracked is not null)
            {
                tracked.Status = EbayListingStatus.Ended;
                tracked.EndTime = DateTime.UtcNow;
                tracked.LastSyncedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }

            _logger.LogInformation("Ended eBay listing {ItemId}", listing.EbayItemId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end eBay listing {ItemId}", listing.EbayItemId);
            return false;
        }
    }

    public async Task<List<EbaySellerPolicy>> GetSellerPoliciesAsync(string policyType)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return [];

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync(
                $"{_settings.ApiBaseUrl}/sell/account/v1/{policyType}_policy?marketplace_id=EBAY_US");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch {PolicyType} policies: {Status}", policyType, response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var results = new List<EbaySellerPolicy>();
            var arrayProp = $"{policyType}Policies";
            if (!doc.RootElement.TryGetProperty(arrayProp, out var policies))
                return results;

            var idProp = $"{policyType}PolicyId";
            foreach (var policy in policies.EnumerateArray())
            {
                results.Add(new EbaySellerPolicy
                {
                    PolicyId = policy.TryGetProperty(idProp, out var idEl) ? idEl.GetString() ?? "" : "",
                    Name = policy.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
                    PolicyType = policyType,
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch {PolicyType} policies", policyType);
            return [];
        }
    }

    private static object BuildInventoryItem(CollectionCard? card, EbayListingOptions options)
    {
        var conditionCode = ConditionMap.GetValueOrDefault(options.Condition, 3000);

        return new
        {
            availability = new
            {
                shipToLocationAvailability = new { quantity = 1 }
            },
            condition = conditionCode switch
            {
                3000 => "NEW_OTHER",
                4000 => "USED_GOOD",
                5000 => "USED_ACCEPTABLE",
                6000 => "USED_ACCEPTABLE",
                7000 => "FOR_PARTS_OR_NOT_WORKING",
                _ => "NEW_OTHER",
            },
            conditionDescription = options.Condition switch
            {
                "NM" => "Near Mint",
                "LP" => "Lightly Played",
                "MP" => "Moderately Played",
                "HP" => "Heavily Played",
                "D" => "Damaged",
                _ => options.Condition,
            },
            product = new
            {
                title = options.Title,
                description = options.Description,
            },
        };
    }

    private object BuildOffer(string sku, EbayListingOptions options)
    {
        return new
        {
            sku,
            marketplaceId = "EBAY_US",
            format = options.ListingType == EbayListingType.Auction ? "AUCTION" : "FIXED_PRICE",
            listingDescription = options.Description,
            pricingSummary = new
            {
                price = new { value = options.Price.ToString("F2"), currency = "USD" },
                auctionStartPrice = options.ListingType == EbayListingType.Auction
                    ? new { value = options.Price.ToString("F2"), currency = "USD" }
                    : null,
            },
            listingDuration = options.ListingType == EbayListingType.Auction && options.AuctionDuration.HasValue
                ? $"DAYS_{options.AuctionDuration.Value}"
                : null,
            listingPolicies = new
            {
                fulfillmentPolicyId = options.ShippingPolicyId,
                returnPolicyId = options.ReturnPolicyId,
                paymentPolicyId = options.PaymentPolicyId,
            },
            categoryId = options.EbayCategoryId ?? "38292",
        };
    }

    private async Task SaveActiveListing(int cardId, EbayListingOptions options, string ebayItemId)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        var existing = await ctx.EbayListings.FirstOrDefaultAsync(l => l.CollectionCardId == cardId);
        if (existing is not null)
        {
            existing.EbayItemId = ebayItemId;
            existing.Status = EbayListingStatus.Active;
            existing.ListingType = options.ListingType;
            existing.ListedPrice = options.Price;
            existing.StartTime = DateTime.UtcNow;
            existing.EndTime = null;
            existing.AuctionDuration = options.AuctionDuration;
            existing.ErrorMessage = null;
            existing.LastSyncedAt = null;
        }
        else
        {
            ctx.EbayListings.Add(new EbayListing
            {
                CollectionCardId = cardId,
                EbayItemId = ebayItemId,
                Status = EbayListingStatus.Active,
                ListingType = options.ListingType,
                ListedPrice = options.Price,
                StartTime = DateTime.UtcNow,
                AuctionDuration = options.AuctionDuration,
            });
        }
        await ctx.SaveChangesAsync();
    }

    private async Task SaveListingError(int cardId, EbayListingOptions options, string error)
    {
        try
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            var existing = await ctx.EbayListings.FirstOrDefaultAsync(l => l.CollectionCardId == cardId);
            if (existing is not null)
            {
                existing.Status = EbayListingStatus.Error;
                existing.ErrorMessage = error;
            }
            else
            {
                ctx.EbayListings.Add(new EbayListing
                {
                    CollectionCardId = cardId,
                    Status = EbayListingStatus.Error,
                    ListingType = options.ListingType,
                    ListedPrice = options.Price,
                    ErrorMessage = error,
                });
            }
            await ctx.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save listing error for card {CardId}", cardId);
        }
    }
}
