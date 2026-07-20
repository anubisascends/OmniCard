using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListingServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;

    public ListingServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void Listing_RoundTrips_ThroughModel()
    {
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.Listings.Add(new Listing
            {
                LotId = 5,
                Channel = SalesChannel.TcgPlayer,
                Status = ListingStatus.Listed,
                ListedPrice = 1.25m,
                Quantity = 1,
                OriginalLocationId = 3,
                ListedAt = new DateTime(2026, 1, 1),
            });
            ctx.SaveChanges();
        }

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var listing = Assert.Single(ctx.Listings.ToList());
            Assert.Equal(SalesChannel.TcgPlayer, listing.Channel);
            Assert.Equal(ListingStatus.Listed, listing.Status);
            Assert.Equal(1.25m, listing.ListedPrice);
        }
    }

    private static (int lotId, int locId) SeedLot(DbContextOptions<OmniCardDbContext> opts, int? locationId = 7)
    {
        using var ctx = new OmniCardDbContext(opts);
        var p = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring" };
        ctx.Products.Add(p);
        ctx.SaveChanges();
        // InventoryLot.LocationId is FK-constrained to StorageContainers.Id, so a
        // StorageContainer row must exist first (schema added after this brief was
        // drafted); seed one with the requested id to satisfy the constraint.
        if (locationId is int locId)
        {
            ctx.StorageContainers.Add(new StorageContainer { Id = locId, Name = $"Location {locId}" });
            ctx.SaveChanges();
        }
        var lot = new InventoryLot { ProductId = p.Id, Quantity = 1, LocationId = locationId };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return (lot.Id, locationId ?? 0);
    }

    private ListingService CreateService()
        => new(new MockFactory(_opts), new StubSalesSettings());

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> o)
        : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }

    private sealed class StubSalesSettings : OmniCard.Interfaces.ISalesSettingsService
    {
        public int? ForSaleLocationId { get; private set; } = 99;
        public void SetForSaleLocationId(int? id) => ForSaleLocationId = id;
    }

    [Fact]
    public void ListForSale_CreatesListedListing_WithLocationSnapshot()
    {
        var (lotId, _) = SeedLot(_opts, locationId: 7);
        var count = CreateService().ListForSale([lotId], SalesChannel.TcgPlayer, 1.50m, 1);
        Assert.Equal(1, count);
        using var ctx = new OmniCardDbContext(_opts);
        var listing = Assert.Single(ctx.Listings.ToList());
        Assert.Equal(ListingStatus.Listed, listing.Status);
        Assert.Equal(7, listing.OriginalLocationId);
        Assert.Equal(1.50m, listing.ListedPrice);
    }

    [Fact]
    public void ListForSale_SkipsLotWithActiveListing()
    {
        var (lotId, _) = SeedLot(_opts);
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);
        var second = svc.ListForSale([lotId], SalesChannel.Manual, 2m, 1);
        Assert.Equal(0, second);
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Single(ctx.Listings.ToList());
    }

    [Fact]
    public void Unlist_CancelsActiveListing()
    {
        var (lotId, _) = SeedLot(_opts);
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);
        svc.Unlist([lotId]);
        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal(ListingStatus.Cancelled, Assert.Single(ctx.Listings.ToList()).Status);
    }
}
