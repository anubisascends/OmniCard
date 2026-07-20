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

public class EbaySyncServiceTests : IDisposable
{
    private readonly EbaySettings _settings = new()
    {
        AppId = "test-app-id",
        CertId = "test-cert-id",
        Environment = "sandbox",
    };

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public EbaySyncServiceTests()
    {
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

    private static int SeedLot(OmniCardDbContext ctx, string name = "Test Card")
    {
        var product = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, Name = name,
            SetName = "Set", SetCode = "TST", CollectorNumber = "1", Rarity = "Common",
            GameCardId = "test-1",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lot = new InventoryLot { ProductId = product.Id, Quantity = 1 };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public async Task SyncAllActiveAsync_UpdatesSoldListings()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        int productId;
        using (var ctx = dbFactory.CreateDbContext())
        {
            lotId = SeedLot(ctx);
            productId = ctx.Lots.Single(l => l.Id == lotId).ProductId;
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId, EbayItemId = "ebay-sold-123",
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
        var listing = verifyCtx.EbayListings.First(l => l.LotId == lotId);
        Assert.Equal(EbayListingStatus.Sold, listing.Status);
        Assert.Equal("buyer123", listing.BuyerUsername);
        Assert.Equal(10.00m, listing.SoldPrice);

        // A sale should seed a Sell movement for the lot's product so it shows up in
        // inventory history alongside manual sells/acquisitions.
        var sellMovement = Assert.Single(verifyCtx.Movements.Where(m => m.LotId == lotId && m.Type == MovementType.Sell));
        Assert.Equal(productId, sellMovement.ProductId);
        Assert.Equal(10.00m, sellMovement.UnitValue);
    }

    [Fact]
    public async Task SyncAllActiveAsync_ReturnsZero_WhenNotConnected()
    {
        var dbFactory = CreateDbFactory();
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

    [Fact]
    public async Task SyncSingleAsync_AlreadySold_DoesNotDuplicateSellMovement()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        int listingId;
        using (var ctx = dbFactory.CreateDbContext())
        {
            lotId = SeedLot(ctx, "Already Sold Card");
            var listing = new EbayListing
            {
                LotId = lotId, EbayItemId = "ebay-sold-999",
                Status = EbayListingStatus.Sold, ListedPrice = 5m, SoldPrice = 5m,
            };
            ctx.EbayListings.Add(listing);
            ctx.SaveChanges();
            listingId = listing.Id;

            // Simulate the Sell movement having already been seeded by a prior sync.
            ctx.Movements.Add(new InventoryMovement
            {
                ProductId = ctx.Lots.Single(l => l.Id == lotId).ProductId,
                LotId = lotId,
                Type = MovementType.Sell,
                Quantity = 1,
                UnitValue = 5m,
            });
            ctx.SaveChanges();
        }

        var ordersJson = JsonSerializer.Serialize(new
        {
            orders = new[]
            {
                new
                {
                    orderId = "order-1",
                    buyer = new { username = "buyer999" },
                    lineItems = new[]
                    {
                        new { legacyItemId = "ebay-sold-999", total = new { value = "5.00", currency = "USD" } }
                    },
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

        await svc.SyncSingleAsync(new EbayListing { Id = listingId });

        using var verifyCtx = dbFactory.CreateDbContext();
        Assert.Single(verifyCtx.Movements.Where(m => m.LotId == lotId && m.Type == MovementType.Sell));
    }
}
