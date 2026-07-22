using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class RiftboundDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RiftboundDbContext> _options;

    public RiftboundDbContextTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void EnsureCreated_RoundTripsCard_AndStampsVersion()
    {
        using var ctx = new RiftboundDbContext(_options);
        ctx.Database.EnsureCreated();
        ctx.ApplySchemaUpgrades();

        ctx.Cards.Add(new RiftboundCard
        {
            Id = "69bc5bd2d308c64675ca879d",
            RiftboundId = "ogn-209-298",
            CollectorNumber = 209,
            Name = "Cull the Weak",
            SetId = "OGN",
            SetName = "Origins",
            Rarity = "Common",
            CardType = "Spell",
            Domain = "Order",
            Orientation = "portrait",
        });
        ctx.SaveChanges();

        Assert.Equal(0, ctx.GetSchemaVersion());
        ctx.MarkMigrationComplete();
        Assert.Equal(RiftboundDbContext.RiftboundSchemaVersion, ctx.GetSchemaVersion());

        var row = ctx.Cards.Single();
        Assert.Equal("Cull the Weak", row.Name);
        Assert.Equal(209, row.CollectorNumber);
        Assert.Equal("OGN", row.SetId);
    }

    [Fact]
    public void CardGameEnum_ContainsRiftbound()
    {
        Assert.True(Enum.IsDefined(CardGame.Riftbound));
    }
}
