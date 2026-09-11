using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection.Inventory;
using OmniCard.Collection.Lists;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;

namespace OmniCard.Tests.Services.Lists;

public class DeckLegalityServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public DeckLegalityServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MockFactory Factory => new(_options);
    private IDeckTypeService DeckTypes()
    {
        var svc = new DeckTypeService(Factory);
        svc.EnsureSeeded();
        return svc;
    }

    private IDeckLegalityService CreateService(IDeckTypeService deckTypes) =>
        new DeckLegalityService(Factory, deckTypes);

    /// <summary>Creates a deck box for the given built-in deck type and fills it with the given cards
    /// (name, quantity, optional tag). Returns the box id.</summary>
    private int SeedDeckBox(string builtInName, CardGame game,
        IReadOnlyList<(string Name, int Qty, string? Tag, string? CardType)> cards)
    {
        var deckTypeId = DeckTypes().GetForGame(game).Single(d => d.Name == builtInName).Id;
        using var ctx = new OmniCardDbContext(_options);
        var box = new StorageContainer
        {
            Name = $"Deck {Guid.NewGuid():N}",
            ContainerType = ContainerType.DeckBox,
            Game = game,
            DeckTypeId = deckTypeId,
        };
        ctx.StorageContainers.Add(box);
        ctx.SaveChanges();

        foreach (var (name, qty, tag, cardType) in cards)
        {
            var product = new Product { Game = game, Category = ProductCategory.Single, Name = name, CardType = cardType };
            ctx.Products.Add(product);
            ctx.SaveChanges();
            var lot = new InventoryLot { ProductId = product.Id, Quantity = qty, LocationId = box.Id };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();

            if (tag != null)
            {
                var t = new Tag { Name = tag };
                ctx.Tags.Add(t);
                ctx.SaveChanges();
                ctx.LotTags.Add(new LotTag { LotId = lot.Id, TagId = t.Id });
                ctx.SaveChanges();
            }
        }
        return box.Id;
    }

    [Fact]
    public void Commander_SingletonViolation_Warns_ButDoesNotThrow()
    {
        // Two copies of a non-basic in a singleton (Commander) deck → copy-limit warning.
        var deckTypes = DeckTypes();
        var boxId = SeedDeckBox("Commander", CardGame.Mtg, new (string, int, string?, string?)[]
        {
            ("Sol Ring", 2, null, "Artifact"),
            ("The Commander", 1, "commander", "Legendary Creature"),
        });

        var result = CreateService(deckTypes).Check(boxId);

        Assert.False(result.Ok);
        Assert.Contains(result.Warnings, w => w.Code == "copy-limit" && w.Message.Contains("Sol Ring"));
    }

    [Fact]
    public void Commander_BasicLandsExempt_FromSingleton()
    {
        var deckTypes = DeckTypes();
        var boxId = SeedDeckBox("Commander", CardGame.Mtg, new (string, int, string?, string?)[]
        {
            ("Forest", 30, null, "Basic Land"),
            ("The Commander", 1, "commander", "Legendary Creature"),
        });

        var result = CreateService(deckTypes).Check(boxId);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "copy-limit");
    }

    [Fact]
    public void Standard_DeckTooSmall_Warns()
    {
        var deckTypes = DeckTypes();
        var boxId = SeedDeckBox("Standard", CardGame.Mtg, new (string, int, string?, string?)[]
        {
            ("Island", 10, null, "Basic Land"),
        });

        var result = CreateService(deckTypes).Check(boxId);

        Assert.Contains(result.Warnings, w => w.Code == "deck-size-min");
    }

    [Fact]
    public void Standard_CopyLimitExceeded_Warns()
    {
        var deckTypes = DeckTypes();
        var boxId = SeedDeckBox("Standard", CardGame.Mtg, new (string, int, string?, string?)[]
        {
            ("Lightning Bolt", 5, null, "Instant"), // max 4 in Standard
        });

        var result = CreateService(deckTypes).Check(boxId);

        Assert.Contains(result.Warnings, w => w.Code == "copy-limit" && w.Message.Contains("Lightning Bolt"));
    }

    [Fact]
    public void Commander_NinetyNinePlusCommander_HasNoSizeWarning()
    {
        // A legal Commander deck: 99 unique main-deck cards + 1 commander = 100 total. The commander
        // must count toward the 100, so there should be NO deck-size warning.
        var deckTypes = DeckTypes();
        var cards = Enumerable.Range(1, 99)
            .Select(i => ($"Card {i}", 1, (string?)null, (string?)"Creature"))
            .ToList();
        cards.Add(("The Commander", 1, "commander", "Legendary Creature"));
        var boxId = SeedDeckBox("Commander", CardGame.Mtg, cards);

        var result = CreateService(deckTypes).Check(boxId);

        Assert.Equal(99, result.MainDeckCount);
        Assert.Equal(1, result.CommanderCount);
        Assert.Equal(100, result.TotalDeckCount);
        Assert.DoesNotContain(result.Warnings, w => w.Code == "deck-size-min");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "deck-size-max");
        Assert.True(result.Ok);
    }

    [Fact]
    public void Commander_MultipleCommanders_CountTowardTotal_NoWarning()
    {
        // Partner commanders: 98 unique main-deck cards + 2 commanders = 100 total. Both commanders
        // count toward the 100, and running more than one commander must not warn.
        var deckTypes = DeckTypes();
        var cards = Enumerable.Range(1, 98)
            .Select(i => ($"Card {i}", 1, (string?)null, (string?)"Creature"))
            .ToList();
        cards.Add(("Partner A", 1, "commander", "Legendary Creature"));
        cards.Add(("Partner B", 1, "commander", "Legendary Creature"));
        var boxId = SeedDeckBox("Commander", CardGame.Mtg, cards);

        var result = CreateService(deckTypes).Check(boxId);

        Assert.Equal(2, result.CommanderCount);
        Assert.Equal(100, result.TotalDeckCount);
        Assert.DoesNotContain(result.Warnings, w => w.Code == "deck-size-min");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "commander-count");
        Assert.True(result.Ok);
    }

    [Fact]
    public void Commander_MissingCommander_WarnsOnCount()
    {
        var deckTypes = DeckTypes();
        // 100 distinct cards but no commander tag → commander-count warning.
        var cards = Enumerable.Range(1, 100)
            .Select(i => ($"Card {i}", 1, (string?)null, (string?)"Creature"))
            .ToList();
        var boxId = SeedDeckBox("Commander", CardGame.Mtg, cards);

        var result = CreateService(deckTypes).Check(boxId);

        Assert.Contains(result.Warnings, w => w.Code == "commander-count");
    }

    [Fact]
    public void NoDeckType_ReturnsOk()
    {
        var deckTypes = DeckTypes();
        int boxId;
        using (var ctx = new OmniCardDbContext(_options))
        {
            var box = new StorageContainer { Name = "Unset", ContainerType = ContainerType.DeckBox, Game = CardGame.Mtg };
            ctx.StorageContainers.Add(box);
            ctx.SaveChanges();
            boxId = box.Id;
        }

        var result = CreateService(deckTypes).Check(boxId);

        Assert.True(result.Ok);
        Assert.Empty(result.Warnings);
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
