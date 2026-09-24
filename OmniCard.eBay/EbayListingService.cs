using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;

namespace OmniCard.eBay;

public class EbayListingService : IEbayListingService
{
    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _ebayAuthService;
    private readonly IDbContextFactory<OmniCardDbContext> _dbContextFactory;
    private readonly IEbaySellingSettingsService _sellingSettings;
    private readonly IListingService _listingService;
    private readonly ILogger<EbayListingService> _logger;

    // eBay's leaf category for trading-card singles ("Collectible Card Games ▸ CCG Individual Cards").
    // It must be a LEAF (you can't list in a parent node) and must match the "Card Condition" descriptor
    // (name 40001) we send on the inventory item — otherwise publish fails with a BadRequest. The
    // per-listing category we accept from callers is only used when non-empty; otherwise we fall back
    // to this known-good value rather than a parent node.
    public const string SinglesCategoryId = "183454";

    // Trading-card singles (category 183454) accept only USED_VERY_GOOD (ungraded) or
    // LIKE_NEW (graded). Ungraded cards additionally carry a "Card Condition" descriptor
    // (name 40001) whose value encodes the grade (400010 = Near Mint or Better … 400013 = Poor).
    private static readonly Dictionary<string, string> CardConditionDescriptorMap = new()
    {
        ["NM"] = "400010", // Near Mint or Better
        ["LP"] = "400011", // Excellent
        ["MP"] = "400012", // Very Good
        ["HP"] = "400013", // Poor
        ["D"] = "400013",  // Poor (no distinct ungraded "damaged" grade)
        ["DMG"] = "400013",// Poor (the app's "Damaged" condition code)
    };

