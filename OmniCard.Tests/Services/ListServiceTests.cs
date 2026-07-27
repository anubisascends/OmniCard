using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;

    public ListServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection).Options;
        _dbFactory = new TestOmniDbFactory(options);
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestOmniDbFactory(DbContextOptions<OmniCardDbContext> options)
        : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public void CardList_And_Items_RoundTrip()
    {
        using (var ctx = _dbFactory.CreateDbContext())
        {
            var list = new CardList { Name = "Budget Deck", Game = CardGame.Mtg };
            ctx.CardLists.Add(list);
            ctx.SaveChanges();
            ctx.CardListItems.Add(new CardListItem
            {
                CardListId = list.Id, Quantity = 2, GameCardId = "abc",
                CardName = "Sol Ring", SetCode = "C21", AddedMarketPrice = 1.23m,
                Source = ListItemSource.Paste,
            });
            ctx.SaveChanges();
        }

        using (var ctx = _dbFactory.CreateDbContext())
        {
            var list = Assert.Single(ctx.CardLists.AsNoTracking().ToList());
            Assert.Equal("Budget Deck", list.Name);
            Assert.Equal(CardGame.Mtg, list.Game);
            var item = Assert.Single(ctx.CardListItems.AsNoTracking().ToList());
            Assert.Equal(2, item.Quantity);
            Assert.Equal(1.23m, item.AddedMarketPrice);
            Assert.Equal(ListItemSource.Paste, item.Source);
        }
    }
}
