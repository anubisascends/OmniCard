using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
}
