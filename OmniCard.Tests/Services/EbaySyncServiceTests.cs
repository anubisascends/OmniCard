using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.eBay;
using System.Net;
using System.Text.Json;

namespace OmniCard.Tests.Services;

public class EbaySyncServiceTests
{
    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        Environment = "sandbox",
    };

    private IDbContextFactory<CollectionDbContext> CreateInMemoryDbFactory()
    {
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(options);
    }

    [Fact]
    public async Task SyncAllActiveAsync_UpdatesSoldListings()
    {
        var dbFactory = CreateInMemoryDbFactory();

        using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Id = 1, Name = "Test Card", SetName = "Set", SetCode = "TST",
                Number = "1", Rarity = "Common", GameCardId = "test-1",
            });
            ctx.EbayListings.Add(new EbayListing
            {
                Id = 1, CollectionCardId = 1, EbayItemId = "ebay-sold-123",
                Status = EbayListingStatus.Active, ListedPrice = 10m,
            });
            ctx.SaveChanges();
        }

        // Simulate order found for this item
        var ordersJson = JsonSerializer.Serialize(new
        {
            orders = new[]
            {
                new
                {
                    orderId = "order-1",
                    buyer = new { username = "buyer123" },
                    lineItems = new[]
                    {
                        new { legacyItemId = "ebay-sold-123", total = new { value = "10.00", currency = "USD" } }
                    },
                    orderFulfillmentStatus = "FULFILLED",
                }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, ordersJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbaySyncService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbaySyncService>.Instance);

        var synced = await svc.SyncAllActiveAsync();

        Assert.Equal(1, synced);

        using var verifyCtx = dbFactory.CreateDbContext();
        var listing = verifyCtx.EbayListings.First(l => l.Id == 1);
        Assert.Equal(EbayListingStatus.Sold, listing.Status);
        Assert.Equal("buyer123", listing.BuyerUsername);
        Assert.Equal(10.00m, listing.SoldPrice);
    }

    [Fact]
    public async Task SyncAllActiveAsync_ReturnsZero_WhenNotConnected()
    {
        var dbFactory = CreateInMemoryDbFactory();
        var authService = new FakeEbayAuthService(null);
        var factory = new FakeHttpClientFactory(new FakeHttpHandler(HttpStatusCode.OK, "{}"));

        var svc = new EbaySyncService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbaySyncService>.Instance);

        var synced = await svc.SyncAllActiveAsync();
        Assert.Equal(0, synced);
    }
}