    public EbayListingService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService ebayAuthService,
        IDbContextFactory<OmniCardDbContext> dbContextFactory,
        IEbaySellingSettingsService sellingSettings,
        IListingService listingService,
        ILogger<EbayListingService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _ebayAuthService = ebayAuthService;
        _dbContextFactory = dbContextFactory;
        _sellingSettings = sellingSettings;
        _listingService = listingService;
        _logger = logger;
    }

    public Task<bool> CreateListingAsync(CollectionCard card, EbayListingOptions options)
        => CreateListingCoreAsync(card.Id, card.Name, BuildInventoryItem(card, options), options);

    public Task<bool> CreateSealedListingAsync(Product product, int lotId, EbayListingOptions options)
        => CreateListingCoreAsync(lotId, product.Name, BuildSealedInventoryItem(product, options), options);

    // Shared listing pipeline for both singles (CreateListingAsync) and sealed products
    // (CreateSealedListingAsync). The only thing that differs between them is how the eBay
    // inventory item is shaped (condition / aspects / descriptors), which the caller builds and
    // passes in as <paramref name="inventoryItem"/>. Everything after that — SKU, offer
    // create/update, publish, and bridging into the generic listing system — is identical.
    private async Task<bool> CreateListingCoreAsync(int lotId, string displayName, object inventoryItem, EbayListingOptions options)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        var selling = _sellingSettings.Get();
        if (!_sellingSettings.IsSetupComplete())
        {
            _logger.LogWarning("eBay listing blocked — seller setup incomplete for lot {LotId}", lotId);
            await SaveListingError(lotId, options, "eBay setup incomplete — run Settings ▸ eBay Selling ▸ Run eBay Setup.");
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Step 1: Create inventory item
            var sku = $"omnicard-{lotId}";
            var inventoryJson = JsonSerializer.Serialize(inventoryItem);

            var inventoryResponse = await client.PutAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                JsonContent(inventoryJson));

            if (!inventoryResponse.IsSuccessStatusCode)
            {
                var error = await inventoryResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to create inventory item: {Status} — {Error}", inventoryResponse.StatusCode, error);
                await SaveListingError(lotId, options, $"Inventory creation failed: {DescribeEbayError(error, inventoryResponse.StatusCode)}");
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
                    await SaveListingError(lotId, options, $"Offer creation failed: {DescribeEbayError(error, offerResponse.StatusCode)}");
                    return false;
                }

                var offerResponseJson = await offerResponse.Content.ReadAsStringAsync();
                using var offerDoc = JsonDocument.Parse(offerResponseJson);

                // If the offer response already contains listingId, treat as published
                if (offerDoc.RootElement.TryGetProperty("listingId", out var directListingId))
                {
                    var ebayItemIdDirect = directListingId.GetString() ?? "";
                    await SaveActiveListing(lotId, options, ebayItemIdDirect);
                    _logger.LogInformation("Created eBay listing {ItemId} for lot {LotId} ({Name})",
                        ebayItemIdDirect, lotId, displayName);
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
                    await SaveListingError(lotId, options, $"Offer update failed: {DescribeEbayError(error, updateResponse.StatusCode)}");
                    return false;
                }
            }

            // Step 3: Publish offer.
            var (published, ebayItemId, publishError) = await TryPublishAsync(client, offerId);

            // Relisting an item whose eBay listing has ended: a stale offer still exists for the SKU
            // (so FindExistingOfferAsync returned it above), but its prior listing has ended and eBay
            // won't republish it directly — the publish fails and the item never reaches eBay. Recover
            // by deleting the eBay inventory item (which clears the leftover offer), recreating it, and
            // creating + publishing a fresh offer. This only runs on a publish failure of an existing
            // offer, so it never disturbs a normal first-time list or a successful revise/republish.
            if (!published && existingOfferId is not null)
            {
                _logger.LogInformation(
                    "Publish of existing offer {OfferId} for lot {LotId} failed ({Error}); recreating for relist",
                    offerId, lotId, publishError);

                await client.DeleteAsync($"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}");

                var reInventory = await client.PutAsync(
                    $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                    JsonContent(inventoryJson));
                if (reInventory.IsSuccessStatusCode)
                {
                    var freshOfferResponse = await client.PostAsync(
                        $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer",
                        JsonContent(JsonSerializer.Serialize(BuildOffer(sku, options, selling))));
                    if (freshOfferResponse.IsSuccessStatusCode)
                    {
                        var freshJson = await freshOfferResponse.Content.ReadAsStringAsync();
                        using var freshDoc = JsonDocument.Parse(freshJson);
                        if (freshDoc.RootElement.TryGetProperty("listingId", out var freshListingId))
                            (published, ebayItemId, publishError) = (true, freshListingId.GetString() ?? "", null);
                        else if (freshDoc.RootElement.TryGetProperty("offerId", out var freshOfferId))
                            (published, ebayItemId, publishError) = await TryPublishAsync(client, freshOfferId.GetString() ?? "");
                    }
                    else
                    {
                        publishError = $"Relist offer creation failed: {freshOfferResponse.StatusCode}";
                    }
                }
                else
                {
                    publishError = $"Relist inventory creation failed: {reInventory.StatusCode}";
                }
            }

            if (!published)
            {
                await SaveListingError(lotId, options, publishError ?? "Publish failed");
                return false;
            }

            await SaveActiveListing(lotId, options, ebayItemId);
            _logger.LogInformation("Created eBay listing {ItemId} for lot {LotId} ({Name})",
                ebayItemId, lotId, displayName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create eBay listing for lot {LotId}", lotId);
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

    public async Task<bool> UpdateQuantityAsync(CollectionCard card, EbayListingOptions options)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return false;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // A published listing's quantity follows its inventory item, so re-PUT just the inventory
            // item with the new quantity. We deliberately do NOT update/re-publish the offer here —
            // doing so right after a quantity change fails with errorId 25604 "Availability not found".
            var sku = $"omnicard-{card.Id}";
            var inventoryJson = JsonSerializer.Serialize(BuildInventoryItem(card, options));

            var response = await client.PutAsync(
                $"{_settings.ApiBaseUrl}/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(sku)}",
                JsonContent(inventoryJson));

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to update listing quantity for lot {LotId}: {Status} — {Error}", card.Id, response.StatusCode, error);
                await SaveListingError(card.Id, options, $"Quantity update failed: {DescribeEbayError(error, response.StatusCode)}");
                return false;
            }

            using var ctx = _dbContextFactory.CreateDbContext();
            var tracked = await ctx.EbayListings.FirstOrDefaultAsync(l => l.LotId == card.Id);
            if (tracked is not null)
            {
                tracked.Status = EbayListingStatus.Active;
                tracked.ErrorMessage = null;
                tracked.LastSyncedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }

            _logger.LogInformation("Updated eBay listing quantity to {Quantity} for lot {LotId}", options.Quantity, card.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update eBay listing quantity for lot {LotId}", card.Id);
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

            // Keep the general listing/pick-list system in sync — remove it from the pick list.
            _listingService.Unlist([listing.LotId]);

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

    // Pulls a human-readable reason out of an eBay error response body (the { "errors": [...] } shape
    // eBay returns on 4xx), so failures surface the actual cause (e.g. "Immediate Pay is required")
    // instead of a bare "BadRequest". Falls back to the HTTP status when the body isn't parseable.
    private static string DescribeEbayError(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var parts = new List<string>();
                foreach (var e in errors.EnumerateArray())
                {
                    var msg = e.TryGetProperty("longMessage", out var lm) ? lm.GetString()
                        : e.TryGetProperty("message", out var m) ? m.GetString()
                        : null;
                    var id = e.TryGetProperty("errorId", out var idEl) ? idEl.GetRawText() : null;
                    if (!string.IsNullOrWhiteSpace(msg))
                        parts.Add(id is null ? msg! : $"{msg} (errorId {id})");
                }
                if (parts.Count > 0)
                    return string.Join("; ", parts);
            }
        }
        catch (JsonException)
        {
            // Not the expected JSON shape — fall through to the status code.
        }
        return status.ToString();
    }

    // eBay's Inventory API requires a Content-Language header on inventory_item and
    // offer requests; omitting it fails createOffer with errorId 25709.
    private static StringContent JsonContent(string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("Content-Language", "en-US");
        return content;
    }

    // eBay's "Game" item specific for CCG singles (183454) is a constrained aspect; these are
    // the values eBay accepts (which differ from the app's in-app display names for some games).
    private static string EbayGameAspect(CardGame game) => game switch
    {
        CardGame.Mtg => "Magic: The Gathering",
        CardGame.Pokemon => "Pokémon TCG",
        CardGame.YuGiOh => "Yu-Gi-Oh! TCG",
        CardGame.OnePiece => "One Piece Card Game",
        CardGame.FinalFantasy => "Final Fantasy Trading Card Game",
        CardGame.Riftbound => "Riftbound",
        _ => "Magic: The Gathering",
    };

    private static object BuildInventoryItem(CollectionCard? card, EbayListingOptions options)
    {
        var descriptorValue = CardConditionDescriptorMap.GetValueOrDefault(options.Condition, "400010");

        // Category 183454 requires item specifics (aspects). "Game" is mandatory; Card Name and
        // Language round out the commonly-required set. Values come from the card being listed.
        Dictionary<string, string[]>? aspects = card is null ? null : new()
        {
            ["Game"] = [EbayGameAspect(card.Game)],
            ["Card Name"] = [string.IsNullOrWhiteSpace(card.Name) ? options.Title : card.Name],
            ["Language"] = ["English"],
        };

        // eBay fetches listing images from public URLs. The card's catalog image (Scryfall etc.)
        // is already a public HTTPS URL. Scan images are local files and would need the
        // WebCompanion served on a public host, so they are not included here yet.
        string[]? imageUrls =
            options.IncludeStockImage
            && card?.ImageUri is { Length: > 0 } stockUrl
            && stockUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? [stockUrl]
                : null;

        return new
        {
            availability = new
            {
                shipToLocationAvailability = new { quantity = Math.Max(1, options.Quantity) }
            },
            // Ungraded card. The granular grade (NM/LP/…) is carried by the Card Condition
            // descriptor below, which category 183454 requires.
            condition = "USED_VERY_GOOD",
            conditionDescriptors = new[]
            {
                new { name = "40001", values = new[] { descriptorValue } }
            },
            product = new
            {
                title = options.Title,
                description = options.Description,
                aspects,
                imageUrls,
            },
        };
    }

    // Sealed products (booster boxes/packs/cases/decks/bundles) are brand-new factory-sealed
    // goods, NOT graded trading-card singles — so they carry condition NEW with no "Card Condition"
    // grade descriptor (which is specific to the CCG-singles category 183454). The eBay leaf
    // category and any category-specific required aspects come from the catalog match the dialog
    // picks (EbayCategoryId), the same way singles get theirs.
    private static object BuildSealedInventoryItem(Product product, EbayListingOptions options)
    {
        var aspects = new Dictionary<string, string[]>
        {
            ["Game"] = [EbayGameAspect(product.Game)],
            ["Type"] = [product.Category.ToString()],
        };

        string[]? imageUrls =
            options.IncludeStockImage
            && product.ImageUri is { Length: > 0 } stockUrl
            && stockUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? [stockUrl]
                : null;

        return new
        {
            availability = new
            {
                shipToLocationAvailability = new { quantity = Math.Max(1, options.Quantity) }
            },
            condition = "NEW",
            product = new
            {
                title = options.Title,
                description = options.Description,
                aspects,
                imageUrls,
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
            categoryId = string.IsNullOrWhiteSpace(options.EbayCategoryId) ? SinglesCategoryId : options.EbayCategoryId,
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
            categoryId = string.IsNullOrWhiteSpace(options.EbayCategoryId) ? SinglesCategoryId : options.EbayCategoryId,
        };
    }

    // Publishes an offer. Returns (ok, ebayItemId, error). A failure is logged; the caller decides
    // whether to recover (e.g. the relist path recreates the offer and tries again).
    // Number of publish attempts before giving up on a transient eBay error.
    private const int PublishAttempts = 3;

    private async Task<(bool ok, string ebayItemId, string? error)> TryPublishAsync(HttpClient client, string offerId)
    {
        (bool ok, string ebayItemId, string? error) result = default;
        for (var attempt = 1; attempt <= PublishAttempts; attempt++)
        {
            result = await PublishOnceAsync(client, offerId);
            // Success, or a real (non-transient) error → return immediately.
            if (result.ok || !IsTransientEbayError(result.error))
                return result;

            // eBay reported a transient/system error and asked us to retry — wait briefly and try again.
            _logger.LogInformation("Transient eBay publish error for offer {OfferId} (attempt {Attempt}/{Max}): {Error}",
                offerId, attempt, PublishAttempts, result.error);
            if (attempt < PublishAttempts)
                await DelayBetweenPublishAttemptsAsync(attempt);
        }
        return result;
    }

    private async Task<(bool ok, string ebayItemId, string? error)> PublishOnceAsync(HttpClient client, string offerId)
    {
        var response = await client.PostAsync(
            $"{_settings.ApiBaseUrl}/sell/inventory/v1/offer/{Uri.EscapeDataString(offerId)}/publish",
            JsonContent("{}"));

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.TryGetProperty("listingId", out var listingIdEl) ? listingIdEl.GetString() ?? "" : "";
            return (true, id, null);
        }

        var error = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to publish offer {OfferId}: {Status} — {Error}", offerId, response.StatusCode, error);
        return (false, "", $"Publish failed: {DescribeEbayError(error, response.StatusCode)}");
    }

    // eBay intermittently returns a transient "System error … please try again later" (errorId 25002) or
    // "Availability not found … please try again" (25604) during publish. These are not real validation
    // failures — eBay explicitly asks us to retry — so treat them as retryable.
    private static bool IsTransientEbayError(string? error) =>
        error is not null
        && (error.Contains("try again", StringComparison.OrdinalIgnoreCase)
            || error.Contains("System error", StringComparison.OrdinalIgnoreCase));

    /// <summary>Delay between transient-publish retries. Virtual so tests can override it to run instantly.</summary>
    protected virtual Task DelayBetweenPublishAttemptsAsync(int attempt) =>
        Task.Delay(TimeSpan.FromSeconds(attempt)); // 1s, then 2s

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

        // Bridge into the general listing/pick-list system so the card shows the "LISTED"
        // badge and appears on the pick list. ListForSale is idempotent (skips lots already
        // actively listed), so re-listing the same card won't create duplicate rows.
        _listingService.ListForSale([lotId], SalesChannel.Ebay, options.Price, quantity: 1);
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
