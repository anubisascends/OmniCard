using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
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

    private static int SeedLot(OmniCardDbContext ctx, string name = "Test Card", int quantity = 1)
    {
        var product = new Product
        {
            Game = CardGame.Mtg, Category = ProductCategory.Single, Name = name,
            SetName = "Set", SetCode = "TST", CollectorNumber = "1", Rarity = "Common",
            GameCardId = "test-1",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        var lot = new InventoryLot { ProductId = product.Id, Quantity = quantity };
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
            // Acquire movement so realized P&L (cost basis) has something to net against.
            ctx.Movements.Add(new InventoryMovement
            {
                ProductId = productId, LotId = lotId, Type = MovementType.Acquire,
                Quantity = 1, UnitValue = 4m,
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

        // The sold lot is removed from holdings so it stops double-counting alongside the
        // realized Sell movement, and the (now Sold) listing cascade-deletes with the lot.
        Assert.Null(verifyCtx.Lots.Find(lotId));
        Assert.Empty(verifyCtx.EbayListings.Where(l => l.LotId == lotId));

        // A sale should seed a Sell movement for the lot's product so it shows up in
        // inventory history alongside manual sells/acquisitions. The eBay item id is stamped
        // on the Note since it's the only place that provenance survives after the listing
        // row is gone.
        var sellMovement = Assert.Single(verifyCtx.Movements.Where(m => m.LotId == lotId && m.Type == MovementType.Sell));
        Assert.Equal(productId, sellMovement.ProductId);
        Assert.Equal(10.00m, sellMovement.UnitValue);
        Assert.Equal("ebay-sold-123", sellMovement.Note);

        // Realized P&L still computes correctly off the surviving movements even though the
        // lot row itself is gone.
        var analytics = new AnalyticsService(dbFactory, Array.Empty<ICardGameService>());
        var realized = analytics.GetRealized();
        Assert.Equal(1, realized.TotalSold);
        Assert.Equal(10.00m, realized.TotalProceeds);
        Assert.Equal(4.00m, realized.TotalCost);
    }

    [Fact]
    public async Task SyncAllActiveAsync_QtyTwoLot_DecrementsInsteadOfRemoving()
    {
        // A lot with quantity 2 should survive a single eBay sale with its quantity reduced to 1,
        // rather than being deleted outright — only the sold unit is decremented off the lot.
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
        {
            lotId = SeedLot(ctx, "Multi-Qty Card", quantity: 2);
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId, EbayItemId = "ebay-sold-qty2",
                Status = EbayListingStatus.Active, ListedPrice = 15m,
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
                    buyer = new { username = "buyerqty2" },
                    lineItems = new[]
                    {
                        new { legacyItemId = "ebay-sold-qty2", total = new { value = "15.00", currency = "USD" } }
                    },
                }
            }
        });

        var factory = new FakeHttpClientFactory(new FakeHttpHandler(HttpStatusCode.OK, ordersJson));
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

        // The lot survives with its remaining quantity — it is not removed just because one
        // unit sold on eBay.
        var lot = verifyCtx.Lots.Find(lotId);
        Assert.NotNull(lot);
        Assert.Equal(1, lot!.Quantity);

        // Exactly one Sell movement (qty 1) was seeded for the sale.
        var sellMovement = Assert.Single(verifyCtx.Movements.Where(m => m.LotId == lotId && m.Type == MovementType.Sell));
        Assert.Equal(1, sellMovement.Quantity);
        Assert.Equal(15.00m, sellMovement.UnitValue);
        Assert.Equal("ebay-sold-qty2", sellMovement.Note);
    }

    [Fact]
    public async Task SyncAllActiveAsync_SecondSync_DoesNotReprocessRemovedLot()
    {
        var dbFactory = CreateDbFactory();
        int lotId;
        using (var ctx = dbFactory.CreateDbContext())
        {
            lotId = SeedLot(ctx, "Double Sync Card");
            ctx.EbayListings.Add(new EbayListing
            {
                LotId = lotId, EbayItemId = "ebay-sold-777",
                Status = EbayListingStatus.Active, ListedPrice = 20m,
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
                    buyer = new { username = "buyer777" },
                    lineItems = new[]
                    {
                        new { legacyItemId = "ebay-sold-777", total = new { value = "20.00", currency = "USD" } }
                    },
                }
            }
        });

        var factory = new FakeHttpClientFactory(new FakeHttpHandler(HttpStatusCode.OK, ordersJson));
        var authService = new FakeEbayAuthService("test-token");

        var svc = new EbaySyncService(
            Options.Create(_settings),
            factory,
            authService,
            dbFactory,
            NullLogger<EbaySyncService>.Instance);

        var firstSync = await svc.SyncAllActiveAsync();
        Assert.Equal(1, firstSync);

        // Second sync should find no active listings left (the listing cascade-deleted with the
        // lot), so it must not throw and must not seed a second Sell movement.
        var secondSync = await svc.SyncAllActiveAsync();
        Assert.Equal(0, secondSync);

        using var verifyCtx = dbFactory.CreateDbContext();
        Assert.Single(verifyCtx.Movements.Where(m => m.LotId == lotId && m.Type == MovementType.Sell));
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
