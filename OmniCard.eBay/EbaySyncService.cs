using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.eBay;

public class EbaySyncService : IEbaySyncService
{
    private readonly EbaySettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEbayAuthService _ebayAuthService;
    private readonly IDbContextFactory<OmniCardDbContext> _dbContextFactory;
    private readonly ILogger<EbaySyncService> _logger;

    public EbaySyncService(
        IOptions<EbaySettings> settings,
        IHttpClientFactory httpClientFactory,
        IEbayAuthService ebayAuthService,
        IDbContextFactory<OmniCardDbContext> dbContextFactory,
        ILogger<EbaySyncService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _ebayAuthService = ebayAuthService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<int> SyncAllActiveAsync()
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return 0;

        using var ctx = _dbContextFactory.CreateDbContext();
        var activeListings = await ctx.EbayListings
            .Where(l => l.Status == EbayListingStatus.Active)
            .ToListAsync();

        if (activeListings.Count == 0)
            return 0;

        var soldItemIds = await FetchSoldItemIdsAsync(token);
        var syncedCount = 0;

        foreach (var listing in activeListings)
        {
            if (soldItemIds.TryGetValue(listing.EbayItemId, out var saleInfo))
            {
                listing.Status = EbayListingStatus.Sold;
                listing.SoldPrice = saleInfo.SoldPrice;
                listing.BuyerUsername = saleInfo.BuyerUsername;
                listing.EndTime = DateTime.UtcNow;
                listing.LastSyncedAt = DateTime.UtcNow;
                syncedCount++;
                _logger.LogInformation("Listing {ItemId} marked as sold to {Buyer} for {Price}",
                    listing.EbayItemId, saleInfo.BuyerUsername, saleInfo.SoldPrice);

                await SeedSellMovementAsync(ctx, listing.LotId, saleInfo.SoldPrice, listing.EbayItemId);
            }
            else
            {
                listing.LastSyncedAt = DateTime.UtcNow;
            }
        }

        await ctx.SaveChangesAsync();
        _logger.LogInformation("eBay sync complete: {Synced} of {Total} active listings updated", syncedCount, activeListings.Count);
        return syncedCount;
    }

    public async Task SyncSingleAsync(EbayListing listing)
    {
        var token = await _ebayAuthService.GetAccessTokenAsync();
        if (token is null)
            return;

        var soldItemIds = await FetchSoldItemIdsAsync(token);

        using var ctx = _dbContextFactory.CreateDbContext();
        var tracked = await ctx.EbayListings.FindAsync(listing.Id);
        if (tracked is null)
            return;

        // Guard against re-seeding a Sell movement if this listing was already synced as Sold
        // by an earlier call (e.g. SyncAllActiveAsync ran first).
        if (tracked.Status != EbayListingStatus.Sold && soldItemIds.TryGetValue(tracked.EbayItemId, out var saleInfo))
        {
            tracked.Status = EbayListingStatus.Sold;
            tracked.SoldPrice = saleInfo.SoldPrice;
            tracked.BuyerUsername = saleInfo.BuyerUsername;
            tracked.EndTime = DateTime.UtcNow;

            await SeedSellMovementAsync(ctx, tracked.LotId, saleInfo.SoldPrice, tracked.EbayItemId);
        }

        tracked.LastSyncedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Records the sale as a <see cref="MovementType.Sell"/> inventory movement against the lot's
    /// product, so eBay sales show up in the same movement history as manual sells/acquisitions,
    /// then removes the sold lot from holdings so it stops counting toward the collection (a sold
    /// card is no longer held). The eBay item id is stamped on the movement's <c>Note</c> because
    /// removing the lot cascade-deletes the (now Sold) <see cref="EbayListing"/> row, and the
    /// movement is the only durable record of the sale afterward.
    /// Best-effort: if the lot has already been removed (e.g. by an earlier sync, or removed from
    /// the collection before the sale synced), this silently does nothing rather than fail the
    /// whole sync — that also guards against double-seeding a Sell movement on re-sync, since once
    /// the lot is gone there's nothing left to seed against.
    /// </summary>
    private static async Task SeedSellMovementAsync(OmniCardDbContext ctx, int lotId, decimal? soldPrice, string ebayItemId)
    {
        var lot = await ctx.Lots.FindAsync(lotId);
        if (lot is null)
            return;

        ctx.Movements.Add(new InventoryMovement
        {
            ProductId = lot.ProductId,
            LotId = lot.Id,
            Type = MovementType.Sell,
            Quantity = 1,
            UnitValue = soldPrice,
            Note = ebayItemId,
        });

        // Remove the sold lot last: the movement above already captured everything needed for
        // P&L (proceeds via UnitValue, provenance via Note) by scalar value, not FK, so it
        // survives this same SaveChanges even though the lot row is being deleted.
        ctx.Lots.Remove(lot);
    }

    private async Task<Dictionary<string, SaleInfo>> FetchSoldItemIdsAsync(string token)
    {
        var result = new Dictionary<string, SaleInfo>();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync(
                $"{_settings.ApiBaseUrl}/sell/fulfillment/v1/order?limit=50&orderBy=creationdate%20desc");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch eBay orders: {Status}", response.StatusCode);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("orders", out var orders))
                return result;

            foreach (var order in orders.EnumerateArray())
            {
                string? buyerUsername = null;
                if (order.TryGetProperty("buyer", out var buyer)
                    && buyer.TryGetProperty("username", out var username))
                {
                    buyerUsername = username.GetString();
                }

                if (!order.TryGetProperty("lineItems", out var lineItems))
                    continue;

                foreach (var lineItem in lineItems.EnumerateArray())
                {
                    if (lineItem.TryGetProperty("legacyItemId", out var itemIdElem))
                    {
                        var itemId = itemIdElem.GetString();
                        if (itemId is null) continue;

                        decimal? soldPrice = null;
                        if (lineItem.TryGetProperty("total", out var total)
                            && total.TryGetProperty("value", out var val)
                            && decimal.TryParse(val.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
                        {
                            soldPrice = price;
                        }

                        result.TryAdd(itemId, new SaleInfo(soldPrice, buyerUsername));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch sold items from eBay");
        }

        return result;
    }

    private record SaleInfo(decimal? SoldPrice, string? BuyerUsername);
}
