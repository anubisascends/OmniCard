using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class ColorCardTypeMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public ColorCardTypeMigrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void CollectionCard_HasColorAndCardTypeColumns()
    {
        using var ctx = new CollectionDbContext(_options);
        var card = new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "test-id",
            Name = "Test Card",
            Color = "W",
            CardType = "Creature"
        };
        ctx.Cards.Add(card);
        ctx.SaveChanges();

        var loaded = ctx.Cards.AsNoTracking().First();
        Assert.Equal("W", loaded.Color);
        Assert.Equal("Creature", loaded.CardType);
    }

    [Fact]
    public void CollectionCard_ColorAndCardTypeAreNullable()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "test-id",
            Name = "No Color"
        });
        ctx.SaveChanges();

        var loaded = ctx.Cards.AsNoTracking().First();
        Assert.Null(loaded.Color);
        Assert.Null(loaded.CardType);
    }

    [Fact]
    public void EnsureColorCardTypeColumns_AddsColumnsWhenMissing()
    {
        // Simulate a pre-migration database by dropping the columns
        using var cmd = _connection.CreateCommand();
        // SQLite doesn't support DROP COLUMN easily, so test the migration method
        // on a fresh connection where we create the table without those columns
        var freshConn = new SqliteConnection("Data Source=:memory:");
        freshConn.Open();

        using var createCmd = freshConn.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE Cards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Game TEXT NOT NULL,
                GameCardId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT ''
            )
            """;
        createCmd.ExecuteNonQuery();

        App.EnsureColorCardTypeColumns(freshConn);

        // Verify columns exist
        using var verifyCmd = freshConn.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'Color'";
        Assert.Equal(1L, (long)verifyCmd.ExecuteScalar()!);

        verifyCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'CardType'";
        Assert.Equal(1L, (long)verifyCmd.ExecuteScalar()!);

        freshConn.Dispose();
    }
}
