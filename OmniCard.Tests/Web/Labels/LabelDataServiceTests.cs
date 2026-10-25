using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Tests.Services.Ebay;
using OmniCard.Web.Services.Labels;

namespace OmniCard.Tests.Web.Labels;

public class LabelDataServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly TestDbContextFactory _factory;
    private readonly LabelDataService _service;

    public LabelDataServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _factory = new TestDbContextFactory(new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options);
        using (var ctx = _factory.CreateDbContext()) ctx.Database.EnsureCreated();

        // Pokémon prices: "pika" is $2.50 non-foil and $9 foil.
        var pokemon = new Mock<ICardGameService>();
        pokemon.Setup(g => g.Game).Returns(CardGame.Pokemon);
        pokemon.Setup(g => g.GetCurrentPrices(It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
            .Returns((IEnumerable<string> ids, bool foil) =>
                ids.Where(i => i == "12345").ToDictionary(i => i, _ => foil ? 9m : 2.5m));
        var cardService = new Mock<ICardService>();
        cardService.Setup(s => s.GetGameService(CardGame.Pokemon)).Returns(pokemon.Object);

        _service = new LabelDataService(_factory, [pokemon.Object], cardService.Object);
    }

    public void Dispose() => _conn.Dispose();

    private int AddSite(string name)
    {
        using var db = _factory.CreateDbContext();
        var site = new Site { Name = name };
        db.Sites.Add(site);
        db.SaveChanges();
        return site.Id;
    }

    private int AddContainer(string name, int siteId = Site.DefaultSiteId)
    {
        using var db = _factory.CreateDbContext();
        var c = new StorageContainer { Name = name, SiteId = siteId, ContainerType = ContainerType.Binder };
        db.StorageContainers.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private int AddLot(string name, int? locationId, string gameCardId = "12345", bool foil = false, string? foilType = null,
        int quantity = 1, int? page = null, int? slot = null, ProductCategory category = ProductCategory.Single)
    {
        using var db = _factory.CreateDbContext();
        var p = new Product
        {
            Game = CardGame.Pokemon, Category = category, Name = name, GameCardId = gameCardId,
            SetCode = "BS", SetName = "Base Set", CollectorNumber = "58", Rarity = "Common", Foil = foil, FoilType = foilType,
        };
        db.Products.Add(p);
        db.SaveChanges();
        var lot = new InventoryLot
        {
            ProductId = p.Id, LocationId = locationId, Quantity = quantity, Condition = "LP", Page = page, Slot = slot,
        };
        db.Lots.Add(lot);
        db.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void Build_FillsTheLabelValuesForASelection()
    {
        var binder = AddContainer("Trade binder");
        var id = AddLot("Pikachu", binder, foil: true, foilType: "Reverse Holofoil", quantity: 3);

        var card = Assert.Single(_service.Build(new LabelDataRequest { LotIds = [id] }, null).Cards);

        Assert.Equal("Pikachu", card.Name);
        Assert.Equal("Pokemon", card.Game);
        Assert.Equal("Pokémon", card.GameName);
        Assert.Equal("BS", card.SetCode);
        Assert.Equal("58", card.CollectorNumber);
        Assert.Equal("LP", card.Condition);
        Assert.Equal("Reverse Holofoil", card.Finish);
        Assert.Equal(3, card.Quantity);
        Assert.Equal(9m, card.MarketPrice);
        Assert.Equal(12345, card.TcgPlayerId);
        Assert.Equal("https://www.tcgplayer.com/product/12345", card.TcgPlayerUrl);
        Assert.Equal("Trade binder", card.Location);
    }

    [Fact]
    public void Build_KeepsTheSelectionOrderAndSkipsSealedProduct()
    {
        var a = AddLot("Alpha", null);
        var b = AddLot("Bravo", null, gameCardId: "777");
        var box = AddLot("Booster Box", null, category: ProductCategory.Box);

        var cards = _service.Build(new LabelDataRequest { LotIds = [b, box, a] }, null).Cards;

        Assert.Equal(["Bravo", "Alpha"], cards.Select(c => c.Name));
        Assert.Equal("", cards[0].Finish);
        Assert.Equal(0m, cards[0].MarketPrice); // no catalog price
    }

    [Fact]
    public void Build_DropsCardsInSitesTheUserCantRead()
    {
        var shop = AddSite("Shop");
        var home = AddLot("Home card", AddContainer("Home box"));
        var away = AddLot("Shop card", AddContainer("Shop box", shop));

        var cards = _service.Build(new LabelDataRequest { LotIds = [home, away] }, [Site.DefaultSiteId]).Cards;

        Assert.Equal(["Home card"], cards.Select(c => c.Name));
    }

    [Fact]
    public void Build_ForALocation_ListsItsCardsInBinderOrder()
    {
        var binder = AddContainer("Binder");
        AddLot("Zapdos", binder, page: 1, slot: 2);
        AddLot("Charizard", binder, page: 2, slot: 1);
        AddLot("Abra", binder);                 // unplaced → after the pages
        AddLot("Mew", binder, page: 1, slot: 1);
        AddLot("Elsewhere", AddContainer("Other"));

        var result = _service.Build(new LabelDataRequest { ContainerId = binder }, null);

        Assert.False(result.Truncated);
        Assert.Equal(["Mew", "Zapdos", "Charizard", "Abra"], result.Cards.Select(c => c.Name));
    }

    [Fact]
    public void Build_ForALocation_AppliesTheSearch()
    {
        var binder = AddContainer("Binder");
        AddLot("Pikachu", binder);
        AddLot("Raichu", binder);

        var cards = _service.Build(new LabelDataRequest { ContainerId = binder, Game = "Pokemon", Q = "Raichu" }, null).Cards;

        Assert.Equal(["Raichu"], cards.Select(c => c.Name));
    }

    [Fact]
    public void Build_WithNothingRequested_ReturnsNoCards() =>
        Assert.Empty(_service.Build(new LabelDataRequest(), null).Cards);
}
