using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class OptcgSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public OptcgSchemaTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private OptcgDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OptcgDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public void FreshDatabase_HasVariantColumns()
    {
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new OptcgCard
        {
            CardSetId = "OP01-001_p1",
            CardNumber = "OP01-001",
            VariantIndex = 1,
            VariantLabel = "Alternate Art",
            Artist = "Some Artist",
            CardName = "Zoro",
            SetId = "OP01",
        });
        ctx.SaveChanges();

        var loaded = ctx.Cards.Single(c => c.CardSetId == "OP01-001_p1");
        Assert.Equal("OP01-001", loaded.CardNumber);
        Assert.Equal(1, loaded.VariantIndex);
        Assert.Equal("Alternate Art", loaded.VariantLabel);
        Assert.Equal("Some Artist", loaded.Artist);
    }

    [Fact]
    public void ApplySchemaUpgrades_AddsColumns_ToLegacyTable()
    {
        // Simulate an old table that lacks the new columns.
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE Cards (
                CardSetId TEXT PRIMARY KEY, CardName TEXT, SetId TEXT, SetName TEXT,
                Rarity TEXT, CardColor TEXT, CardType TEXT, CardCost TEXT, CardPower TEXT,
                Life TEXT, CardText TEXT, SubTypes TEXT, Attribute TEXT, CounterAmount INTEGER,
                InventoryPrice TEXT, MarketPrice TEXT, CardImageId TEXT, CardImageUri TEXT,
                DateScraped TEXT, ImageHash INTEGER, LocalImagePath TEXT);";
            cmd.ExecuteNonQuery();
        }

        using var ctx = NewContext();
        ctx.ApplySchemaUpgrades(); // must not throw and must add CardNumber/VariantIndex/VariantLabel/Artist

        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name IN ('CardNumber','VariantIndex','VariantLabel','Artist');";
        Assert.Equal(4L, (long)check.ExecuteScalar()!);
    }
}
