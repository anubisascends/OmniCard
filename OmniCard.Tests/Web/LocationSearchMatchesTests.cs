using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sites;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Tests.Web;

/// <summary>
/// The Locations page search: a collection query counted per location, so only locations holding at
/// least one matching card stay visible.
/// </summary>
public class LocationSearchMatchesTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;

    public LocationSearchMatchesTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private OmniCardDbContext Db() => new(_opts);

    private int AddSite(string name)
    {
        using var db = Db();
        var s = new Site { Name = name };
        db.Sites.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    private int AddLocation(string name, int siteId = Site.DefaultSiteId)
    {
        using var db = Db();
        var c = new StorageContainer { Name = name, ContainerType = ContainerType.Box, SiteId = siteId };
        db.StorageContainers.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private void AddLot(string cardName, int? locationId, CardGame game = CardGame.Pokemon)
    {
        using var db = Db();
        var p = new Product
        {
            Game = game, Category = ProductCategory.Single,
            GameCardId = cardName.ToLowerInvariant(), Name = cardName, SetCode = "BASE", CollectorNumber = "1",
        };
        db.Products.Add(p);
        db.SaveChanges();
        db.Lots.Add(new InventoryLot { ProductId = p.Id, Quantity = 1, Condition = "NM", LocationId = locationId });
        db.SaveChanges();
    }

    private Dictionary<int, int> Matches(string q, CardGame? game = null, IReadOnlyCollection<int>? siteIds = null)
    {
        using var ctx = Db();
        return LocationsController.CountSearchMatches(ctx, q, game, siteIds)
            .ToDictionary(m => m.LocationId, m => m.MatchCount);
    }

    [Fact]
    public void OnlyLocationsWithAMatch_AreReturned_WithTheirMatchCount()
    {
        var binder = AddLocation("Binder");
        var box = AddLocation("Box");
        var empty = AddLocation("Empty");
        AddLot("Pikachu", binder);
        AddLot("Pikachu", binder);
        AddLot("Raichu", binder);
        AddLot("Charizard", box);

        var result = Matches("pikachu");

        Assert.Equal(new Dictionary<int, int> { [binder] = 2 }, result);
        Assert.DoesNotContain(empty, result.Keys);
    }

    [Fact]
    public void UnlocatedCards_AreIgnored()
    {
        AddLot("Pikachu", null);
        Assert.Empty(Matches("pikachu"));
    }

    [Fact]
    public void GameFilter_CountsOnlyThatGamesCards()
    {
        var box = AddLocation("Box");
        AddLot("Bolt", box, CardGame.Mtg);
        AddLot("Bolt Pokemon", box, CardGame.Pokemon);

        Assert.Equal(2, Matches("bolt")[box]);
        Assert.Equal(1, Matches("bolt", CardGame.Mtg)[box]);
    }

    [Fact]
    public void SiteScope_HidesLocationsInUnreadableSites()
    {
        var hers = AddSite("Hers");
        var mine = AddLocation("Mine");
        var theirs = AddLocation("Theirs", hers);
        AddLot("Pikachu", mine);
        AddLot("Pikachu", theirs);

        Assert.Equal([mine, theirs], Matches("pikachu").Keys.Order());
        Assert.Equal([mine], Matches("pikachu", siteIds: [Site.DefaultSiteId]).Keys);
    }
}
