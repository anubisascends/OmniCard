using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class FlagReasonMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public FlagReasonMigrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void FlagReason_ColumnCreatedByEnsureCreated()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "1",
            Name = "Test",
            FlagReason = FlagReason.MissingFromDatabase,
        });
        ctx.SaveChanges();

        var card = ctx.Cards.First();
        Assert.Equal(FlagReason.MissingFromDatabase, card.FlagReason);
    }

    [Fact]
    public void FlagReason_NullByDefault()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();

        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "2",
            Name = "Normal Card",
        });
        ctx.SaveChanges();

        var card = ctx.Cards.First();
        Assert.Null(card.FlagReason);
    }
}
