using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class CollectionDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public CollectionDbContextTests()
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
    public void CanInsertAndQueryCollectionCard_Mtg()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "0000579f-7b35-4ed3-b44c-db2a538066fe",
            Name = "Fury Sliver",
            SetName = "Time Spiral",
            SetCode = "tsp",
            Number = "157",
            Rarity = "uncommon",
            ImageUri = "https://cards.scryfall.io/normal/front/fury.jpg",
            Condition = "NM",
            IsFoil = false,
            PurchasePrice = 0.35m,
        });
        ctx.SaveChanges();

        using var readCtx = new CollectionDbContext(_options);
        var card = readCtx.Cards.AsNoTracking().Single();
        Assert.Equal(CardGame.Mtg, card.Game);
        Assert.Equal("0000579f-7b35-4ed3-b44c-db2a538066fe", card.GameCardId);
        Assert.Equal("Fury Sliver", card.Name);
    }

    [Fact]
    public void CanInsertAndQueryCollectionCard_OnePiece()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Cards.Add(new CollectionCard
        {
            Game = CardGame.OnePiece,
            GameCardId = "OP01-001",
            Name = "Roronoa Zoro",
            SetName = "Romance Dawn",
            SetCode = "OP01",
            Number = "OP01-001",
            Rarity = "SR",
            Condition = "NM",
        });
        ctx.SaveChanges();

        using var readCtx = new CollectionDbContext(_options);
        var card = readCtx.Cards.AsNoTracking().Single();
        Assert.Equal(CardGame.OnePiece, card.Game);
        Assert.Equal("OP01-001", card.GameCardId);
    }

    [Fact]
    public void CanFilterByGame()
    {
        using var ctx = new CollectionDbContext(_options);
        ctx.Cards.Add(new CollectionCard { Game = CardGame.Mtg, GameCardId = "id1", Name = "MTG Card" });
        ctx.Cards.Add(new CollectionCard { Game = CardGame.OnePiece, GameCardId = "id2", Name = "OP Card" });
        ctx.SaveChanges();

        using var readCtx = new CollectionDbContext(_options);
        var mtgCards = readCtx.Cards.AsNoTracking().Where(c => c.Game == CardGame.Mtg).ToList();
        Assert.Single(mtgCards);
        Assert.Equal("MTG Card", mtgCards[0].Name);
    }
}
