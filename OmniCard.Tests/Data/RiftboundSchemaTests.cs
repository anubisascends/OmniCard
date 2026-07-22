using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class RiftboundSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RiftboundSchemaTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private RiftboundDbContext NewContext() =>
        new(new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public void FreshDatabase_HasPriceColumns_AndRoundTrips()
    {
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new RiftboundCard
        {
            Id = "c1",
            Name = "Cull the Weak",
            SetId = "OGN",
            TcgplayerId = "653002",
            MarketPrice = 1.23m,
            FoilMarketPrice = 4.56m,
            PriceUpdatedAt = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
        });
        ctx.SaveChanges();

        var loaded = ctx.Cards.Single(c => c.Id == "c1");
        Assert.Equal(1.23m, loaded.MarketPrice);
        Assert.Equal(4.56m, loaded.FoilMarketPrice);
        Assert.Equal(new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc), loaded.PriceUpdatedAt);
    }

    [Fact]
    public void ApplySchemaUpgrades_AddsPriceColumns_ToLegacyTable()
    {
        // Simulate an old Cards table that lacks the price columns.
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE Cards (
                Id TEXT PRIMARY KEY, RiftboundId TEXT, TcgplayerId TEXT, CollectorNumber INTEGER,
                Name TEXT, CleanName TEXT, SetId TEXT, SetName TEXT, Rarity TEXT, CardType TEXT,
                Supertype TEXT, Domain TEXT, Energy INTEGER, Might INTEGER, Power INTEGER,
                CardText TEXT, Flavour TEXT, Artist TEXT, Orientation TEXT, AlternateArt INTEGER,
                Overnumbered INTEGER, Signature INTEGER, CardImageUri TEXT, DateScraped TEXT,
                ImageHash INTEGER, EdgeHash INTEGER, LocalImagePath TEXT);";
            cmd.ExecuteNonQuery();
        }

        using var ctx = NewContext();
        ctx.ApplySchemaUpgrades();

        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name IN ('MarketPrice','FoilMarketPrice','PriceUpdatedAt');";
        Assert.Equal(3L, (long)check.ExecuteScalar()!);
    }
}
