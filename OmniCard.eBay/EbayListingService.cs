using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.eBay;

public class EbayListingService : IEbayListingService
{
    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _ebayAuthService;
    private readonly IDbContextFactory<OmniCardDbContext> _dbContextFactory;
    private readonly IEbaySellingSettingsService _sellingSettings;
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
        IDbContextFactory<OmniCardDbContext> dbContextFactory,
        IEbaySellingSettingsService sellingSettings,
        ILogger<EbayListingService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _ebayAuthService = ebayAuthService;
        _dbContextFactory = dbContextFactory;
        _sellingSettings = sellingSettings;
        _logger = logger;
    }

    public async Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        var selling = _sellingSettings.Get();
        if (!_sellingSettings.IsSetupComplete())
        {
            _logger.LogWarning("eBay listing blocked — seller setup incomplete for card {CardId}", card.Id);
            await SaveListingError(card.Id, options, "eBay setup incomplete — run Settings ▸ eBay Selling ▸ Run eBay Setup.");
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Step 1: Create inventory item
            var sku = $"omnicard-{card.Id}";
            var inventoryItem = BuildInventoryItem(card, options);
            var inventoryJson = JsonSerializer.Serialize(inventoryItem);

            var inventoryResponse = await client.PutAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                JsonContent(inventoryJson));

            if (!inventoryResponse.IsSuccessStatusCode)
            {
                var error = await inventoryResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to create inventory item: {Status} — {Error}", inventoryResponse.StatusCode, error);
                await SaveListingError(card.Id, options, $"Inventory creation failed: {inventoryResponse.StatusCode}");
                return false;
            }

            // Step 2: Create or update the offer. eBay allows only ONE offer per SKU, so a
            // prior attempt that created an offer but didn't publish leaves one behind that we
            // must update (with current pricing/policies/location) rather than recreate —
            // otherwise createOffer fails with errorId 25002 "Offer entity already exists".
            var existingOfferId = await FindExistingOfferAsync(client, sku);

            string offerId;
            if (existingOfferId is null)
            {
                var offerJson = JsonSerializer.Serialize(BuildOffer(sku, options, selling));
                var offerResponse = await client.PostAsync(
                    $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer",
                    JsonContent(offerJson));

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

                offerId = offerDoc.RootElement.TryGetProperty("offerId", out var offerIdEl)
                    ? offerIdEl.GetString() ?? ""
                    : "";
            }
            else
            {
                // Update the existing offer so stale data from an earlier failed attempt
                // (e.g. empty policies before setup ran) is replaced with current values.
                offerId = existingOfferId;
                var updateJson = JsonSerializer.Serialize(BuildOfferUpdate(options, selling));
                var updateResponse = await client.PutAsync(
                    $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer/{Uri.EscapeDataString(offerId)}",
                    JsonContent(updateJson));

                if (!updateResponse.IsSuccessStatusCode)
                {
                    var error = await updateResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("Failed to update existing offer {OfferId}: {Status} — {Error}", offerId, updateResponse.StatusCode, error);
                    await SaveListingError(card.Id, options, $"Offer update failed: {updateResponse.StatusCode}");
                    return false;
                }
            }

            // Step 3: Publish offer
            var publishResponse = await client.PostAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer/{Uri.EscapeDataString(offerId)}/publish",
                JsonContent("{}"));

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

            var sku = $"omnicard-{listing.LotId}";
            var inventoryItem = BuildInventoryItem(null, options);
            var inventoryJson = JsonSerializer.Serialize(inventoryItem);

            var response = await client.PutAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                JsonContent(inventoryJson));

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

            var sku = $"omnicard-{listing.LotId}";

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
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to fetch {PolicyType} policies: {Status} — {Error}", policyType, response.StatusCode, error);
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

    // eBay's Inventory API requires a Content-Language header on inventory_item and
    // offer requests; omitting it fails createOffer with errorId 25709.
    private static StringContent JsonContent(string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("Content-Language", "en-US");
        return content;
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

    private object BuildOffer(string sku, EbayListingOptions options, EbaySellingSettings selling)
    {
        return new
        {
            sku,
            marketplaceId = "EBAY_US",
            format = options.ListingType == EbayListingType.Auction ? "AUCTION" : "FIXED_PRICE",
            listingDescription = options.Description,
            merchantLocationKey = selling.MerchantLocationKey,
            pricingSummary = new
            {
                price = new { value = options.Price.ToString("F2", CultureInfo.InvariantCulture), currency = "USD" },
                auctionStartPrice = options.ListingType == EbayListingType.Auction
                    ? new { value = options.Price.ToString("F2", CultureInfo.InvariantCulture), currency = "USD" }
                    : null,
            },
            listingDuration = options.ListingType == EbayListingType.Auction && options.AuctionDuration.HasValue
                ? $"DAYS_{options.AuctionDuration.Value}"
                : null,
            listingPolicies = new
            {
                fulfillmentPolicyId = options.ShippingPolicyId ?? selling.FulfillmentPolicyId,
                returnPolicyId = options.ReturnPolicyId ?? selling.ReturnPolicyId,
                paymentPolicyId = options.PaymentPolicyId ?? selling.PaymentPolicyId,
            },
            categoryId = options.EbayCategoryId ?? "38292",
        };
    }

    // updateOffer payload: same as create minus the immutable sku/marketplaceId/format fields.
    private object BuildOfferUpdate(EbayListingOptions options, EbaySellingSettings selling)
    {
        return new
        {
            listingDescription = options.Description,
            merchantLocationKey = selling.MerchantLocationKey,
            pricingSummary = new
            {
                price = new { value = options.Price.ToString("F2", CultureInfo.InvariantCulture), currency = "USD" },
                auctionStartPrice = options.ListingType == EbayListingType.Auction
                    ? new { value = options.Price.ToString("F2", CultureInfo.InvariantCulture), currency = "USD" }
                    : null,
            },
            listingDuration = options.ListingType == EbayListingType.Auction && options.AuctionDuration.HasValue
                ? $"DAYS_{options.AuctionDuration.Value}"
                : null,
            listingPolicies = new
            {
                fulfillmentPolicyId = options.ShippingPolicyId ?? selling.FulfillmentPolicyId,
                returnPolicyId = options.ReturnPolicyId ?? selling.ReturnPolicyId,
                paymentPolicyId = options.PaymentPolicyId ?? selling.PaymentPolicyId,
            },
            categoryId = options.EbayCategoryId ?? "38292",
        };
    }

    // Returns the offerId of an existing offer for this SKU (eBay allows one per SKU), or null.
    private async Task<string?> FindExistingOfferAsync(HttpClient client, string sku)
    {
        var response = await client.GetAsync(
            $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer?sku={Uri.EscapeDataString(sku)}&marketplace_id=EBAY_US");

        // 404 = no offers for this SKU yet.
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("offers", out var offers)
            && offers.ValueKind == JsonValueKind.Array
            && offers.GetArrayLength() > 0
            && offers[0].TryGetProperty("offerId", out var offerIdEl))
        {
            return offerIdEl.GetString();
        }
        return null;
    }

    private async Task SaveActiveListing(int lotId, EbayListingOptions options, string ebayItemId)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        var existing = await ctx.EbayListings.FirstOrDefaultAsync(l => l.LotId == lotId);
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
                LotId = lotId,
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

    private async Task SaveListingError(int lotId, EbayListingOptions options, string error)
    {
        try
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            var existing = await ctx.EbayListings.FirstOrDefaultAsync(l => l.LotId == lotId);
            if (existing is not null)
            {
                existing.Status = EbayListingStatus.Error;
                existing.ErrorMessage = error;
            }
            else
            {
                ctx.EbayListings.Add(new EbayListing
                {
                    LotId = lotId,
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
            _logger.LogWarning(ex, "Failed to save listing error for lot {LotId}", lotId);
        }
    }
}
