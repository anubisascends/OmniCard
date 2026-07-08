using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.eBay;
using System.Net;
using System.Text.Json;

namespace OmniCard.Tests.Services;

public class EbayListingServiceTests
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
    public async Task CreateListingAsync_SavesEbayListing_WhenApiSucceeds()
    {
        var dbFactory = CreateInMemoryDbFactory();

        // Seed a card
        using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Id = 1, Name = "Black Lotus", SetName = "Alpha", SetCode = "LEA",
                Number = "232", Rarity = "Rare", GameCardId = "scryfall-123",
            });
            ctx.SaveChanges();
        }

        var responseJson = JsonSerializer.Serialize(new { listingId = "ebay-item-12345" });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbayListingService>.Instance);

        var options = new EbayListingOptions
        {
            Title = "MTG Black Lotus [LEA] #232 NM",
            Description = "Near Mint Black Lotus from Alpha",
            Price = 5000m,
            ListingType = EbayListingType.FixedPrice,
        };

        var card = dbFactory.CreateDbContext().Cards.First(c => c.Id == 1);
        var result = await svc.CreateListingAsync(card, options);

        Assert.True(result);

        using var verifyCtx = dbFactory.CreateDbContext();
        var listing = verifyCtx.EbayListings.FirstOrDefault(l => l.CollectionCardId == 1);
        Assert.NotNull(listing);
        Assert.Equal(EbayListingStatus.Active, listing.Status);
        Assert.Equal(5000m, listing.ListedPrice);
    }

    [Fact]
    public async Task EndListingAsync_UpdatesStatusToEnded()
    {
        var dbFactory = CreateInMemoryDbFactory();

        using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.Cards.Add(new CollectionCard
            {
                Id = 1, Name = "Test", SetName = "Set", SetCode = "TST",
                Number = "1", Rarity = "Common", GameCardId = "test-1",
            });
            ctx.EbayListings.Add(new EbayListing
            {
                Id = 1, CollectionCardId = 1, EbayItemId = "ebay-123",
                Status = EbayListingStatus.Active, ListedPrice = 10m,
            });
            ctx.SaveChanges();
        }

        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbayListingService>.Instance);

        var listing = dbFactory.CreateDbContext().EbayListings.First(l => l.Id == 1);
        var result = await svc.EndListingAsync(listing);

        Assert.True(result);

        using var verifyCtx = dbFactory.CreateDbContext();
        var updated = verifyCtx.EbayListings.First(l => l.Id == 1);
        Assert.Equal(EbayListingStatus.Ended, updated.Status);
    }

    [Fact]
    public async Task GetSellerPoliciesAsync_ReturnsPolicies()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            fulfillmentPolicies = new[]
            {
                new { fulfillmentPolicyId = "policy-1", name = "Standard Shipping" }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var factory = new FakeHttpClientFactory(handler);
        var authService = new FakeEbayAuthService("test-token");
        var dbFactory = CreateInMemoryDbFactory();

        var svc = new EbayListingService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbayListingService>.Instance);

        var policies = await svc.GetSellerPoliciesAsync("fulfillment");

        Assert.Single(policies);
        Assert.Equal("policy-1", policies[0].PolicyId);
        Assert.Equal("Standard Shipping", policies[0].Name);
    }
}

public class TestDbContextFactory : IDbContextFactory<CollectionDbContext>
{
    private readonly DbContextOptions<CollectionDbContext> _options;
    public TestDbContextFactory(DbContextOptions<CollectionDbContext> options) => _options = options;
    public CollectionDbContext CreateDbContext() => new(_options);
}
