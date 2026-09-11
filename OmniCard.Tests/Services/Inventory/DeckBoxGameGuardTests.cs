using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Storage;

namespace OmniCard.Tests.Services.Inventory;

public class DeckBoxGameGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public DeckBoxGameGuardTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private int NewContainer(ContainerType type, CardGame? game)
    {
        using var ctx = new OmniCardDbContext(_options);
        var c = new StorageContainer { Name = $"C{Guid.NewGuid():N}", ContainerType = type, Game = game };
        ctx.StorageContainers.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    [Fact]
    public void MatchingGame_Allowed()
    {
        var id = NewContainer(ContainerType.DeckBox, CardGame.Mtg);
        using var ctx = new OmniCardDbContext(_options);
        // Does not throw.
        DeckBoxGameGuard.ValidateIncoming(ctx, id, [CardGame.Mtg]);
    }

    [Fact]
    public void MismatchedGame_Throws()
    {
        var id = NewContainer(ContainerType.DeckBox, CardGame.Mtg);
        using var ctx = new OmniCardDbContext(_options);
        var ex = Assert.Throws<DeckBoxGameMismatchException>(() =>
            DeckBoxGameGuard.ValidateIncoming(ctx, id, [CardGame.Mtg, CardGame.Pokemon]));
        Assert.Equal(CardGame.Mtg, ex.DeckBoxGame);
        Assert.Equal(CardGame.Pokemon, ex.OffendingGame);
    }

    [Fact]
    public void DeckBoxWithoutGame_AllowsAnything()
    {
        var id = NewContainer(ContainerType.DeckBox, null);
        using var ctx = new OmniCardDbContext(_options);
        DeckBoxGameGuard.ValidateIncoming(ctx, id, [CardGame.Mtg, CardGame.Pokemon]);
    }

    [Fact]
    public void NonDeckBox_AllowsAnything()
    {
        var id = NewContainer(ContainerType.Box, null);
        using var ctx = new OmniCardDbContext(_options);
        DeckBoxGameGuard.ValidateIncoming(ctx, id, [CardGame.Pokemon]);
    }

    [Fact]
    public void NullTarget_AllowsAnything()
    {
        using var ctx = new OmniCardDbContext(_options);
        DeckBoxGameGuard.ValidateIncoming(ctx, null, [CardGame.Pokemon]);
    }
}
