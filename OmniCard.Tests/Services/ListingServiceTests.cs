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

    [Fact]
    public void Unlist_PickedListing_RestoresLocationAndRecordsMoveMovement()
    {
        int originalLocationId = 7;
        int forSaleLocationId = 99; // Also the default from StubSalesSettings
        int lotId;

        // Seed the original location, for-sale location, product, and lot
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = "Sol Ring" };
            ctx.Products.Add(product);
            ctx.SaveChanges();

            ctx.StorageContainers.Add(new StorageContainer { Id = originalLocationId, Name = "Original Location" });
            ctx.StorageContainers.Add(new StorageContainer { Id = forSaleLocationId, Name = "For Sale Location" });
            ctx.SaveChanges();

            // Lot is currently at the for-sale location (because it's picked)
            var lot = new InventoryLot { ProductId = product.Id, Quantity = 1, LocationId = forSaleLocationId };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;

            // Create a listing with status Picked, with OriginalLocationId set to the original container
            var listing = new Listing
            {
                LotId = lot.Id,
                Channel = SalesChannel.Manual,
                Status = ListingStatus.Picked,
                ListedPrice = 1.50m,
                Quantity = 1,
                OriginalLocationId = originalLocationId,
                ListedAt = new DateTime(2026, 1, 1),
            };
            ctx.Listings.Add(listing);
            ctx.SaveChanges();
        }

        // Call Unlist
        CreateService().Unlist([lotId]);

        // Verify
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var listing = Assert.Single(ctx.Listings.ToList());
            Assert.Equal(ListingStatus.Cancelled, listing.Status);

            var lot = Assert.Single(ctx.Lots.ToList());
            Assert.Equal(originalLocationId, lot.LocationId);

            var movement = Assert.Single(ctx.Movements.Where(m => m.LotId == lotId).ToList());
            Assert.Equal(MovementType.Move, movement.Type);
        }
    }

    [Fact]
    public void MarkPicked_MovesLotToForSaleLocation_AndRecordsMovement()
    {
        var (lotId, _) = SeedLot(_opts, locationId: 7);
        // For-Sale location (Id 99, the StubSalesSettings default) must exist as a
        // StorageContainer row, since InventoryLot.LocationId is FK-constrained.
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.StorageContainers.Add(new StorageContainer { Id = 99, Name = "For Sale Location" });
            ctx.SaveChanges();
        }
        var svc = CreateService(); // StubSalesSettings.ForSaleLocationId = 99
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);

        var count = svc.MarkPicked([lotId]);

        Assert.Equal(1, count);
        using var ctx2 = new OmniCardDbContext(_opts);
        var listing = Assert.Single(ctx2.Listings.ToList());
        Assert.Equal(ListingStatus.Picked, listing.Status);
        Assert.NotNull(listing.PickedAt);
        Assert.Equal(99, ctx2.Lots.Single(l => l.Id == lotId).LocationId);
        Assert.Contains(ctx2.Movements.ToList(), m => m.Type == MovementType.Move && m.LotId == lotId);
    }

    [Fact]
    public void MarkPicked_Throws_WhenNoForSaleLocationConfigured()
    {
        var (lotId, _) = SeedLot(_opts);
        var settings = new StubSalesSettings();
        settings.SetForSaleLocationId(null);
        var svc = new ListingService(new MockFactory(_opts), settings);
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);
        Assert.Throws<InvalidOperationException>(() => svc.MarkPicked([lotId]));
    }

    [Fact]
    public void GetPickList_ReturnsListedNotPicked_GroupedByLocation()
    {
        var (lotId, _) = SeedLot(_opts, locationId: null);
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 3.25m, 1);

        var pick = svc.GetPickList(CardGame.Mtg);

        var entry = Assert.Single(pick);
        Assert.Equal(lotId, entry.LotId);
        Assert.Equal("Sol Ring", entry.Name);
        Assert.Equal(3.25m, entry.ListedPrice);
    }

    [Fact]
    public void GetPickList_ExcludesPicked()
    {
        var (lotId, _) = SeedLot(_opts, locationId: 7);
        // MarkPicked moves the lot to the "For Sale" location (Id 99, the
        // StubSalesSettings default), which is FK-constrained to StorageContainers.
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.StorageContainers.Add(new StorageContainer { Id = 99, Name = "For Sale Location" });
            ctx.SaveChanges();
        }
        var svc = CreateService();
        svc.ListForSale([lotId], SalesChannel.Manual, 1m, 1);
        svc.MarkPicked([lotId]);
        Assert.Empty(svc.GetPickList());
    }

    [Fact]
    public void GetActiveListingStatusByLot_ReportsListedAndPicked()
    {
        var a = SeedLot(_opts, locationId: 7).lotId;
        var b = SeedLot(_opts, locationId: 8).lotId;
        // MarkPicked moves the lot to the "For Sale" location (Id 99, the
        // StubSalesSettings default), which is FK-constrained to StorageContainers.
        using (var ctx = new OmniCardDbContext(_opts))
        {
            ctx.StorageContainers.Add(new StorageContainer { Id = 99, Name = "For Sale Location" });
            ctx.SaveChanges();
        }
        var svc = CreateService();
        svc.ListForSale([a, b], SalesChannel.Manual, 1m, 1);
        svc.MarkPicked([b]);

        var map = svc.GetActiveListingStatusByLot([a, b]);
        Assert.Equal(ListingStatus.Listed, map[a]);
        Assert.Equal(ListingStatus.Picked, map[b]);
    }
}
