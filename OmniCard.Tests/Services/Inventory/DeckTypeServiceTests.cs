using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Storage;

namespace OmniCard.Tests.Services.Inventory;

public class DeckTypeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public DeckTypeServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IDeckTypeService CreateService() => new DeckTypeService(new MockFactory(_options));

    [Fact]
    public void EnsureSeeded_SeedsBuiltIns_AndIsIdempotent()
    {
        var service = CreateService();

        service.EnsureSeeded();
        var afterFirst = new OmniCardDbContext(_options).DeckTypes.Count();
        service.EnsureSeeded(); // second run must not duplicate
        var afterSecond = new OmniCardDbContext(_options).DeckTypes.Count();

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, afterSecond);

        // Commander is present with its singleton + commander-slot rules.
        var commander = service.GetForGame(CardGame.Mtg).Single(d => d.Name == "Commander");
        Assert.True(commander.Singleton);
        Assert.Equal(1, commander.CommanderSlots);
        Assert.True(commander.IsBuiltIn);
    }

    [Fact]
    public void EnsureSeeded_DoesNotOverwriteRenamedBuiltIn()
    {
        var service = CreateService();
        service.EnsureSeeded();
        var commander = service.GetForGame(CardGame.Mtg).Single(d => d.Name == "Commander");

        service.Update(commander.Id, new DeckType { Name = "EDH", CommanderSlots = 1, Singleton = true });
        service.EnsureSeeded(); // re-seed must respect the rename (keyed on BuiltInKey)

        var all = service.GetForGame(CardGame.Mtg);
        Assert.Contains(all, d => d.Name == "EDH");
        Assert.DoesNotContain(all, d => d.Name == "Commander");
    }

    [Fact]
    public void Create_Custom_AddsType_AndRejectsDuplicateNamePerGame()
    {
        var service = CreateService();

        var created = service.Create(new DeckType { Game = CardGame.Mtg, Name = "Canadian Highlander" });
        Assert.False(created.IsBuiltIn);
        Assert.Null(created.BuiltInKey);

        Assert.Throws<InvalidOperationException>(() =>
            service.Create(new DeckType { Game = CardGame.Mtg, Name = "canadian highlander" }));

        // Same name under a different game is fine.
        var other = service.Create(new DeckType { Game = CardGame.Pokemon, Name = "Canadian Highlander" });
        Assert.NotEqual(created.Id, other.Id);
    }

    [Fact]
    public void Delete_RemovesType_AndUnsetsReferencingDeckBoxes()
    {
        var service = CreateService();
        var deckType = service.Create(new DeckType { Game = CardGame.Mtg, Name = "Temp" });

        int boxId;
        using (var ctx = new OmniCardDbContext(_options))
        {
            var box = new StorageContainer
            {
                Name = "Deck",
                ContainerType = ContainerType.DeckBox,
                Game = CardGame.Mtg,
                DeckTypeId = deckType.Id,
            };
            ctx.StorageContainers.Add(box);
            ctx.SaveChanges();
            boxId = box.Id;
        }

        service.Delete(deckType.Id);

        using var check = new OmniCardDbContext(_options);
        Assert.Empty(check.DeckTypes.Where(d => d.Id == deckType.Id));
        // FK SetNull clears the reference but keeps the box.
        var savedBox = check.StorageContainers.Single(c => c.Id == boxId);
        Assert.Null(savedBox.DeckTypeId);
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
