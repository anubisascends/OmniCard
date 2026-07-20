using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.eBay;
using System.Net;
using System.Text.Json;

namespace OmniCard.Tests.Services;

public class EbayListingServiceTests : IDisposable
{
    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        Environment = "sandbox",
    };

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public EbayListingServiceTests()
    {
        // Real SQLite (not InMemory) so the EbayListing -> InventoryLot FK/cascade configured in
        // OmniCardDbContext is actually enforced by the database, matching production behavior.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IDbContextFactory<OmniCardDbContext> CreateDbFactory() => new TestDbContextFactory(_options);

    private static int SeedLot(OmniCardDbContext ctx, string name = "Black Lotus")
    {
        var product = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, Name = name,
            SetName = "Alpha", SetCode = "LEA", CollectorNumber = "232", Rarity = "Rare",
            GameCardId = "scryfall-123",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lot = new InventoryLot { ProductId = product.Id, Quantity = 1 };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public async Task CreateListingAsync_SavesEbayListing_WhenApiSucceeds()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
            lotId = SeedLot(ctx);

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

        // The DTO's Id is the LotId (per the unified read facade); CreateListingAsync doesn't
        // require the CollectionCard itself to be persisted anywhere.
        var card = new CollectionCard { Id = lotId, Name = "Black Lotus" };
        var result = await svc.CreateListingAsync(card, options);

        Assert.True(result);

        using var verifyCtx = dbFactory.CreateDbContext();
        var listing = verifyCtx.EbayListings.FirstOrDefault(l => l.LotId == lotId);
        Assert.NotNull(listing);
        Assert.Equal(EbayListingStatus.Active, listing.Status);
        Assert.Equal(5000m, listing.ListedPrice);
    }

    [Fact]
    public async Task EndListingAsync_UpdatesStatusToEnded()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
        {
            lotId = SeedLot(ctx, "Test");
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId, EbayItemId = "ebay-123",
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

        var listing = dbFactory.CreateDbContext().EbayListings.First(l => l.LotId == lotId);
        var result = await svc.EndListingAsync(listing);

        Assert.True(result);

        using var verifyCtx = dbFactory.CreateDbContext();
        var updated = verifyCtx.EbayListings.First(l => l.LotId == lotId);
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
        var dbFactory = CreateDbFactory();

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

    [Fact]
    public void DeletingLot_CascadesEbayListing()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
        {
            lotId = SeedLot(ctx, "Cascade Test");
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId, EbayItemId = "ebay-cascade", Status = EbayListingStatus.Active, ListedPrice = 1m,
            });
            ctx.SaveChanges();
        }

        using (var ctx = dbFactory.CreateDbContext())
        {
            var lot = ctx.Lots.Single(l => l.Id == lotId);
            ctx.Lots.Remove(lot);
            ctx.SaveChanges();
        }

        using var verifyCtx = dbFactory.CreateDbContext();
        Assert.Empty(verifyCtx.EbayListings.Where(l => l.LotId == lotId));
    }
}

public class TestDbContextFactory : IDbContextFactory<OmniCardDbContext>
{
    private readonly DbContextOptions<OmniCardDbContext> _options;
    public TestDbContextFactory(DbContextOptions<OmniCardDbContext> options) => _options = options;
    public OmniCardDbContext CreateDbContext() => new(_options);
}
